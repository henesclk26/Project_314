using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public class TaskManager : NetworkBehaviour
{
    public static TaskManager Instance { get; private set; }

    public NetworkList<TaskRun> ActiveTaskRuns = new NetworkList<TaskRun>();
    public NetworkList<TerminalHackState> TerminalHackStates = new NetworkList<TerminalHackState>();
    public NetworkVariable<int> CrewTaskProgress = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> CrewTaskTarget = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> KillerSabotagePoints = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<byte> CompletedRogueHackMask = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public List<TaskDefinition> AvailableTasks;

    private static readonly string[] KillerHackSequence =
    {
        "MissionComputer",
        "CircuitMission",
        "WaveFrequency"
    };

    private static readonly string[] QuickTestNormalTaskIds =
    {
        "MissionComputer",
        "CircuitMission",
        "WaveFrequency",
        "PressureTerminal",
        "ReactorTerminal"
    };

    // Sabotage points are reserved for the later limited-sabotage loop. Keep
    // their economy bounded even before spending rules are introduced.
    public static int MaxKillerSabotagePoints => DemoBalanceConfig.MaxKillerSabotagePoints;
    private int nextCooperativeSessionId = 1;
    private float nextTaskCleanupTime;
    private readonly Dictionary<ulong, Queue<string>> recentNormalTaskIds = new Dictionary<ulong, Queue<string>>();
    private readonly HashSet<ulong> basketballRewardedPlayers = new HashSet<ulong>();
    // Cooperative missions are a finite sequence for the current match. Once
    // a mission is completed it must not be selected again in the same match.
    private readonly HashSet<string> completedCooperativeTaskIds = new HashSet<string>(StringComparer.Ordinal);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (GetComponent<UpgradeManager>() == null)
            gameObject.AddComponent<UpgradeManager>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            InitializeTerminalHackStates();
            var mfm = MatchFlowManager.Instance ?? FindAnyObjectByType<MatchFlowManager>();
            if (mfm != null)
            {
                mfm.CurrentPhase.OnValueChanged += OnMatchPhaseChanged;
            }
            
            var rm = RoleManager.Instance ?? FindAnyObjectByType<RoleManager>();
            if (rm != null)
            {
                rm.OnRolesDistributed += HandleRolesDistributed;
                
                // If roles are already distributed and we joined late
                if (rm.GetLocalPlayerRole() != PlayerRole.None && ActiveTaskRuns.Count == 0 && mfm != null && (mfm.CurrentPhase.Value == MatchPhase.BootProtection || mfm.CurrentPhase.Value == MatchPhase.Active))
                {
                    HandleRolesDistributed();
                }
            }
        }
    }

    private void Update()
    {
        if (!IsServer || NetworkManager.Singleton == null)
            return;

        if (Time.unscaledTime >= nextTaskCleanupTime)
        {
            nextTaskCleanupTime = Time.unscaledTime + 0.25f;
            CleanupInvalidTaskRuns();
        }

        double now = NetworkManager.Singleton.ServerTime.Time;
        for (int i = 0; i < TerminalHackStates.Count; i++)
        {
            TerminalHackState hack = TerminalHackStates[i];
            if ((hack.Phase == TerminalHackPhase.Preparing ||
                 hack.Phase == TerminalHackPhase.Cooldown) &&
                now >= hack.ServerTime)
            {
                hack.Phase = hack.Phase == TerminalHackPhase.Preparing
                    ? TerminalHackPhase.Available
                    : TerminalHackPhase.Idle;
                hack.ServerTime = 0d;
                hack.Revision++;
                TerminalHackStates[i] = hack;
            }
        }

        int cooperativeSessionId = GetActiveCooperativeSessionId();
        if (cooperativeSessionId > 0 && IsCooperativeMissionCompleted(cooperativeSessionId))
            CompleteCooperativeTask(cooperativeSessionId);

        CompleteQuickTestCooperativeTaskIfReady();
    }

    private void HandleRolesDistributed()
    {
        if (IsServer && ActiveTaskRuns.Count == 0)
        {
            basketballRewardedPlayers.Clear();
            InitializeCrewTarget();
            AssignInitialTasks();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            var mfm = MatchFlowManager.Instance ?? FindAnyObjectByType<MatchFlowManager>();
            if (mfm != null)
            {
                mfm.CurrentPhase.OnValueChanged -= OnMatchPhaseChanged;
            }
            
            var rm = RoleManager.Instance ?? FindAnyObjectByType<RoleManager>();
            if (rm != null)
            {
                rm.OnRolesDistributed -= HandleRolesDistributed;
            }
        }

        base.OnNetworkDespawn();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer)
            return;

        CleanupInvalidTaskRuns();
    }

    private void CleanupInvalidTaskRuns()
    {
        if (ActiveTaskRuns.Count == 0 || MatchFlowManager.Instance?.CurrentPhase.Value == MatchPhase.Ended)
            return;

        HashSet<int> affectedCooperativeSessions = new HashSet<int>();
        List<ulong> removedSoloOwners = new List<ulong>();

        for (int i = ActiveTaskRuns.Count - 1; i >= 0; i--)
        {
            TaskRun run = ActiveTaskRuns[i];
            if (IsTaskOwnerAliveAndConnected(run.OwnerClientId))
                continue;

            if (run.CooperativeSessionId > 0)
                affectedCooperativeSessions.Add(run.CooperativeSessionId);
            else
                removedSoloOwners.Add(run.OwnerClientId);

            ActiveTaskRuns.RemoveAt(i);
        }

        foreach (int sessionId in affectedCooperativeSessions)
            RepairCooperativeSession(sessionId);

        // A dead player must not receive a replacement. Their next assignment
        // is created when a living player completes a normal task.
        if (removedSoloOwners.Count > 0)
            Debug.Log($"[TaskManager] Removed {removedSoloOwners.Count} invalid personal task run(s).");
    }

    private bool IsTaskOwnerAliveAndConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.ConnectedClientsIds.Contains(clientId))
            return false;

        FirstPersonController player = NetworkPlayerLookup.Find(clientId);
        return player != null && !player.isDead.Value;
    }

    private void RepairCooperativeSession(int sessionId)
    {
        List<TaskRun> sessionRuns = GetCooperativeSessionRuns(sessionId);
        if (sessionRuns.Count == 0)
            return;

        TaskDefinition definition = AvailableTasks?.FirstOrDefault(task =>
            task != null && task.TaskID == sessionRuns[0].TaskID.ToString().Trim());
        int requiredParticipants = definition != null ? definition.RequiredVillagers : 3;

        List<ulong> eligibleVillagers = GetEligibleVillagers();
        HashSet<ulong> assigned = new HashSet<ulong>(sessionRuns.Select(run => run.OwnerClientId));
        List<ulong> replacements = eligibleVillagers
            .Where(clientId => !assigned.Contains(clientId))
            .OrderBy(_ => UnityEngine.Random.value)
            .Take(Mathf.Max(0, requiredParticipants - sessionRuns.Count))
            .ToList();

        byte nextRoleIndex = (byte)sessionRuns.Count;
        foreach (ulong replacement in replacements)
        {
            ReservePersonalTaskForCooperativeSession(replacement);
            ActiveTaskRuns.Add(new TaskRun
            {
                OwnerClientId = replacement,
                TaskID = sessionRuns[0].TaskID,
                Kind = TaskRunKind.Normal,
                SequenceIndex = -1,
                CooperativeSessionId = sessionId,
                CooperativeRoleIndex = nextRoleIndex++, 
                State = sessionRuns[0].State,
                Progress = 0f
            });
        }

        sessionRuns = GetCooperativeSessionRuns(sessionId);
        if (sessionRuns.Count >= requiredParticipants)
        {
            Debug.Log($"[TaskManager] Repaired cooperative session {sessionId} with {replacements.Count} replacement(s).");
            return;
        }

        // The run is impossible with the current living population. Release it
        // without awarding progress and return surviving participants to solo work.
        for (int i = ActiveTaskRuns.Count - 1; i >= 0; i--)
        {
            if (ActiveTaskRuns[i].CooperativeSessionId == sessionId)
                ActiveTaskRuns.RemoveAt(i);
        }

        foreach (TaskRun participant in sessionRuns)
        {
            if (IsTaskOwnerAliveAndConnected(participant.OwnerClientId) &&
                RoleManager.Instance.GetPlayerRole(participant.OwnerClientId) == PlayerRole.Villager)
            {
                RestoreOrAssignNormalTask(participant.OwnerClientId);
            }
        }

        Debug.LogWarning($"[TaskManager] Cancelled impossible cooperative session {sessionId}; fewer than {requiredParticipants} villagers remain.");
    }

    private List<ulong> GetEligibleVillagers()
    {
        if (NetworkManager.Singleton == null || RoleManager.Instance == null)
            return new List<ulong>();

        return NetworkManager.Singleton.ConnectedClientsIds
            .Where(clientId => RoleManager.Instance.GetPlayerRole(clientId) == PlayerRole.Villager &&
                               IsTaskOwnerAliveAndConnected(clientId))
            .ToList();
    }

    private void ReservePersonalTaskForCooperativeSession(ulong clientId)
    {
        for (int i = 0; i < ActiveTaskRuns.Count; i++)
        {
            TaskRun run = ActiveTaskRuns[i];
            if (run.OwnerClientId != clientId || run.CooperativeSessionId > 0)
                continue;

            if (run.State == TaskRunState.InProgress || run.State == TaskRunState.Assigned)
            {
                run.State = TaskRunState.Reserved;
                ActiveTaskRuns[i] = run;
            }
            return;
        }
    }

    private void RestoreOrAssignNormalTask(ulong clientId)
    {
        TaskRun? existingRun = GetActiveTaskForPlayer(clientId);
        if (existingRun.HasValue && existingRun.Value.CooperativeSessionId == 0)
            return;

        AssignNormalTask(clientId);
    }

    private void OnMatchPhaseChanged(MatchPhase previous, MatchPhase current)
    {
        // Do nothing here for task assignment. We wait for OnRolesDistributed.
        if (current == MatchPhase.Meeting)
        {
            // Pause all in-progress tasks
            for (int i = 0; i < ActiveTaskRuns.Count; i++)
            {
                var run = ActiveTaskRuns[i];
                if (run.State == TaskRunState.InProgress)
                {
                    run.State = TaskRunState.Reserved;
                    ActiveTaskRuns[i] = run;
                }
            }
        }
        else if (current == MatchPhase.Ended)
        {
            ActiveTaskRuns.Clear();
            TerminalHackStates.Clear();
            MissionManager.Instance?.ReleaseSharedValveSession();
            CrewTaskProgress.Value = 0;
            CrewTaskTarget.Value = 0;
            KillerSabotagePoints.Value = 0;
            CompletedRogueHackMask.Value = 0;
            if (MissionManager.Instance != null)
                MissionManager.Instance.IsValveOverrideUnlocked.Value = false;
            recentNormalTaskIds.Clear();
            basketballRewardedPlayers.Clear();
            completedCooperativeTaskIds.Clear();
        }
    }

    private void InitializeCrewTarget()
    {
        int startingVillagers = 0;
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (RoleManager.Instance.GetPlayerRole(clientId) == PlayerRole.Villager)
                startingVillagers++;
        }
        CrewTaskTarget.Value = startingVillagers * DemoBalanceConfig.CrewTaskRunsPerVillager;
    }

    private void AssignInitialTasks()
    {
        List<ulong> villagers = NetworkManager.Singleton.ConnectedClientsIds
            .Where(id => RoleManager.Instance.GetPlayerRole(id) == PlayerRole.Villager)
            .ToList();

        TaskDefinition cooperativeTask = SelectCooperativeTask(villagers.Count);
        if (cooperativeTask != null)
        {
            List<ulong> participants = villagers
                .OrderBy(_ => UnityEngine.Random.value)
                .Take(cooperativeTask.RequiredVillagers)
                .ToList();
            AssignCooperativeTask(cooperativeTask, participants);
            villagers = villagers.Except(participants).ToList();
        }

        foreach (ulong clientId in villagers)
        {
            PlayerRole role = RoleManager.Instance.GetPlayerRole(clientId);
            if (role == PlayerRole.Villager)
                AssignNormalTask(clientId);
        }

        // The killer deliberately receives no villager TaskRun. Rogue work is
        // created only when a completed terminal finishes its hack preparation.
    }

    private TaskDefinition SelectCooperativeTask(int livingVillagerCount)
    {
        if (AvailableTasks == null || livingVillagerCount < 3)
            return null;

            List<TaskDefinition> candidates = AvailableTasks
            .Where(t => t != null && t.IsCooperative && !t.IsSpecialMapSequence &&
                        t.RequiredVillagers >= 3 && t.RequiredVillagers <= livingVillagerCount &&
                        !completedCooperativeTaskIds.Contains(t.TaskID.Trim()))
            .ToList();
        return candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    private void AssignCooperativeTask(TaskDefinition definition, List<ulong> participants)
    {
        if (definition == null || participants == null || participants.Count < definition.RequiredVillagers)
            return;

        int sessionId = nextCooperativeSessionId++;
        MissionManager.Instance?.ResetNormalTaskState(definition.TaskID);
        if (definition.TaskID == "ReactorTerminal")
            ReactorMissionManager.Instance?.ResetForTaskAssignment();
        for (byte roleIndex = 0; roleIndex < participants.Count; roleIndex++)
        {
            ActiveTaskRuns.Add(new TaskRun
            {
                OwnerClientId = participants[roleIndex],
                TaskID = definition.TaskID,
                Kind = TaskRunKind.Normal,
                SequenceIndex = -1,
                CooperativeSessionId = sessionId,
                CooperativeRoleIndex = roleIndex,
                State = TaskRunState.Assigned,
                Progress = 0f
            });
        }

        Debug.Log($"[TaskManager] Cooperative task '{definition.TaskID}' assigned to {participants.Count} villagers (session {sessionId}).");
    }

    private int GetActiveCooperativeSessionId()
    {
        for (int i = 0; i < ActiveTaskRuns.Count; i++)
        {
            if (ActiveTaskRuns[i].CooperativeSessionId > 0)
                return ActiveTaskRuns[i].CooperativeSessionId;
        }

        return 0;
    }

    private bool IsCooperativeMissionCompleted(int sessionId)
    {
        for (int i = 0; i < ActiveTaskRuns.Count; i++)
        {
            TaskRun run = ActiveTaskRuns[i];
            if (run.CooperativeSessionId != sessionId)
                continue;

            string taskId = run.TaskID.ToString().Trim();
            if (taskId == "PressureTerminal")
                return MissionManager.Instance != null && MissionManager.Instance.IsPressureMissionCompleted.Value;
            if (taskId == "ReactorTerminal")
            {
                ReactorMissionManager reactor = ReactorMissionManager.Instance;
                return reactor != null && reactor.IsMissionCompleted.Value;
            }
        }

        return false;
    }

    private void CompleteCooperativeTask(int sessionId)
    {
        List<ulong> participants = new List<ulong>();
        string completedTaskId = null;
        for (int i = ActiveTaskRuns.Count - 1; i >= 0; i--)
        {
            TaskRun run = ActiveTaskRuns[i];
            if (run.CooperativeSessionId != sessionId)
                continue;

            if (string.IsNullOrWhiteSpace(completedTaskId))
                completedTaskId = run.TaskID.ToString().Trim();
            participants.Add(run.OwnerClientId);
            ActiveTaskRuns.RemoveAt(i);
        }

        if (participants.Count == 0)
            return;

        CrewTaskProgress.Value++;
        Debug.Log($"[TaskManager] Cooperative task session {sessionId} completed; crew progress: {CrewTaskProgress.Value}.");
        CheckWinCondition();
        foreach (ulong participant in participants)
        {
            if (NetworkManager.Singleton.ConnectedClientsIds.Contains(participant) &&
                RoleManager.Instance.GetPlayerRole(participant) == PlayerRole.Villager)
            {
                UpgradeManager.Instance?.AwardTaskPoint(participant);
                RestoreOrAssignNormalTask(participant);
            }
        }

        if (!string.IsNullOrWhiteSpace(completedTaskId))
            completedCooperativeTaskIds.Add(completedTaskId);

        TryAssignCooperativeOverlay();
    }

    private void TryAssignCooperativeOverlay()
    {
        if (MatchFlowManager.Instance == null ||
            (MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active &&
             MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.BootProtection))
            return;

        if (GetActiveCooperativeSessionId() > 0)
            return;

        List<ulong> villagers = GetEligibleVillagers();
        TaskDefinition cooperativeTask = SelectCooperativeTask(villagers.Count);
        if (cooperativeTask == null)
        {
            Debug.Log("[TaskManager] All available cooperative tasks are complete for this match (or fewer than three villagers remain); continuing with personal tasks.");
            return;
        }

        for (int i = villagers.Count - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            (villagers[i], villagers[swapIndex]) = (villagers[swapIndex], villagers[i]);
        }

        List<ulong> participants = villagers.Take(cooperativeTask.RequiredVillagers).ToList();
        foreach (ulong participant in participants)
            ReservePersonalTaskForCooperativeSession(participant);
        AssignCooperativeTask(cooperativeTask, participants);
    }

    private void AssignNormalTask(ulong clientId)
    {
        if (AvailableTasks == null || AvailableTasks.Count == 0) return;

        var validTasks = AvailableTasks
            .Where(t => t != null && !t.IsCooperative && !t.IsSpecialMapSequence)
            .ToList();

        recentNormalTaskIds.TryGetValue(clientId, out Queue<string> recentTasks);
        List<TaskDefinition> preferredTasks = validTasks
            .Where(task => recentTasks == null || !recentTasks.Contains(task.TaskID))
            .ToList();
        if (preferredTasks.Count > 0)
            validTasks = preferredTasks;
        
        if (validTasks.Count > 0)
        {
            var chosenTask = validTasks[UnityEngine.Random.Range(0, validTasks.Count)];

            MissionManager.Instance?.ResetNormalTaskState(chosenTask.TaskID);
            
            TaskRun newRun = new TaskRun
            {
                OwnerClientId = clientId,
                TaskID = chosenTask.TaskID,
                Kind = TaskRunKind.Normal,
                SequenceIndex = -1,
                CooperativeSessionId = 0,
                CooperativeRoleIndex = 0,
                State = TaskRunState.Assigned,
                Progress = 0f
            };

            ActiveTaskRuns.Add(newRun);
        }
    }

    private void AssignNextKillerHack(ulong clientId, int sequenceIndex)
    {
        if (KillerHackSequence.Length == 0)
            return;

        int normalizedIndex = sequenceIndex % KillerHackSequence.Length;
        TaskRun newRun = new TaskRun
        {
            OwnerClientId = clientId,
            TaskID = KillerHackSequence[normalizedIndex],
            Kind = TaskRunKind.Rogue,
            SequenceIndex = normalizedIndex,
            CooperativeSessionId = 0,
            CooperativeRoleIndex = 0,
            State = TaskRunState.Assigned,
            Progress = 0f
        };

        ActiveTaskRuns.Add(newRun);
        Debug.Log($"[TaskManager] Killer {clientId} assigned hack {newRun.TaskID} ({normalizedIndex + 1}/{KillerHackSequence.Length}).");
    }

    private void InitializeTerminalHackStates()
    {
        if (TerminalHackStates.Count > 0)
            return;

        foreach (string taskId in KillerHackSequence)
        {
            TerminalHackStates.Add(new TerminalHackState
            {
                TaskID = taskId,
                Phase = TerminalHackPhase.Idle,
                ServerTime = 0d,
                Revision = 0
            });
        }
    }

    public bool IsRogueTaskForPlayer(ulong clientId, string taskID)
    {
        TaskRun? run = GetActiveTaskForPlayer(clientId);
        return run.HasValue &&
               run.Value.Kind == TaskRunKind.Rogue &&
               TaskIdsEqual(run.Value.TaskID, taskID);
    }

    public bool CanUseRogueTask(ulong clientId, string taskID)
    {
        if (GameplayInteractionGate.IsQuickTestMode)
        {
            return GameplayInteractionGate.IsQuickTestRogueTaskMode &&
                   IsQuickTestRogueTaskId(taskID) &&
                   IsTaskPhaseOpen() &&
                   IsTaskOwnerAliveAndConnected(clientId);
        }

        if (MatchFlowManager.Instance == null ||
            MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active ||
            RoleManager.Instance == null ||
            RoleManager.Instance.GetPlayerRole(clientId) != PlayerRole.Impostor ||
            !IsTaskOwnerAliveAndConnected(clientId))
        {
            return false;
        }

        TerminalHackPhase phase = GetTerminalHackPhase(taskID);
        return phase == TerminalHackPhase.Available ||
               phase == TerminalHackPhase.Active;
    }

    public bool CanUseAlibiTask(ulong clientId, string taskID)
    {
        // The alibi path is intentionally available only in the explicit
        // local Quick Test normal-task mode. Production villagers/killers
        // retain the normal role-separated rules.
        return GameplayInteractionGate.IsQuickTestNormalTaskMode &&
               IsQuickTestTaskId(taskID) &&
               IsTaskPhaseOpen() &&
               IsTaskOwnerAliveAndConnected(clientId);
    }

    private static bool IsQuickTestTaskId(string taskID)
    {
        if (string.IsNullOrWhiteSpace(taskID))
            return false;

        return QuickTestNormalTaskIds.Contains(taskID.Trim(), StringComparer.Ordinal);
    }

    private static bool IsQuickTestRogueTaskId(string taskID)
    {
        if (string.IsNullOrWhiteSpace(taskID))
            return false;

        return KillerHackSequence.Contains(taskID.Trim(), StringComparer.Ordinal);
    }

    private bool IsTaskPhaseOpen()
    {
        if (MatchFlowManager.Instance == null)
            return false;

        MatchPhase phase = MatchFlowManager.Instance.CurrentPhase.Value;
        return phase == MatchPhase.BootProtection || phase == MatchPhase.Active;
    }

    public TerminalHackPhase GetTerminalHackPhase(string taskID)
    {
        for (int i = 0; i < TerminalHackStates.Count; i++)
        {
            if (TaskIdsEqual(TerminalHackStates[i].TaskID, taskID))
                return TerminalHackStates[i].Phase;
        }

        return TerminalHackPhase.Idle;
    }

    private int GetTerminalHackStateIndex(string taskID)
    {
        for (int i = 0; i < TerminalHackStates.Count; i++)
        {
            if (TaskIdsEqual(TerminalHackStates[i].TaskID, taskID))
                return i;
        }

        return -1;
    }

    private void BeginHackPreparation(string taskID)
    {
        int index = GetTerminalHackStateIndex(taskID);
        if (index < 0 || NetworkManager.Singleton == null)
            return;

        TerminalHackState hack = TerminalHackStates[index];
        if (hack.Phase != TerminalHackPhase.Idle)
            return;

        hack.Phase = TerminalHackPhase.Preparing;
        hack.ServerTime = NetworkManager.Singleton.ServerTime.Time + DemoBalanceConfig.TerminalHackPreparationSeconds;
        hack.Revision++;
        TerminalHackStates[index] = hack;
        Debug.Log($"[TaskManager] Hack preparation started for {taskID}; available in {DemoBalanceConfig.TerminalHackPreparationSeconds:0}s.");
    }

    private void StartHack(string taskID)
    {
        int index = GetTerminalHackStateIndex(taskID);
        if (index < 0)
            return;

        TerminalHackState hack = TerminalHackStates[index];
        if (hack.Phase != TerminalHackPhase.Available)
            return;

        hack.Phase = TerminalHackPhase.Active;
        hack.ServerTime = 0d;
        hack.Revision++;
        TerminalHackStates[index] = hack;
        MissionManager.Instance?.ResetRogueTaskState(taskID);
    }

    private void CompleteHack(string taskID)
    {
        int index = GetTerminalHackStateIndex(taskID);
        if (index < 0 || NetworkManager.Singleton == null)
            return;

        TerminalHackState hack = TerminalHackStates[index];
        hack.Phase = TerminalHackPhase.Cooldown;
        hack.ServerTime = NetworkManager.Singleton.ServerTime.Time + DemoBalanceConfig.TerminalHackCooldownSeconds;
        hack.Revision++;
        TerminalHackStates[index] = hack;
    }

    private static bool TaskIdsEqual(Unity.Collections.FixedString32Bytes value, string taskID)
    {
        return !string.IsNullOrWhiteSpace(taskID) &&
               string.Equals(value.ToString().Trim(), taskID.Trim(), StringComparison.Ordinal);
    }

    public bool IsRogueHackCompleted(string taskID)
    {
        return (CompletedRogueHackMask.Value & GetRogueHackBit(taskID)) != 0;
    }

    private static byte GetRogueHackBit(string taskID)
    {
        for (int i = 0; i < KillerHackSequence.Length; i++)
        {
            if (string.Equals(KillerHackSequence[i], taskID, StringComparison.Ordinal))
                return (byte)(1 << i);
        }

        return 0;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestStartTaskRpc(string taskID, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        Debug.Log($"[TaskManager] RequestStartTaskRpc called by sender {senderClientId}, task '{taskID}'.");

        if (!IsTaskPhaseOpen() || !IsTaskOwnerAliveAndConnected(senderClientId))
            return;

        int runIndex = GetTaskRunIndex(senderClientId, taskID);
        if (GameplayInteractionGate.IsQuickTestMode && runIndex >= 0)
        {
            TaskRun existingRun = ActiveTaskRuns[runIndex];
            bool modeMatches = GameplayInteractionGate.IsQuickTestRogueTaskMode
                ? existingRun.Kind == TaskRunKind.Rogue
                : existingRun.Kind == TaskRunKind.Normal;
            if (!modeMatches)
            {
                // The same physical terminal is shared by both Quick Test
                // task sets. Discard only the stale local test run when F1
                // changes modes; this branch is never reachable in a lobby
                // match because Quick Test mode is explicitly disabled there.
                ActiveTaskRuns.RemoveAt(runIndex);
                runIndex = -1;
            }
        }
        Debug.Log($"[TaskManager] runIndex = {runIndex}");
        if (runIndex >= 0)
        {
            var run = ActiveTaskRuns[runIndex];
            if (!IsTaskOwnerAliveAndConnected(senderClientId))
                return;

            if (!GameplayInteractionGate.IsQuickTestMode &&
                run.Kind == TaskRunKind.Normal &&
                RoleManager.Instance.GetPlayerRole(senderClientId) != PlayerRole.Villager)
                return;

            if (run.Kind == TaskRunKind.Alibi &&
                !CanUseAlibiTask(senderClientId, taskID))
                return;

            // The server, rather than the interaction prompt, owns the
            // physical terminal reservation. This closes the race where a
            // villager and the killer start the same terminal in one tick.
            if (run.Kind == TaskRunKind.Normal &&
                run.CooperativeSessionId == 0 &&
                !IsTerminalAvailable(taskID, senderClientId))
                return;

            Debug.Log($"[TaskManager] Current state: {run.State}");
            if (run.Kind == TaskRunKind.Rogue && !CanUseRogueTask(senderClientId, taskID))
            {
                Debug.LogWarning($"[TaskManager] Hack start rejected for sender {senderClientId}: terminal '{taskID}' is not available.");
                return;
            }

            if (run.State == TaskRunState.Assigned || run.State == TaskRunState.Reserved)
            {
                if (run.CooperativeSessionId > 0)
                {
                    if (!AreCooperativeSlotsValid(run.CooperativeSessionId))
                    {
                        Debug.LogWarning($"[TaskManager] Cooperative start rejected for session {run.CooperativeSessionId}: required living villager slots are not filled.");
                        return;
                    }

                    for (int i = 0; i < ActiveTaskRuns.Count; i++)
                    {
                        TaskRun participant = ActiveTaskRuns[i];
                        if (participant.CooperativeSessionId == run.CooperativeSessionId &&
                            (participant.State == TaskRunState.Assigned || participant.State == TaskRunState.Reserved))
                        {
                            participant.State = TaskRunState.InProgress;
                            ActiveTaskRuns[i] = participant;
                        }
                    }
                }
                else
                {
                    run.State = TaskRunState.InProgress;
                    ActiveTaskRuns[runIndex] = run;
                }
                if (run.Kind == TaskRunKind.Rogue)
                {
                    if (GameplayInteractionGate.IsQuickTestMode)
                    {
                        // Quick Test skips the production terminal unlock
                        // sequence so every rogue UI can be exercised on
                        // demand. The production path still starts the
                        // terminal-owned HackActive state here.
                        MissionManager.Instance?.ResetRogueTaskState(taskID);
                    }
                    else
                    {
                        StartHack(taskID);
                    }
                }
                Debug.Log($"[TaskManager] State changed to InProgress");
            }
        }
        else
        {
            if (CanUseRogueTask(senderClientId, taskID) &&
                (GameplayInteractionGate.IsQuickTestMode ||
                 GetTerminalHackPhase(taskID) == TerminalHackPhase.Available))
            {
                if (GameplayInteractionGate.IsQuickTestMode)
                    MissionManager.Instance?.ResetRogueTaskState(taskID);
                else
                    StartHack(taskID);

                ActiveTaskRuns.Add(new TaskRun
                {
                    OwnerClientId = senderClientId,
                    TaskID = taskID,
                    Kind = TaskRunKind.Rogue,
                    SequenceIndex = -1,
                    CooperativeSessionId = 0,
                    CooperativeRoleIndex = 0,
                    State = TaskRunState.InProgress,
                    Progress = 0f
                });
                Debug.Log($"[TaskManager] Killer {senderClientId} started rogue task '{taskID}'.");
                return;
            }

            if (CanUseAlibiTask(senderClientId, taskID))
            {
                MissionManager.Instance?.ResetNormalTaskState(taskID);
                if (taskID == "ReactorTerminal")
                    ReactorMissionManager.Instance?.ResetForTaskAssignment();

                ActiveTaskRuns.Add(new TaskRun
                {
                    OwnerClientId = senderClientId,
                    TaskID = taskID,
                    Kind = GameplayInteractionGate.IsQuickTestMode
                        ? TaskRunKind.Normal
                        : TaskRunKind.Alibi,
                    SequenceIndex = -1,
                    CooperativeSessionId = 0,
                    CooperativeRoleIndex = 0,
                    State = TaskRunState.InProgress,
                    Progress = 0f
                });
                Debug.Log(GameplayInteractionGate.IsQuickTestMode
                    ? $"[TaskManager] Quick Test started normal task '{taskID}'."
                    : $"[TaskManager] Killer {senderClientId} started alibi task '{taskID}'.");
                return;
            }

            Debug.LogWarning($"[TaskManager] Start rejected: no task '{taskID}' is assigned to sender {senderClientId}.");
        }
    }

    private bool AreCooperativeSlotsValid(int sessionId)
    {
        List<TaskRun> sessionRuns = GetCooperativeSessionRuns(sessionId);
        sessionRuns.RemoveAll(run => run.Kind != TaskRunKind.Normal);
        if (sessionRuns.Count == 0 || RoleManager.Instance == null)
            return false;

        TaskDefinition definition = AvailableTasks?.FirstOrDefault(task =>
            task != null && task.TaskID == sessionRuns[0].TaskID.ToString().Trim());
        int required = definition != null ? definition.RequiredVillagers : sessionRuns.Count;
        if (sessionRuns.Count != required)
            return false;

        HashSet<byte> roles = new HashSet<byte>();
        foreach (TaskRun participant in sessionRuns)
        {
            if (!roles.Add(participant.CooperativeRoleIndex) ||
                RoleManager.Instance.GetPlayerRole(participant.OwnerClientId) != PlayerRole.Villager ||
                !IsTaskOwnerAliveAndConnected(participant.OwnerClientId))
                return false;
        }

        return true;
    }

    private List<TaskRun> GetCooperativeSessionRuns(int sessionId)
    {
        List<TaskRun> sessionRuns = new List<TaskRun>();
        for (int i = 0; i < ActiveTaskRuns.Count; i++)
        {
            TaskRun run = ActiveTaskRuns[i];
            if (run.CooperativeSessionId == sessionId)
                sessionRuns.Add(run);
        }

        return sessionRuns;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportTaskCompletedRpc(string taskID, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        Debug.Log($"[TaskManager] ReportTaskCompletedRpc called by sender {senderClientId}, task '{taskID}'.");

        if (!IsTaskPhaseOpen() || !IsTaskOwnerAliveAndConnected(senderClientId) || RoleManager.Instance == null)
            return;

        int runIndex = GetTaskRunIndex(senderClientId, taskID);
        Debug.Log($"[TaskManager] runIndex = {runIndex}");
        if (runIndex >= 0)
        {
            var run = ActiveTaskRuns[runIndex];

            if (run.CooperativeSessionId > 0)
            {
                Debug.LogWarning($"[TaskManager] Cooperative completion rejected for '{taskID}'. The mission manager owns shared completion.");
                return;
            }

            if (!GameplayInteractionGate.IsQuickTestMode &&
                run.Kind == TaskRunKind.Normal &&
                RoleManager.Instance.GetPlayerRole(senderClientId) != PlayerRole.Villager)
                return;

            if (run.Kind == TaskRunKind.Rogue &&
                (!GameplayInteractionGate.IsQuickTestMode &&
                 (RoleManager.Instance.GetPlayerRole(senderClientId) != PlayerRole.Impostor ||
                  MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active ||
                  GetTerminalHackPhase(taskID) != TerminalHackPhase.Active)))
                return;

            if (run.Kind == TaskRunKind.Alibi && !GameplayInteractionGate.IsQuickTestMode)
                return;

            Debug.Log($"[TaskManager] Current state: {run.State}");
            // Completion can arrive in the same network tick as the start RPC.
            // The assignment and sender are already validated above, so promote
            // an Assigned/Reserved run instead of dropping a legitimate result.
            if (run.State == TaskRunState.Assigned ||
                run.State == TaskRunState.Reserved ||
                run.State == TaskRunState.InProgress)
            {
                if (run.State != TaskRunState.InProgress)
                    Debug.LogWarning($"[TaskManager] Completion arrived before start was applied for '{taskID}'. Promoting {run.State} to Completed.");

                run.State = TaskRunState.Completed;
                ActiveTaskRuns[runIndex] = run;
                Debug.Log($"[TaskManager] Task {taskID} marked as Completed!");

                if (run.Kind == TaskRunKind.Rogue)
                {
                    AwardKillerSabotagePoint(senderClientId);
                    CompleteHack(taskID);
                    CompletedRogueHackMask.Value = (byte)(CompletedRogueHackMask.Value | GetRogueHackBit(taskID));
                    // Rogue tasks never advance crew progress, but they are
                    // the killer's personal task loop and therefore award
                    // personal upgrade points at the same even thresholds as
                    // villager tasks.
                    UpgradeManager.Instance?.AwardTaskPoint(senderClientId);
                    Debug.Log($"[TaskManager] Rogue task completed by killer {senderClientId}; sabotage points: {KillerSabotagePoints.Value}, personal upgrade points awarded.");
                }
                else if (run.Kind == TaskRunKind.Normal)
                {
                    MissionManager.Instance?.CompleteNormalTaskServer(taskID);
                    CrewTaskProgress.Value++;
                    UpgradeManager.Instance?.AwardTaskPoint(senderClientId);
                    BeginHackPreparation(taskID);
                    RecordNormalTaskCompletion(senderClientId, taskID);
                    Debug.Log($"[TaskManager] Normal task completed by villager {senderClientId}; crew progress incremented to {CrewTaskProgress.Value}");
                    if (!GameplayInteractionGate.IsQuickTestMode)
                        CheckWinCondition();
                }
                else
                {
                    Debug.Log($"[TaskManager] Killer {senderClientId} completed alibi task '{taskID}'; no reward granted.");
                }

                // Remove and reassign a new task after a short delay (for now, instantly)
                ActiveTaskRuns.RemoveAt(runIndex);
                if (run.Kind == TaskRunKind.Normal)
                    AssignNormalTask(senderClientId);
            }
            else
            {
                Debug.LogWarning($"[TaskManager] Completion rejected: task '{taskID}' for sender {senderClientId} is {run.State}, not InProgress.");
            }
        }
        else
        {
            Debug.LogWarning($"[TaskManager] Completion rejected: no task '{taskID}' is assigned to sender {senderClientId}.");
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportBasketballTaskCompletedRpc(RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!IsTaskPhaseOpen() || !IsTaskOwnerAliveAndConnected(senderClientId) ||
            RoleManager.Instance == null || basketballRewardedPlayers.Contains(senderClientId))
            return;

        basketballRewardedPlayers.Add(senderClientId);

        int assignedRunIndex = GetTaskRunIndex(senderClientId, BasketballArcadeInteractable.TaskId);
        bool assignedNormalTask = assignedRunIndex >= 0 &&
                                  ActiveTaskRuns[assignedRunIndex].Kind == TaskRunKind.Normal &&
                                  ActiveTaskRuns[assignedRunIndex].CooperativeSessionId == 0 &&
                                  RoleManager.Instance.GetPlayerRole(senderClientId) == PlayerRole.Villager;

        UpgradeManager.Instance?.AwardTaskPoint(senderClientId);
        if (assignedNormalTask)
        {
            CrewTaskProgress.Value++;
            RecordNormalTaskCompletion(senderClientId, BasketballArcadeInteractable.TaskId);
            CheckWinCondition();
            ActiveTaskRuns.RemoveAt(assignedRunIndex);
            AssignNormalTask(senderClientId);
        }

        Debug.Log($"[TaskManager] Basketball challenge completed by {senderClientId}; personal reward granted once. Assigned task consumed: {assignedNormalTask}.");
    }
    
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StopTaskInteractionRpc(ulong clientId, string taskID, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != clientId ||
            !IsTaskPhaseOpen() ||
            !IsTaskOwnerAliveAndConnected(clientId))
            return;

        int runIndex = GetTaskRunIndex(clientId, taskID);
        if (runIndex >= 0)
        {
            var run = ActiveTaskRuns[runIndex];
            if (run.State == TaskRunState.InProgress)
            {
                run.State = TaskRunState.Reserved; // Kept reserved for them
                ActiveTaskRuns[runIndex] = run;
            }
        }
    }

    public TaskRun? GetActiveTaskForPlayer(ulong clientId)
    {
        TaskRun? fallback = null;
        for (int i = 0; i < ActiveTaskRuns.Count; i++)
        {
            TaskRun run = ActiveTaskRuns[i];
            if (run.OwnerClientId != clientId || run.State == TaskRunState.Completed || run.State == TaskRunState.Cancelled)
                continue;

            if (GameplayInteractionGate.IsQuickTestMode &&
                ((GameplayInteractionGate.IsQuickTestRogueTaskMode && run.Kind != TaskRunKind.Rogue) ||
                 (GameplayInteractionGate.IsQuickTestNormalTaskMode && run.Kind == TaskRunKind.Rogue)))
                continue;

            // A cooperative assignment is a priority overlay over the paused
            // personal task. Prefer it for HUD, interaction, and role checks.
            if (run.CooperativeSessionId > 0)
                return run;

            fallback ??= run;
        }

        return fallback;
    }

    public bool IsTerminalAvailable(string taskID, ulong requestedClientId)
    {
        if (MatchFlowManager.Instance == null ||
            (MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.BootProtection &&
             MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active))
        {
            return false;
        }

        bool hasCooperativeRun = false;
        bool requestedClientIsCooperativeParticipant = false;
        for (int i = 0; i < ActiveTaskRuns.Count; i++)
        {
            if (TaskIdsEqual(ActiveTaskRuns[i].TaskID, taskID))
            {
                if (ActiveTaskRuns[i].CooperativeSessionId > 0)
                {
                    hasCooperativeRun = true;
                    if (ActiveTaskRuns[i].OwnerClientId == requestedClientId)
                        requestedClientIsCooperativeParticipant = true;
                    continue;
                }

                if (ActiveTaskRuns[i].OwnerClientId != requestedClientId && 
                    (ActiveTaskRuns[i].State == TaskRunState.InProgress || ActiveTaskRuns[i].State == TaskRunState.Reserved))
                {
                    return false; // Reserved by someone else
                }
            }
        }

        // A cooperative terminal is a shared workstation. Every assigned,
        // living participant may open it even after another participant has
        // started the shared run; their own run state remains authoritative.
        if (hasCooperativeRun)
            return requestedClientIsCooperativeParticipant;

        return true;
    }

    public bool IsCooperativeRoleOwner(ulong clientId, string taskID, byte requiredRoleIndex)
    {
        TaskRun? run = GetActiveTaskForPlayer(clientId);
        return IsCooperativeTaskParticipant(clientId, taskID) &&
               run.HasValue &&
               run.Value.CooperativeRoleIndex == requiredRoleIndex;
    }

    public bool IsCooperativeTaskParticipant(ulong clientId, string taskID)
    {
        TaskRun? run = GetActiveTaskForPlayer(clientId);
        return run.HasValue &&
               run.Value.Kind == TaskRunKind.Normal &&
               run.Value.CooperativeSessionId > 0 &&
               TaskIdsEqual(run.Value.TaskID, taskID) &&
               RoleManager.Instance != null &&
               (GameplayInteractionGate.IsQuickTestMode ||
                RoleManager.Instance.GetPlayerRole(clientId) == PlayerRole.Villager) &&
               IsTaskOwnerAliveAndConnected(clientId);
    }

    private void CompleteQuickTestCooperativeTaskIfReady()
    {
        if (!GameplayInteractionGate.IsQuickTestMode ||
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
            return;

        ulong clientId = NetworkManager.Singleton.LocalClientId;
        TaskRun? activeRun = GetActiveTaskForPlayer(clientId);
        if (!activeRun.HasValue || activeRun.Value.State == TaskRunState.Completed ||
            activeRun.Value.State == TaskRunState.Cancelled)
            return;

        string taskID = activeRun.Value.TaskID.ToString().Trim();
        bool completed = taskID == "PressureTerminal" &&
                         MissionManager.Instance != null &&
                         MissionManager.Instance.IsPressureMissionCompleted.Value;
        if (!completed && taskID == "ReactorTerminal")
        {
            completed = ReactorMissionManager.Instance != null &&
                        ReactorMissionManager.Instance.IsMissionCompleted.Value;
        }

        if (!completed)
            return;

        int runIndex = GetTaskRunIndex(clientId, taskID);
        if (runIndex < 0)
            return;

        TaskRun run = ActiveTaskRuns[runIndex];
        run.State = TaskRunState.Completed;
        ActiveTaskRuns[runIndex] = run;
        CrewTaskProgress.Value++;
        UpgradeManager.Instance?.AwardTaskPoint(clientId);
        RecordNormalTaskCompletion(clientId, taskID);
        ActiveTaskRuns.RemoveAt(runIndex);
        AssignNormalTask(clientId);
        Debug.Log($"[TaskManager] Quick Test cooperative task '{taskID}' completed by {clientId}.");
    }

    public List<ulong> GetEligibleVillagerIds()
    {
        return GetEligibleVillagers();
    }

    public void AwardKillerSabotagePoint(ulong killerClientId)
    {
        if (!IsServer || RoleManager.Instance == null ||
            RoleManager.Instance.GetPlayerRole(killerClientId) != PlayerRole.Impostor)
            return;

        if (KillerSabotagePoints.Value >= MaxKillerSabotagePoints)
        {
            Debug.Log($"[TaskManager] Sabotage point rejected for killer {killerClientId}; cap reached ({MaxKillerSabotagePoints}).");
            return;
        }

        KillerSabotagePoints.Value++;
    }

    private void RecordNormalTaskCompletion(ulong clientId, string taskId)
    {
        if (!recentNormalTaskIds.TryGetValue(clientId, out Queue<string> recentTasks))
        {
            recentTasks = new Queue<string>();
            recentNormalTaskIds.Add(clientId, recentTasks);
        }

        recentTasks.Enqueue(taskId);
        while (recentTasks.Count > 2)
            recentTasks.Dequeue();

    }

    private int GetTaskRunIndex(ulong clientId, string taskID)
    {
        if (string.IsNullOrWhiteSpace(taskID)) return -1;

        for (int i = 0; i < ActiveTaskRuns.Count; i++)
        {
            TaskRun run = ActiveTaskRuns[i];
            if (run.OwnerClientId == clientId &&
                string.Equals(run.TaskID.ToString().Trim(), taskID.Trim(), StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private void CheckWinCondition()
    {
        MatchFlowManager.Instance?.CheckWinConditions();
    }
}
