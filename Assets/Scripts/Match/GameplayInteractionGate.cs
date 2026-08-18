public static class GameplayInteractionGate
{
    private static bool quickTestMode;
    private static bool quickTestRogueTaskMode;

    public static bool IsQuickTestMode => quickTestMode;
    public static bool IsQuickTestRogueTaskMode =>
        quickTestMode && quickTestRogueTaskMode;
    public static bool IsQuickTestNormalTaskMode =>
        quickTestMode && !quickTestRogueTaskMode;

    /// <summary>
    /// Quick Test is an explicit local development mode. Production lobby
    /// matches never infer this mode from missing lobby data.
    /// </summary>
    public static void SetQuickTestMode(bool enabled)
    {
        quickTestMode = enabled;
        if (!enabled)
            quickTestRogueTaskMode = false;
    }

    /// <summary>
    /// In Quick Test only, F1 switches the local task content between the
    /// normal villager task set and the killer hack task set.
    /// </summary>
    public static void ProcessQuickTestInput()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!quickTestMode || !UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F1))
            return;

        quickTestRogueTaskMode = !quickTestRogueTaskMode;
        UnityEngine.Debug.Log(
            $"[QuickTest] Task mode: {(quickTestRogueTaskMode ? "KILLER HACKS" : "VILLAGER TASKS")}. " +
            "Press F1 to switch.");
#endif
    }

    public static bool IsTaskInteractionPhaseOpen()
    {
        MatchFlowManager flow = MatchFlowManager.Instance;
        if (flow == null)
        {
            // Quick Test and other offline iteration flows do not have a
            // networked match phase. An online session without its authoritative
            // phase owner must fail closed instead of accepting input.
            return Unity.Netcode.NetworkManager.Singleton == null ||
                   !Unity.Netcode.NetworkManager.Singleton.IsListening;
        }

        MatchPhase phase = flow.CurrentPhase.Value;
        return phase == MatchPhase.BootProtection || phase == MatchPhase.Active;
    }
}
