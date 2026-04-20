using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Toplantı mantığını yöneten singleton.
/// NetworkObject gerektirmez — sahne yüklendiğinde normal MonoBehaviour olarak çalışır.
/// RPC'ler MeetingTrigger (NetworkBehaviour) üzerinden gelir.
/// </summary>
public class MeetingManager : MonoBehaviour
{
    public static MeetingManager Instance { get; private set; }

    public bool IsMeetingActive { get; private set; }
    public float MeetingTimer { get; private set; }
    public float meetingDuration = 60f;

    // voterId -> votedForId (ulong.MaxValue = Skip)
    private Dictionary<ulong, ulong> currentVotes = new Dictionary<ulong, ulong>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        // Sunucu: geri sayım ve oylama / bitiş
        if (nm.IsServer)
        {
            if (!IsMeetingActive) return;

            MeetingTimer -= Time.deltaTime;

            if (MeetingTimer <= 0)
                EndMeeting();
            else
                CheckIfAllVoted();
            return;
        }

        // Saf istemci: MeetingTimer sadece sunucuda güncelleniyordu; UI için yerel geri sayım
        if (nm.IsClient && IsMeetingActive)
        {
            MeetingTimer -= Time.deltaTime;
            if (MeetingTimer < 0f) MeetingTimer = 0f;
        }
    }

    /// <summary>
    /// OpenMeetingUIClientRpc tüm makinelerde çalışır; host zaten StartMeetingServer'da ayarladı.
    /// Saf istemcilerde IsMeetingActive ve süre burada dolar ki TimerText güncellensin.
    /// </summary>
    public void OnMeetingUIOpenedOnClient()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer) return;
        IsMeetingActive = true;
        MeetingTimer = meetingDuration;
    }

    public void OnMeetingUIClosedOnClient()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer) return;
        IsMeetingActive = false;
        MeetingTimer = 0f;
    }

    // ─── Sunucudan çağrılır (MeetingTrigger'ın ServerRpc'si buraya yönlendirir) ───
    public void StartMeetingServer()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (IsMeetingActive) return;

        IsMeetingActive = true;
        MeetingTimer = meetingDuration;
        currentVotes.Clear();

        // Tüm istemcilere UI aç komutu gönder
        MeetingTrigger.Singleton?.OpenMeetingUIClientRpc();
    }

    public void SubmitVoteServer(ulong voterId, ulong targetId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (!IsMeetingActive) return;

        FirstPersonController voter = GetPlayerById(voterId);
        if (voter == null || voter.isDead.Value) return;

        if (targetId != ulong.MaxValue)
        {
            FirstPersonController target = GetPlayerById(targetId);
            if (target == null || target.isDead.Value) return;
        }

        currentVotes[voterId] = targetId;
        MeetingTrigger.Singleton?.UpdateVoteStatusClientRpc(voterId);
    }

    private void CheckIfAllVoted()
    {
        int aliveCount = 0;
        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var p in players) { if (!p.isDead.Value) aliveCount++; }

        if (currentVotes.Count >= aliveCount && aliveCount > 0)
            EndMeeting();
    }

    private void EndMeeting()
    {
        IsMeetingActive = false;

        // Oylama sonucu
        ulong eliminatedPlayerId = ulong.MaxValue;
        int maxVotes = 0;
        bool isTie = false;

        Dictionary<ulong, int> voteCounts = new Dictionary<ulong, int>();
        foreach (var vote in currentVotes.Values)
        {
            if (vote == ulong.MaxValue) continue;
            if (!voteCounts.ContainsKey(vote)) voteCounts[vote] = 0;
            voteCounts[vote]++;

            if (voteCounts[vote] > maxVotes)
            {
                maxVotes = voteCounts[vote];
                eliminatedPlayerId = vote;
                isTie = false;
            }
            else if (voteCounts[vote] == maxVotes)
            {
                isTie = true;
            }
        }

        if (!isTie && eliminatedPlayerId != ulong.MaxValue)
        {
            FirstPersonController eliminated = GetPlayerById(eliminatedPlayerId);
            if (eliminated != null && !eliminated.isDead.Value)
            {
                eliminated.deathCause.Value = FirstPersonController.PlayerDeathCause.Ejected;
                eliminated.corpseHidden.Value = true;
                eliminated.isDead.Value = true;
                MeetingTrigger.Singleton?.KillPlayerClientRpc(eliminatedPlayerId);
            }
        }

        MeetingTrigger.Singleton?.CloseMeetingUIClientRpc();
        RespawnAllPlayers();
    }

    private void RespawnAllPlayers()
    {
        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("Respawn");
        if (spawnPoints == null || spawnPoints.Length == 0) return;

        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var p in players)
        {
            // Ölü oyuncuları teleport etme — ölüler spectator modunda kalacak
            if (p.isDead.Value) continue;

            int spawnIndex = (int)(p.OwnerClientId % (ulong)spawnPoints.Length);
            Transform pt = spawnPoints[spawnIndex].transform;
            MeetingTrigger.Singleton?.TeleportPlayerClientRpc(p.OwnerClientId, pt.position, pt.rotation);
        }
    }

    private FirstPersonController GetPlayerById(ulong clientId)
    {
        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var p in players) { if (p.OwnerClientId == clientId) return p; }
        return null;
    }
}
