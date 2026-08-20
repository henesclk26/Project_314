using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Resolves the local player without scanning every FirstPersonController on every frame.
/// The refresh window also covers spawn/despawn and ownership changes during lobby transitions.
/// </summary>
public static class LocalPlayerResolver
{
    private const float RefreshIntervalSeconds = 0.25f;

    private static FirstPersonController cachedPlayer;
    private static float nextRefreshTime;

    public static FirstPersonController Get()
    {
        if (cachedPlayer != null && cachedPlayer.isActiveAndEnabled &&
            Time.unscaledTime < nextRefreshTime)
        {
            return cachedPlayer;
        }

        nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
        cachedPlayer = null;

        FirstPersonController[] players =
            Object.FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        bool isNetworkSession = NetworkManager.Singleton != null &&
                                NetworkManager.Singleton.IsListening;

        if (isNetworkSession)
        {
            foreach (FirstPersonController player in players)
            {
                if (player != null && player.isActiveAndEnabled && player.IsOwner)
                {
                    cachedPlayer = player;
                    return cachedPlayer;
                }
            }

            return null;
        }

        foreach (FirstPersonController player in players)
        {
            if (player != null && player.isActiveAndEnabled)
            {
                cachedPlayer = player;
                return cachedPlayer;
            }
        }

        return null;
    }

    public static void Invalidate()
    {
        cachedPlayer = null;
        nextRefreshTime = 0f;
    }
}
