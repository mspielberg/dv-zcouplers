using MPAPI.Interfaces.Packets;

namespace DvMod.ZCouplers.Integrations.Multiplayer
{
    /// <summary>
    /// Client -> Server: Request to change coupler lock (ready) state.
    /// </summary>
    public class CouplerStateChangeRequest : IPacket
    {
        public ushort CarNetId { get; set; }
        public bool IsFront { get; set; }
        public bool Locked { get; set; }
        public uint Tick { get; set; }
    }

    /// <summary>
    /// Server -> Clients: Authoritative coupler state.
    /// </summary>
    public class CouplerStateSync : IPacket
    {
        public ushort CarNetId { get; set; }
        public bool IsFront { get; set; }
        public bool Locked { get; set; }
        public byte State { get; set; }
        public uint Tick { get; set; }
    }

    /// <summary>
    /// Joint type for replication.
    /// </summary>
    public enum JointKind : byte
    {
        Tension = 1,
        Compression = 2,
    }

    /// <summary>
    /// Server -> Clients: Create a joint between two couplers.
    /// </summary>
    public class JointCreate : IPacket
    {
        public ushort ACarNetId { get; set; }
        public bool AIsFront { get; set; }
        public ushort BCarNetId { get; set; }
        public bool BIsFront { get; set; }
        public JointKind Kind { get; set; }
        public uint Tick { get; set; }
    }

    /// <summary>
    /// Server -> Clients: Destroy a joint between two couplers.
    /// </summary>
    public class JointDestroy : IPacket
    {
        public ushort ACarNetId { get; set; }
        public bool AIsFront { get; set; }
        public ushort BCarNetId { get; set; }
        public bool BIsFront { get; set; }
        public JointKind Kind { get; set; }
        public uint Tick { get; set; }
    }
}
