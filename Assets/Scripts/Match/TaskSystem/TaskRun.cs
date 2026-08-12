using Unity.Netcode;
using System;

public enum TaskRunState : byte
{
    Unassigned,
    Assigned,
    Reserved,
    InProgress,
    Completed,
    Cancelled
}

public enum TaskRunKind : byte
{
    Normal = 0,
    Rogue = 1,
    // A killer may perform a solo normal terminal task as an alibi. It is
    // tracked for reservation/resume behavior, but never rewards progress.
    Alibi = 2
}

public enum TerminalHackPhase : byte
{
    Idle = 0,
    Preparing = 1,
    Available = 2,
    Active = 3,
    Cooldown = 4
}

public struct TerminalHackState : INetworkSerializable, IEquatable<TerminalHackState>
{
    public Unity.Collections.FixedString32Bytes TaskID;
    public TerminalHackPhase Phase;
    public double ServerTime;
    public int Revision;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref TaskID);
        serializer.SerializeValue(ref Phase);
        serializer.SerializeValue(ref ServerTime);
        serializer.SerializeValue(ref Revision);
    }

    public bool Equals(TerminalHackState other)
    {
        return TaskID.Equals(other.TaskID) &&
               Phase == other.Phase &&
               ServerTime.Equals(other.ServerTime) &&
               Revision == other.Revision;
    }
}

public struct TaskRun : INetworkSerializable, IEquatable<TaskRun>
{
    public ulong OwnerClientId;
    public Unity.Collections.FixedString32Bytes TaskID;
    public TaskRunKind Kind;
    public int SequenceIndex;
    public int CooperativeSessionId;
    public byte CooperativeRoleIndex;
    public TaskRunState State;
    public float Progress;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref OwnerClientId);
        serializer.SerializeValue(ref TaskID);
        serializer.SerializeValue(ref Kind);
        serializer.SerializeValue(ref SequenceIndex);
        serializer.SerializeValue(ref CooperativeSessionId);
        serializer.SerializeValue(ref CooperativeRoleIndex);
        serializer.SerializeValue(ref State);
        serializer.SerializeValue(ref Progress);
    }

    public bool Equals(TaskRun other)
    {
        return OwnerClientId == other.OwnerClientId &&
               TaskID.Equals(other.TaskID) &&
               Kind == other.Kind &&
               SequenceIndex == other.SequenceIndex &&
               CooperativeSessionId == other.CooperativeSessionId &&
               CooperativeRoleIndex == other.CooperativeRoleIndex;
    }
}
