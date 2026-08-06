using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Oyuncu rollerini yöneten ana sistem.
/// Oyun başladığında server rolleri dağıtır ve NetworkList ile tüm client'lara senkronize eder.
/// İleride oylama, sabotaj, kill gibi sistemler OnRoleAssigned / OnRolesDistributed eventlerini dinleyerek entegre olabilir.
/// </summary>
public enum PlayerRole : byte
{
    None = 0,
    Villager = 1,   // Köylü
    Impostor = 2,   // Katil
}

/// <summary>
/// NetworkList içinde tutulacak her bir oyuncunun rol bilgisi.
/// INetworkSerializable + IEquatable gerekli.
/// </summary>
public struct RoleEntry : INetworkSerializable, IEquatable<RoleEntry>
{
    public ulong ClientId;
    public PlayerRole Role;

    public RoleEntry(ulong clientId, PlayerRole role)
    {
        ClientId = clientId;
        Role = role;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Role);
    }

    public bool Equals(RoleEntry other)
    {
        return ClientId == other.ClientId && Role == other.Role;
    }

    public override bool Equals(object obj) => obj is RoleEntry other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ClientId, Role);
}

public class RoleManager : NetworkBehaviour
{
    public static RoleManager Instance { get; private set; }

    /// <summary>
    /// Tüm oyuncuların rolleri. Server yazabilir, herkes okuyabilir.
    /// </summary>
    private NetworkList<RoleEntry> roleEntries;

    /// <summary>
    /// Belirli bir oyuncuya rol atandığında tetiklenir. (clientId, role)
    /// </summary>
    public event Action<ulong, PlayerRole> OnRoleAssigned;

    /// <summary>
    /// Tüm roller dağıtıldığında tetiklenir.
    /// </summary>
    public event Action OnRolesDistributed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // NetworkList, Awake'te initialize edilmeli
        roleEntries = new NetworkList<RoleEntry>(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server
        );
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Client'lar liste değişikliklerini dinlesin
        roleEntries.OnListChanged += OnRoleListChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        roleEntries.OnListChanged -= OnRoleListChanged;
    }

    private void OnRoleListChanged(NetworkListEvent<RoleEntry> changeEvent)
    {
        if (changeEvent.Type == NetworkListEvent<RoleEntry>.EventType.Add)
        {
            var entry = changeEvent.Value;
            OnRoleAssigned?.Invoke(entry.ClientId, entry.Role);
            Debug.Log($"[RoleManager] Client {entry.ClientId} -> {entry.Role}");
        }
    }

    // ─── Public API ───

    /// <summary>
    /// Oyuncu sayısına göre kaç katil olacağını hesaplar.
    /// 8 ve altı: 1 katil, 9 ve üzeri: 2 katil.
    /// </summary>
    public int GetImpostorCount()
    {
        int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
        return playerCount >= 9 ? 2 : 1;
    }

    /// <summary>
    /// Belirli bir oyuncunun rolünü döndürür.
    /// </summary>
    public PlayerRole GetPlayerRole(ulong clientId)
    {
        for (int i = 0; i < roleEntries.Count; i++)
        {
            if (roleEntries[i].ClientId == clientId)
                return roleEntries[i].Role;
        }
        return PlayerRole.None;
    }

    /// <summary>
    /// Lokal oyuncunun rolünü döndürür.
    /// </summary>
    public PlayerRole GetLocalPlayerRole()
    {
        if (NetworkManager.Singleton == null) return PlayerRole.None;
        return GetPlayerRole(NetworkManager.Singleton.LocalClientId);
    }

    /// <summary>
    /// Lokal oyuncu katil mi?
    /// </summary>
    public bool IsLocalPlayerImpostor()
    {
        return GetLocalPlayerRole() == PlayerRole.Impostor;
    }

    /// <summary>
    /// Tüm katillerin clientId listesini döndürür.
    /// </summary>
    public List<ulong> GetImpostors()
    {
        var impostors = new List<ulong>();
        for (int i = 0; i < roleEntries.Count; i++)
        {
            if (roleEntries[i].Role == PlayerRole.Impostor)
                impostors.Add(roleEntries[i].ClientId);
        }
        return impostors;
    }

    /// <summary>
    /// Roller zaten dağıtıldı mı?
    /// </summary>
    public bool AreRolesDistributed()
    {
        return roleEntries.Count > 0;
    }

    // ─── Server: Rol Dağıtımı ───

    [ServerRpc(RequireOwnership = false)]
    public void DistributeRolesServerRpc()
    {
        if (!IsServer) return;
        if (roleEntries.Count > 0)
        {
            Debug.Log("[RoleManager] Roller zaten dağıtılmış, tekrar dağıtılmıyor.");
            return;
        }

        var connectedClients = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        int playerCount = connectedClients.Count;
        int impostorCount = playerCount >= 9 ? 2 : 1;

        // Listeyi karıştır (Fisher-Yates shuffle)
        for (int i = connectedClients.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (connectedClients[i], connectedClients[j]) = (connectedClients[j], connectedClients[i]);
        }

        // İlk N kişi katil, geri kalanlar köylü
        for (int i = 0; i < connectedClients.Count; i++)
        {
            PlayerRole role = i < impostorCount ? PlayerRole.Impostor : PlayerRole.Villager;
            roleEntries.Add(new RoleEntry(connectedClients[i], role));
        }

        Debug.Log($"[RoleManager] Roller dağıtıldı! {playerCount} oyuncu, {impostorCount} katil.");
        OnRolesDistributed?.Invoke();
    }
}
