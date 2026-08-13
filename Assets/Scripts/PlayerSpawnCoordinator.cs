using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class PlayerSpawnCoordinator : MonoBehaviour
{
    private const int ExpectedSpawnPointCount = 8;
    private const float SharedPointOffset = 0.8f;
    private const float SpawnClearance = 0.06f;
    private static readonly string[] SpawnPointNames =
    {
        "spawn1", "spawn2", "spawn3", "spawn4",
        "spawn5", "spawn6", "spawn7", "spawn8"
    };

    private readonly List<Transform> spawnPoints = new();
    private readonly Dictionary<ulong, int> assignedSlots = new();
    private readonly Dictionary<ulong, int> placedSlots = new();
    private bool shuffleInitialized;
    private bool callbacksRegistered;

    private void Start()
    {
        CacheAndShuffleSpawnPoints();
        TryRegisterServerCallbacks();

        if (callbacksRegistered)
            RequestDistribution();
    }

    private void Update()
    {
        if (callbacksRegistered && !IsServerReady())
            UnregisterServerCallbacks();

        bool wasRegistered = callbacksRegistered;
        TryRegisterServerCallbacks();

        if (!wasRegistered && callbacksRegistered)
            RequestDistribution();
    }

    private void OnDestroy()
    {
        UnregisterServerCallbacks();
    }

    public void RequestDistribution(bool repositionExistingPlayers = false)
    {
        TryRegisterServerCallbacks();

        if (!IsServerReady())
            return;

        CacheAndShuffleSpawnPoints();

        if (spawnPoints.Count == 0)
        {
            Debug.LogError("[PlayerSpawnCoordinator] Geçerli spawn noktası bulunamadı.");
            return;
        }

        var connectedClientIds = NetworkManager.Singleton.ConnectedClientsIds
            .OrderBy(clientId => clientId)
            .ToList();

        RemoveDisconnectedAssignments(connectedClientIds);
        var slotsToPlace = new HashSet<int>();
        if (repositionExistingPlayers)
        {
            foreach (int slot in assignedSlots.Values)
                slotsToPlace.Add(slot);
        }

        AssignMissingClients(connectedClientIds);
        foreach (ulong clientId in connectedClientIds)
        {
            if (!assignedSlots.TryGetValue(clientId, out int slot))
                continue;

            if (!placedSlots.TryGetValue(clientId, out int placedSlot) || placedSlot != slot)
                slotsToPlace.Add(slot);
        }

        PlaceAssignedPlayers(connectedClientIds, slotsToPlace);
    }

    private void OnClientConnected(ulong clientId)
    {
        StartCoroutine(DistributeWhenPlayerExists(clientId));
    }

    private void OnClientDisconnected(ulong clientId)
    {
        assignedSlots.Remove(clientId);
        placedSlots.Remove(clientId);
        StartCoroutine(DistributeAfterDisconnect());
    }

    private IEnumerator DistributeAfterDisconnect()
    {
        // Netcode updates ConnectedClientsIds after invoking the disconnect callback.
        yield return null;
        RequestDistribution();
    }

    private void TryRegisterServerCallbacks()
    {
        if (callbacksRegistered || !IsServerReady())
            return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        callbacksRegistered = true;
    }

    private void UnregisterServerCallbacks()
    {
        if (NetworkManager.Singleton == null || !callbacksRegistered)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        callbacksRegistered = false;
    }

    private IEnumerator DistributeWhenPlayerExists(ulong clientId)
    {
        const float timeoutSeconds = 5f;
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (TryGetPlayer(clientId, out _))
            {
                RequestDistribution();
                yield break;
            }

            yield return null;
        }

        RequestDistribution();
    }

    private void CacheAndShuffleSpawnPoints()
    {
        if (shuffleInitialized)
            return;

        spawnPoints.Clear();

        foreach (string pointName in SpawnPointNames)
        {
            Transform point = transform.Find(pointName);
            if (point == null)
            {
                Debug.LogError($"[PlayerSpawnCoordinator] SpawnPoints altında '{pointName}' bulunamadı.");
                continue;
            }

            spawnPoints.Add(point);
        }

        if (spawnPoints.Count < ExpectedSpawnPointCount)
        {
            Debug.LogError($"[PlayerSpawnCoordinator] {ExpectedSpawnPointCount} spawn noktası bekleniyordu, {spawnPoints.Count} geçerli nokta bulundu.");
        }

        var rng = new System.Random(Environment.TickCount);
        for (int i = spawnPoints.Count - 1; i > 0; i--)
        {
            int swapIndex = rng.Next(i + 1);
            (spawnPoints[i], spawnPoints[swapIndex]) = (spawnPoints[swapIndex], spawnPoints[i]);
        }

        shuffleInitialized = true;
    }

    private void RemoveDisconnectedAssignments(List<ulong> connectedClientIds)
    {
        var connectedSet = new HashSet<ulong>(connectedClientIds);
        var staleClientIds = assignedSlots.Keys
            .Where(clientId => !connectedSet.Contains(clientId))
            .ToList();

        foreach (ulong clientId in staleClientIds)
        {
            assignedSlots.Remove(clientId);
            placedSlots.Remove(clientId);
        }
    }

    private void AssignMissingClients(List<ulong> connectedClientIds)
    {
        var occupancyBySlot = new int[spawnPoints.Count];
        foreach (int slot in assignedSlots.Values)
        {
            if (slot >= 0 && slot < occupancyBySlot.Length)
                occupancyBySlot[slot]++;
        }

        foreach (ulong clientId in connectedClientIds)
        {
            if (assignedSlots.ContainsKey(clientId))
                continue;

            int selectedSlot = 0;
            for (int slot = 1; slot < occupancyBySlot.Length; slot++)
            {
                if (occupancyBySlot[slot] < occupancyBySlot[selectedSlot])
                    selectedSlot = slot;
            }

            assignedSlots[clientId] = selectedSlot;
            occupancyBySlot[selectedSlot]++;
        }
    }

    private void PlaceAssignedPlayers(List<ulong> connectedClientIds, HashSet<int> slotsToPlace)
    {
        var clientsBySlot = connectedClientIds
            .Where(clientId => assignedSlots.ContainsKey(clientId))
            .GroupBy(clientId => assignedSlots[clientId]);

        foreach (var slotGroup in clientsBySlot)
        {
            int slot = slotGroup.Key;
            if (slot < 0 || slot >= spawnPoints.Count)
                continue;

            if (!slotsToPlace.Contains(slot))
                continue;

            Transform spawnPoint = spawnPoints[slot];
            var slotClientIds = slotGroup
                .Where(clientId => TryGetPlayer(clientId, out FirstPersonController player) &&
                                   !player.isDead.Value)
                .OrderBy(clientId => clientId)
                .ToList();

            for (int i = 0; i < slotClientIds.Count; i++)
            {
                if (!TryGetPlayer(slotClientIds[i], out FirstPersonController player))
                    continue;

                Vector3 position = GetSpawnPosition(spawnPoint, player);
                position += spawnPoint.right * GetOffsetForSharedSlot(i, slotClientIds.Count);
                TeleportPlayer(player, position, spawnPoint.rotation);
                placedSlots[slotClientIds[i]] = slot;
            }
        }
    }

    private static float GetOffsetForSharedSlot(int index, int count)
    {
        if (count <= 1)
            return 0f;

        if (count == 2)
            return index == 0 ? -SharedPointOffset : SharedPointOffset;

        float center = (count - 1) * 0.5f;
        return (index - center) * SharedPointOffset;
    }

    private static Vector3 GetSpawnPosition(Transform spawnPoint, FirstPersonController player)
    {
        Vector3 position = spawnPoint.position;
        float surfaceY = spawnPoint.position.y;

        var padCollider = spawnPoint.GetComponent<BoxCollider>();
        if (padCollider != null)
        {
            surfaceY = padCollider.bounds.max.y;
        }
        else
        {
            position += Vector3.up * 1.8f;
            return position;
        }

        float playerBottomOffset = GetPlayerBottomOffset(player);
        position.y = surfaceY + SpawnClearance - playerBottomOffset;
        return position;
    }

    private static float GetPlayerBottomOffset(FirstPersonController player)
    {
        var capsule = player.GetComponent<CapsuleCollider>();
        if (capsule == null)
            return -1.8f;

        Vector3 scale = player.transform.lossyScale;
        float scaledHeight = capsule.height * Mathf.Abs(scale.y);
        float scaledRadius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float halfHeight = Mathf.Max(scaledHeight * 0.5f, scaledRadius);
        return capsule.center.y * scale.y - halfHeight;
    }

    public static void TeleportPlayer(FirstPersonController player, Vector3 position, Quaternion rotation)
    {
        var rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Host için (veya sunucu yetkisi varsa) lokal olarak taşı, ardından Client'lara bildir (kendisi dahil)
        player.transform.SetPositionAndRotation(position, rotation);
        
        // Bu NetworkVariable, istemci tamamen yüklendiğinde pozisyonu bir kez zorlayacaktır.
        if (player.IsServer)
        {
            player.serverSpawnPosition.Value = position;
            player.serverSpawnRotation.Value = rotation;
            player.hasServerSpawnPosition.Value = true;
            player.serverSpawnRevision.Value++;
            if (player.IsSpawned)
                player.TeleportClientRpc(position, rotation);
        }
    }

    private static bool TryGetPlayer(ulong clientId, out FirstPersonController player)
    {
        player = null;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
            client.PlayerObject != null)
        {
            player = client.PlayerObject.GetComponent<FirstPersonController>();
            if (player != null)
                return true;
        }

        foreach (FirstPersonController candidate in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (candidate.OwnerClientId == clientId)
            {
                player = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsServerReady()
    {
        return NetworkManager.Singleton != null &&
               NetworkManager.Singleton.IsListening &&
               NetworkManager.Singleton.IsServer;
    }
}
