#define USE_STEAM
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Acil toplantı butonunu temsil eder (sahnede yerleşik NetworkBehaviour).
/// Hem tetikleyici hem de tüm RPC trafiğinin taşıyıcısı olarak çalışır.
/// MeetingManager (MonoBehaviour) işin mantığını halleder, burası ağı halleder.
/// </summary>
public class MeetingTrigger : NetworkBehaviour
{
    public static MeetingTrigger Singleton { get; private set; }
    public float interactionRange = 3f;

    private bool isPlayerInRange = false;

    private void Awake()
    {
        if (Singleton != null && Singleton != this) return;
        Singleton = this;
    }

    private void Update()
    {
        if (MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive) return;

        FirstPersonController localPlayer = GetLocalPlayer();
        if (localPlayer == null || localPlayer.isDead.Value) return;

        float distance = Vector3.Distance(transform.position, localPlayer.transform.position);
        isPlayerInRange = distance <= interactionRange;

        if (isPlayerInRange && Input.GetKeyDown(KeyCode.F))
        {
            RequestMeetingServerRpc(NetworkManager.Singleton.LocalClientId);
        }
    }

    // ─── CLIENT → SERVER ───────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    public void RequestMeetingServerRpc(ulong callerId)
    {
        MeetingManager.Instance?.StartMeetingServer();
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitVoteServerRpc(ulong voterId, ulong targetId)
    {
        MeetingManager.Instance?.SubmitVoteServer(voterId, targetId);
    }

    // ─── SERVER → ALL CLIENTS ──────────────────────────────────────────────────

    [ClientRpc]
    public void OpenMeetingUIClientRpc()
    {
        if (MeetingUI.Instance != null) MeetingUI.Instance.OpenPanel();

        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var p in players)
        {
            if (!p.IsOwner) continue;
            p.playerCanMove = false;
            p.cameraCanMove = false;
            p.lockCursor = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    [ClientRpc]
    public void CloseMeetingUIClientRpc()
    {
        if (MeetingUI.Instance != null) MeetingUI.Instance.ClosePanel();

        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var p in players)
        {
            if (!p.IsOwner || p.isDead.Value) continue;
            p.playerCanMove = true;
            p.cameraCanMove = true;
            p.lockCursor = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    [ClientRpc]
    public void UpdateVoteStatusClientRpc(ulong voterId)
    {
        if (MeetingUI.Instance != null) MeetingUI.Instance.UpdatePlayerVoteStatus(voterId);
    }

    [ClientRpc]
    public void KillPlayerClientRpc(ulong targetId)
    {
        FirstPersonController p = GetPlayerById(targetId);
        if (p != null) p.Die();
    }

    [ClientRpc]
    public void TeleportPlayerClientRpc(ulong clientId, Vector3 position, Quaternion rotation)
    {
        FirstPersonController p = GetPlayerById(clientId);
        if (p == null) return;

        var rb = p.GetComponent<Rigidbody>();
        if (rb != null) rb.linearVelocity = Vector3.zero;
        p.transform.position = position;
        p.transform.rotation = rotation;
    }

    // ─── YARDIMCILAR ──────────────────────────────────────────────────────────

    private FirstPersonController GetLocalPlayer()
    {
        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var p in players) { if (p.IsOwner) return p; }
        return null;
    }

    private FirstPersonController GetPlayerById(ulong clientId)
    {
        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var p in players) { if (p.OwnerClientId == clientId) return p; }
        return null;
    }
}
