public static class GameplayInteractionGate
{
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
