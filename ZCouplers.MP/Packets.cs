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
	/// Server -> Clients: Authoritative states for a batch of couplers. Each entry is a car's net ID in
	/// CarNetIds and, at the same index in Couplers, the coupler's state with the top bit set for the car's
	/// front coupler.
	/// </summary>
	public class CouplerStatesSync : IPacket
	{
		public ushort[] CarNetIds { get; set; } = System.Array.Empty<ushort>();
		public byte[] Couplers { get; set; } = System.Array.Empty<byte>();
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
		/// <summary>The host joint's linear limit (m); 0 when unknown.</summary>
		public float Limit { get; set; }
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
	/// Server -> Clients: Synchronize ZCouplers gameplay settings so joints and coupling behave identically.
	/// Values are the raw settings fields (user units), not the derived physics values.
	/// </summary>
	public class SettingsSync : IPacket
	{
		public string ProfileId { get; set; } = "";
		public byte StrengthPreset { get; set; }
		public float KnuckleStrength { get; set; }
		public float DrawgearSpringRate { get; set; }
		public float DrawgearDamperRate { get; set; }
		public float SafeCouplingSpeed { get; set; }
		public bool ShowBuffersWithKnuckles { get; set; }
		public float AutoCoupleThreshold { get; set; }
		public float MinimumSeparationDistance { get; set; }
		public bool AutoAirAndMuMode { get; set; }
		public bool AutoCouplingMode { get; set; }
		public bool DisableFrontCouplersOnSteamLocos { get; set; }
		public uint Tick { get; set; }
	}
}
