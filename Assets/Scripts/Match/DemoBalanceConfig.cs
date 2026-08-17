using UnityEngine;

/// <summary>
/// Single source of truth for demo balance values that are shared by the
/// server-authoritative match, task, meeting, and upgrade systems.
///
/// The runtime falls back to the documented defaults when the Resources asset
/// is unavailable. This keeps Quick Test and development scenes safe while
/// still allowing future balance passes to change one asset instead of
/// hunting through several networked systems.
/// </summary>
[CreateAssetMenu(menuName = "Project 314/Balance Configuration", fileName = "DemoBalanceConfig")]
public sealed class DemoBalanceConfig : ScriptableObject
{
    [Header("Match phases")]
    [Min(0f)] public float bootProtectionSeconds = 30f;
    [Min(0f)] public float firstEmergencyLockSeconds = 40f;
    [Min(0f)] public float postMeetingLockSeconds = 5f;

    [Header("Meetings")]
    [Min(0f)] public float meetingDiscussionSeconds = 15f;
    [Min(0f)] public float meetingVotingSeconds = 35f;
    [Min(0f)] public float meetingResultsSeconds = 10f;
    [Min(0f)] public float emergencyCooldownSeconds = 40f;

    [Header("Kill and task economy")]
    [Min(0f)] public float firstKillLockSeconds = 60f;
    [Min(0f)] public float baseKillCooldownSeconds = 60f;
    [Min(0f)] public float killCooldownReductionPerUpgradeSeconds = 10f;
    [Min(0f)] public float minimumKillCooldownSeconds = 40f;
    [Min(0f)] public float baseKillRangeMeters = 4f;
    [Min(0f)] public int crewTaskRunsPerVillager = 3;
    [Min(0f)] public float terminalHackPreparationSeconds = 15f;
    [Min(0f)] public float terminalHackCooldownSeconds = 60f;
    [Min(0f)] public float normalTerminalCooldownMinSeconds = 45f;
    [Min(0f)] public float normalTerminalCooldownMaxSeconds = 75f;

    [Header("Killer tools")]
    [Min(0f)] public float valveOverrideSeconds = 30f;
    [Min(0f)] public float systemBlackoutSeconds = 15f;
    [Min(0f)] public float identityScrambleSeconds = 30f;
    [Min(0f)] public float threatSensorRangeMeters = 12f;

    [Header("Bounded sabotage economy")]
    [Min(0)] public int maxKillerSabotagePoints = 2;

    private static DemoBalanceConfig runtime;

    private static DemoBalanceConfig Runtime
    {
        get
        {
            if (runtime == null)
                runtime = Resources.Load<DemoBalanceConfig>("Config/DemoBalanceConfig");

            return runtime;
        }
    }

    public static float BootProtectionSeconds => Runtime != null ? Mathf.Max(0f, Runtime.bootProtectionSeconds) : 30f;
    public static float FirstEmergencyLockSeconds => Runtime != null ? Mathf.Max(0f, Runtime.firstEmergencyLockSeconds) : 40f;
    public static float PostMeetingLockSeconds => Runtime != null ? Mathf.Max(0f, Runtime.postMeetingLockSeconds) : 5f;
    public static float MeetingDiscussionSeconds => Runtime != null ? Mathf.Max(0f, Runtime.meetingDiscussionSeconds) : 15f;
    public static float MeetingVotingSeconds => Runtime != null ? Mathf.Max(0f, Runtime.meetingVotingSeconds) : 35f;
    public static float MeetingResultsSeconds => Runtime != null ? Mathf.Max(0f, Runtime.meetingResultsSeconds) : 10f;
    public static float EmergencyCooldownSeconds => Runtime != null ? Mathf.Max(0f, Runtime.emergencyCooldownSeconds) : 40f;
    public static float FirstKillLockSeconds => Runtime != null ? Mathf.Max(0f, Runtime.firstKillLockSeconds) : 60f;
    public static float BaseKillCooldownSeconds => Runtime != null ? Mathf.Max(0f, Runtime.baseKillCooldownSeconds) : 60f;
    public static float KillCooldownReductionPerUpgradeSeconds => Runtime != null ? Mathf.Max(0f, Runtime.killCooldownReductionPerUpgradeSeconds) : 10f;
    public static float MinimumKillCooldownSeconds => Runtime != null ? Mathf.Max(0f, Runtime.minimumKillCooldownSeconds) : 40f;
    public static float BaseKillRangeMeters => Runtime != null ? Mathf.Max(0f, Runtime.baseKillRangeMeters) : 4f;
    public static int CrewTaskRunsPerVillager => Runtime != null ? Mathf.Max(0, Runtime.crewTaskRunsPerVillager) : 3;
    public static float TerminalHackPreparationSeconds => Runtime != null ? Mathf.Max(0f, Runtime.terminalHackPreparationSeconds) : 15f;
    public static float TerminalHackCooldownSeconds => Runtime != null ? Mathf.Max(0f, Runtime.terminalHackCooldownSeconds) : 60f;
    public static float NormalTerminalCooldownMinSeconds => Runtime != null ? Mathf.Max(0f, Runtime.normalTerminalCooldownMinSeconds) : 45f;
    public static float NormalTerminalCooldownMaxSeconds => Mathf.Max(
        NormalTerminalCooldownMinSeconds,
        Runtime != null ? Mathf.Max(0f, Runtime.normalTerminalCooldownMaxSeconds) : 75f);
    public static float ValveOverrideSeconds => Runtime != null ? Mathf.Max(0f, Runtime.valveOverrideSeconds) : 30f;
    public static float SystemBlackoutSeconds => Runtime != null ? Mathf.Max(0f, Runtime.systemBlackoutSeconds) : 15f;
    public static float IdentityScrambleSeconds => Runtime != null ? Mathf.Max(0f, Runtime.identityScrambleSeconds) : 30f;
    public static float ThreatSensorRangeMeters => Runtime != null ? Mathf.Max(0f, Runtime.threatSensorRangeMeters) : 12f;
    public static int MaxKillerSabotagePoints => Runtime != null ? Mathf.Max(0, Runtime.maxKillerSabotagePoints) : 2;

    public static float GetNormalTerminalCooldownSeconds()
    {
        return Random.Range(NormalTerminalCooldownMinSeconds, NormalTerminalCooldownMaxSeconds);
    }

}
