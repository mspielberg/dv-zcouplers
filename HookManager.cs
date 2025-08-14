using System.Collections;
using System.Collections.Generic;

using DV;
using DV.CabControls;
using DV.CabControls.Spec;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Manages the creation and lifecycle of knuckle coupler hook visuals
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

            // Additional safety check to ensure the pivot transform is still valid
            if (pivot == null || pivot.gameObject == null)
            {
                // Clean up the stale reference
                pivots.Remove(chainScript);
                return null;
            }

            return pivot;
        }

        public static void CreateHook(ChainCouplerInteraction chainScript, GameObject? hookPrefab)
        {
            if (chainScript == null)
                return;

            // Check if hook already exists
            if (GetPivot(chainScript) != null)
            {
                Main.DebugLog(() => $"Knuckle coupler already exists for {chainScript.couplerAdapter?.coupler?.train?.ID}, skipping creation");
                return;
            }

            var coupler = chainScript.couplerAdapter.coupler;
            var pivot = new GameObject(coupler.isFrontCoupler ? "ZCouplers pivot front" : "ZCouplers pivot rear");
            pivot.transform.SetParent(coupler.transform, false);
            pivot.transform.localPosition = new Vector3(0, HeightOffset, -PivotLength);
            pivot.transform.parent = coupler.train.interior;
            pivots.Add(chainScript, pivot.transform);

            if (hookPrefab == null)
            {
                Main.ErrorLog(() => "Hook prefab is null, cannot create knuckle coupler hook");
                return;
            }

            var hook = GameObject.Instantiate(hookPrefab);
            hook.SetActive(false); // defer Awake() until all components are added and initialized
            hook.name = "hook";
            hook.layer = LayerMask.NameToLayer("Interactable");
            hook.transform.SetParent(pivot.transform, false);
            hook.transform.localPosition = PivotLength * Vector3.forward;

            var interactionCollider = hook.GetComponent<BoxCollider>();
            interactionCollider.isTrigger = true;

            var colliderHost = new GameObject("walkable");
            colliderHost.layer = LayerMask.NameToLayer("Train_Walkable");
            colliderHost.transform.SetParent(hook.transform, worldPositionStays: false);
            var walkableCollider = colliderHost.AddComponent<BoxCollider>();
            walkableCollider.center = interactionCollider.center;
            walkableCollider.size = interactionCollider.size;

            var buttonSpec = hook.AddComponent<Button>();
            buttonSpec.createRigidbody = false;
            buttonSpec.useJoints = false;
            buttonSpec.colliderGameObjects = new GameObject[] { hook };

            var infoArea = hook.AddComponent<InfoArea>();
            infoArea.infoType = KnuckleCouplerState.IsUnlocked(coupler) ? KnuckleCouplerLock : KnuckleCouplerUnlock;
            hook.SetActive(true); // this should create an actual Button through excuting
            hook.GetComponent<ButtonBase>().Used += () => OnButtonPressed(chainScript);
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
                var hook = pivot.Find("hook");
                if (hook != null && hook.gameObject != null)
                {
                    hook.localPosition = distance / 2 * Vector3.forward;
                }
            }
            catch (System.Exception ex)
            {
                // Silently handle any transform access exceptions that might occur during destruction
                if (Main.settings.enableLogging)
                {
                    Main.ErrorLog(() => $"Exception in AdjustPivot: {ex.Message}");
                }
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
                // Determine the correct interaction text based on coupler state
                var pivot = GetPivot(chainScript);
                var hook = pivot?.Find("hook");
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
        /// Update hook visual state based on current coupler state
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
                    // Parked = coupler is unlocked, user wants to ready it
                    KnuckleCouplerState.ReadyCoupler(coupler);
                    break;

                case ChainCouplerInteraction.State.Dangling:
                case ChainCouplerInteraction.State.Being_Dragged:
                case ChainCouplerInteraction.State.Attached_Loose:
                case ChainCouplerInteraction.State.Attached_Tight:
                    // All other states = coupler is ready/locked, user wants to unlock it
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