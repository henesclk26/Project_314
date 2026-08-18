using UnityEngine;

public class ComputerInteractable : MonoBehaviour
{
    public ComputerData data;
    public float interactionRange = 3f;

    private bool isInRange = false;

    private void Update()
    {
        if (data == null) return;

        FirstPersonController ownerFpc = GetOwnerFpc();
        if (ownerFpc == null || ownerFpc.isDead.Value) 
        {
            SetInRange(false);
            return;
        }

        if (!GameplayInteractionGate.IsTaskInteractionPhaseOpen())
        {
            ComputerUIManager.Instance?.SetPromptVisible(false);
            return;
        }

        float distance = Vector3.Distance(transform.position, ownerFpc.transform.position);
        if (distance <= interactionRange)
        {
            isInRange = true;
            if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsSystemBlackoutBlocking(ownerFpc.OwnerClientId))
            {
                ComputerUIManager.Instance?.SetPromptVisible(false);
                ownerFpc.SetInteractionText("SYSTEM OFFLINE");
                return;
            }
            if (TaskManager.Instance == null) return;
            
            bool isAvailable = TaskManager.Instance.IsTerminalAvailable("MissionComputer", ownerFpc.OwnerClientId);
            bool isKiller = GameplayInteractionGate.IsQuickTestMode ||
                            (RoleManager.Instance != null &&
                             RoleManager.Instance.GetPlayerRole(ownerFpc.OwnerClientId) == PlayerRole.Impostor);
            var activeTask = TaskManager.Instance.GetActiveTaskForPlayer(ownerFpc.OwnerClientId);
            bool hasTask = activeTask.HasValue && activeTask.Value.TaskID.ToString() == "MissionComputer";
            bool canUseRogueTask = isKiller &&
                                   TaskManager.Instance.CanUseRogueTask(ownerFpc.OwnerClientId, "MissionComputer");
            bool isHackPreparing = isKiller &&
                                   TaskManager.Instance.GetTerminalHackPhase("MissionComputer") == TerminalHackPhase.Preparing;
            bool canUseNormalAlibi = TaskManager.Instance.CanUseAlibiTask(ownerFpc.OwnerClientId, "MissionComputer");

            if (ComputerUIManager.Instance != null)
            {
                if (!isAvailable)
                {
                    // For busy/offline, we could just hide the prompt or show busy. The existing prompt is a generic crosshair text or UI text?
                    ComputerUIManager.Instance.SetPromptVisible(false);
                    ownerFpc.SetInteractionText("SYSTEM BUSY / OFFLINE");
                }
                else if (hasTask || canUseNormalAlibi ||
                         (canUseRogueTask && !isHackPreparing))
                {
                    bool showKillerPrompt = canUseRogueTask && !isHackPreparing;
                    ComputerUIManager.Instance.SetPromptVisible(!showKillerPrompt);
                    ownerFpc.SetInteractionText(showKillerPrompt
                        ? "[F] TERMINALI HACKLE"
                        : string.Empty);
                    if (Input.GetKeyDown(KeyCode.F) && !ComputerUIManager.Instance.IsComputerOpen)
                    {
                        if (hasTask || canUseNormalAlibi ||
                            (canUseRogueTask && !isHackPreparing))
                            TaskManager.Instance.RequestStartTaskRpc("MissionComputer");
                        ComputerUIManager.Instance.OpenComputer(data, ownerFpc, this);
                    }
                }
                else if (isHackPreparing)
                {
                    ComputerUIManager.Instance.SetPromptVisible(false);
                    ownerFpc.SetInteractionText("TERMINAL HACK HAZIRLANIYOR");
                }
                else
                {
                    ComputerUIManager.Instance.SetPromptVisible(false);
                    ownerFpc.SetInteractionText(string.Empty);
                }
            }
        }
        else
        {
            SetInRange(false);
        }
    }

    private void SetInRange(bool rangeStatus)
    {
        if (isInRange != rangeStatus)
        {
            isInRange = rangeStatus;
            if (ComputerUIManager.Instance != null)
            {
                ComputerUIManager.Instance.SetPromptVisible(isInRange);
            }
        }
    }

    private void OnDisable()
    {
        SetInRange(false);
    }

    private FirstPersonController GetOwnerFpc()
    {
        var allFpcs = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            foreach (var f in allFpcs)
            {
                if (f.IsOwner) return f;
            }

            return null;
        }
        if (allFpcs.Length > 0) return allFpcs[0];
        return null;
    }
}
