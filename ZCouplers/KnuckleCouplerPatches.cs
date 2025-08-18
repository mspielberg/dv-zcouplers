using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using DV;
using DV.CabControls;

using HarmonyLib;

using Stateless;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Contains all Harmony patches related to knuckle coupler functionality
    /// </summary>
    public static class KnuckleCouplerPatches
    {
        private static readonly HashSet<string> synchronizedCouplings = new HashSet<string>();

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Enabled))]
        public static class Entry_EnabledPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (KnuckleCouplers.enabled)
                    KnuckleCouplers.CreateHook(__instance);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Enabled))]
        public static class Exit_EnabledPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (KnuckleCouplers.enabled)
                    HookManager.DestroyHook(__instance);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Attached))]
        public static class Entry_AttachedPatch
        {
            // Use Prefix patch to completely replace the original method for knuckle couplers
            public static bool Prefix(ChainCouplerInteraction __instance)
            {
                if (!KnuckleCouplers.enabled)
                    return true; // Let original method run when knuckle couplers are disabled

                // When knuckle couplers are enabled, ALL couplers are knuckle couplers
                try
                {
                    // Safely execute the Entry_Attached logic for knuckle couplers
                    if (__instance.attachedIK?.solver != null && __instance.closestAttachPoint?.transform != null)
                    {
                        __instance.attachedIK.solver.target = __instance.closestAttachPoint.transform;
                    }

                    if (__instance.knob?.transform != null && __instance.closestAttachPoint?.transform != null)
                    {
                        __instance.knob.transform.position = __instance.closestAttachPoint.transform.position;
                    }

                    // Set up knuckle coupler attachedTo relationship
                    if (__instance.closestAttachPoint?.transform?.parent?.parent != null)
                    {
                        __instance.attachedTo = __instance.closestAttachPoint.transform.parent.parent.GetComponent<ChainCouplerInteraction>();
                        if (__instance.attachedTo != null)
                        {
                            __instance.attachedTo.attachedTo = __instance;
                        }
                    }

                    if (__instance.closestAttachPoint != null)
                    {
                        __instance.closestAttachPoint.SetAttachState(attached: true);
                    }

                    if (__instance.screwButton != null)
                    {
                        __instance.screwButton.SetActive(value: true);
                        var screwButtonBase = __instance.screwButton.GetComponent<ButtonBase>();
                        if (screwButtonBase != null)
                        {
                            // Note: We can't directly access the private screwButtonBase field or OnScrewButtonUsed method
                            // This will be handled by the original implementation if needed
                        }
                    }

                    // Trigger the Attached event if it exists
                    var attachedEvent = typeof(ChainCouplerInteraction).GetField("Attached", BindingFlags.Instance | BindingFlags.Public);
                    if (attachedEvent?.GetValue(__instance) is System.Action attachedAction)
                    {
                        attachedAction?.Invoke();
                    }

                    // Play sound if possible
                    if (__instance.knob?.transform != null)
                    {
                        // We can't access private PlaySound method, but that's OK for now
                    }

                    return false; // Skip original method - we handled everything for knuckle couplers
                }
                catch (System.Exception ex)
                {
                    if (Main.settings?.enableLogging == true)
                        Main.ErrorLog(() => $"Exception in Entry_AttachedPatch: {ex.Message}");
                    return true; // Fall back to original method if our patch fails
                }
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.LateUpdate_Attached))]
        public static class LateUpdate_AttachedPatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance)
            {
                if (!KnuckleCouplers.enabled)
                    return true; // Let original method run when knuckle couplers are disabled

                // When knuckle couplers are enabled, completely replace the original LateUpdate_Attached method
                try
                {
                    // Safely execute the original LateUpdate_Attached logic for knuckle couplers
                    if (__instance.attachedTo?.otherAttachedIK?.solver != null)
                    {
                        __instance.attachedTo.otherAttachedIK.solver.Update();
                    }

                    if (__instance.attachedIK?.solver != null)
                    {
                        __instance.attachedIK.solver.Update();
                    }

                    // Add our knuckle coupler-specific logic
                    var pivot = HookManager.GetPivot(__instance);
                    var otherPivot = HookManager.GetPivot(__instance.attachedTo);
                    if (pivot != null && otherPivot != null)
                    {
                        HookManager.AdjustPivot(pivot, otherPivot);
                    }

                    return false; // Skip original method - we handled everything for knuckle couplers
                }
                catch (System.Exception ex)
                {
                    if (Main.settings?.enableLogging == true)
                        Main.ErrorLog(() => $"Exception in LateUpdate_AttachedPatch: {ex.Message}");
                    return true; // Fall back to original method if our patch fails
                }
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Attached))]
        public static class Exit_AttachedPatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance)
            {
                if (!KnuckleCouplers.enabled)
                    return true; // Let original method run when knuckle couplers are disabled

                // When knuckle couplers are enabled, completely replace the original Exit_Attached method
                try
                {
                    // Execute the original Exit_Attached logic for knuckle couplers
                    if (__instance.attachedIK?.solver != null)
                    {
                        __instance.attachedIK.solver.target = null;
                    }

                    if (__instance.closestAttachPoint != null)
                    {
                        __instance.closestAttachPoint.SetAttachState(attached: false);
                    }

                    // Clean up attachedTo reference
                    __instance.attachedTo = null;

                    // Handle screw button cleanup if it exists
                    if (__instance.screwButton != null)
                    {
                        var screwButtonBase = __instance.screwButton.GetComponent<ButtonBase>();
                        if (screwButtonBase != null)
                        {
                            // Note: We can't access the private OnScrewButtonUsed method directly
                            // The original code does: screwButtonBase.Used -= OnScrewButtonUsed;
                            // This will be handled by the game's cleanup if needed
                        }
                        __instance.screwButton.SetActive(value: false);
                    }

                    // Set hackIK target if it exists
                    if (__instance.hackIK != null)
                    {
                        __instance.hackIK.target = 1f;
                    }

                    // Add our knuckle coupler-specific cleanup
                    var pivot = HookManager.GetPivot(__instance);
                    if (pivot != null && pivot.gameObject != null && __instance?.couplerAdapter?.coupler != null)
                    {
                        var coupler = __instance.couplerAdapter.coupler;
                        if (coupler.transform != null)
                        {
                            pivot.localEulerAngles = coupler.transform.localEulerAngles;
                            var hook = pivot.Find("hook") ?? pivot.Find("SA3_closed") ?? pivot.Find("SA3_open");
                            if (hook != null && hook.gameObject != null)
                            {
                                // Base position when disconnecting
                                var basePosition = 1.0f * Vector3.forward; // PivotLength constant

                                // Start with base position
                                var finalPosition = basePosition;

                                // Apply SA3-specific offset if using SA3 couplers
                                if (Main.settings.couplerType == CouplerType.SA3Knuckle)
                                {
                                    // Move SA3 coupler head 0.035 units to the left
                                    finalPosition += new Vector3(-0.035f, 0f, 0f);
                                }

                                // Apply height offset for LocoS282A front coupler
                                if (coupler?.train?.carLivery?.id == "LocoS282A" && coupler.isFrontCoupler)
                                {
                                    // Move front coupler on LocoS282A down by 0.05 units
                                    finalPosition += new Vector3(0f, -0.05f, 0f);
                                }

                                hook.localPosition = finalPosition;
                            }
                        }
                    }

                    return false; // Skip original method - we handled everything for knuckle couplers
                }
                catch (System.Exception ex)
                {
                    if (Main.settings?.enableLogging == true)
                        Main.ErrorLog(() => $"Exception in Exit_AttachedPatch: {ex.Message}");
                    return true; // Fall back to original method if our patch fails
                }
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Parked))]
        public static class Exit_ParkedPatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance)
            {
                if (!KnuckleCouplers.enabled)
                    return true; // Let original method run when knuckle couplers are disabled

                // When knuckle couplers are enabled, completely replace the original Exit_Parked method
                try
                {
                    // Execute the original Exit_Parked logic for knuckle couplers
                    var hackRotation = __instance.GetComponentInChildren<HackRotation>();
                    if (hackRotation != null)
                    {
                        hackRotation.angleThreshold = 58f;
                        hackRotation.targetLocalRot = new Quaternion(0.7071068f, 0f, 0f, 0.7071068f);
                        hackRotation.lerp = 0.01f;
                    }

                    return false; // Skip original method - we handled everything for knuckle couplers
                }
                catch (System.Exception ex)
                {
                    if (Main.settings?.enableLogging == true)
                        Main.ErrorLog(() => $"Exception in Exit_ParkedPatch: {ex.Message}");
                    return true; // Fall back to original method if our patch fails
                }
            }
        }

        [HarmonyPatch(typeof(ChainCouplerVisibilityOptimizer), nameof(ChainCouplerVisibilityOptimizer.Enable))]
        public static class ChainCouplerVisibilityOptimizerEnablePatch
        {
            public static void Postfix(ChainCouplerVisibilityOptimizer __instance)
            {
                if (!KnuckleCouplers.enabled)
                    return;

                var chainTransform = __instance.chain.transform;
                for (int i = 0; i < chainTransform.childCount; i++)
                    chainTransform.GetChild(i).gameObject.SetActive(false);

                // Check if this coupler needs a knuckle coupler created
                var chainScript = __instance.chain.GetComponent<ChainCouplerInteraction>();
                if (chainScript != null && chainScript.enabled && HookManager.GetPivot(chainScript) == null)
                {
                    var coupler = chainScript.couplerAdapter?.coupler;
                    if (coupler != null)
                    {
                        // Removed verbose coupler creation log
                        KnuckleCouplers.CreateHook(chainScript);
                    }
                }
            }
        }

        /// Patch to catch train cars when they're being set up, including teleported trains.
        [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.Start))]
        public static class TrainCarStartPatch
        {
            public static void Postfix(TrainCar __instance)
            {
                if (!KnuckleCouplers.enabled)
                    return;

                // Delay the check to ensure the train car is fully initialized
                __instance.StartCoroutine(HookManager.DelayedKnuckleCouplerCheck(__instance, KnuckleCouplers.GetHookPrefab()));

                // Also check and fix states for teleported trains that might already be coupled
                __instance.StartCoroutine(DelayedCoupledStateCheck(__instance));
            }

            private static IEnumerator DelayedCoupledStateCheck(TrainCar car)
            {
                // Wait for car to be fully initialized
                yield return new WaitForSeconds(0.3f);

                // Check both couplers and fix states if they're already coupled
                if (car.frontCoupler != null && car.frontCoupler.IsCoupled())
                {
                    FixCoupledState(car.frontCoupler);
                }
                if (car.rearCoupler != null && car.rearCoupler.IsCoupled())
                {
                    FixCoupledState(car.rearCoupler);
                }
            }

            private static void FixCoupledState(Coupler coupler)
            {
                var partner = coupler.coupledTo;
                if (partner == null) return;

                // Force both couplers to ready state for knuckle couplers
                if (!KnuckleCouplers.IsReadyToCouple(coupler))
                {
                    KnuckleCouplers.SetCouplerLocked(coupler, true);
                    Main.DebugLog(() => $"Fixed teleported train state: {coupler.train.ID} {coupler.Position()} forced to ready");
                }
                if (!KnuckleCouplers.IsReadyToCouple(partner))
                {
                    KnuckleCouplers.SetCouplerLocked(partner, true);
                    Main.DebugLog(() => $"Fixed teleported train state: {partner.train.ID} {partner.Position()} forced to ready");
                }

                // Ensure both have correct coupled state
                if (coupler.state != ChainCouplerInteraction.State.Attached_Tight)
                {
                    coupler.state = ChainCouplerInteraction.State.Attached_Tight;
                    Main.DebugLog(() => $"Fixed teleported train state: {coupler.train.ID} {coupler.Position()} set to Attached_Tight");
                }
                if (partner.state != ChainCouplerInteraction.State.Attached_Tight)
                {
                    partner.state = ChainCouplerInteraction.State.Attached_Tight;
                    Main.DebugLog(() => $"Fixed teleported train state: {partner.train.ID} {partner.Position()} set to Attached_Tight");
                }

                // Update visual states
                KnuckleCouplerState.UpdateCouplerVisualState(coupler, locked: true);
                KnuckleCouplerState.UpdateCouplerVisualState(partner, locked: true);
            }
        }

        /// Patch to catch all train spawning, including teleported trains.
        [HarmonyPatch(typeof(CarSpawner), nameof(CarSpawner.SpawnCar))]
        public static class CarSpawnerSpawnCarPatch
        {
            public static void Postfix(TrainCar __result)
            {
                if (!KnuckleCouplers.enabled)
                    return;

                if (__result == null)
                    return;

                // Delay the check to ensure the train car is fully set up
                __result.StartCoroutine(HookManager.DelayedSpawnKnuckleCouplerCheck(__result, KnuckleCouplers.GetHookPrefab()));
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.CoupleBrokenExternally))]
        public static class CoupleBrokenExternallyPatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance)
            {
                if (!KnuckleCouplers.enabled)
                    return true; // Let original method run when knuckle couplers are disabled

                // When knuckle couplers are enabled, ALL couplers are knuckle couplers
                __instance.UncoupledExternally();
                return false;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerCouplerAdapter), nameof(ChainCouplerCouplerAdapter.OnCoupled))]
        public static class OnCoupledPatch
        {
            public static void Postfix(ChainCouplerCouplerAdapter __instance, CoupleEventArgs e)
            {
                if (!KnuckleCouplers.enabled)
                    return;

                Main.DebugLog(() => $"OnCoupled: {e.thisCoupler.train.ID} <-> {e.otherCoupler.train.ID}, viaChain={e.viaChainInteraction}");

                // Update knuckle coupler visual state to show coupled (locked) without triggering uncoupling
                KnuckleCouplerState.UpdateCouplerVisualState(e.thisCoupler, locked: true);
                KnuckleCouplerState.UpdateCouplerVisualState(e.otherCoupler, locked: true);

                // Directly update coupler states after coupling
                var thisChainScript = e.thisCoupler.visualCoupler?.chain?.GetComponent<ChainCouplerInteraction>();
                var otherChainScript = e.otherCoupler.visualCoupler?.chain?.GetComponent<ChainCouplerInteraction>();

                // For knuckle couplers: if physically coupled, force both to be ready
                if (!KnuckleCouplers.IsReadyToCouple(e.thisCoupler))
                {
                    KnuckleCouplers.SetCouplerLocked(e.thisCoupler, true);
                    Main.DebugLog(() => $"Force ready on couple: {e.thisCoupler.train.ID} {e.thisCoupler.Position()}");
                }
                if (!KnuckleCouplers.IsReadyToCouple(e.otherCoupler))
                {
                    KnuckleCouplers.SetCouplerLocked(e.otherCoupler, true);
                    Main.DebugLog(() => $"Force ready on couple: {e.otherCoupler.train.ID} {e.otherCoupler.Position()}");
                }

                // Now both are ready and coupled -> Attached_Tight
                var newState = ChainCouplerInteraction.State.Attached_Tight;

                // Update both coupler states directly
                if (thisChainScript != null && e.thisCoupler.IsCoupled())
                {
                    e.thisCoupler.state = newState;
                    Main.DebugLog(() => $"Set coupled state: {e.thisCoupler.train.ID} {e.thisCoupler.Position()} -> {newState}");
                }

                if (otherChainScript != null && e.otherCoupler.IsCoupled())
                {
                    e.otherCoupler.state = newState;
                    Main.DebugLog(() => $"Set coupled state: {e.otherCoupler.train.ID} {e.otherCoupler.Position()} -> {newState}");
                }

                // Ensure both coupler state machines are synchronized for external coupling
                // During UI coupling, only one OnCoupled event may fire, so we need to ensure 
                // both couplers have their states properly updated
                if (!e.viaChainInteraction)
                {
                    // Create a unique coupling ID to prevent duplicate synchronization
                    var couplingId = $"{e.thisCoupler.train.ID}-{e.otherCoupler.train.ID}";
                    var reverseCouplingId = $"{e.otherCoupler.train.ID}-{e.thisCoupler.train.ID}";

                    if (!synchronizedCouplings.Contains(couplingId) && !synchronizedCouplings.Contains(reverseCouplingId))
                    {
                        synchronizedCouplings.Add(couplingId);

                        // Force state machine re-evaluation for both couplers by disabling and re-enabling
                        var thisChainScriptSync = e.thisCoupler.visualCoupler?.chain?.GetComponent<ChainCouplerInteraction>();
                        var otherChainScriptSync = e.otherCoupler.visualCoupler?.chain?.GetComponent<ChainCouplerInteraction>();

                        if (thisChainScriptSync != null && otherChainScriptSync != null)
                        {
                            // Temporarily disable and re-enable to force state refresh
                            thisChainScriptSync.enabled = false;
                            otherChainScriptSync.enabled = false;
                            thisChainScriptSync.enabled = true;
                            otherChainScriptSync.enabled = true;

                            Main.DebugLog(() => $"Forced state sync after external coupling: {e.thisCoupler.train.ID} & {e.otherCoupler.train.ID}");
                        }

                        // Clean up the synchronization record after a short delay
                        __instance.StartCoroutine(CleanupSynchronizationRecord(couplingId));
                    }
                }

                // Force a state update check after a short delay to catch any missed state updates
                __instance.StartCoroutine(DelayedStateUpdateCheck(e.thisCoupler, e.otherCoupler));
            }

            private static IEnumerator DelayedStateUpdateCheck(Coupler thisCoupler, Coupler otherCoupler)
            {
                yield return new WaitForSeconds(0.1f); // Small delay to allow coupling to complete

                // Force state update for both couplers if they're still in wrong states or mismatched
                if (thisCoupler != null && thisCoupler.IsCoupled() && otherCoupler != null && otherCoupler.IsCoupled())
                {
                    // Check for state mismatches between coupled couplers
                    bool thisCouplerWrongState = thisCoupler.state == ChainCouplerInteraction.State.Parked || thisCoupler.state == ChainCouplerInteraction.State.Dangling;
                    bool otherCouplerWrongState = otherCoupler.state == ChainCouplerInteraction.State.Parked || otherCoupler.state == ChainCouplerInteraction.State.Dangling;
                    bool statesMismatch = thisCoupler.state != otherCoupler.state;

                    if (thisCouplerWrongState || otherCouplerWrongState || statesMismatch)
                    {
                        // For knuckle couplers: if physically coupled, force both to be ready
                        if (!KnuckleCouplers.IsReadyToCouple(thisCoupler))
                        {
                            KnuckleCouplers.SetCouplerLocked(thisCoupler, true);
                            Main.DebugLog(() => $"Force ready: {thisCoupler.train.ID} {thisCoupler.Position()}");
                        }
                        if (!KnuckleCouplers.IsReadyToCouple(otherCoupler))
                        {
                            KnuckleCouplers.SetCouplerLocked(otherCoupler, true);
                            Main.DebugLog(() => $"Force ready: {otherCoupler.train.ID} {otherCoupler.Position()}");
                        }

                        // Now both are ready and coupled -> Attached_Tight
                        var correctState = ChainCouplerInteraction.State.Attached_Tight;

                        // Update both couplers to the same correct state
                        if (thisCoupler.state != correctState)
                        {
                            thisCoupler.state = correctState;
                            Main.DebugLog(() => $"Correct state: {thisCoupler.train.ID} {thisCoupler.Position()} -> {correctState}");
                        }

                        if (otherCoupler.state != correctState)
                        {
                            otherCoupler.state = correctState;
                            Main.DebugLog(() => $"Correct state: {otherCoupler.train.ID} {otherCoupler.Position()} -> {correctState}");
                        }

                        // Also ensure visual states are consistent (both should be locked/ready now)
                        KnuckleCouplerState.UpdateCouplerVisualState(thisCoupler, locked: true);
                        KnuckleCouplerState.UpdateCouplerVisualState(otherCoupler, locked: true);
                    }
                }
            }

            private static IEnumerator CleanupSynchronizationRecord(string couplingId)
            {
                yield return new WaitForSeconds(1.0f);
                synchronizedCouplings.Remove(couplingId);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.DetermineNextState))]
        public static class DetermineNextStatePatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance, ref ChainCouplerInteraction.State __result)
            {
                if (!KnuckleCouplers.enabled)
                    return true; // Let original method run when knuckle couplers are disabled

                // When knuckle couplers are enabled, ALL couplers are knuckle couplers
                // Check if we need to create a knuckle coupler for this chain script
                if (HookManager.GetPivot(__instance) == null && __instance.couplerAdapter?.coupler != null)
                {
                    var coupler = __instance.couplerAdapter.coupler;
                    // Removed verbose coupler creation log
                    KnuckleCouplers.CreateHook(__instance);
                }

                if (__instance.couplerAdapter?.IsCoupled() == true)
                {
                    // For knuckle couplers: if physically coupled, both must be ready
                    var coupler = __instance.couplerAdapter.coupler;
                    var partnerCoupler = coupler?.coupledTo;
                    if (coupler != null && partnerCoupler != null)
                    {
                        // If either coupler is not ready but they are physically coupled,
                        // force both to be ready (knuckle couplers can't be "not ready" while coupled)
                        if (!KnuckleCouplers.IsReadyToCouple(coupler))
                        {
                            KnuckleCouplers.SetCouplerLocked(coupler, true);
                            Main.DebugLog(() => $"Forced {coupler.train.ID} {coupler.Position()} to ready state (was coupled but not ready)");
                        }
                        if (!KnuckleCouplers.IsReadyToCouple(partnerCoupler))
                        {
                            KnuckleCouplers.SetCouplerLocked(partnerCoupler, true);
                            Main.DebugLog(() => $"Forced {partnerCoupler.train.ID} {partnerCoupler.Position()} to ready state (was coupled but not ready)");
                        }

                        // Now both are ready and coupled -> Attached_Tight
                        __result = ChainCouplerInteraction.State.Attached_Tight;
                    }
                    else
                    {
                        __result = ChainCouplerInteraction.State.Attached_Tight;
                    }
                }
                else
                {
                    // For uncoupled couplers, determine state based on readiness
                    bool isReady = __instance.couplerAdapter?.coupler != null &&
                                   KnuckleCouplers.IsReadyToCouple(__instance.couplerAdapter.coupler);
                    __result = isReady ? ChainCouplerInteraction.State.Dangling : ChainCouplerInteraction.State.Parked;
                }

                return false;
            }

            public static void Postfix(ChainCouplerInteraction __instance, ref ChainCouplerInteraction.State __result)
            {
                if (!KnuckleCouplers.enabled || __instance?.couplerAdapter?.coupler == null)
                    return;

                // Notify coupling handler of state changes
                var coupler = __instance.couplerAdapter.coupler;
                try
                {
                    // Update visual state based on the new coupler state
                    HookManager.UpdateHookVisualStateFromCouplerState(coupler);
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Error notifying coupling handler of state change: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.MakeFSM))]
        public static class MakeFSMPatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance, ref StateMachine<ChainCouplerInteraction.State, ChainCouplerInteraction.Trigger> __result)
            {
                if (!KnuckleCouplers.enabled)
                    return true; // Let original method run when knuckle couplers are disabled

                // When knuckle couplers are enabled, create a simplified state machine
                try
                {
                    var state = ChainCouplerInteraction.State.Disabled;
                    var stateMachine = new StateMachine<ChainCouplerInteraction.State, ChainCouplerInteraction.Trigger>(
                        () => state,
                        s => state = s
                    );

                    // Configure simplified knuckle coupler states
                    stateMachine.Configure(ChainCouplerInteraction.State.Disabled)
                        .Permit(ChainCouplerInteraction.Trigger.Enable, ChainCouplerInteraction.State.Enabled)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Disable, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.UpdateVisible, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.LateUpdateVisible, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Coupled_Externally, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Uncoupled_Externally, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Couple_Broken_Externally, () => true);

                    stateMachine.Configure(ChainCouplerInteraction.State.Enabled)
                        .InitialTransition(ChainCouplerInteraction.State.Parked)
                        .Permit(ChainCouplerInteraction.Trigger.Disable, ChainCouplerInteraction.State.Disabled)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.UpdateVisible, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.LateUpdateVisible, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Our_Hook_Occupied, () => true);

                    stateMachine.Configure(ChainCouplerInteraction.State.Parked)
                        .SubstateOf(ChainCouplerInteraction.State.Enabled)
                        .PermitDynamic(ChainCouplerInteraction.Trigger.Coupled_Externally, () =>
                        {
                            // For knuckle couplers: coupling forces both to ready state
                            var coupler = __instance.couplerAdapter?.coupler;
                            var partner = coupler?.coupledTo;
                            if (coupler != null && partner != null)
                            {
                                // Force both couplers to be ready if they're coupled
                                if (!KnuckleCouplers.IsReadyToCouple(coupler))
                                    KnuckleCouplers.SetCouplerLocked(coupler, true);
                                if (!KnuckleCouplers.IsReadyToCouple(partner))
                                    KnuckleCouplers.SetCouplerLocked(partner, true);

                                return ChainCouplerInteraction.State.Attached_Tight;
                            }
                            return ChainCouplerInteraction.State.Attached_Tight;
                        })
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Enable, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.UpdateVisible, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Picked_Up_By_Player, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Uncoupled_Externally, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Couple_Broken_Externally, () => true);

                    stateMachine.Configure(ChainCouplerInteraction.State.Dangling)
                        .SubstateOf(ChainCouplerInteraction.State.Enabled)
                        .PermitDynamic(ChainCouplerInteraction.Trigger.Coupled_Externally, () =>
                        {
                            // For knuckle couplers: coupling forces both to ready state
                            var coupler = __instance.couplerAdapter?.coupler;
                            var partner = coupler?.coupledTo;
                            if (coupler != null && partner != null)
                            {
                                // Force both couplers to be ready if they're coupled
                                if (!KnuckleCouplers.IsReadyToCouple(coupler))
                                    KnuckleCouplers.SetCouplerLocked(coupler, true);
                                if (!KnuckleCouplers.IsReadyToCouple(partner))
                                    KnuckleCouplers.SetCouplerLocked(partner, true);

                                return ChainCouplerInteraction.State.Attached_Tight;
                            }
                            return ChainCouplerInteraction.State.Attached_Tight;
                        })
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Enable, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.UpdateVisible, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Picked_Up_By_Player, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Uncoupled_Externally, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Couple_Broken_Externally, () => true);

                    stateMachine.Configure(ChainCouplerInteraction.State.Attached_Tight)
                        .SubstateOf(ChainCouplerInteraction.State.Enabled)
                        .Permit(ChainCouplerInteraction.Trigger.Uncoupled_Externally, ChainCouplerInteraction.State.Dangling)
                        .Permit(ChainCouplerInteraction.Trigger.Couple_Broken_Externally, ChainCouplerInteraction.State.Dangling)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Enable, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Our_Hook_Freed, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Screw_Used, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.LateUpdateVisible, () => true);

                    // Keep Attached_Loose configuration for backwards compatibility with old save files
                    // Knuckle couplers should not actively use this state - they force both couplers to ready when coupled
                    stateMachine.Configure(ChainCouplerInteraction.State.Attached_Loose)
                        .SubstateOf(ChainCouplerInteraction.State.Enabled)
                        .Permit(ChainCouplerInteraction.Trigger.Uncoupled_Externally, ChainCouplerInteraction.State.Dangling)
                        .Permit(ChainCouplerInteraction.Trigger.Couple_Broken_Externally, ChainCouplerInteraction.State.Dangling)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Enable, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Our_Hook_Freed, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.Screw_Used, () => true)
                        .IgnoreIf(ChainCouplerInteraction.Trigger.LateUpdateVisible, () => true);

                    // Add error logging for unhandled triggers
                    stateMachine.OnUnhandledTrigger((s, trigger) =>
                    {
                        if (Main.settings?.enableLogging == true)
                            Main.DebugLog(() => $"Unhandled trigger '{trigger}' for state '{s}'");
                    });

                    __result = stateMachine;

                    if (Main.settings?.enableLogging == true)
                    {
                        // Removed verbose FSM creation log
                    }

                    return false; // Skip original method
                }
                catch (System.Exception ex)
                {
                    if (Main.settings?.enableLogging == true)
                        Main.ErrorLog(() => $"Exception in MakeFSMPatch: {ex.Message}");
                    return true; // Fall back to original method if our patch fails
                }
            }
        }

        [HarmonyPatch(typeof(InteractionText), nameof(InteractionText.GetText))]
        public static class GetTextPatch
        {
            public static bool Prefix(InteractionInfoType infoType, ref string __result)
            {
                if (!KnuckleCouplers.enabled)
                    return true; // Let original method run when knuckle couplers are disabled

                // When knuckle couplers are enabled, handle knuckle coupler-specific text
                if (infoType == HookManager.KnuckleCouplerUnlock)
                {
                    __result = $"Press {InteractionText.Instance.BtnUse} to unlock coupler";
                    return false;
                }
                if (infoType == HookManager.KnuckleCouplerLock)
                {
                    __result = $"Press {InteractionText.Instance.BtnUse} to ready coupler";
                    return false;
                }
                return true;
            }
        }
    }
}