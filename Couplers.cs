using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DvMod.ZCouplers
{
    public static class Couplers
    {
        // Custom tension joint management
        private static readonly Dictionary<Coupler, ConfigurableJoint> customTensionJoints = new Dictionary<Coupler, ConfigurableJoint>();
        
        // Track when joints were last created to prevent rapid recreation
        private static readonly Dictionary<Coupler, float> lastJointCreationTime = new Dictionary<Coupler, float>();
        private const float MinJointCreationInterval = 2.0f; // Seconds between joint creation attempts
        
        private const float LooseChainLength = 1.1f;
        private const float TightChainLength = 1.0f;
        private const float TightenSpeed = 0.1f;
        private const float BufferTravel = 0.25f;

        private static CouplingScanner? GetScanner(Coupler coupler)
        {
            return coupler.visualCoupler?.GetComponent<CouplingScanner>();
        }

        private static void KillCouplingScanner(Coupler coupler)
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
                Main.DebugLog(() => $"Error killing coupling scanner: {ex.Message}");
            }
        }

        private static void DestroyJointsBetweenCars(TrainCar car1, TrainCar car2)
        {
            if (car1?.gameObject == null || car2?.gameObject == null)
                return;
                
            try
            {
                Main.DebugLog(() => $"CLEANUP: Destroying all joints between {car1.ID} and {car2.ID}");
                
                // Check all joints on car1 that connect to car2
                var jointsOnCar1 = car1.GetComponents<Joint>();
                foreach (var joint in jointsOnCar1)
                {
                    if (joint?.connectedBody != null && joint.connectedBody.gameObject == car2.gameObject)
                    {
                        Main.DebugLog(() => $"CLEANUP: Destroying {joint.GetType().Name} on {car1.ID} connecting to {car2.ID}");
                        Component.Destroy(joint);
                    }
                }
                
                // Check all joints on car2 that connect to car1
                var jointsOnCar2 = car2.GetComponents<Joint>();
                foreach (var joint in jointsOnCar2)
                {
                    if (joint?.connectedBody != null && joint.connectedBody.gameObject == car1.gameObject)
                    {
                        Main.DebugLog(() => $"CLEANUP: Destroying {joint.GetType().Name} on {car2.ID} connecting to {car1.ID}");
                        Component.Destroy(joint);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error destroying joints between {car1.ID} and {car2.ID}: {ex.Message}");
            }
        }

        private static void LogRemainingJoints(TrainCar car, string context)
        {
            if (car?.gameObject == null)
                return;
                
            try
            {
                var allJoints = car.GetComponents<ConfigurableJoint>();
                var fixedJoints = car.GetComponents<FixedJoint>();
                var springJoints = car.GetComponents<SpringJoint>();
                var hingeJoints = car.GetComponents<HingeJoint>();
                
                Main.DebugLog(() => $"JOINT DEBUG {context} - {car.ID}: ConfigurableJoints={allJoints.Length}, FixedJoints={fixedJoints.Length}, SpringJoints={springJoints.Length}, HingeJoints={hingeJoints.Length}");
                
                foreach (var joint in allJoints)
                {
                    if (joint?.connectedBody != null)
                    {
                        var connectedCar = TrainCar.Resolve(joint.connectedBody.gameObject);
                        Main.DebugLog(() => $"  ConfigurableJoint: {car.ID} -> {connectedCar?.ID ?? "unknown"}");
                    }
                }
                
                foreach (var joint in fixedJoints)
                {
                    if (joint?.connectedBody != null)
                    {
                        var connectedCar = TrainCar.Resolve(joint.connectedBody.gameObject);
                        Main.DebugLog(() => $"  FixedJoint: {car.ID} -> {connectedCar?.ID ?? "unknown"}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error logging joints for {car.ID}: {ex.Message}");
            }
        }

        private static void RestartCouplingScanner(Coupler coupler)
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
                Main.DebugLog(() => $"Error restarting coupling scanner: {ex.Message}");
            }
        }
        
        private static void SeparateCarsAfterUncoupling(Coupler coupler1, Coupler coupler2)
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
                    scanner1.StartCoroutine(ReEnableScanner(scanner1, coupler1.train.ID, 3.0f));
                }
                if (scanner2 != null)
                {
                    scanner2.enabled = false;
                    scanner2.StartCoroutine(ReEnableScanner(scanner2, coupler2.train.ID, 3.0f));
                }
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error in uncoupling cleanup: {ex.Message}");
            }
        }
        
        private static IEnumerator ReEnableScanner(CouplingScanner scanner, string trainId, float delay)
        {
            yield return new WaitForSeconds(delay);
            
            if (scanner != null)
            {
                scanner.enabled = true;
                Main.DebugLog(() => $"Re-enabled coupling scanner for {trainId}");
            }
        }
        
        private static void DestroyAllJoints(Coupler coupler)
        {
            if (coupler?.train?.gameObject == null)
                return;
                
            try
            {
                var trainCar = coupler.train;
                Main.DebugLog(() => $"Destroying all joints on {trainCar.ID}");
                
                // Destroy all Unity joints on this car to ensure complete disconnection
                var allJoints = trainCar.GetComponents<ConfigurableJoint>();
                foreach (var joint in allJoints)
                {
                    if (joint != null)
                        Component.Destroy(joint);
                }
                
                var fixedJoints = trainCar.GetComponents<FixedJoint>();
                foreach (var joint in fixedJoints)
                {
                    if (joint != null)
                        Component.Destroy(joint);
                }
                
                var springJoints = trainCar.GetComponents<SpringJoint>();
                foreach (var joint in springJoints)
                {
                    if (joint != null)
                        Component.Destroy(joint);
                }
                
                var hingeJoints = trainCar.GetComponents<HingeJoint>();
                foreach (var joint in hingeJoints)
                {
                    if (joint != null)
                        Component.Destroy(joint);
                }
                
                // Clear the rigidCJ and jointCoroRigid from both couplers on this car
                if (trainCar.frontCoupler != null)
                {
                    trainCar.frontCoupler.rigidCJ = null;
                    trainCar.frontCoupler.jointCoroRigid = null;
                }
                if (trainCar.rearCoupler != null)
                {
                    trainCar.rearCoupler.rigidCJ = null;
                    trainCar.rearCoupler.jointCoroRigid = null;
                }
                
                Main.DebugLog(() => $"Completed joint destruction for {trainCar.ID}");
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error in joint cleanup: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Coupler), nameof(Coupler.CreateJoints))]
        public static class CreateJointsPatch
        {
            public static bool Prefix(Coupler __instance)
            {
                // Allow tender joints to use original behavior
                if (__instance.train.GetComponent<DV.SteamTenderAutoCoupleMechanism>() != null && !__instance.isFrontCoupler)
                {
                    return true;
                }

                // Prevent joint creation during save loading to avoid physics instability
                if (SaveManager.IsLoadingFromSave)
                {
                    Main.DebugLog(() => $"Skipping joint creation during save loading: {__instance.train.ID}");
                    return true;
                }

                // Safety checks to prevent joint creation in unstable conditions
                if (__instance.train.derailed || __instance.coupledTo?.train.derailed == true)
                {
                    Main.DebugLog(() => $"Skipping joint creation - train derailed: {__instance.train.ID}");
                    return true;
                }

                var velocity1 = __instance.train.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
                var velocity2 = __instance.coupledTo?.train.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
                if (velocity1 > 5f || velocity2 > 5f)
                {
                    Main.DebugLog(() => $"Skipping joint creation - cars moving too fast: {__instance.train.ID} ({velocity1:F1} m/s) to {__instance.coupledTo?.train.ID} ({velocity2:F1} m/s)");
                    return true;
                }

                // Prevent rapid joint recreation
                var currentTime = Time.time;
                if (lastJointCreationTime.TryGetValue(__instance, out var lastTime) && (currentTime - lastTime) < MinJointCreationInterval)
                {
                    Main.DebugLog(() => $"Skipping joint creation - too soon after last creation: {__instance.train.ID}");
                    return true;
                }

                // Prevent duplicate joint creation
                if (customTensionJoints.ContainsKey(__instance) || (__instance.coupledTo != null && customTensionJoints.ContainsKey(__instance.coupledTo)))
                {
                    Main.DebugLog(() => $"Skipping joint creation - joints already exist: {__instance.train.ID}");
                    return false;
                }

                // Ensure we have a valid coupled partner
                if (__instance.coupledTo == null)
                {
                    Main.DebugLog(() => $"Skipping joint creation - no coupled partner: {__instance.train.ID}");
                    return true;
                }

                Main.DebugLog(() => $"Creating tension joint between {__instance.train.ID} and {__instance.coupledTo.train.ID}");
                
                // Record the time of joint creation
                lastJointCreationTime[__instance] = currentTime;
                lastJointCreationTime[__instance.coupledTo] = currentTime;
                
                // Create custom joints
                CreateTensionJoint(__instance);
                var breaker = __instance.gameObject.AddComponent<CouplerBreaker>();
                if (customTensionJoints.TryGetValue(__instance, out var tensionJoint))
                    breaker.joint = tensionJoint;
                if (__instance.rigidCJ == null && __instance.coupledTo.rigidCJ == null)
                    CreateCompressionJoint(__instance, __instance.coupledTo);
                KillCouplingScanner(__instance);
                KillCouplingScanner(__instance.coupledTo);
                return false;
            }
        }

        [HarmonyPatch(typeof(Coupler), nameof(Coupler.Uncouple))]
        public static class UncouplePatch
        {
            private static readonly Dictionary<Coupler, ConfigurableJoint> compressionJoints = new Dictionary<Coupler, ConfigurableJoint>();
            private static readonly Dictionary<Coupler, Coroutine> coros = new Dictionary<Coupler, Coroutine>();
            private static readonly Dictionary<Coupler, Coupler> partnerCouplers = new Dictionary<Coupler, Coupler>();

            public static void Prefix(Coupler __instance)
            {
                Main.DebugLog(() => $"Uncoupling {__instance.train.ID} from {__instance.coupledTo?.train.ID}");
                
                // Store partner coupler reference and clear pending states
                if (__instance.coupledTo != null)
                {
                    partnerCouplers[__instance] = __instance.coupledTo;
                }
                
                // Clear any pending coupler states for these cars
                SaveManager.ClearPendingStatesForCar(__instance.train);
                if (__instance.coupledTo?.train != null)
                    SaveManager.ClearPendingStatesForCar(__instance.coupledTo.train);
                
                // Store compression joints to prevent game from destroying them prematurely
                compressionJoints[__instance] = __instance.rigidCJ;
                __instance.rigidCJ = null;
                coros[__instance] = __instance.jointCoroRigid;
                __instance.jointCoroRigid = null;
                
                // Handle partner coupler's joints as well
                if (__instance.coupledTo != null)
                {
                    compressionJoints[__instance.coupledTo] = __instance.coupledTo.rigidCJ;
                    __instance.coupledTo.rigidCJ = null;
                    coros[__instance.coupledTo] = __instance.coupledTo.jointCoroRigid;
                    __instance.coupledTo.jointCoroRigid = null;
                    Main.DebugLog(() => $"Stored partner coupler joints for {__instance.coupledTo.train.ID}");
                }

                // Destroy all custom joints to ensure clean disconnection
                DestroyTensionJoint(__instance);
                DestroyCompressionJoint(__instance);
                
                // Additional cleanup: destroy any remaining joints between these specific cars
                if (__instance.coupledTo?.train != null)
                {
                    DestroyJointsBetweenCars(__instance.train, __instance.coupledTo.train);
                }
                
                // Debug: Log any remaining joints on both cars
                LogRemainingJoints(__instance.train, "after uncoupling");
                if (__instance.coupledTo?.train != null)
                    LogRemainingJoints(__instance.coupledTo.train, "after uncoupling");
                
                // Restart coupling scanners and apply separation
                RestartCouplingScanner(__instance);
                if (__instance.coupledTo != null)
                {
                    RestartCouplingScanner(__instance.coupledTo);
                    SeparateCarsAfterUncoupling(__instance, __instance.coupledTo);
                }
                    
                Main.DebugLog(() => $"Completed uncoupling cleanup for {__instance.train.ID}");
            }

            public static void Postfix(Coupler __instance)
            {
                // Conditionally restore compression joint based on coupling success
                if (compressionJoints.TryGetValue(__instance, out var storedJoint))
                {
                    if (__instance.IsCoupled())
                    {
                        // Uncoupling failed, restore the joint
                        __instance.rigidCJ = storedJoint;
                        Main.DebugLog(() => $"Restored rigidCJ for {__instance.train.ID} - uncoupling failed, still coupled to {__instance.coupledTo?.train.ID}");
                    }
                    else
                    {
                        // Uncoupling succeeded, destroy the stored joint
                        if (storedJoint != null)
                            Component.Destroy(storedJoint);
                        Main.DebugLog(() => $"Destroyed stored rigidCJ for {__instance.train.ID} - uncoupling succeeded");
                    }
                    compressionJoints.Remove(__instance);
                }
                
                // Conditionally restore coroutine based on coupling success
                if (coros.TryGetValue(__instance, out var storedCoro))
                {
                    if (__instance.IsCoupled())
                    {
                        __instance.jointCoroRigid = storedCoro;
                        Main.DebugLog(() => $"Restored jointCoroRigid for {__instance.train.ID} - uncoupling failed");
                    }
                    else
                    {
                        Main.DebugLog(() => $"Did not restore jointCoroRigid for {__instance.train.ID} - uncoupling succeeded");
                    }
                    coros.Remove(__instance);
                }
                
                // Handle partner coupler's joints and visual state
                if (partnerCouplers.TryGetValue(__instance, out var partnerCoupler))
                {
                    // Handle partner's compression joint
                    if (compressionJoints.TryGetValue(partnerCoupler, out var partnerStoredJoint))
                    {
                        if (partnerCoupler.IsCoupled())
                        {
                            partnerCoupler.rigidCJ = partnerStoredJoint;
                            Main.DebugLog(() => $"Restored partner rigidCJ for {partnerCoupler.train.ID} - uncoupling failed, still coupled to {partnerCoupler.coupledTo?.train.ID}");
                        }
                        else
                        {
                            if (partnerStoredJoint != null)
                                Component.Destroy(partnerStoredJoint);
                            Main.DebugLog(() => $"Destroyed stored partner rigidCJ for {partnerCoupler.train.ID} - uncoupling succeeded");
                        }
                        compressionJoints.Remove(partnerCoupler);
                    }
                    
                    // Handle partner's coroutine
                    if (coros.TryGetValue(partnerCoupler, out var partnerStoredCoro))
                    {
                        if (partnerCoupler.IsCoupled())
                        {
                            partnerCoupler.jointCoroRigid = partnerStoredCoro;
                            Main.DebugLog(() => $"Restored partner jointCoroRigid for {partnerCoupler.train.ID} - uncoupling failed");
                        }
                        else
                        {
                            Main.DebugLog(() => $"Did not restore partner jointCoroRigid for {partnerCoupler.train.ID} - uncoupling succeeded");
                        }
                        coros.Remove(partnerCoupler);
                    }
                    
                    // Update visual state for both couplers
                    KnuckleCouplers.UpdateCouplerVisualState(partnerCoupler, locked: false);
                    partnerCouplers.Remove(__instance);
                    Main.DebugLog(() => $"Updated visual state for both uncoupled couplers: {__instance.train.ID} and {partnerCoupler.train.ID}");
                }
                
                // Update knuckle coupler visual state
                KnuckleCouplers.UpdateCouplerVisualState(__instance, locked: false);
                
                // Log final status
                if (!partnerCouplers.ContainsKey(__instance))
                {
                    Main.DebugLog(() => $"Updated visual state for uncoupled coupler: {__instance.train.ID} {__instance.Position()}");
                }
                else
                {
                    Main.DebugLog(() => $"Updated visual state for uncoupled coupler: {__instance.train.ID} {__instance.Position()}");
                }
            }
        }

        [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.UncoupleSelf))]
        public static class UncoupleSelfPatch
        {
            public static void Postfix(TrainCar __instance)
            {
                Main.DebugLog(() => "TrainCar.UncoupleSelf.Postfix");
                // Remove joints before car is teleported
                DestroyCompressionJoint(__instance.frontCoupler);
                DestroyCompressionJoint(__instance.rearCoupler);
                
                DestroyTensionJoint(__instance.frontCoupler);
                DestroyTensionJoint(__instance.rearCoupler);
                
                KillCouplingScanner(__instance.frontCoupler);
                KillCouplingScanner(__instance.rearCoupler);
            }
        }

        [HarmonyPatch(typeof(CarSpawner), nameof(CarSpawner.PrepareTrainCarForDeleting))]
        public static class PrepareTrainCarForDeletingPatch
        {
            public static void Postfix(TrainCar trainCar)
            {
                try
                {
                    if (trainCar == null)
                        return;
                        
                    Main.DebugLog(() => $"TrainCar.PrepareTrainCarForDeleting.Postfix for {trainCar.ID}");
                    
                    // Clean up joints and scanners before deletion
                    SafeCleanupCoupler(trainCar.frontCoupler);
                    SafeCleanupCoupler(trainCar.rearCoupler);
                }
                catch (System.Exception ex)
                {
                    Main.DebugLog(() => $"Error during car deletion cleanup: {ex.Message}");
                    // Don't rethrow - let the game continue its deletion process
                }
            }
            
            private static void SafeCleanupCoupler(Coupler coupler)
            {
                if (coupler == null || coupler.gameObject == null)
                    return;
                    
                try
                {
                    // Clean up coupler components if objects are still valid
                    DestroyCompressionJoint(coupler);
                    DestroyTensionJoint(coupler);
                    KillCouplingScanner(coupler);
                }
                catch (System.Exception ex)
                {
                    Main.DebugLog(() => $"Error cleaning up coupler: {ex.Message}");
                }
            }
        }

        private static void CreateTensionJoint(Coupler coupler)
        {
            var coupledTo = coupler.coupledTo;
            Main.DebugLog(() => $"TENSION JOINT: Creating for {coupler.train.ID} {coupler.Position()} -> {coupledTo?.train.ID} {coupledTo?.Position()}");
            
            // Calculate actual distance between couplers for debugging
            var actualDistance = Vector3.Distance(coupler.transform.position, coupledTo.transform.position);
            var desiredDistance = TightChainLength;
            
            // Use desired distance for anchor offset - this is what sets the target separation
            var anchorOffset = Vector3.forward * desiredDistance * (coupler.isFrontCoupler ? -1f : 1f);
            
            Main.DebugLog(() => $"TENSION JOINT: Actual distance: {actualDistance:F2}m, desired: {desiredDistance:F2}m, using desired for anchors");

            var cj = coupler.train.gameObject.AddComponent<ConfigurableJoint>();
            cj.autoConfigureConnectedAnchor = false;
            cj.anchor = coupler.transform.localPosition + anchorOffset;
            cj.connectedBody = coupler.coupledTo.train.gameObject.GetComponent<Rigidbody>();
            cj.connectedAnchor = coupler.coupledTo.transform.localPosition;

            // Configure joint motion constraints
            cj.xMotion = ConfigurableJointMotion.Limited;
            cj.yMotion = ConfigurableJointMotion.Limited;
            cj.zMotion = ConfigurableJointMotion.Limited;
            cj.angularXMotion = ConfigurableJointMotion.Limited;
            cj.angularYMotion = ConfigurableJointMotion.Limited;
            cj.angularZMotion = ConfigurableJointMotion.Limited;

            // Set angular limits
            cj.lowAngularXLimit = new SoftJointLimit { limit = 5f };
            cj.highAngularXLimit = new SoftJointLimit { limit = 5f };
            cj.angularYLimit = new SoftJointLimit { limit = 30f };
            cj.angularZLimit = new SoftJointLimit { limit = 5 };

            // Configure spring forces
            cj.angularXLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };
            cj.angularYZLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };

            cj.linearLimit = new SoftJointLimit { limit = desiredDistance };
            cj.linearLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };
            cj.enableCollision = false;
            cj.breakForce = float.PositiveInfinity;
            cj.breakTorque = float.PositiveInfinity;

            // Store tension joint
            customTensionJoints[coupler] = cj;
            Main.DebugLog(() => $"TENSION JOINT: Created successfully for {coupler.train.ID} {coupler.Position()}, total tension joints: {customTensionJoints.Count}");
            
            // Set the joint to the desired tight length immediately
            cj.linearLimit = new SoftJointLimit { limit = TightChainLength };
            Main.DebugLog(() => $"TENSION JOINT: Set final limit to {TightChainLength}m for {coupler.train.ID} {coupler.Position()}");
        }

        public static void TightenChain(Coupler coupler)
        {
            if (!customTensionJoints.TryGetValue(coupler, out var tensionJoint))
            {
                Main.DebugLog(() => $"TIGHTEN CHAIN: No tension joint found for {coupler.train.ID} {coupler.Position()}, checking partner");
                if (coupler.coupledTo != null && customTensionJoints.ContainsKey(coupler.coupledTo))
                    TightenChain(coupler.coupledTo);
                return;
            }
            
            var oldLimit = tensionJoint.linearLimit.limit;
            tensionJoint.linearLimit = new SoftJointLimit { limit = TightChainLength };
            Main.DebugLog(() => $"TIGHTEN CHAIN: Changed limit for {coupler.train.ID} {coupler.Position()} from {oldLimit} to {TightChainLength}");
        }

        public static void LoosenChain(Coupler coupler)
        {
            if (!customTensionJoints.TryGetValue(coupler, out var tensionJoint))
            {
                if (coupler.coupledTo != null && customTensionJoints.ContainsKey(coupler.coupledTo))
                    LoosenChain(coupler.coupledTo);
                return;
            }
            tensionJoint.linearLimit = new SoftJointLimit { limit = LooseChainLength };
        }

        // Ensure CouplingScanners start active
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

        // Ensure CouplingScanners stay active when not in view
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
                        var otherCoupler = otherScanner.transform.localPosition.z > 0 ? otherCar.frontCoupler : otherCar.rearCoupler;
                        
                        // Only create compression joint if both couplers are ready to couple (not parked/unlocked)
                        if (coupler.rigidCJ == null && otherCoupler.rigidCJ == null 
                            && KnuckleCouplers.IsReadyToCouple(coupler) 
                            && KnuckleCouplers.IsReadyToCouple(otherCoupler))
                        {
                            Main.DebugLog(() => $"Creating compression joint between {coupler.train.ID} and {otherCoupler.train.ID} - both couplers ready");
                            CreateCompressionJoint(coupler, otherCoupler);
                        }
                        else if (coupler.rigidCJ == null && otherCoupler.rigidCJ == null)
                        {
                            Main.DebugLog(() => $"Skipping compression joint creation between {coupler.train.ID} and {otherCoupler.train.ID} - couplers not ready (coupler ready: {KnuckleCouplers.IsReadyToCouple(coupler)}, other ready: {KnuckleCouplers.IsReadyToCouple(otherCoupler)})");
                        }
                    }
                    else
                    {
                        DestroyCompressionJoint(coupler);
                    }
                };
            }
        }

        [HarmonyPatch(typeof(CouplingScanner), nameof(CouplingScanner.MasterCoro))]
        public static class CouplerScannerMasterCoroPatch
        {
            public static bool Prefix(CouplingScanner __instance, ref IEnumerator __result)
            {
                __result = ReplacementCoro(__instance);
                return false;
            }

            private static Coupler GetCoupler(CouplingScanner scanner)
            {
                var car = TrainCar.Resolve(scanner.gameObject);
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
                if (coupler.IsCoupled())
                {
                    Main.DebugLog(() => $"{coupler.train.ID} {coupler.Position()}: MasterCoro exiting immediately");
                    __instance.masterCoro = null;
                    yield break;
                }
                else
                {
                    Main.DebugLog(() =>
                    {
                        var otherCoupler = GetCoupler(__instance.nearbyScanner);
                        return $"{coupler.train.ID} {coupler.Position()}: MasterCoro started with {otherCoupler.train.ID} {otherCoupler.Position()}";
                    });
                }

                var wait = WaitFor.Seconds(0.1f);
                while (true)
                {
                    yield return wait;
                    var offset = __instance.transform.InverseTransformPoint(__instance.nearbyScanner.transform.position);
                    if (Mathf.Abs(offset.x) > 1.6f || Mathf.Abs(offset.z) > 2f)
                    {
                        break;
                    }
                    else
                    {
                        Main.DebugLog(coupler.train, () => $"{coupler.train.ID}: offset.z = {offset.z}");
                        var compression = StaticOffset - offset.z;
                        if (KnuckleCouplers.enabled
                            && __instance.nearbyScanner.isActiveAndEnabled
                            && compression > Main.settings.autoCoupleThreshold * 1e-3f
                            && KnuckleCouplers.IsReadyToCouple(coupler)
                            && KnuckleCouplers.IsReadyToCouple(GetCoupler(__instance.nearbyScanner)))
                        {
                            Main.DebugLog(() => $"{coupler.train.ID} {coupler.Position()}: auto coupling due to compression={compression}");
                            TryCouple(coupler);
                        }
                    }
                }
                __instance.Unpair(true);
            }
        }

        internal static readonly Dictionary<Coupler, (Coupler otherCoupler, ConfigurableJoint joint)> bufferJoints =
            new Dictionary<Coupler, (Coupler otherCoupler, ConfigurableJoint joint)>();

        private static void CreateCompressionJoint(Coupler a, Coupler b)
        {
            Main.DebugLog(() => $"Creating compression joint between {TrainCar.Resolve(a.gameObject)?.ID} and {TrainCar.Resolve(b.gameObject)?.ID}");

            // Create rigid (bottoming out) joint
            var bottomedCj = a.train.gameObject.AddComponent<ConfigurableJoint>();
            bottomedCj.autoConfigureConnectedAnchor = false;
            bottomedCj.anchor = a.transform.localPosition + (2 * (a.isFrontCoupler ? Vector3.forward : Vector3.back));
            bottomedCj.connectedBody = b.train.gameObject.GetComponent<Rigidbody>();
            bottomedCj.connectedAnchor = b.transform.localPosition;
            bottomedCj.zMotion = ConfigurableJointMotion.Limited;

            bottomedCj.linearLimit = new SoftJointLimit { limit = BufferTravel + 2f };
            bottomedCj.linearLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };
            bottomedCj.enableCollision = false;
            bottomedCj.breakForce = float.PositiveInfinity;
            bottomedCj.breakTorque = float.PositiveInfinity;

            a.rigidCJ = bottomedCj;

            // Create buffer joint
            var bufferCj = a.train.gameObject.AddComponent<ConfigurableJoint>();
            bufferCj.autoConfigureConnectedAnchor = false;
            bufferCj.anchor = a.transform.localPosition + (2 * (a.isFrontCoupler ? Vector3.forward : Vector3.back));
            bufferCj.connectedBody = b.train.gameObject.GetComponent<Rigidbody>();
            bufferCj.connectedAnchor = b.transform.localPosition;
            bufferCj.zMotion = ConfigurableJointMotion.Limited;

            bufferCj.linearLimit = new SoftJointLimit { limit = 2f };
            bufferCj.linearLimitSpring = new SoftJointLimitSpring
            {
                spring = Main.settings.GetSpringRate(),
                damper = Main.settings.GetDamperRate(),
            };
            bufferCj.enableCollision = false;
            bufferCj.breakForce = float.PositiveInfinity;
            bufferCj.breakTorque = float.PositiveInfinity;

            bufferJoints.Add(a, (b, bufferCj));
            bufferJoints.Add(b, (a, bufferCj));
        }

        private static void DestroyCompressionJoint(Coupler coupler)
        {
            if (coupler == null || !bufferJoints.TryGetValue(coupler, out var result))
                return;

            try
            {
                Main.DebugLog(() => $"Destroying compression joint between {TrainCar.Resolve(coupler.gameObject)?.ID} and {TrainCar.Resolve(result.otherCoupler.gameObject)?.ID}");
                
                // Destroy the joint
                if (result.joint != null)
                    Component.Destroy(result.joint);

                foreach (var c in new Coupler[] { coupler, result.otherCoupler })
                {
                    if (c != null)
                    {
                        try
                        {
                            if (c.jointCoroRigid != null)
                            {
                                c.StopCoroutine(c.jointCoroRigid);
                                c.jointCoroRigid = null;
                            }
                            if (c.rigidCJ != null)
                            {
                                Component.Destroy(c.rigidCJ);
                                c.rigidCJ = null;
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Main.DebugLog(() => $"Error cleaning up coupler {c?.train?.ID}: {ex.Message}");
                        }
                    }
                }

                bufferJoints.Remove(coupler);
                bufferJoints.Remove(result.otherCoupler);
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error destroying compression joint: {ex.Message}");
                // Clean up dictionaries to prevent memory leaks
                bufferJoints.Remove(coupler);
                if (result.otherCoupler != null)
                    bufferJoints.Remove(result.otherCoupler);
            }
        }

        private static void DestroyTensionJoint(Coupler coupler)
        {
            if (coupler == null)
                return;
                
            try
            {
                // Try to find tension joint on this coupler first
                if (customTensionJoints.TryGetValue(coupler, out var tensionJoint))
                {
                    if (tensionJoint != null)
                    {
                        Main.DebugLog(() => $"TENSION JOINT: Destroying for {coupler.train.ID} {coupler.Position()}");
                        UnityEngine.Object.Destroy(tensionJoint);
                    }
                    customTensionJoints.Remove(coupler);
                    lastJointCreationTime.Remove(coupler);
                    Main.DebugLog(() => $"TENSION JOINT: Destroyed and removed from dictionary for {coupler.train.ID} {coupler.Position()}, remaining joints: {customTensionJoints.Count}");
                    return;
                }
                
                // If not found on this coupler, try to find it on the partner coupler
                if (coupler.coupledTo != null && customTensionJoints.TryGetValue(coupler.coupledTo, out tensionJoint))
                {
                    if (tensionJoint != null)
                    {
                        Main.DebugLog(() => $"TENSION JOINT: Destroying partner joint for {coupler.coupledTo.train.ID} {coupler.coupledTo.Position()} (called from {coupler.train.ID})");
                        UnityEngine.Object.Destroy(tensionJoint);
                    }
                    customTensionJoints.Remove(coupler.coupledTo);
                    lastJointCreationTime.Remove(coupler.coupledTo);
                    Main.DebugLog(() => $"TENSION JOINT: Destroyed and removed partner from dictionary for {coupler.coupledTo.train.ID} {coupler.coupledTo.Position()}, remaining joints: {customTensionJoints.Count}");
                    return;
                }
                
                Main.DebugLog(() => $"TENSION JOINT: No tension joint found to destroy for {coupler.train.ID} {coupler.Position()} or its partner");
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error destroying tension joint: {ex.Message}");
                // Clean up dictionaries to prevent memory leaks
                customTensionJoints.Remove(coupler);
                lastJointCreationTime.Remove(coupler);
                if (coupler.coupledTo != null)
                {
                    customTensionJoints.Remove(coupler.coupledTo);
                    lastJointCreationTime.Remove(coupler.coupledTo);
                }
            }
        }

        // Public method to check if tension joint exists
        public static bool HasTensionJoint(Coupler coupler)
        {
            return coupler != null && customTensionJoints.ContainsKey(coupler);
        }

        // Public method to force create tension joint (used by SaveManager)
        public static void ForceCreateTensionJoint(Coupler coupler)
        {
            if (coupler == null || !coupler.IsCoupled() || coupler.coupledTo == null)
                return;
                
            if (customTensionJoints.ContainsKey(coupler))
                return; // Already exists
                
            Main.DebugLog(() => $"FORCE CREATING tension joint for {coupler.train.ID} {coupler.Position()} -> {coupler.coupledTo.train.ID} {coupler.coupledTo.Position()}");
            CreateTensionJoint(coupler);
            
            // Also create compression joint if needed
            if (coupler.rigidCJ == null && coupler.coupledTo.rigidCJ == null)
                CreateCompressionJoint(coupler, coupler.coupledTo);
        }

        // Public method to check joint states for debugging
        public static void LogJointStates(string context)
        {
            Main.DebugLog(() => $"JOINT STATES ({context}): Tension joints: {customTensionJoints.Count}");
            foreach (var kvp in customTensionJoints.ToList())
            {
                var coupler = kvp.Key;
                var joint = kvp.Value;
                if (coupler?.train != null && joint != null)
                {
                    var distance = joint.connectedBody != null ? 
                        Vector3.Distance(coupler.transform.position, joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)) : 0f;
                    Main.DebugLog(() => $"  {coupler.train.ID} {coupler.Position()} -> {coupler.coupledTo?.train.ID}, distance: {distance:F2}m, joint valid: {joint != null}");
                }
            }
        }

        private static Vector3 JointDelta(Joint joint, bool isFrontCoupler)
        {
            var delta = joint.transform.InverseTransformPoint(joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)) - joint.anchor;
            return isFrontCoupler ? delta : -delta;
        }

        public static void UpdateAllCompressionJoints()
        {
            if (bufferJoints.Count == 0)
                return;

            var springRate = Main.settings.GetSpringRate();
            var damperRate = Main.settings.GetDamperRate();

            var firstJoint = bufferJoints.Values.FirstOrDefault().joint;
            if (firstJoint == null || (firstJoint.linearLimitSpring.spring == springRate && firstJoint.linearLimitSpring.damper == damperRate))
                return;

            foreach (var joint in bufferJoints.Values.Select(x => x.joint))
            {
                joint.linearLimitSpring = new SoftJointLimitSpring
                {
                    spring = springRate,
                    damper = damperRate,
                };
            }
        }

        public static string Position(this Coupler coupler) => coupler.isFrontCoupler ? "front" : "rear";
    }
}
