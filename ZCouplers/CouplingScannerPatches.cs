using System.Collections;

using HarmonyLib;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles coupling scanner functionality and Harmony patches.
    /// </summary>
    public static class CouplingScannerPatches
    {
        /// <summary>
        /// Get the coupling scanner component from a coupler.
        /// </summary>
        public static CouplingScanner? GetScanner(Coupler coupler)
        {
            try
            {
                return coupler?.visualCoupler?.GetComponent<CouplingScanner>();
            }
            catch (System.Exception)
            {
                // Return null if any exception occurs during component access
                return null;
            }
        }

        /// <summary>
        /// Stop and kill a coupling scanner safely.
        /// </summary>
        public static void KillCouplingScanner(Coupler coupler)
        {
            if (coupler == null)
                return;

            try
            {
                var scanner = GetScanner(coupler);
                if (scanner?.masterCoro != null)
                {
                    scanner.StopCoroutine(scanner.masterCoro);
                    scanner.masterCoro = null;
                }
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error killing coupling scanner: {ex.Message}");
            }
        }

        /// <summary>
        /// Restart a coupling scanner safely.
        /// </summary>
        public static void RestartCouplingScanner(Coupler coupler)
        {
            if (coupler == null)
                return;

            try
            {
                var scanner = GetScanner(coupler);
                if (scanner != null && scanner.masterCoro == null && scanner.isActiveAndEnabled)
                {
                    scanner.masterCoro = scanner.StartCoroutine(scanner.MasterCoro());
                }
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error restarting coupling scanner: {ex.Message}");
            }
        }

        /// <summary>
        /// Separate cars after uncoupling with temporary scanner disable.
        /// </summary>
        public static void SeparateCarsAfterUncoupling(Coupler coupler1, Coupler coupler2)
        {
            if (coupler1?.train?.gameObject == null || coupler2?.train?.gameObject == null)
                return;

            try
            {
                // Temporarily disable coupling scanners to prevent immediate recoupling
                var scanner1 = GetScanner(coupler1);
                var scanner2 = GetScanner(coupler2);

                if (scanner1 != null)
                {
                    scanner1.enabled = false;
                    scanner1.StartCoroutine(ReEnableScanner(scanner1, coupler1.train.ID, 0.2f));
                }
                if (scanner2 != null)
                {
                    scanner2.enabled = false;
                    scanner2.StartCoroutine(ReEnableScanner(scanner2, coupler2.train.ID, 0.2f));
                }
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error in uncoupling cleanup: {ex.Message}");
            }
        }

        private static IEnumerator ReEnableScanner(CouplingScanner scanner, string trainId, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (scanner != null)
            {
                scanner.enabled = true;
            }
        }

        /// <summary>
        /// Patches for coupling scanner behavior.
        /// </summary>
        [HarmonyPatch(typeof(Coupler), nameof(Coupler.AutoCouple))]
        public static class AutoCouplePatch
        {
            public static void Postfix(Coupler __instance, ref IEnumerator __result)
            {
                var scanner = GetScanner(__instance);
                if (scanner == null)
                    return;

                scanner.enabled = false;

                __result = new EnumeratorWrapper(__result, () => scanner.enabled = true);
            }
        }

        /// <summary>
        /// Ensure CouplingScanners stay active when not in view.
        /// </summary>
        [HarmonyPatch(typeof(ChainCouplerVisibilityOptimizer), nameof(ChainCouplerVisibilityOptimizer.Disable))]
        public static class ChainCouplerVisibilityOptimizerDisablePatch
        {
            public static bool Prefix(ChainCouplerVisibilityOptimizer __instance)
            {
                if (!__instance.enabled)
                    return false;
                __instance.enabled = false;
                __instance.chain.SetActive(false);
                return false;
            }
        }

        /// <summary>
        /// Handle coupling scanner initialization.
        /// </summary>
        [HarmonyPatch(typeof(CouplingScanner), nameof(CouplingScanner.Start))]
        public static class CouplingScannerStartPatch
        {
            public static void Postfix(CouplingScanner __instance)
            {
                var scanner = __instance;
                __instance.ScanStateChanged += (CouplingScanner otherScanner) =>
                {
                    if (scanner == null)
                        return;
                    var car = TrainCar.Resolve(scanner.gameObject);
                    if (car == null)
                        return;
                    var coupler = scanner.transform.localPosition.z > 0 ? car.frontCoupler : car.rearCoupler;
                    if (coupler == null)
                        return;

                    if (otherScanner != null)
                    {
                        var otherCar = TrainCar.Resolve(otherScanner.gameObject);
                        if (otherCar == null)
                            return;
                        var otherCoupler = otherScanner.transform.localPosition.z > 0 ? otherCar.frontCoupler : otherCar.rearCoupler;
                        if (otherCoupler == null)
                            return;

                        // Create a compression joint only if both couplers are ready and not during save loading.
                        if (coupler.rigidCJ == null && otherCoupler.rigidCJ == null
                            && KnuckleCouplers.IsReadyToCouple(coupler)
                            && KnuckleCouplers.IsReadyToCouple(otherCoupler)
                            && !SaveManager.IsLoadingFromSave)
                        {
                            Main.DebugLog(() => $"Creating compression joint between {coupler.train.ID} and {otherCoupler.train.ID} - both couplers ready");
                            JointManager.CreateCompressionJoint(coupler, otherCoupler);
                        }
                        else if (SaveManager.IsLoadingFromSave)
                        {
                            Main.DebugLog(() => $"Skipping compression joint creation during save loading between {coupler.train.ID} and {otherCoupler.train.ID}");
                        }
                        else if (coupler.rigidCJ == null && otherCoupler.rigidCJ == null)
                        {
                            Main.DebugLog(() => $"Skipping compression joint creation between {coupler.train.ID} and {otherCoupler.train.ID} - couplers not ready (coupler ready: {KnuckleCouplers.IsReadyToCouple(coupler)}, other ready: {KnuckleCouplers.IsReadyToCouple(otherCoupler)})");
                        }
                    }
                    else
                    {
                        // Preserve compression joints when scanners lose contact; buffer physics should persist until proper uncoupling or car deletion.
                        Main.DebugLog(() => $"Scanner lost contact - preserving compression joint for {coupler.train.ID} to maintain buffer physics");
                    }
                };
            }
        }

        /// <summary>
        /// Custom coupling scanner master coroutine.
        /// </summary>
        [HarmonyPatch(typeof(CouplingScanner), nameof(CouplingScanner.MasterCoro))]
        public static class CouplerScannerMasterCoroPatch
        {
            public static bool Prefix(CouplingScanner __instance, ref IEnumerator __result)
            {
                __result = ReplacementCoro(__instance);
                return false;
            }

            private static Coupler? GetCoupler(CouplingScanner scanner)
            {
                try
                {
                    if (scanner?.gameObject == null || scanner.transform == null)
                        return null;

                    var car = TrainCar.Resolve(scanner.gameObject);
                    if (car == null)
                        return null;

                    return scanner.transform.localPosition.z > 0 ? car.frontCoupler : car.rearCoupler;
                }
                catch (System.Exception)
                {
                    // Return null if any exception occurs
                    return null;
                }
            }

            private static void TryCouple(Coupler coupler)
            {
                if (coupler.IsCoupled() || coupler.train.derailed)
                    return;
                var otherCoupler = coupler.GetFirstCouplerInRange();
                if (otherCoupler == null || otherCoupler.train.derailed)
                    return;
                coupler.CoupleTo(otherCoupler, viaChainInteraction: true);

                // Full Automatic Mode: connect air hoses and open brake valves.
                if (Main.settings.EffectiveFullAutomaticMode && coupler.IsCoupled() && otherCoupler.IsCoupled())
                {
                    TryConnectAirSystemsAutomatically(coupler, otherCoupler);
                }
            }

            private static void TryConnectAirSystemsAutomatically(Coupler coupler, Coupler otherCoupler)
            {
                try
                {
                    // Connect air hoses if both have them and they're not already connected.
                    var hoseAndCock1 = coupler.hoseAndCock;
                    var hoseAndCock2 = otherCoupler.hoseAndCock;

                    if (hoseAndCock1 != null && hoseAndCock2 != null)
                    {
                        // Open brake valves (angle cocks) on both sides first.
                        if (!coupler.IsCockOpen)
                        {
                            coupler.IsCockOpen = true;
                            Main.DebugLog(() => $"Auto-opened brake valve on {coupler.train.ID} {(coupler.isFrontCoupler ? "front" : "rear")}");
                        }

                        if (!otherCoupler.IsCockOpen)
                        {
                            otherCoupler.IsCockOpen = true;
                            Main.DebugLog(() => $"Auto-opened brake valve on {otherCoupler.train.ID} {(otherCoupler.isFrontCoupler ? "front" : "rear")}");
                        }

                        // Connect air hoses if not already connected.
                        if (!hoseAndCock1.IsHoseConnected && !hoseAndCock2.IsHoseConnected)
                        {
                            // Use the game's native connection system.
                            coupler.ConnectAirHose(otherCoupler, playAudio: true);
                            Main.DebugLog(() => $"Auto-connected air hoses between {coupler.train.ID} and {otherCoupler.train.ID}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Error in automatic air system connection: {ex.Message}");
                }
            }

            private const float StaticOffset = 0.5f;
            private static IEnumerator ReplacementCoro(CouplingScanner __instance)
            {
                yield return null;
                var coupler = GetCoupler(__instance);
                if (coupler == null)
                {
                    // Exit early if no coupler is available.
                    __instance.masterCoro = null;
                    yield break;
                }

                if (coupler.IsCoupled())
                {
                    // Already coupled; stop the coroutine.
                    __instance.masterCoro = null;
                    yield break;
                }
                else
                {
                    // Omit routine start logs

                    // Check if both couplers are in Attached_Tight state but not actually coupled (save loading case)
                    var otherCoupler = GetCoupler(__instance.nearbyScanner);
                    if (otherCoupler != null &&
                        coupler.state == ChainCouplerInteraction.State.Attached_Tight &&
                        otherCoupler.state == ChainCouplerInteraction.State.Attached_Tight &&
                        !coupler.IsCoupled() && !otherCoupler.IsCoupled())
                    {
                        Main.DebugLog(() => $"Attached_Tight but not coupled -> triggering native coupling between {coupler.train.ID} and {otherCoupler.train.ID}");
                        TryCouple(coupler);

                        // After coupling, create tension joints if they don't exist
                        if (coupler.IsCoupled() && otherCoupler.IsCoupled())
                        {
                            if (!JointManager.HasTensionJoint(coupler))
                            {
                                Main.DebugLog(() => $"Creating tension joint after MasterCoro coupling: {coupler.train.ID} {coupler.Position()}");
                                JointManager.ForceCreateTensionJoint(coupler);
                            }

                            if (!JointManager.HasTensionJoint(otherCoupler))
                            {
                                Main.DebugLog(() => $"Creating tension joint after MasterCoro coupling: {otherCoupler.train.ID} {otherCoupler.Position()}");
                                JointManager.ForceCreateTensionJoint(otherCoupler);
                            }

                            // Handle Full Automatic Mode for already coupled cars during save loading
                            if (Main.settings.EffectiveFullAutomaticMode)
                            {
                                TryConnectAirSystemsAutomatically(coupler, otherCoupler);
                            }

                            // Coupling complete; stop the coroutine.
                            __instance.masterCoro = null;
                            yield break;
                        }
                    }

                    // Fix mismatched save-loading states where both are ready but states differ.
                    if (otherCoupler != null && !coupler.IsCoupled() && !otherCoupler.IsCoupled())
                    {
                        bool couplerReady = KnuckleCouplers.IsReadyToCouple(coupler);
                        bool otherReady = KnuckleCouplers.IsReadyToCouple(otherCoupler);

                        // If both couplers are ready but have mismatched states, fix them and couple
                        if (couplerReady && otherReady)
                        {
                            bool needsFix = false;

                            // Fix scenarios like: one shows Parked/Dangling, other shows Attached_*
                            if ((coupler.state == ChainCouplerInteraction.State.Parked || coupler.state == ChainCouplerInteraction.State.Dangling) &&
                                (otherCoupler.state == ChainCouplerInteraction.State.Attached_Tight || otherCoupler.state == ChainCouplerInteraction.State.Attached_Loose))
                            {
                                needsFix = true;
                            }
                            else if ((otherCoupler.state == ChainCouplerInteraction.State.Parked || otherCoupler.state == ChainCouplerInteraction.State.Dangling) &&
                                     (coupler.state == ChainCouplerInteraction.State.Attached_Tight || coupler.state == ChainCouplerInteraction.State.Attached_Loose))
                            {
                                needsFix = true;
                            }

                            if (needsFix)
                            {
                                Main.DebugLog(() => $"Fixing mismatched save states and coupling {coupler.train.ID} ({coupler.state}) <-> {otherCoupler.train.ID} ({otherCoupler.state})");

                                // Set both to Dangling state first (ready but uncoupled)
                                coupler.state = ChainCouplerInteraction.State.Dangling;
                                otherCoupler.state = ChainCouplerInteraction.State.Dangling;

                                TryCouple(coupler);

                                // After coupling, create tension joints if they don't exist
                                if (coupler.IsCoupled() && otherCoupler.IsCoupled())
                                {
                                    if (!JointManager.HasTensionJoint(coupler))
                                    {
                                        Main.DebugLog(() => $"Creating tension joint after save-state fix: {coupler.train.ID} {coupler.Position()}");
                                        JointManager.ForceCreateTensionJoint(coupler);
                                    }

                                    if (!JointManager.HasTensionJoint(otherCoupler))
                                    {
                                        Main.DebugLog(() => $"Creating tension joint after save-state fix: {otherCoupler.train.ID} {otherCoupler.Position()}");
                                        JointManager.ForceCreateTensionJoint(otherCoupler);
                                    }

                                    // Handle Full Automatic Mode for couplers fixed from mismatched states
                                    if (Main.settings.EffectiveFullAutomaticMode)
                                    {
                                        TryConnectAirSystemsAutomatically(coupler, otherCoupler);
                                    }

                                    // Exit since coupling is complete
                                    __instance.masterCoro = null;
                                    yield break;
                                }
                            }
                        }
                    }
                }

                var wait = WaitFor.Seconds(0.1f);
                while (true)
                {
                    yield return wait;

                    // Safety check for null references
                    if (__instance?.transform == null || __instance.gameObject == null)
                    {
                        // Exit if instance is invalid
                        break;
                    }

                    // Additional safety check for nearby scanner
                    if (__instance.nearbyScanner?.transform == null || __instance.nearbyScanner.gameObject == null)
                    {
                        // Exit if nearby scanner is invalid
                        break;
                    }

                    var offset = __instance.transform.InverseTransformPoint(__instance.nearbyScanner.transform.position);
                    if (Mathf.Abs(offset.x) > 1.6f || Mathf.Abs(offset.z) > 2f)
                    {
                        break;
                    }
                    else
                    {
                        // Omit continuous offset logging
                        var compression = StaticOffset - offset.z;
                        var nearbyNearbyeCoupler = GetCoupler(__instance.nearbyScanner);
                        if (__instance.nearbyScanner.isActiveAndEnabled
                            && compression > Main.settings.autoCoupleThreshold * 1e-3f
                            && KnuckleCouplers.IsReadyToCouple(coupler)
                            && nearbyNearbyeCoupler != null
                            && KnuckleCouplers.IsReadyToCouple(nearbyNearbyeCoupler))
                        {
                            Main.DebugLog(() => $"{coupler.train.ID} {coupler.Position()}: auto-coupling due to compression={compression:F3}");
                            TryCouple(coupler);
                        }
                    }
                }

                // Safely unpair with additional null checks
                try
                {
                    if (__instance != null && __instance.gameObject != null)
                    {
                        __instance.Unpair(true);
                    }
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Error during coupling scanner unpair: {ex.Message}");
                }
                finally
                {
                    if (__instance != null)
                    {
                        __instance.masterCoro = null;
                    }
                }
            }
        }
    }
}