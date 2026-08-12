using Unity.Netcode;
using Unity.Collections;
using System;

public enum UpgradeSelectionKind : byte
{
    None = 0,
    Passive = 1,
    Tool = 2
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

public struct PlayerUpgradeState : INetworkSerializable, IEquatable<PlayerUpgradeState>
{
    public ulong ClientId;
    public int Points;
    public byte SelectionCount;
    public UpgradeSelectionKind PendingSelection;
    public PassiveUpgradeId Passive;
    public ActiveToolId Tool;
    public bool ToolArmed;
    public bool ToolConsumed;

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
    }

    public bool Equals(PlayerUpgradeState other)
    {
        return ClientId == other.ClientId && Points == other.Points &&
               SelectionCount == other.SelectionCount && PendingSelection == other.PendingSelection &&
               Passive == other.Passive && Tool == other.Tool &&
               ToolArmed == other.ToolArmed && ToolConsumed == other.ToolConsumed;
    }
}

public struct AutomaticDefenseState : INetworkSerializable, IEquatable<AutomaticDefenseState>
{
    public ulong ClientId;
    public double EndTime;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref EndTime);
    }

    public bool Equals(AutomaticDefenseState other)
    {
        return ClientId == other.ClientId && EndTime.Equals(other.EndTime);
    }
}
