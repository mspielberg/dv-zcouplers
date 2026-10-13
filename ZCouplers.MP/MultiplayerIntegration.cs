using System;
using System.Collections.Generic;
using System.Linq;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Profiles;
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

        // On the host, the last state sent for each coupler and the couplers that may have changed since then
        private static readonly Dictionary<Coupler, ChainCouplerInteraction.State> lastState = new();
        private static readonly HashSet<Coupler> dirtyCouplers = new();
        private static readonly HashSet<byte> readyPlayers = new();
        private static int sweepIndex;
        private static bool initialised;

        private static bool IsLoaded => MultiplayerAPI.IsMultiplayerLoaded;
        private static bool IsConnected => MultiplayerAPI.Instance?.IsConnected == true;
        public static bool IsHost => MultiplayerAPI.Instance?.IsHost == true;
        public static bool IsClientActive => IsLoaded && IsConnected && !IsHost;
        internal static bool ClientAllowsJointOps { get; set; }

        private static readonly HashSet<string> serverKnownTensionPairs = new();
        private static readonly HashSet<string> clientKnownTensionPairs = new();

        private static readonly List<JointCreate> clientPendingJoints = new();
        private static readonly List<JointDestroy> clientPendingDestroys = new();

        // Client's own settings captured before the host's SettingsSync overwrites them
        private static SettingsSync? clientLocalSettings;

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
                server.OnPlayerDisconnected += OnPlayerDisconnected;
                CouplerStateChanges.Changed += HostMarkDirty;
                Main.DebugLog(() => "[MP] Server integration ready");
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Server start error: {e.Message}"); }
        }

        private static void OnClientStarted(IClient client)
        {
            try
            {
                MultiplayerAPI.Instance.SetModCompatibility(ModId, MultiplayerCompatibility.All);

                // The host also starts a loopback client. It is authoritative and must neither
                // be flagged as a client nor re-apply its own broadcasts.
                if (IsHost)
                {
                    Main.DebugLog(() => "[MP] Skipping client integration on host");
                    return;
                }

                client.RegisterPacket<CouplerStatesSync>(OnClientCouplerStatesSync);
                client.RegisterPacket<JointCreate>(OnClientJointCreate);
                client.RegisterPacket<JointDestroy>(OnClientJointDestroy);
                client.RegisterPacket<RecouplingBlock>(OnClientRecouplingBlock);
                client.RegisterPacket<RecouplingUnblock>(OnClientRecouplingUnblock);
                client.RegisterPacket<SettingsSync>(OnClientSettingsSync);
                clientLocalSettings = CreateSettingsPacket();
                MpShim.SetIsClientActive(true);
                Main.DebugLog(() => "[MP] Client integration ready");
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client start error: {e.Message}"); }
        }

        private static void OnServerStopped()
        {
            MpShim.SetIsHost(false);
            try
            {
                MultiplayerAPI.Instance.OnTick -= OnTickHost;
                CouplerStateChanges.Changed -= HostMarkDirty;
                UnhookCarDeletion();
                lastState.Clear();
                dirtyCouplers.Clear();
                readyPlayers.Clear();
                sweepIndex = 0;
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
            RestoreClientLocalSettings();
        }

        private const uint PruneIntervalTicks = 240; // ~10 s at 24 ticks/s
        // The sweep checks a few cars each tick and gets through all of them in this many ticks (about 3 s)
        private const int SweepPeriodTicks = 72;
        private const int MaxStatesPerPacket = 512;

        /// <summary>
        /// Sends clients the coupler states that changed since the last tick, all in one packet. Couplers
        /// are marked dirty as they change. Harmony patches catch the game's coupling, uncoupling and chain
        /// interactions, and CouplerStateChanges catches ZCouplers' own changes. The chain state machine
        /// only runs near the host player, so this is how clients hear about states ZCouplers sets
        /// anywhere else, for example while coupling, uncoupling or auto-readying. A slow sweep over all
        /// cars picks up anything that was missed.
        /// </summary>
        private static void OnTickHost(uint tick)
        {
            try
            {
                HookCarDeletion();
                SweepForMissedChanges();
                FlushDirtyCouplers();

                if (tick % PruneIntervalTicks == 0)
                {
                    foreach (var destroyed in lastState.Keys.Where(c => c == null).ToList())
                        lastState.Remove(destroyed);
                }
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Host state replication failed: {e.Message}"); }
        }

        public static void HostMarkDirty(Coupler coupler)
        {
            if (IsHost && coupler != null)
                dirtyCouplers.Add(coupler);
        }

        private static CarSpawner? hookedSpawner;

        /// <summary>
        /// DV puts deleted cars back in a pool and reuses them, couplers included. Forgetting a deleted
        /// car's couplers makes sure their first state is sent again when the car comes back.
        /// </summary>
        private static void HookCarDeletion()
        {
            var spawner = CarSpawner.Instance;
            if (spawner == null || spawner == hookedSpawner) return;
            if (hookedSpawner != null) hookedSpawner.CarAboutToBeDeleted -= OnCarAboutToBeDeleted;
            spawner.CarAboutToBeDeleted += OnCarAboutToBeDeleted;
            hookedSpawner = spawner;
        }

        private static void UnhookCarDeletion()
        {
            if (hookedSpawner != null) hookedSpawner.CarAboutToBeDeleted -= OnCarAboutToBeDeleted;
            hookedSpawner = null;
        }

        private static void OnCarAboutToBeDeleted(TrainCar car)
        {
            if (car == null) return;
            foreach (var coupler in new[] { car.frontCoupler, car.rearCoupler })
            {
                if (coupler == null) continue;
                lastState.Remove(coupler);
                dirtyCouplers.Remove(coupler);
            }
        }

        private static void SweepForMissedChanges()
        {
            var cars = CarSpawner.Instance?.allCars;
            if (cars == null || cars.Count == 0) return;
            var perTick = (cars.Count + SweepPeriodTicks - 1) / SweepPeriodTicks;
            for (int i = 0; i < perTick; i++)
            {
                if (sweepIndex >= cars.Count) sweepIndex = 0;
                var car = cars[sweepIndex++];
                if (car == null) continue;
                MarkDirtyIfChanged(car.frontCoupler);
                MarkDirtyIfChanged(car.rearCoupler);
            }

            static void MarkDirtyIfChanged(Coupler coupler)
            {
                if (coupler != null && (!lastState.TryGetValue(coupler, out var sent) || sent != coupler.state))
                    dirtyCouplers.Add(coupler);
            }
        }

        private static void FlushDirtyCouplers()
        {
            if (dirtyCouplers.Count == 0) return;
            var carIds = new List<ushort>();
            var couplers = new List<byte>();
            List<Coupler>? notNetworkedYet = null;
            foreach (var coupler in dirtyCouplers)
            {
                // Deleted cars go back to DV's car pool inactive rather than being destroyed
                if (coupler == null || coupler.train == null || !coupler.train.gameObject.activeInHierarchy) continue;
                var state = coupler.state;
                if (lastState.TryGetValue(coupler, out var sent) && sent == state) continue;
                // A car spawned this tick may not have a NetId yet, so try it again on the next one
                if (MultiplayerAPI.Instance?.TryGetNetId(coupler.train, out ushort carId) != true)
                {
                    (notNetworkedYet ??= new List<Coupler>()).Add(coupler);
                    continue;
                }
                lastState[coupler] = state;
                carIds.Add(carId);
                couplers.Add(EncodeCoupler(coupler.isFrontCoupler, state));
            }
            dirtyCouplers.Clear();
            if (notNetworkedYet != null)
                dirtyCouplers.UnionWith(notNetworkedYet);

            // Nothing needs sending while nobody is connected. Players who join get the full snapshot.
            if (carIds.Count > 0 && readyPlayers.Count > 0)
                SendCouplerStates(carIds, couplers, player: null);
        }

        private static byte EncodeCoupler(bool isFront, ChainCouplerInteraction.State state) =>
            (byte)((isFront ? 0x80 : 0) | ((byte)state & 0x7F));

        private static void SendCouplerStates(List<ushort> carIds, List<byte> couplers, IPlayer? player)
        {
            var server = MultiplayerAPI.Server;
            if (server == null) return;
            for (int start = 0; start < carIds.Count; start += MaxStatesPerPacket)
            {
                var count = Math.Min(MaxStatesPerPacket, carIds.Count - start);
                var packet = new CouplerStatesSync
                {
                    CarNetIds = carIds.GetRange(start, count).ToArray(),
                    Couplers = couplers.GetRange(start, count).ToArray(),
                    Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
                };
                if (player == null)
                    server.SendPacketToAll(packet, reliable: true, excludeSelf: true);
                else
                    server.SendPacketToPlayer(packet, player, reliable: true);
            }
        }

        private static void OnPlayerDisconnected(IPlayer player)
        {
            if (player != null)
                readyPlayers.Remove(player.PlayerId);
        }

        private static void OnPlayerReady(IPlayer player)
        {
            // The host's own loopback player already has the authoritative state
            if (player == null || player.IsHost) return;
            try
            {
                var server = MultiplayerAPI.Server;
                if (server == null) return;
                var spawner = CarSpawner.Instance;
                if (spawner?.allCars == null) return;

                SendSettingsToPlayer(player);
                readyPlayers.Add(player.PlayerId);

                var carIds = new List<ushort>();
                var couplers = new List<byte>();
                foreach (var car in spawner.allCars)
                {
                    if (car == null || !TryGetCarNetId(car, out var carId)) continue;
                    foreach (var coupler in new[] { car.frontCoupler, car.rearCoupler })
                    {
                        if (coupler == null) continue;
                        carIds.Add(carId);
                        couplers.Add(EncodeCoupler(coupler.isFrontCoupler, coupler.state));
                    }
                }
                SendCouplerStates(carIds, couplers, player);

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
                HostMarkDirty(coupler);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Server apply request failed: {e.Message}"); }
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
                Limit = HostJointLimit(a, b),
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };
            MultiplayerAPI.Server.SendPacketToPlayer(pkt, player, reliable: true);
        }

        private static void OnClientCouplerStatesSync(CouplerStatesSync packet)
        {
            var count = Math.Min(packet.CarNetIds?.Length ?? 0, packet.Couplers?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                var encoded = packet.Couplers![i];
                if (!TryResolveCoupler(packet.CarNetIds![i], (encoded & 0x80) != 0, out var coupler)) continue;
                try
                {
                    coupler.state = (ChainCouplerInteraction.State)(encoded & 0x7F);
                    HookManager.UpdateHookVisualStateFromCouplerState(coupler);
                }
                catch (Exception e) { Main.ErrorLog(() => $"[MP] Client apply sync failed: {e.Message}"); }
            }
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
                Limit = HostJointLimit(a, b),
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };
            MultiplayerAPI.Server.SendPacketToAll(pkt, reliable: true, excludeSelf: true);
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
            MultiplayerAPI.Server.SendPacketToAll(pkt, reliable: true, excludeSelf: true);
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

                // The client's cars lag behind the host's; don't keep a joint sized from their distance
                var joint = (a != null ? JointManager.GetTensionJoint(a) : null) ?? (b != null ? JointManager.GetTensionJoint(b) : null);
                if (joint != null)
                    JointLimitRatchet.Attach(joint, packet.Limit);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client joint create failed: {e.Message}"); }
        }

        /// <summary>
        /// The limit the host's joint settles at (not its current one, which may still be narrowing), so the
        /// client's joint narrows to the same value.
        /// </summary>
        private static float HostJointLimit(Coupler a, Coupler b) => JointManager.TargetLimit;

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

        private static SettingsSync CreateSettingsPacket()
        {
            var s = Main.settings;
            return new SettingsSync
            {
                ProfileId = s.selectedCoupler ?? "",
                StrengthPreset = (byte)s.strengthValues,
                KnuckleStrength = s.knuckleStrength,
                DrawgearSpringRate = s.drawgearSpringRate,
                DrawgearDamperRate = s.drawgearDamperRate,
                ShowBuffersWithKnuckles = s.showBuffersWithKnuckles,
                AutoCoupleThreshold = s.autoCoupleThreshold,
                MinimumSeparationDistance = s.minimumSeparationDistance,
                AutoAirAndMuMode = s.autoAirAndMuMode,
                AutoCouplingMode = s.autoCouplingMode,
                DisableFrontCouplersOnSteamLocos = s.disableFrontCouplersOnSteamLocos,
                Tick = MultiplayerAPI.Instance?.CurrentTick ?? 0,
            };
        }

        private static void SendSettingsToPlayer(IPlayer player)
        {
            if (MultiplayerAPI.Server == null) return;
            try
            {
                MultiplayerAPI.Server.SendPacketToPlayer(CreateSettingsPacket(), player, reliable: true);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] SendSettingsToPlayer failed: {e.Message}"); }
        }

        public static void HostBroadcastSettings()
        {
            if (!IsHost || MultiplayerAPI.Server == null) return;
            try
            {
                MultiplayerAPI.Server.SendPacketToAll(CreateSettingsPacket(), reliable: true, excludeSelf: true);
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] HostBroadcastSettings failed: {e.Message}"); }
        }

        private static void OnClientSettingsSync(SettingsSync packet)
        {
            try
            {
                var s = Main.settings;
                if (!string.IsNullOrEmpty(packet.ProfileId) && CouplerProfiles.GetById(packet.ProfileId) != null)
                    s.selectedCoupler = packet.ProfileId;
                else
                    Main.ErrorLog(() => $"[MP] Host coupler profile '{packet.ProfileId}' is not available, keeping '{s.selectedCoupler}'");
                ApplySettingsValues(packet);
                s.OnChange();
                Main.DebugLog(() => $"[MP] Client applied host settings: profile={s.selectedCoupler}, strength={s.strengthValues}, spring={s.GetSpringRate():E2}, damper={s.GetDamperRate():E2}, autoCoupling={s.autoCouplingMode}");
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Client apply settings sync failed: {e.Message}"); }
        }

        private static void ApplySettingsValues(SettingsSync packet)
        {
            var s = Main.settings;
            if (Enum.IsDefined(typeof(strengthPreset), (int)packet.StrengthPreset))
                s.strengthValues = (strengthPreset)packet.StrengthPreset;
            s.knuckleStrength = packet.KnuckleStrength;
            s.drawgearSpringRate = packet.DrawgearSpringRate;
            s.drawgearDamperRate = packet.DrawgearDamperRate;
            s.showBuffersWithKnuckles = packet.ShowBuffersWithKnuckles;
            s.autoCoupleThreshold = packet.AutoCoupleThreshold;
            s.minimumSeparationDistance = packet.MinimumSeparationDistance;
            s.autoAirAndMuMode = packet.AutoAirAndMuMode;
            s.autoCouplingMode = packet.AutoCouplingMode;
            s.disableFrontCouplersOnSteamLocos = packet.DisableFrontCouplersOnSteamLocos;
        }

        /// <summary>
        /// Put back the settings the client had before joining, so the host's values
        /// are not persisted to the client's own settings file.
        /// The world is unloading at this point, so no runtime refresh is triggered.
        /// </summary>
        private static void RestoreClientLocalSettings()
        {
            var local = clientLocalSettings;
            clientLocalSettings = null;
            if (local == null) return;
            try
            {
                var s = Main.settings;
                s.selectedCoupler = local.ProfileId;
                s.lastCouplerProfile = s.couplerProfile;
                ApplySettingsValues(local);
                Main.DebugLog(() => $"[MP] Restored local settings after session: profile={s.selectedCoupler}, strength={s.strengthValues}");
            }
            catch (Exception e) { Main.ErrorLog(() => $"[MP] Restoring local settings failed: {e.Message}"); }
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
            MultiplayerAPI.Server.SendPacketToAll(packet, reliable: true, excludeSelf: true);
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
            MultiplayerAPI.Server.SendPacketToAll(packet, reliable: true, excludeSelf: true);
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
