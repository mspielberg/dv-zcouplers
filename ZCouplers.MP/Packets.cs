using MPAPI.Interfaces.Packets;

namespace DvMod.ZCouplers
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

	/// <summary>
	/// Server -> Clients: Block a coupler pair from recoupling.
	/// </summary>
	public class RecouplingBlock : IPacket
	{
		public ushort ACarNetId { get; set; }
		public bool AIsFront { get; set; }
		public ushort BCarNetId { get; set; }
		public bool BIsFront { get; set; }
		public uint Tick { get; set; }
	}

	/// <summary>
	/// Server -> Clients: Unblock a coupler pair, allowing recoupling.
	/// </summary>
	public class RecouplingUnblock : IPacket
	{
		public ushort ACarNetId { get; set; }
		public bool AIsFront { get; set; }
		public ushort BCarNetId { get; set; }
		public bool BIsFront { get; set; }
		public uint Tick { get; set; }
	}

	/// <summary>
	/// Server -> Clients: Synchronize ZCouplers physics settings so joints are identical.
	/// </summary>
	public class SettingsSync : IPacket
	{
		public float SpringRate { get; set; }
		public float DamperRate { get; set; }
		public bool ShowBuffersWithKnuckles { get; set; }
		public float MinimumSeparationDistance { get; set; }
		public uint Tick { get; set; }
	}
}
