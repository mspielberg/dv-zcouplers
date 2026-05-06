using System;
using System.Collections.Generic;
using System.Linq;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Utils;
using DvMod.ZCouplers.Physics;
using DvMod.ZCouplers.Visuals;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Types;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Bootstraps DV Multiplayer integration and provides helpers for host/client checks and broadcasting state.
    /// </summary>
    public static class MultiplayerIntegration
    {
        private const string ModId = "ZCouplers"; // matches resources/info.json ID

        // Track last-known state per coupler on host to avoid spamming
        private static readonly Dictionary<Coupler, ChainCouplerInteraction.State> lastState = new();

        private static bool initialised;

        private static bool IsLoaded => MultiplayerAPI.IsMultiplayerLoaded;
        private static bool IsConnected => MultiplayerAPI.Instance?.IsConnected == true;
        public static bool IsHost => MultiplayerAPI.Instance?.IsHost == true;
        public static bool IsClientActive => IsLoaded && IsConnected && !IsHost;
		// When true on client, allow joint operations as they are being replayed from host.
		internal static bool ClientAllowsJointOps { get; private set; }

        // Pair-level dedupe: track known joints to avoid duplicate broadcasts/applications
        private static readonly HashSet<string> serverKnownTensionPairs = new();
        private static readonly HashSet<string> serverKnownCompressionPairs = new();
        private static readonly HashSet<string> clientKnownTensionPairs = new();
        private static readonly HashSet<string> clientKnownCompressionPairs = new();

        // Client: Store pending joint packets for cars that aren't loaded yet
        private static readonly List<JointCreate> clientPendingJoints = new();
        private static readonly List<JointDestroy> clientPendingDestroys = new();

        // Build an ordered pair key from two endpoints
        private static string PairKey(ushort id1, bool f1, ushort id2, bool f2)
        {
            int s1 = f1 ? 1 : 0;
            int s2 = f2 ? 1 : 0;
            if (id2 < id1 || (id2 == id1 && s2 < s1))
            {
                (id1, id2) = (id2, id1);
                (s1, s2) = (s2, s1);
            }
            return $"{id1}:{s1}-{id2}:{s2}";
        }

        public static void Initialize()
        {
            if (initialised)
                return;

            initialised = true;

            // Soft-fail if MP isn't present; we keep handlers registered for when it loads.
            try
            {
                // Set compatibility preference if API already present
                if (MultiplayerAPI.Instance != null)
                {
                    MultiplayerAPI.Instance.SetModCompatibility(ModId, MultiplayerCompatibility.All);
                    Main.DebugLog(() => "[MP] API compatibility set");
                    Main.DebugLog(() => $"[MP] IsHost={IsHost}, IsClientActive={IsClientActive}");
                    Main.DebugLog(() => $"[MP] Loaded API Version: {MultiplayerAPI.LoadedApiVersion}");
                }

                // Subscribe lifecycle
                MultiplayerAPI.ServerStarted += OnServerStarted;
                MultiplayerAPI.ClientStarted += OnClientStarted;
                MultiplayerAPI.ServerStopped += OnServerStopped;
                MultiplayerAPI.ClientStopped += OnClientStopped;

                // If already connected, wire now
                if (!IsConnected) return;
                if (IsHost) OnServerStarted(MultiplayerAPI.Server);
                else OnClientStarted(MultiplayerAPI.Client);
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Init failed: {e.Message}");
            }
        }

        private static void OnServerStarted(IServer server)
        {
            try
            {
                MultiplayerAPI.Instance.SetModCompatibility(ModId, MultiplayerCompatibility.All);

                // Register packet handlers
                server.RegisterPacket<CouplerStateChangeRequest>(OnServerCouplerStateChangeRequest);

                // Tick to batch optional broadcasts (not strictly needed now)
                MultiplayerAPI.Instance.OnTick += OnTickHost;

                // Initial full-state sync per joining player
                server.OnPlayerReady += OnPlayerReady;

                Main.DebugLog(() => "[MP] Server integration ready");
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Server start error: {e.Message}");
            }
        }

        private static void OnClientStarted(IClient client)
        {
            try
            {
                MultiplayerAPI.Instance.SetModCompatibility(ModId, MultiplayerCompatibility.All);
                client.RegisterPacket<CouplerStateSync>(OnClientCouplerStateSync);
                client.RegisterPacket<JointCreate>(OnClientJointCreate);
                client.RegisterPacket<JointDestroy>(OnClientJointDestroy);
                client.RegisterPacket<RecouplingBlock>(OnClientRecouplingBlock);
                client.RegisterPacket<RecouplingUnblock>(OnClientRecouplingUnblock);

                // Update MpShim flags so core code knows we're a client
                MpShim.SetIsClientActive(true);

                Main.DebugLog(() => "[MP] Client integration ready");
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Client start error: {e.Message}");
            }
        }

        private static void OnServerStopped()
        {
            try
            {
                MultiplayerAPI.Instance.OnTick -= OnTickHost;
                lastState.Clear();
                serverKnownTensionPairs.Clear();
                serverKnownCompressionPairs.Clear();
            }
            catch
            {
	            // ignored
            }
        }

        private static void OnClientStopped()
        {
            // Clear all client state
            ClientAllowsJointOps = false;
            clientKnownTensionPairs.Clear();
            clientKnownCompressionPairs.Clear();
            clientPendingJoints.Clear();
            clientPendingDestroys.Clear();

            // Update MpShim flag so core code knows we're no longer a client
            MpShim.SetIsClientActive(false);
        }

        private static void OnTickHost(uint tick)
        {
            // Reserved for future batching; no-op currently
        }

        /// <summary>
        /// Server: when a player is ready, push current coupler states for all cars.
        /// </summary>
        private static void OnPlayerReady(IPlayer player)
        {
	        try
	        {
		        var server = MultiplayerAPI.Server;
		        if (server == null)
			        return;

		        var spawner = CarSpawner.Instance;
		        if (spawner?.allCars == null)
			        return;

		        foreach (var car in spawner.allCars)
		        {
			        if (car == null) continue;

			        if (car.frontCoupler != null)
				        SendCouplerStateToPlayer(car.frontCoupler, player);
			        if (car.rearCoupler != null)
				        SendCouplerStateToPlayer(car.rearCoupler, player);
		        }

		        // After states, sync existing joints so the client doesn't try to simulate locally.
		        var sentCompression = new HashSet<string>();
		        var sentTension = new HashSet<string>();
		        foreach (var car in spawner.allCars.OfType<TrainCar>())
		        {
			        TrySyncCoupler(car.frontCoupler);
			        TrySyncCoupler(car.rearCoupler);
			        continue;

			        void TrySyncCoupler(Coupler c)
			        {
				        if (c == null) return;

				        // Tension joint
				        if (c.coupledTo != null && JointManager.HasTensionJoint(c))
				        {
					        if (TryGetCarNetId(c.train, out var aId) && TryGetCarNetId(c.coupledTo.train, out var bId))
					        {
						        var key = PairKey(aId, c.isFrontCoupler, bId, c.coupledTo.isFrontCoupler);
						        if (sentTension.Add(key))

						        {
							        SendJointCreateToPlayer(c, c.coupledTo, JointKind.Tension, player);
						        }
					        }
				        }

				        // Compression joint (dedupe pairs)
				        if (JointManager.HasCompressionJoint(c) &&
				            JointManager.bufferJoints.TryGetValue(c, out var tup))
				        {
					        var other = tup.otherCoupler;
					        if (other != null && TryGetCarNetId(c.train, out var aId) &&
					            TryGetCarNetId(other.train, out var bId))
					        {
						        var key = PairKey(aId, c.isFrontCoupler, bId, other.isFrontCoupler);
						        if (sentCompression.Add(key))
						        {
							        SendJointCreateToPlayer(c, other, JointKind.Compression, player);
						        }
					        }
				        }
			        }
		        }
	        }

	        catch (Exception e)
	        {
		        Main.ErrorLog(() => $"[MP] OnPlayerReady sync failed: {e.Message}");
	        }
        }


        private static void OnServerCouplerStateChangeRequest(CouplerStateChangeRequest packet, IPlayer sender)
        {
	        // Resolve the requested coupler
	        if (!TryResolveCoupler(packet.CarNetId, packet.IsFront, out var coupler))
		        return;

	        try
	        {
		        // Apply requested action authoritatively
		        if (packet.Locked)
		        {
			        // Only change if actually parked
			        if (coupler.state == ChainCouplerInteraction.State.Parked)
				        KnuckleCouplerState.ReadyCoupler(coupler);
		        }
		        else
		        {
                    // Unlock always allowed; will uncouple if necessary
                    KnuckleCouplerState.UnlockCoupler(coupler, viaChainInteraction: true);
		        }

                // Broadcast the resulting state to all clients
                BroadcastCouplerState(coupler);
	        }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Server apply request failed: {e.Message}");
            }
        }

        /// <summary>
        /// Host: Broadcast a coupler's current state to all clients.
        /// </summary>
        public static void BroadcastCouplerState(Coupler coupler)
        {
            if (coupler == null || MultiplayerAPI.Server == null)
                return;

            if (!TryGetCarNetId(coupler.train, out var carId))
                return;

            var packet = new CouplerStateSync
            {
                CarNetId = carId,
                IsFront = coupler.isFrontCoupler,
                Locked = coupler.state != ChainCouplerInteraction.State.Parked,
                State = (byte)coupler.state,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };

            MultiplayerAPI.Server.SendPacketToAll(packet, reliable: true);
        }

        // Host: send a joint create to a specific player
        private static void SendJointCreateToPlayer(Coupler a, Coupler b, JointKind kind, IPlayer player)
        {
            if (MultiplayerAPI.Server == null || a == null || b == null)
                return;
            if (!TryGetCarNetId(a.train, out var aId) || !TryGetCarNetId(b.train, out var bId))
                return;
            var pkt = new JointCreate
            {
                ACarNetId = aId,
                AIsFront = a.isFrontCoupler,
                BCarNetId = bId,
                BIsFront = b.isFrontCoupler,
                Kind = kind,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };
            MultiplayerAPI.Server.SendPacketToPlayer(pkt, player, reliable: true);
        }

        /// <summary>
        /// Host: Send a coupler's current state to a specific player.
        /// </summary>
        private static void SendCouplerStateToPlayer(Coupler coupler, IPlayer player)
        {
            if (coupler == null || MultiplayerAPI.Server == null)
                return;

            if (!TryGetCarNetId(coupler.train, out var carId))
                return;

            var packet = new CouplerStateSync
            {
                CarNetId = carId,
                IsFront = coupler.isFrontCoupler,
                Locked = coupler.state != ChainCouplerInteraction.State.Parked,
                State = (byte)coupler.state,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };

            MultiplayerAPI.Server.SendPacketToPlayer(packet, player, reliable: true);
        }

        /// <summary>
        /// Client: Apply a state sync from server.
        /// </summary>
        private static void OnClientCouplerStateSync(CouplerStateSync packet)
        {
            if (!TryResolveCoupler(packet.CarNetId, packet.IsFront, out var coupler))
                return;

            try
            {
                // Apply state directly and refresh visuals; avoid calling methods that would send packets
                coupler.state = (ChainCouplerInteraction.State)packet.State;
                HookManager.UpdateHookVisualStateFromCouplerState(coupler);
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Client apply sync failed: {e.Message}");
            }
        }

        // -------- Joint replication --------

        public static void HostBroadcastJointCreate(Coupler a, Coupler b, JointKind kind)
        {
            if (MultiplayerAPI.Server == null) return;
            if (!TryGetCarNetId(a.train, out var aId)) return;
            if (!TryGetCarNetId(b.train, out var bId)) return;
            var key = PairKey(aId, a.isFrontCoupler, bId, b.isFrontCoupler);
            var set = kind == JointKind.Tension ? serverKnownTensionPairs : serverKnownCompressionPairs;
            if (set.Contains(key)) return; // already broadcast for this existing joint
            set.Add(key);
            var pkt = new JointCreate
            {
                ACarNetId = aId,
                AIsFront = a.isFrontCoupler,
                BCarNetId = bId,
                BIsFront = b.isFrontCoupler,
                Kind = kind,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };
            MultiplayerAPI.Server.SendPacketToAll(pkt, reliable: true);
        }

        public static void HostBroadcastJointDestroy(Coupler a, Coupler b, JointKind kind)
        {
            if (MultiplayerAPI.Server == null) return;
            if (!TryGetCarNetId(a.train, out var aId)) return;
            if (!TryGetCarNetId(b.train, out var bId)) return;
            var key = PairKey(aId, a.isFrontCoupler, bId, b.isFrontCoupler);
            var set = kind == JointKind.Tension ? serverKnownTensionPairs : serverKnownCompressionPairs;
            set.Remove(key);
            var pkt = new JointDestroy
            {
                ACarNetId = aId,
                AIsFront = a.isFrontCoupler,
                BCarNetId = bId,
                BIsFront = b.isFrontCoupler,
                Kind = kind,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };
            MultiplayerAPI.Server.SendPacketToAll(pkt, reliable: true);
        }

        private static void OnClientJointCreate(JointCreate packet)
        {
            if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a) ||
                !TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b))
            {
                // Cars not loaded yet - queue for later
                clientPendingJoints.Add(packet);
                Main.DebugLog(() => $"[MP] Client queued pending joint (cars not loaded): {packet.ACarNetId}:{packet.AIsFront} <-> {packet.BCarNetId}:{packet.BIsFront}");
                return;
            }

            ApplyJointCreate(packet, a, b);
        }

        /// <summary>
        /// Apply a joint create operation with the resolved couplers.
        /// </summary>
        private static void ApplyJointCreate(JointCreate packet, Coupler a, Coupler b)
        {
            try
            {
                var key = PairKey(packet.ACarNetId, packet.AIsFront, packet.BCarNetId, packet.BIsFront);
                var set = packet.Kind == JointKind.Tension ? clientKnownTensionPairs : clientKnownCompressionPairs;
                if (set.Contains(key)) return; // already applied
                set.Add(key);
                // For Tension (i.e., actual coupling), prefer invoking the game's CoupleTo so TrainSets match host
                if (packet.Kind == JointKind.Tension)
                {
                    // If not already coupled to the intended partner, perform a local couple
                    if (a == null || b == null || a.coupledTo != b)
                    {
                        ClientAllowsJointOps = true; // allow joint creation triggered by CoupleTo
                        MpShim.SetClientAllowsJointOps(true);
                        try
                        {
                            a?.CoupleTo(b, viaChainInteraction: true);
                            Main.DebugLog(() => $"[MP] Client CoupleTo {a?.train.ID} <-> {b?.train.ID} (this merges trainsets)");
                        }
                        finally
                        {
                            ClientAllowsJointOps = false;
                            MpShim.SetClientAllowsJointOps(false);
                        }
                    }
                }
                else
                {
                    // Compression joints don't affect TrainSet membership; just mirror host
                    ClientAllowsJointOps = true;
                    MpShim.SetClientAllowsJointOps(true);
                    try
                    {
                        JointManager.CreateCompressionJoint(a, b);
                    }
                    finally
                    {
                        ClientAllowsJointOps = false;
                        MpShim.SetClientAllowsJointOps(false);
                    }
                }
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Client joint create failed: {e.Message}");
            }
        }

        private static void OnClientJointDestroy(JointDestroy packet)
        {
            if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a)) return;
            if (!TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b)) return;
            try
            {
                var key = PairKey(packet.ACarNetId, packet.AIsFront, packet.BCarNetId, packet.BIsFront);
                var set = packet.Kind == JointKind.Tension ? clientKnownTensionPairs : clientKnownCompressionPairs;
                if (!set.Contains(key)) return; // already removed / unknown
                if (packet.Kind == JointKind.Tension)
                {
                    // Prefer native Uncouple to ensure TrainSets are split identically to host
                    if (a != null)
                    {
                        // Uncouple will remove joints; allow any joint ops during this call
                        ClientAllowsJointOps = true;
                        try
                        {
                            a.Uncouple(playAudio: false, calledOnOtherCoupler: false, dueToBrokenCouple: false, viaChainInteraction: true);
                        }
                        finally
                        {
                            ClientAllowsJointOps = false;
                        }
                    }
                }
                else
                {
                    ClientAllowsJointOps = true;
                    try
                    {
                        JointManager.DestroyCompressionJoint(a, caller: "MP");
                    }
                    finally
                    {
                        ClientAllowsJointOps = false;
                    }
                }
                set.Remove(key);
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Client joint destroy failed: {e.Message}");
            }
        }

        /// <summary>
        /// Client: Send a lock/unlock request for a coupler to the server.
        /// </summary>
        public static void SendCouplerToggleRequest(Coupler coupler, bool locked)
        {
            if (coupler == null || MultiplayerAPI.Client == null)
                return;

            if (!TryGetCarNetId(coupler.train, out var carId))
                return;

            var packet = new CouplerStateChangeRequest
            {
                CarNetId = carId,
                IsFront = coupler.isFrontCoupler,
                Locked = locked,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };

            MultiplayerAPI.Client.SendPacketToServer(packet, reliable: true);
        }

        private static bool TryGetCarNetId(TrainCar car, out ushort netId)
        {
            netId = 0;
            var ok = MultiplayerAPI.Instance?.TryGetNetId(car, out netId) == true;
            if (!ok)
                Main.DebugLog(() => $"[MP] NetId not found for TrainCar {car.ID}");
            return ok;
        }

        private static bool TryResolveCoupler(ushort carNetId, bool isFront, out Coupler coupler)
        {
            coupler = null!;
            if (MultiplayerAPI.Instance?.TryGetObjectFromNetId<TrainCar>(carNetId, out var car) == true && car != null)
            {
                coupler = isFront ? car.frontCoupler : car.rearCoupler;
                return coupler != null;
            }
            return false;
        }

        /// <summary>
        /// Host hook: call when a coupler's state may have changed to replicate to clients if needed.
        /// </summary>
        public static void HostMaybeReplicate(Coupler coupler)
        {
            if (!IsHost || coupler == null)
                return;

            var state = coupler.state;
            if (!lastState.TryGetValue(coupler, out var prev) || prev != state)
            {
                lastState[coupler] = state;
                BroadcastCouplerState(coupler);
            }
        }

        // -------- Recoupling Prevention Sync --------

        /// <summary>
        /// Host: Broadcast that a coupler pair should be blocked from recoupling.
        /// </summary>
        public static void HostBroadcastRecouplingBlock(Coupler a, Coupler b)
        {
            if (MultiplayerAPI.Server == null || a == null || b == null)
                return;
            if (!TryGetCarNetId(a.train, out var aId) || !TryGetCarNetId(b.train, out var bId))
                return;

            var packet = new RecouplingBlock
            {
                ACarNetId = aId,
                AIsFront = a.isFrontCoupler,
                BCarNetId = bId,
                BIsFront = b.isFrontCoupler,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };

            MultiplayerAPI.Server.SendPacketToAll(packet, reliable: true);
            Main.DebugLog(() => $"[MP] Host broadcast recoupling block: {a.train.ID} <-> {b.train.ID}");
        }

        /// <summary>
        /// Host: Broadcast that a coupler pair should be unblocked and allowed to recouple.
        /// </summary>
        public static void HostBroadcastRecouplingUnblock(Coupler a, Coupler b)
        {
            if (MultiplayerAPI.Server == null || a == null || b == null)
                return;
            if (!TryGetCarNetId(a.train, out var aId) || !TryGetCarNetId(b.train, out var bId))
                return;

            var packet = new RecouplingUnblock
            {
                ACarNetId = aId,
                AIsFront = a.isFrontCoupler,
                BCarNetId = bId,
                BIsFront = b.isFrontCoupler,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };

            MultiplayerAPI.Server.SendPacketToAll(packet, reliable: true);
            Main.DebugLog(() => $"[MP] Host broadcast recoupling unblock: {a.train.ID} <-> {b.train.ID}");
        }

        /// <summary>
        /// Client: Apply a recoupling block from the host.
        /// </summary>
        private static void OnClientRecouplingBlock(RecouplingBlock packet)
        {
            if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a)) return;
            if (!TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b)) return;

            try
            {
                // Directly add to blockedPairs - no need for a separate method
                var pair = new RecouplingPrevention.CouplerPair(a, b);
                RecouplingPrevention.blockedPairs.Add(pair);
                Main.DebugLog(() => $"[MP] Client applied recoupling block: {a.train.ID} <-> {b.train.ID}");
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Client recoupling block failed: {e.Message}");
            }
        }

        /// <summary>
        /// Client: Apply a recoupling unblock from the host.
        /// </summary>
        private static void OnClientRecouplingUnblock(RecouplingUnblock packet)
        {
            if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a)) return;
            if (!TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b)) return;

            try
            {
                // Directly remove from blockedPairs - no need for a separate method
                var pair = new RecouplingPrevention.CouplerPair(a, b);
                RecouplingPrevention.blockedPairs.Remove(pair);
                Main.DebugLog(() => $"[MP] Client applied recoupling unblock: {a.train.ID} <-> {b.train.ID}");
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Client recoupling unblock failed: {e.Message}");
            }
        }

        /// <summary>
        /// Client: Process pending joint operations for a car that just loaded.
        /// This ensures CoupleTo gets called for trainset merging even when cars load asynchronously.
        /// Call this when a car finishes loading on the client.
        /// </summary>
        public static void ClientProcessPendingJointsForCar(TrainCar car)
        {
            if (!IsClientActive || car == null)
                return;

            try
            {
                if (!TryGetCarNetId(car, out var carNetId))
                    return;

                // Try to apply pending joint creates
                for (int i = clientPendingJoints.Count - 1; i >= 0; i--)
                {
                    var packet = clientPendingJoints[i];

                    // Check if this packet involves the newly loaded car
                    if (packet.ACarNetId != carNetId && packet.BCarNetId != carNetId)
                        continue;

                    // Try to resolve both couplers
                    if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a) ||
                        !TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b))
                        continue; // Still can't resolve, leave it queued

                    // Both cars are now loaded - apply the joint and remove from queue
                    Main.DebugLog(() => $"[MP] Client applying pending joint (cars now loaded): {packet.ACarNetId}:{packet.AIsFront} <-> {packet.BCarNetId}:{packet.BIsFront}");
                    ApplyJointCreate(packet, a, b);
                    clientPendingJoints.RemoveAt(i);
                }

                // Try to apply pending joint destroys
                for (int i = clientPendingDestroys.Count - 1; i >= 0; i--)
                {
                    var packet = clientPendingDestroys[i];

                    // Check if this packet involves the newly loaded car
                    if (packet.ACarNetId != carNetId && packet.BCarNetId != carNetId)
                        continue;

                    // Try to resolve both couplers
                    if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a) ||
                        !TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b))
                        continue; // Still can't resolve, leave it queued

                    // Both cars are now loaded - apply the destroy and remove from queue
                    Main.DebugLog(() => $"[MP] Client applying pending joint destroy (cars now loaded): {packet.ACarNetId}:{packet.AIsFront} <-> {packet.BCarNetId}:{packet.BIsFront}");

                    var key = PairKey(packet.ACarNetId, packet.AIsFront, packet.BCarNetId, packet.BIsFront);
                    var set = packet.Kind == JointKind.Tension ? clientKnownTensionPairs : clientKnownCompressionPairs;

                    if (set.Contains(key))
                    {
                        if (packet.Kind == JointKind.Tension)
                        {
                            ClientAllowsJointOps = true;
                            MpShim.SetClientAllowsJointOps(true);
                            try
                            {
                                a?.Uncouple(playAudio: false, calledOnOtherCoupler: false, dueToBrokenCouple: false, viaChainInteraction: true);
                            }
                            finally
                            {
                                ClientAllowsJointOps = false;
                                MpShim.SetClientAllowsJointOps(false);
                            }
                        }
                        else
                        {
                            ClientAllowsJointOps = true;
                            MpShim.SetClientAllowsJointOps(true);
                            try
                            {
                                JointManager.DestroyCompressionJoint(a, caller: "MP-Pending");
                            }
                            finally
                            {
                                ClientAllowsJointOps = false;
                                MpShim.SetClientAllowsJointOps(false);
                            }
                        }
                        set.Remove(key);
                    }

                    clientPendingDestroys.RemoveAt(i);
                }
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Client process pending joints failed: {e.Message}");
            }
        }
    }
}
