public static class GameplayInteractionGate
{
    public static bool IsTaskInteractionPhaseOpen()
    {
        MatchFlowManager flow = MatchFlowManager.Instance;
        if (flow == null)
            return true;

        MatchPhase phase = flow.CurrentPhase.Value;
        return phase == MatchPhase.BootProtection || phase == MatchPhase.Active;
    }
}
