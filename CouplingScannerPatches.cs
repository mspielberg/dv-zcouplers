using HarmonyLib;
using System.Collections;
using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles coupling scanner functionality and patches
    /// </summary>
    public static class CouplingScannerPatches
    {
        /// <summary>
        /// Get the coupling scanner component from a coupler
        /// </summary>
        public static CouplingScanner? GetScanner(Coupler coupler)
        {
            return coupler.visualCoupler?.GetComponent<CouplingScanner>();
        }

        /// <summary>
        /// Stop and kill a coupling scanner safely
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
        /// Restart a coupling scanner safely
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
        /// Separate cars after uncoupling with temporary scanner disable
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
                // Removed verbose scanner restart log
            }
        }

        /// <summary>
        /// Patches for coupling scanner behavior
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
        /// Ensure CouplingScanners stay active when not in view
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
        /// Handle coupling scanner initialization
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
                        
                        // Only create compression joint if both couplers are ready to couple (not parked/unlocked)
                        // and we're not in the middle of save loading
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
                        // Don't destroy compression joints when scanners lose contact
                        // Buffer physics should persist until proper uncoupling or car deletion
                        Main.DebugLog(() => $"Scanner lost contact - preserving compression joint for {coupler.train.ID} to maintain buffer physics");
                    }
                };
            }
        }

        /// <summary>
        /// Custom coupling scanner master coroutine
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
                if (scanner?.gameObject == null || scanner.transform == null)
                    return null;
                    
                var car = TrainCar.Resolve(scanner.gameObject);
                if (car == null)
                    return null;
                    
                return scanner.transform.localPosition.z > 0 ? car.frontCoupler : car.rearCoupler;
            }

            private static void TryCouple(Coupler coupler)
            {
                if (coupler.IsCoupled() || coupler.train.derailed)
                    return;
                var otherCoupler = coupler.GetFirstCouplerInRange();
                if (otherCoupler == null || otherCoupler.train.derailed)
                    return;
                coupler.CoupleTo(otherCoupler, viaChainInteraction: true);
            }

            private const float StaticOffset = 0.5f;
            private static IEnumerator ReplacementCoro(CouplingScanner __instance)
            {
                yield return null;
                var coupler = GetCoupler(__instance);
                if (coupler == null)
                {
                    // Removed verbose MasterCoro exit log
                    __instance.masterCoro = null;
                    yield break;
                }
                
                if (coupler.IsCoupled())
                {
                    // Removed verbose MasterCoro exit log
                    __instance.masterCoro = null;
                    yield break;
                }
                else
                {
                    Main.DebugLog(() =>
                    {
                        var otherCoupler = GetCoupler(__instance.nearbyScanner);
                        if (otherCoupler == null)
                            return $"{coupler.train.ID} {coupler.Position()}: MasterCoro started with null nearby coupler";
                        return $"{coupler.train.ID} {coupler.Position()}: MasterCoro started with {otherCoupler.train.ID} {otherCoupler.Position()}";
                    });
                    
                    // Check if both couplers are in Attached_Tight state but not actually coupled (save loading case)
                    var otherCoupler = GetCoupler(__instance.nearbyScanner);
                    if (otherCoupler != null && 
                        coupler.state == ChainCouplerInteraction.State.Attached_Tight && 
                        otherCoupler.state == ChainCouplerInteraction.State.Attached_Tight &&
                        !coupler.IsCoupled() && !otherCoupler.IsCoupled())
                    {
                        Main.DebugLog(() => $"Found couplers in Attached_Tight state but not coupled - triggering native coupling between {coupler.train.ID} and {otherCoupler.train.ID}");
                        TryCouple(coupler);
                        
                        // After coupling, create tension joints if they don't exist
                        if (coupler.IsCoupled() && otherCoupler.IsCoupled())
                        {
                            if (!JointManager.HasTensionJoint(coupler))
                            {
                                Main.DebugLog(() => $"Creating tension joint for {coupler.train.ID} {coupler.Position()} after MasterCoro coupling");
                                JointManager.ForceCreateTensionJoint(coupler);
                            }
                            
                            if (!JointManager.HasTensionJoint(otherCoupler))
                            {
                                Main.DebugLog(() => $"Creating tension joint for {otherCoupler.train.ID} {otherCoupler.Position()} after MasterCoro coupling");
                                JointManager.ForceCreateTensionJoint(otherCoupler);
                            }
                            
                            // Exit since coupling is complete
                            // Removed verbose MasterCoro exit log
                            __instance.masterCoro = null;
                            yield break;
                        }
                    }
                }

                var wait = WaitFor.Seconds(0.1f);
                while (true)
                {
                    yield return wait;
                    
                    // Safety check for null references
                    if (__instance?.transform == null || __instance.nearbyScanner?.transform == null)
                    {
                        // Removed verbose MasterCoro exit log
                        break;
                    }
                    
                    var offset = __instance.transform.InverseTransformPoint(__instance.nearbyScanner.transform.position);
                    if (Mathf.Abs(offset.x) > 1.6f || Mathf.Abs(offset.z) > 2f)
                    {
                        break;
                    }
                    else
                    {
                        Main.DebugLog(coupler.train, () => $"{coupler.train.ID}: offset.z = {offset.z}");
                        var compression = StaticOffset - offset.z;
                        var nearbyNearbyeCoupler = GetCoupler(__instance.nearbyScanner);
                        if (__instance.nearbyScanner.isActiveAndEnabled
                            && compression > Main.settings.autoCoupleThreshold * 1e-3f
                            && KnuckleCouplers.IsReadyToCouple(coupler)
                            && nearbyNearbyeCoupler != null
                            && KnuckleCouplers.IsReadyToCouple(nearbyNearbyeCoupler))
                        {
                            Main.DebugLog(() => $"{coupler.train.ID} {coupler.Position()}: auto coupling due to compression={compression}");
                            TryCouple(coupler);
                        }
                    }
                }
                __instance?.Unpair(true);
            }
        }
    }
}
