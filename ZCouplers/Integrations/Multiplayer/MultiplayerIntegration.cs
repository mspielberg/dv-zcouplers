using System;
using System.Collections.Generic;

using DV;
using DvMod.ZCouplers;

using HarmonyLib;

using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using MPAPI.Types;

using UnityEngine;

namespace DvMod.ZCouplers.Integrations.Multiplayer
{
    /// <summary>
    /// Bootstraps DV Multiplayer integration and provides helpers for host/client checks and broadcasting state.
    /// </summary>
    public static class MultiplayerIntegration
    {
        private const string ModId = "ZCouplers"; // matches resources/info.json Id

        // Track last-known state per coupler on host to avoid spamming
        private static readonly Dictionary<Coupler, ChainCouplerInteraction.State> lastState = new();

        private static bool initialised;

        public static bool IsLoaded => MultiplayerAPI.IsMultiplayerLoaded;
        public static bool IsConnected => MultiplayerAPI.Instance?.IsConnected == true;
        public static bool IsHost => MultiplayerAPI.Instance?.IsHost == true;
        public static bool IsClientActive => IsLoaded && IsConnected && !IsHost;
    // When true on client, allow joint operations as they are being replayed from host.
    internal static bool ClientAllowsJointOps { get; private set; }

        // Pair-level dedupe: track known joints to avoid duplicate broadcasts/applications
        private static readonly HashSet<string> serverKnownTensionPairs = new();
        private static readonly HashSet<string> serverKnownCompressionPairs = new();
        private static readonly HashSet<string> clientKnownTensionPairs = new();
        private static readonly HashSet<string> clientKnownCompressionPairs = new();

        // Build an ordered pair key from two endpoints
        private static string PairKey(ushort id1, bool f1, ushort id2, bool f2)
        {
            int s1 = f1 ? 1 : 0; int s2 = f2 ? 1 : 0;
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
                }

                // Subscribe lifecycle
                MultiplayerAPI.ServerStarted += OnServerStarted;
                MultiplayerAPI.ClientStarted += OnClientStarted;
                MultiplayerAPI.ServerStopped += OnServerStopped;
                MultiplayerAPI.ClientStopped += OnClientStopped;

                // If already connected, wire now
                if (IsConnected)
                {
                    if (IsHost) OnServerStarted(MultiplayerAPI.Server);
                    else OnClientStarted(MultiplayerAPI.Client);
                }
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
            catch { }
        }

        private static void OnClientStopped()
        {
            // Nothing special for now
            ClientAllowsJointOps = false;
            clientKnownTensionPairs.Clear();
            clientKnownCompressionPairs.Clear();
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
                foreach (var car in spawner.allCars)
                {
                    if (car == null) continue;

                    void TrySyncCoupler(Coupler c)
                    {
                        if (c == null) return;

                        // Tension joint
                        if (c.coupledTo != null && JointManager.HasTensionJoint(c))
                        {
                            if (TryGetCarNetId(c.train, out var aId) && TryGetCarNetId(c.coupledTo.train, out var bId))
                            {
                                var key = PairKey(aId, c.isFrontCoupler, bId, c.coupledTo.isFrontCoupler);
                                if (!sentTension.Contains(key))
                                {
                                    sentTension.Add(key);
                                    SendJointCreateToPlayer(c, c.coupledTo, JointKind.Tension, player);
                                }
                            }
                        }

                        // Compression joint (dedupe pairs)
                        if (JointManager.HasCompressionJoint(c) && JointManager.bufferJoints.TryGetValue(c, out var tup))
                        {
                            var other = tup.otherCoupler;
                            if (other != null && TryGetCarNetId(c.train, out var aId) && TryGetCarNetId(other.train, out var bId))
                            {
                                var key = PairKey(aId, c.isFrontCoupler, bId, other.isFrontCoupler);
                                if (!sentCompression.Contains(key))
                                {
                                    sentCompression.Add(key);
                                    SendJointCreateToPlayer(c, other, JointKind.Compression, player);
                                }
                            }
                        }
                    }

                    TrySyncCoupler(car.frontCoupler);
                    TrySyncCoupler(car.rearCoupler);
                }
            }
            catch (System.Exception e)
            {
                Main.ErrorLog(() => $"[MP] OnPlayerReady sync failed: {e.Message}");
            }
        }

        /// <summary>
        /// Called on server when a client requests a lock/unlock change.
        /// </summary>
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
            if (MultiplayerAPI.Server == null || a == null || b == null || player == null)
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
            if (coupler == null || MultiplayerAPI.Server == null || player == null)
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
            if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a)) return;
            if (!TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b)) return;
            try
            {
                var key = PairKey(packet.ACarNetId, packet.AIsFront, packet.BCarNetId, packet.BIsFront);
                var set = packet.Kind == JointKind.Tension ? clientKnownTensionPairs : clientKnownCompressionPairs;
                if (set.Contains(key)) return; // already applied
                set.Add(key);
                ClientAllowsJointOps = true;
                switch (packet.Kind)
                {
                    case JointKind.Tension:
                        JointManager.CreateTensionJoint(a);
                        break;
                    case JointKind.Compression:
                        JointManager.CreateCompressionJoint(a, b);
                        break;
                }
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Client joint create failed: {e.Message}");
            }
            finally
            {
                ClientAllowsJointOps = false;
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
                ClientAllowsJointOps = true;
                switch (packet.Kind)
                {
                    case JointKind.Tension:
                        JointManager.DestroyTensionJoint(a);
                        break;
                    case JointKind.Compression:
                        JointManager.DestroyCompressionJoint(a, caller: "MP");
                        break;
                }
                set.Remove(key);
            }
            catch (Exception e)
            {
                Main.ErrorLog(() => $"[MP] Client joint destroy failed: {e.Message}");
            }
            finally
            {
                ClientAllowsJointOps = false;
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
                Main.DebugLog(() => $"[MP] NetId not found for TrainCar {car?.ID}");
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
    }
}
