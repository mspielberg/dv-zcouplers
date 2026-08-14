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
    public static class MultiplayerIntegration
    {
        private const string ModId = "ZCouplers";

        private static readonly Dictionary<Coupler, ChainCouplerInteraction.State> lastState = new();
        private static bool initialised;

        private static bool IsLoaded => MultiplayerAPI.IsMultiplayerLoaded;
        private static bool IsConnected => MultiplayerAPI.Instance?.IsConnected == true;
        public static bool IsHost => MultiplayerAPI.Instance?.IsHost == true;
        public static bool IsClientActive => IsLoaded && IsConnected && !IsHost;
        internal static bool ClientAllowsJointOps { get; private set; }

        private static readonly HashSet<string> serverKnownTensionPairs = new();
        private static readonly HashSet<string> clientKnownTensionPairs = new();

        private static readonly List<JointCreate> clientPendingJoints = new();
        private static readonly List<JointDestroy> clientPendingDestroys = new();

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
            if (initialised) return;
            initialised = true;
            try
            {
                if (MultiplayerAPI.Instance != null)
                {
                    MultiplayerAPI.Instance.SetModCompatibility(ModId, MultiplayerCompatibility.All);
                    Main.DebugLog(() => "[MP] API compatibility set");
                }
                MultiplayerAPI.ServerStarted += OnServerStarted;
                MultiplayerAPI.ClientStarted += OnClientStarted;
                MultiplayerAPI.ServerStopped += OnServerStopped;
                MultiplayerAPI.ClientStopped += OnClientStopped;
                if (!IsConnected) return;
                if (IsHost) OnServerStarted(MultiplayerAPI.Server);
                else OnClientStarted(MultiplayerAPI.Client);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Init failed: {e.Message}"); }
        }

        private static void OnServerStarted(IServer server)
        {
            try
            {
                MultiplayerAPI.Instance.SetModCompatibility(ModId, MultiplayerCompatibility.All);
                MpShim.SetIsHost(true);
                MpShim.SetIsClientActive(false);
                server.RegisterPacket<CouplerStateChangeRequest>(OnServerCouplerStateChangeRequest);
                MultiplayerAPI.Instance.OnTick += OnTickHost;
                server.OnPlayerReady += OnPlayerReady;
                Main.DebugLog(() => "[MP] Server integration ready");
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Server start error: {e.Message}"); }
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
                client.RegisterPacket<SettingsSync>(OnClientSettingsSync);
                MpShim.SetIsClientActive(true);
                Main.DebugLog(() => "[MP] Client integration ready");
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client start error: {e.Message}"); }
        }

        private static void OnServerStopped()
        {
            try
            {
                MultiplayerAPI.Instance.OnTick -= OnTickHost;
                lastState.Clear();
                serverKnownTensionPairs.Clear();
            }
            catch { }
        }

        private static void OnClientStopped()
        {
            ClientAllowsJointOps = false;
            clientKnownTensionPairs.Clear();
            clientPendingJoints.Clear();
            clientPendingDestroys.Clear();
            MpShim.SetIsClientActive(false);
        }

        private static void OnTickHost(uint tick) { }

        private static void OnPlayerReady(IPlayer player)
        {
            try
            {
                var server = MultiplayerAPI.Server;
                if (server == null) return;
                var spawner = CarSpawner.Instance;
                if (spawner?.allCars == null) return;

                SendSettingsToPlayer(player);

                foreach (var car in spawner.allCars)
                {
                    if (car == null) continue;
                    if (car.frontCoupler != null) SendCouplerStateToPlayer(car.frontCoupler, player);
                    if (car.rearCoupler != null) SendCouplerStateToPlayer(car.rearCoupler, player);
                }

                var sentTension = new HashSet<string>();
                foreach (var car in spawner.allCars.OfType<TrainCar>())
                {
                    TrySyncCoupler(car.frontCoupler);
                    TrySyncCoupler(car.rearCoupler);
                }

                void TrySyncCoupler(Coupler c)
                {
                    if (c == null) return;
                    if (c.coupledTo != null && JointManager.HasTensionJoint(c))
                    {
                        if (TryGetCarNetId(c.train, out var aId) && TryGetCarNetId(c.coupledTo.train, out var bId))
                        {
                            var key = PairKey(aId, c.isFrontCoupler, bId, c.coupledTo.isFrontCoupler);
                            if (sentTension.Add(key))
                                SendJointCreateToPlayer(c, c.coupledTo, JointKind.Tension, player);
                        }
                    }
                }
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] OnPlayerReady sync failed: {e.Message}"); }
        }

        private static void OnServerCouplerStateChangeRequest(CouplerStateChangeRequest packet, IPlayer sender)
        {
            if (!TryResolveCoupler(packet.CarNetId, packet.IsFront, out var coupler)) return;
            try
            {
                if (packet.Locked)
                {
                    if (coupler.state == ChainCouplerInteraction.State.Parked)
                        KnuckleCouplerState.ReadyCoupler(coupler);
                }
                else
                {
                    KnuckleCouplerState.UnlockCoupler(coupler, viaChainInteraction: true);
                }
                BroadcastCouplerState(coupler);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Server apply request failed: {e.Message}"); }
        }

        public static void BroadcastCouplerState(Coupler coupler)
        {
            if (coupler == null || MultiplayerAPI.Server == null) return;
            if (!TryGetCarNetId(coupler.train, out var carId)) return;
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

        private static void SendJointCreateToPlayer(Coupler a, Coupler b, JointKind kind, IPlayer player)
        {
            if (MultiplayerAPI.Server == null || a == null || b == null) return;
            if (!TryGetCarNetId(a.train, out var aId) || !TryGetCarNetId(b.train, out var bId)) return;
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

        private static void SendCouplerStateToPlayer(Coupler coupler, IPlayer player)
        {
            if (coupler == null || MultiplayerAPI.Server == null) return;
            if (!TryGetCarNetId(coupler.train, out var carId)) return;
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

        private static void OnClientCouplerStateSync(CouplerStateSync packet)
        {
            if (!TryResolveCoupler(packet.CarNetId, packet.IsFront, out var coupler)) return;
            try
            {
                coupler.state = (ChainCouplerInteraction.State)packet.State;
                HookManager.UpdateHookVisualStateFromCouplerState(coupler);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client apply sync failed: {e.Message}"); }
        }

        public static void HostBroadcastJointCreate(Coupler a, Coupler b, JointKind kind)
        {
            if (MultiplayerAPI.Server == null) return;
            if (!TryGetCarNetId(a.train, out var aId)) return;
            if (!TryGetCarNetId(b.train, out var bId)) return;
            var key = PairKey(aId, a.isFrontCoupler, bId, b.isFrontCoupler);
            if (serverKnownTensionPairs.Contains(key)) return;
            serverKnownTensionPairs.Add(key);
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
            serverKnownTensionPairs.Remove(key);
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
                clientPendingJoints.Add(packet);
                Main.DebugLog(() => $"[MP] Client queued pending joint: {packet.ACarNetId}:{packet.AIsFront} <-> {packet.BCarNetId}:{packet.BIsFront}");
                return;
            }
            ApplyJointCreate(packet, a, b);
        }

        private static void ApplyJointCreate(JointCreate packet, Coupler a, Coupler b)
        {
            try
            {
                var key = PairKey(packet.ACarNetId, packet.AIsFront, packet.BCarNetId, packet.BIsFront);
                if (clientKnownTensionPairs.Contains(key)) return;
                clientKnownTensionPairs.Add(key);

                if (a == null || b == null || a.coupledTo != b)
                {
                    if (a != null) JointManager.DestroyUntrackedJoints(a.train.gameObject);
                    if (b != null) JointManager.DestroyUntrackedJoints(b.train.gameObject);
                    ClientAllowsJointOps = true;
                    MpShim.SetClientAllowsJointOps(true);
                    try
                    {
                        a?.CoupleTo(b, viaChainInteraction: true);
                        Main.DebugLog(() => $"[MP] Client CoupleTo {a?.train.ID} <-> {b?.train.ID}");
                    }
                    finally
                    {
                        ClientAllowsJointOps = false;
                        MpShim.SetClientAllowsJointOps(false);
                    }
                }
                else if (!JointManager.HasTensionJoint(a))
                {
                    JointManager.DestroyUntrackedJoints(a.train.gameObject);
                    JointManager.DestroyUntrackedJoints(b.train.gameObject);
                    ClientAllowsJointOps = true;
                    MpShim.SetClientAllowsJointOps(true);
                    try
                    {
                        Main.DebugLog(() => $"[MP] Client creating tension joint {a.train.ID} <-> {b.train.ID}");
                        JointManager.CreateTensionJoint(a);
                    }
                    finally
                    {
                        ClientAllowsJointOps = false;
                        MpShim.SetClientAllowsJointOps(false);
                    }
                }
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client joint create failed: {e.Message}"); }
        }

        private static void OnClientJointDestroy(JointDestroy packet)
        {
            if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a)) return;
            if (!TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b)) return;
            try
            {
                var key = PairKey(packet.ACarNetId, packet.AIsFront, packet.BCarNetId, packet.BIsFront);
                if (!clientKnownTensionPairs.Contains(key)) return;
                if (a != null)
                {
                    ClientAllowsJointOps = true;
                    try
                    {
                        a.Uncouple(playAudio: false, calledOnOtherCoupler: false, dueToBrokenCouple: false, viaChainInteraction: true);
                    }
                    finally { ClientAllowsJointOps = false; }
                }
                clientKnownTensionPairs.Remove(key);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client joint destroy failed: {e.Message}"); }
        }

        public static void SendCouplerToggleRequest(Coupler coupler, bool locked)
        {
            if (coupler == null || MultiplayerAPI.Client == null) return;
            if (!TryGetCarNetId(coupler.train, out var carId)) return;
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
            if (!ok) Main.DebugLog(() => $"[MP] NetId not found for TrainCar {car.ID}");
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

        public static void HostMaybeReplicate(Coupler coupler)
        {
            if (!IsHost || coupler == null) return;
            var state = coupler.state;
            if (!lastState.TryGetValue(coupler, out var prev) || prev != state)
            {
                lastState[coupler] = state;
                BroadcastCouplerState(coupler);
            }
        }

        private static void SendSettingsToPlayer(IPlayer player)
        {
            if (MultiplayerAPI.Server == null) return;
            try
            {
                var packet = new SettingsSync
                {
                    SpringRate = Main.settings.GetSpringRate(),
                    DamperRate = Main.settings.GetDamperRate(),
                    ShowBuffersWithKnuckles = Main.settings.showBuffersWithKnuckles,
                    MinimumSeparationDistance = Main.settings.minimumSeparationDistance,
                    Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
                };
                MultiplayerAPI.Server.SendPacketToPlayer(packet, player, reliable: true);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] SendSettingsToPlayer failed: {e.Message}"); }
        }

        public static void HostBroadcastSettings()
        {
            if (!IsHost || MultiplayerAPI.Server == null) return;
            try
            {
                var packet = new SettingsSync
                {
                    SpringRate = Main.settings.GetSpringRate(),
                    DamperRate = Main.settings.GetDamperRate(),
                    ShowBuffersWithKnuckles = Main.settings.showBuffersWithKnuckles,
                    MinimumSeparationDistance = Main.settings.minimumSeparationDistance,
                    Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
                };
                MultiplayerAPI.Server.SendPacketToAll(packet, reliable: true);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] HostBroadcastSettings failed: {e.Message}"); }
        }

        private static void OnClientSettingsSync(SettingsSync packet)
        {
            try
            {
                Main.settings.drawgearSpringRate = packet.SpringRate;
                Main.settings.drawgearDamperRate = packet.DamperRate;
                Main.settings.showBuffersWithKnuckles = packet.ShowBuffersWithKnuckles;
                Main.settings.minimumSeparationDistance = packet.MinimumSeparationDistance;
                JointManager.UpdateAllJointParameters();
                Main.DebugLog(() => "[MP] Client applied host settings and updated joints");
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client apply settings sync failed: {e.Message}"); }
        }

        public static void HostBroadcastRecouplingBlock(Coupler a, Coupler b)
        {
            if (MultiplayerAPI.Server == null || a == null || b == null) return;
            if (!TryGetCarNetId(a.train, out var aId) || !TryGetCarNetId(b.train, out var bId)) return;
            var packet = new RecouplingBlock
            {
                ACarNetId = aId,
                AIsFront = a.isFrontCoupler,
                BCarNetId = bId,
                BIsFront = b.isFrontCoupler,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };
            MultiplayerAPI.Server.SendPacketToAll(packet, reliable: true);
        }

        public static void HostBroadcastRecouplingUnblock(Coupler a, Coupler b)
        {
            if (MultiplayerAPI.Server == null || a == null || b == null) return;
            if (!TryGetCarNetId(a.train, out var aId) || !TryGetCarNetId(b.train, out var bId)) return;
            var packet = new RecouplingUnblock
            {
                ACarNetId = aId,
                AIsFront = a.isFrontCoupler,
                BCarNetId = bId,
                BIsFront = b.isFrontCoupler,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };
            MultiplayerAPI.Server.SendPacketToAll(packet, reliable: true);
        }

        private static void OnClientRecouplingBlock(RecouplingBlock packet)
        {
            if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a)) return;
            if (!TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b)) return;
            try
            {
                var pair = new RecouplingPrevention.CouplerPair(a, b);
                RecouplingPrevention.blockedPairs.Add(pair);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client recoupling block failed: {e.Message}"); }
        }

        private static void OnClientRecouplingUnblock(RecouplingUnblock packet)
        {
            if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a)) return;
            if (!TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b)) return;
            try
            {
                var pair = new RecouplingPrevention.CouplerPair(a, b);
                RecouplingPrevention.blockedPairs.Remove(pair);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client recoupling unblock failed: {e.Message}"); }
        }

        public static void ClientProcessPendingJointsForCar(TrainCar car)
        {
            if (!IsClientActive || car == null) return;
            try
            {
                if (!TryGetCarNetId(car, out var carNetId)) return;
                for (int i = clientPendingJoints.Count - 1; i >= 0; i--)
                {
                    var packet = clientPendingJoints[i];
                    if (packet.ACarNetId != carNetId && packet.BCarNetId != carNetId) continue;
                    if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a) ||
                        !TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b)) continue;
                    ApplyJointCreate(packet, a, b);
                    clientPendingJoints.RemoveAt(i);
                }

                for (int i = clientPendingDestroys.Count - 1; i >= 0; i--)
                {
                    var packet = clientPendingDestroys[i];
                    if (packet.ACarNetId != carNetId && packet.BCarNetId != carNetId) continue;
                    if (!TryResolveCoupler(packet.ACarNetId, packet.AIsFront, out var a) ||
                        !TryResolveCoupler(packet.BCarNetId, packet.BIsFront, out var b)) continue;
                    var key = PairKey(packet.ACarNetId, packet.AIsFront, packet.BCarNetId, packet.BIsFront);
                    if (clientKnownTensionPairs.Contains(key))
                    {
                        ClientAllowsJointOps = true;
                        try { a?.Uncouple(playAudio: false, calledOnOtherCoupler: false, dueToBrokenCouple: false, viaChainInteraction: true); }
                        finally { ClientAllowsJointOps = false; }
                        clientKnownTensionPairs.Remove(key);
                    }
                    clientPendingDestroys.RemoveAt(i);
                }
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client process pending joints failed: {e.Message}"); }
        }
    }
}
