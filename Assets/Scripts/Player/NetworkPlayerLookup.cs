using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Resolves spawned players through Netcode's client table first. The scene scan
/// is kept only as a safe fallback for quick tests and editor-only transitions.
/// </summary>
public static class NetworkPlayerLookup
{
    public static FirstPersonController Find(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
            client.PlayerObject != null)
        {
            FirstPersonController networkPlayer =
                client.PlayerObject.GetComponent<FirstPersonController>();
            if (networkPlayer != null)
                return networkPlayer;
        }

        foreach (FirstPersonController player in
                 Object.FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (player != null && player.OwnerClientId == clientId)
                return player;
        }

        return null;
    }
}
