using System.Collections;
using System.Collections.Generic;

using DV;
using DV.CabControls;
using DV.CabControls.Spec;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Manages creation and lifecycle of knuckle coupler hook visuals.
    /// </summary>
    public static class HookManager
    {
        private static readonly Dictionary<ChainCouplerInteraction, Transform> pivots = new Dictionary<ChainCouplerInteraction, Transform>();
        private const float PivotLength = 1.0f;
        private const float HeightOffset = -0.067f;

        public static InteractionInfoType KnuckleCouplerUnlock = (InteractionInfoType)23000;
        public static InteractionInfoType KnuckleCouplerLock = (InteractionInfoType)23001;

        public static Transform? GetPivot(ChainCouplerInteraction? chainScript)
        {
            if (chainScript == null)
                return null;

            if (!pivots.TryGetValue(chainScript, out var pivot))
                return null;

            // Safety check to ensure the pivot transform is still valid
            if (pivot == null || pivot.gameObject == null)
            {
                // Clean up the stale reference
                pivots.Remove(chainScript);
                return null;
            }

            return pivot;
        }

        public static void CreateHook(ChainCouplerInteraction chainScript, GameObject? fallbackHookPrefab = null)
        {
            if (chainScript == null)
                return;

            // Check if hook already exists
            if (GetPivot(chainScript) != null)
            {
                return;
            }

            // Ensure assets are loaded
            if (!AssetManager.AreAssetsLoaded())
            {
                Main.ErrorLog(() => "Assets not loaded, cannot create knuckle coupler hook");
                return;
            }

            var coupler = chainScript.couplerAdapter.coupler;
            var pivot = new GameObject(coupler.isFrontCoupler ? "ZCouplers pivot front" : "ZCouplers pivot rear");
            pivot.transform.SetParent(coupler.transform, false);
            pivot.transform.localPosition = new Vector3(0, HeightOffset, -PivotLength);
            pivot.transform.parent = coupler.train.interior;
            pivots.Add(chainScript, pivot.transform);

            // Determine which hook prefab to use based on coupler type and state
            var couplerType = Main.settings.couplerType;
            var isParked = coupler.state == ChainCouplerInteraction.State.Parked;
            var actualHookPrefab = AssetManager.GetHookPrefabForState(couplerType, isParked) ?? fallbackHookPrefab;

            if (actualHookPrefab == null)
            {
                Main.ErrorLog(() => $"Hook prefab is null for coupler type {couplerType}, state parked: {isParked}, cannot create knuckle coupler hook");
                return;
            }

            if (!ValidateHookPrefab(actualHookPrefab))
            {
                Main.ErrorLog(() => $"Hook prefab validation failed for {actualHookPrefab.name}, cannot create knuckle coupler hook");
                return;
            }

            CreateHookInstance(pivot.transform, actualHookPrefab, chainScript, coupler);

            // Add the visual updater component to ensure rotation works
            if (chainScript.gameObject.GetComponent<CouplerVisualUpdater>() == null)
            {
                chainScript.gameObject.AddComponent<CouplerVisualUpdater>();
            }
        }

        /// <summary>
        /// Validate that a hook prefab has the required components.
        /// </summary>
        private static bool ValidateHookPrefab(GameObject hookPrefab)
        {
            if (hookPrefab == null)
                return false;
            return true;
        }

        private static void CreateHookInstance(Transform pivot, GameObject hookPrefab, ChainCouplerInteraction chainScript, Coupler coupler, string desiredName = "hook")
        {
            if (pivot == null)
            {
                Main.ErrorLog(() => "Pivot is null in CreateHookInstance");
                return;
            }

            if (hookPrefab == null)
            {
                Main.ErrorLog(() => "Hook prefab is null in CreateHookInstance");
                return;
            }

            var hook = GameObject.Instantiate(hookPrefab);
            if (hook == null)
            {
                Main.ErrorLog(() => "Failed to instantiate hook from prefab");
                return;
            }

            hook.SetActive(false); // Defer Awake() until all components are added and initialized
            hook.name = desiredName; // Use the desired name instead of always "hook"
            hook.layer = LayerMask.NameToLayer("Interactable");
            hook.transform.SetParent(pivot, false);
            hook.transform.localPosition = PivotLength * Vector3.forward;

            // Use the existing colliders from the prefab - NO automatic creation at all
            var interactionCollider = hook.GetComponent<BoxCollider>();
            if (interactionCollider != null)
            {
                interactionCollider.isTrigger = true; // Ensure it's set as trigger for interaction
            }

            var buttonSpec = hook.AddComponent<Button>();
            buttonSpec.createRigidbody = false;
            buttonSpec.useJoints = false;
            buttonSpec.colliderGameObjects = new GameObject[] { hook };

            var infoArea = hook.AddComponent<InfoArea>();
            infoArea.infoType = KnuckleCouplerState.IsUnlocked(coupler) ? KnuckleCouplerLock : KnuckleCouplerUnlock;
            hook.SetActive(true); // Activate after initialization completes

            var buttonBase = hook.GetComponent<ButtonBase>();
            if (buttonBase == null)
            {
                Main.ErrorLog(() => "Failed to get ButtonBase component after setting hook active");
                GameObject.Destroy(hook);
                return;
            }
            buttonBase.Used += () => OnButtonPressed(chainScript);
        }

        public static void DestroyHook(ChainCouplerInteraction chainScript)
        {
            if (chainScript == null)
                return;

            var pivot = GetPivot(chainScript);
            if (pivot != null)
            {
                GameObject.Destroy(pivot.gameObject);
                pivots.Remove(chainScript);
            }

            // Remove the visual updater component if it exists
            var visualUpdater = chainScript.gameObject.GetComponent<CouplerVisualUpdater>();
            if (visualUpdater != null)
            {
                GameObject.Destroy(visualUpdater);
                Main.DebugLog(() => $"Removed CouplerVisualUpdater from {chainScript.couplerAdapter?.coupler?.train?.ID}");
            }
        }

        public static void AdjustPivot(Transform pivot, Transform target)
        {
            if (pivot == null || target == null)
                return;

            // Additional safety check to ensure transforms are still valid
            if (pivot.gameObject == null || target.gameObject == null)
                return;

            try
            {
                pivot.localEulerAngles = Vector3.zero;
                var offset = pivot.InverseTransformPoint(target.position);
                var angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                pivot.localEulerAngles = new Vector3(0, angle, 0);

                offset.y = 0f;
                var distance = offset.magnitude;
                var hook = pivot.Find("hook") ?? pivot.Find("hook_open");
                if (hook != null && hook.gameObject != null)
                {
                    hook.localPosition = distance / 2 * Vector3.forward;
                }
            }
            catch (System.Exception ex)
            {
                // Log only when logging is enabled
                if (Main.settings.enableLogging)
                    Main.ErrorLog(() => $"Exception in AdjustPivot: {ex.Message}");
            }
        }

        public static void UpdateHookVisualState(ChainCouplerInteraction chainScript, bool locked)
        {
            if (chainScript == null)
                return;

            var coupler = chainScript.couplerAdapter?.coupler;
            if (coupler == null)
                return;

            try
            {
                // Check if we need to swap the hook visual for couplers that support multiple states
                var couplerType = Main.settings.couplerType;
                if (couplerType == CouplerType.AARKnuckle || couplerType == CouplerType.SA3Knuckle)
                {
                    SwapHookVisualIfNeeded(chainScript, coupler);
                }

                // Determine the correct interaction text based on coupler state
                var pivot = GetPivot(chainScript);
                var hook = pivot?.Find("hook") ?? pivot?.Find("hook_open");
                if (hook?.GetComponent<InfoArea>() is InfoArea infoArea)
                {
                    // Base the text on the actual coupler state, not just the locked flag
                    switch (coupler.state)
                    {
                        case ChainCouplerInteraction.State.Parked:
                            // Parked = coupler is unlocked and ready to be made ready
                            infoArea.infoType = KnuckleCouplerLock; // "Press to ready coupler"
                            break;

                        case ChainCouplerInteraction.State.Dangling:
                        case ChainCouplerInteraction.State.Being_Dragged:
                        case ChainCouplerInteraction.State.Attached_Loose:
                        case ChainCouplerInteraction.State.Attached_Tight:
                            // All other states = coupler is ready/locked and can be unlocked
                            infoArea.infoType = KnuckleCouplerUnlock; // "Press to unlock coupler"
                            break;
                    }
                }

                // Handle visual disconnection for unlocked couplers
                if (coupler.state == ChainCouplerInteraction.State.Parked)
                {
                    // Manually trigger visual disconnection for knuckle couplers
                    if (pivot != null && pivot.gameObject != null && coupler.transform != null)
                    {
                        pivot.localEulerAngles = coupler.transform.localEulerAngles;
                        if (hook != null && hook.gameObject != null)
                        {
                            hook.localPosition = PivotLength * Vector3.forward;
                            Main.DebugLog(() => $"Reset knuckle coupler hook position for {coupler.train.ID} {coupler.Position()}");
                        }
                    }

                    // Clear the attached reference if it exists
                    if (chainScript.attachedTo != null)
                    {
                        chainScript.attachedTo.attachedTo = null;
                        chainScript.attachedTo = null;
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (Main.settings.enableLogging)
                {
                    Main.ErrorLog(() => $"Exception in UpdateHookVisualState: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Swap the hook visual for AAR couplers between normal and open states based on coupler state.
        /// </summary>
        private static void SwapHookVisualIfNeeded(ChainCouplerInteraction chainScript, Coupler coupler)
        {
            var pivot = GetPivot(chainScript);
            if (pivot == null)
            {
                return;
            }

            // Find hook by name - check for all possible variations
            var hookOpen = pivot.Find("hook_open") ?? pivot.Find("SA3_open");
            var hookClosed = pivot.Find("hook") ?? pivot.Find("SA3_closed");
            var hook = hookOpen ?? hookClosed;

            // Collect child names for potential diagnostics
            var childNames = new System.Collections.Generic.List<string>();
            for (int i = 0; i < pivot.childCount; i++)
            {
                var child = pivot.GetChild(i);
                if (child.name.Contains("hook") || child.name.Contains("SA3"))
                    childNames.Add(child.name);
            }

            if (hook == null)
            {
                return;
            }

            var isParked = coupler.state == ChainCouplerInteraction.State.Parked;
            var couplerType = Main.settings.couplerType;

            // Determine whether to use the open variant based on coupler type
            bool shouldUseOpenHook = false;
            if (couplerType == CouplerType.AARKnuckle)
            {
                shouldUseOpenHook = isParked && AssetManager.GetAAROpenPrefab() != null;
            }
            else if (couplerType == CouplerType.SA3Knuckle)
            {
                shouldUseOpenHook = isParked && AssetManager.GetSA3OpenPrefab() != null;
            }

            // Check if we need to swap the hook visual
            var currentHookName = hook.name;
            var needsSwap = false;
            var isCurrentlyOpen = currentHookName.Contains("open");

            if (shouldUseOpenHook && !isCurrentlyOpen)
            {
                needsSwap = true;
            }
            else if (!shouldUseOpenHook && isCurrentlyOpen)
            {
                needsSwap = true;
            }

            if (needsSwap)
            {
                Main.DebugLog(() => $"Hook visual swapped for {coupler.train.ID} {coupler.Position()} -> {(shouldUseOpenHook ? "open" : "closed")} state");

                // Play appropriate sound for the state change
                if (!shouldUseOpenHook && isCurrentlyOpen)
                {
                    // Swapping from open to closed - play park sound (coupler becoming ready)
                    chainScript.PlaySound(chainScript.parkSound, chainScript.transform.position);
                }
                else if (shouldUseOpenHook && !isCurrentlyOpen)
                {
                    // Swapping from closed to open - play attach sound (coupler becoming unlocked)
                    chainScript.PlaySound(chainScript.attachSound, chainScript.transform.position);
                }

                // Store current state
                var buttonSpec = hook.GetComponent<Button>();
                var infoArea = hook.GetComponent<InfoArea>();
                var currentPosition = hook.localPosition;
                var wasActive = hook.gameObject.activeSelf;

                // Remove old hook immediately to prevent conflicts
                GameObject.DestroyImmediate(hook.gameObject);

                // Create new hook with appropriate prefab
                GameObject? newHookPrefab = null;
                string desiredName = "";

                if (couplerType == CouplerType.AARKnuckle)
                {
                    newHookPrefab = shouldUseOpenHook ? AssetManager.GetAAROpenPrefab() : AssetManager.GetAARClosedPrefab();
                    desiredName = shouldUseOpenHook ? "hook_open" : "hook";
                }
                else if (couplerType == CouplerType.SA3Knuckle)
                {
                    newHookPrefab = shouldUseOpenHook ? AssetManager.GetSA3OpenPrefab() : AssetManager.GetSA3ClosedPrefab();
                    desiredName = shouldUseOpenHook ? "SA3_open" : "SA3_closed";
                }

                if (newHookPrefab != null && pivot != null)
                {
                    CreateHookInstance(pivot, newHookPrefab, chainScript, coupler, desiredName);
                    // Optionally verify creation by name if needed during debugging
                }
            }
        }

        /// <summary>
        /// Update hook visual state based on current coupler state.
        /// </summary>
        public static void UpdateHookVisualStateFromCouplerState(Coupler coupler)
        {
            if (coupler?.visualCoupler?.chainAdapter?.chainScript == null)
                return;

            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;

            // Use the existing UpdateHookVisualState method, but pass a dummy locked value
            // since the method now determines the correct state internally
            UpdateHookVisualState(chainScript, false);
        }

        private static void OnButtonPressed(ChainCouplerInteraction chainScript)
        {
            if (chainScript?.couplerAdapter?.coupler == null)
                return;

            var coupler = chainScript.couplerAdapter.coupler;

            // Use the coupler state to determine the action, consistent with visual text logic
            switch (coupler.state)
            {
                case ChainCouplerInteraction.State.Parked:
                    // Parked = coupler is unlocked; user wants to ready it
                    KnuckleCouplerState.ReadyCoupler(coupler);
                    break;

                case ChainCouplerInteraction.State.Dangling:
                case ChainCouplerInteraction.State.Being_Dragged:
                case ChainCouplerInteraction.State.Attached_Loose:
                case ChainCouplerInteraction.State.Attached_Tight:
                    // All other states = coupler is ready/locked; user wants to unlock it
                    KnuckleCouplerState.UnlockCoupler(coupler, viaChainInteraction: true);
                    break;
            }
        }

        /// Ensures a specific train car has knuckle couplers on both ends.
        /// Returns the number of knuckle couplers created.
        public static int EnsureKnuckleCouplersForTrain(TrainCar car, GameObject? hookPrefab)
        {
            if (car?.gameObject == null)
                return 0;

            int created = 0;

            // Check front coupler
            if (car.frontCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
            {
                var frontChainScript = car.frontCoupler.visualCoupler.chainAdapter.chainScript;
                if (GetPivot(frontChainScript) == null && frontChainScript.enabled)
                {
                    // Removed routine coupler creation log
                    CreateHook(frontChainScript, hookPrefab);
                    created++;
                }
            }

            // Check rear coupler
            if (car.rearCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
            {
                var rearChainScript = car.rearCoupler.visualCoupler.chainAdapter.chainScript;
                if (GetPivot(rearChainScript) == null && rearChainScript.enabled)
                {
                    // Removed routine coupler creation log
                    CreateHook(rearChainScript, hookPrefab);
                    created++;
                }
            }

            return created;
        }


        public static IEnumerator DelayedKnuckleCouplerCheck(TrainCar trainCar, GameObject? hookPrefab)
        {
            // Wait a frame for the train car to be fully set up
            yield return null;

            if (trainCar != null)
            {
                int created = EnsureKnuckleCouplersForTrain(trainCar, hookPrefab);
                if (created > 0)
                {
                    // Removed routine creation log
                }
            }
        }

        public static IEnumerator DelayedSpawnKnuckleCouplerCheck(TrainCar trainCar, GameObject? hookPrefab)
        {
            // Wait a bit longer for spawned cars to be fully initialized
            yield return new WaitForSeconds(0.5f);

            if (trainCar != null)
            {
                int created = EnsureKnuckleCouplersForTrain(trainCar, hookPrefab);
                if (created > 0)
                {
                    // Removed routine spawning log
                }
            }
        }
    }
}