using Unity.Netcode;
using System;

public enum UpgradeSelectionKind : byte
{
    None = 0,
    Passive = 1,
    Tool = 2,
    Upgrade = 3
}

public enum PassiveUpgradeId : byte
{
    None = 0,
    OverdriveServos = 1,
    ForensicCache = 2,
    ThreatSensor = 3,
    PursuitProtocol = 4,
    EscapeRoutine = 5,
    AmbushProtocol = 6
}

public enum ActiveToolId : byte
{
    None = 0,
    PriorityUplink = 1,
    IdentityAnchor = 2,
    ValveOverride = 3,
    SystemBlackout = 4,
    IdentityScramble = 5
}

public enum UpgradeCardId : byte
{
    None = 0,
    OverdriveServos = 1,
    ForensicCache = 2,
    ThreatSensor = 3,
    PursuitProtocol = 4,
    EscapeRoutine = 5,
    AmbushProtocol = 6,
    PriorityUplink = 7,
    IdentityAnchor = 8,
    ValveOverride = 9,
    SystemBlackout = 10,
    IdentityScramble = 11
}

public struct PlayerUpgradeState : INetworkSerializable, IEquatable<PlayerUpgradeState>
{
    public ulong ClientId;
    public int Points;
    public int SelectionCount;
    public UpgradeSelectionKind PendingSelection;
    public PassiveUpgradeId Passive;
    public ActiveToolId Tool;
    public bool ToolArmed;
    public bool ToolConsumed;

    public byte OverdriveServosCount;
    public byte ForensicCacheCount;
    public byte ThreatSensorCount;
    public byte PursuitProtocolCount;
    public byte EscapeRoutineCount;
    public byte AmbushProtocolCount;
    public byte PriorityUplinkCount;
    public byte IdentityAnchorCount;
    public byte ValveOverrideCount;
    public byte SystemBlackoutCount;
    public byte IdentityScrambleCount;
    public byte PriorityUplinkCharges;
    public byte IdentityAnchorCharges;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Points);
        serializer.SerializeValue(ref SelectionCount);
        serializer.SerializeValue(ref PendingSelection);
        serializer.SerializeValue(ref Passive);
        serializer.SerializeValue(ref Tool);
        serializer.SerializeValue(ref ToolArmed);
        serializer.SerializeValue(ref ToolConsumed);
        serializer.SerializeValue(ref OverdriveServosCount);
        serializer.SerializeValue(ref ForensicCacheCount);
        serializer.SerializeValue(ref ThreatSensorCount);
        serializer.SerializeValue(ref PursuitProtocolCount);
        serializer.SerializeValue(ref EscapeRoutineCount);
        serializer.SerializeValue(ref AmbushProtocolCount);
        serializer.SerializeValue(ref PriorityUplinkCount);
        serializer.SerializeValue(ref IdentityAnchorCount);
        serializer.SerializeValue(ref ValveOverrideCount);
        serializer.SerializeValue(ref SystemBlackoutCount);
        serializer.SerializeValue(ref IdentityScrambleCount);
        serializer.SerializeValue(ref PriorityUplinkCharges);
        serializer.SerializeValue(ref IdentityAnchorCharges);
    }

    public bool Equals(PlayerUpgradeState other)
    {
        return ClientId == other.ClientId && Points == other.Points &&
               SelectionCount == other.SelectionCount && PendingSelection == other.PendingSelection &&
               Passive == other.Passive && Tool == other.Tool &&
               ToolArmed == other.ToolArmed && ToolConsumed == other.ToolConsumed &&
               OverdriveServosCount == other.OverdriveServosCount &&
               ForensicCacheCount == other.ForensicCacheCount &&
               ThreatSensorCount == other.ThreatSensorCount &&
               PursuitProtocolCount == other.PursuitProtocolCount &&
               EscapeRoutineCount == other.EscapeRoutineCount &&
               AmbushProtocolCount == other.AmbushProtocolCount &&
               PriorityUplinkCount == other.PriorityUplinkCount &&
               IdentityAnchorCount == other.IdentityAnchorCount &&
               ValveOverrideCount == other.ValveOverrideCount &&
               SystemBlackoutCount == other.SystemBlackoutCount &&
               IdentityScrambleCount == other.IdentityScrambleCount &&
               PriorityUplinkCharges == other.PriorityUplinkCharges &&
               IdentityAnchorCharges == other.IdentityAnchorCharges;
    }
}

public struct UpgradeOfferState : INetworkSerializable, IEquatable<UpgradeOfferState>
{
    public ulong ClientId;
    public UpgradeCardId Card0;
    public UpgradeCardId Card1;
    public UpgradeCardId Card2;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Card0);
        serializer.SerializeValue(ref Card1);
        serializer.SerializeValue(ref Card2);
    }

    public bool Equals(UpgradeOfferState other)
    {
        return ClientId == other.ClientId && Card0 == other.Card0 &&
               Card1 == other.Card1 && Card2 == other.Card2;
    }
}

public struct AutomaticDefenseState : INetworkSerializable, IEquatable<AutomaticDefenseState>
{
    public ulong ClientId;
    public ActiveToolId Tool;
    public double EndTime;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Tool);
        serializer.SerializeValue(ref EndTime);
    }

    public bool Equals(AutomaticDefenseState other)
    {
        return ClientId == other.ClientId && Tool == other.Tool && EndTime.Equals(other.EndTime);
    }
}
