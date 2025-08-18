using System;
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

        /// <summary>
        /// Check if the given coupler is the front coupler of a LocoS282A (LocoSteamHeavy).
        /// </summary>
        private static bool IsFrontCouplerOnLocoS282A(Coupler coupler)
        {
            if (coupler?.train?.carLivery?.id != "LocoS282A")
                return false;

            return coupler.isFrontCoupler;
        }

        /// <summary>
        /// Check if the coupler should be disabled based on settings and locomotive type.
        /// </summary>
        public static bool ShouldDisableCoupler(Coupler coupler)
        {
            if (!Main.settings.disableFrontCouplersOnSteamLocos)
                return false;

            if (!coupler.isFrontCoupler)
                return false;

            var liveryId = coupler?.train?.carLivery?.id;
            return liveryId == "LocoS282A" || liveryId == "LocoS060";
        }

        /// <summary>
        /// Check whether a locomotive is a steam locomotive type.
        /// </summary>
        private static bool IsSteamLocomotive(Coupler? coupler)
        {
            var liveryId = coupler?.train?.carLivery?.id;
            return liveryId == "LocoS282A" || liveryId == "LocoS060";
        }

        /// <summary>
        /// Toggle air hoses and coupler mounting hardware for disabled couplers.
        /// Also toggles the coupler component functionality.
        /// For Schafenberg couplers, air hoses are always deactivated.
        /// </summary>
        public static void ToggleCouplerHardware(Coupler coupler, bool visible)
        {
            if (coupler?.train?.gameObject == null)
                return;

            var trainGameObject = coupler.train.gameObject;
            var liveryId = coupler.train.carLivery?.id;

            // Toggle the actual coupler component functionality
            ToggleCouplerComponent(coupler, visible);

            // Profile-driven hose policy (e.g., Schaku hides hoses regardless of visible)
            var profile = CouplerProfiles.Current;
            bool shouldShowAirHose = visible && (profile?.Options.AlwaysHideAirHoses != true);

            // Toggle air hose
            ToggleAirHose(coupler, shouldShowAirHose);

            // Toggle mounting hardware based on locomotive type
            if (liveryId == "LocoS060")
            {
                ToggleHookPlate(trainGameObject, visible);
            }

            // Summary debug
            Main.DebugLog(() => $"Coupler hardware toggled for {coupler.train.ID} {coupler.Position()}: visible={visible}, airHose={shouldShowAirHose}");
        }

        /// <summary>
        /// Toggle the coupler component enabled state to enable or disable coupling functionality.
        /// </summary>
        private static void ToggleCouplerComponent(Coupler coupler, bool enabled)
        {
            if (coupler?.gameObject == null)
                return;

            // Find the coupler component and toggle it
            var couplerComponent = coupler.gameObject.GetComponent<Coupler>();
            if (couplerComponent != null)
            {
                couplerComponent.enabled = enabled;
                Main.DebugLog(() => $"Coupler component set to {enabled} for {coupler.train.ID} {coupler.Position()}");
            }

            // Also toggle the ChainCouplerInteraction component if it exists
            var chainCouplerInteraction = coupler.visualCoupler?.chainAdapter?.chainScript;
            if (chainCouplerInteraction != null)
            {
                chainCouplerInteraction.enabled = enabled;
                Main.DebugLog(() => $"ChainCouplerInteraction set to {enabled} for {coupler.train.ID} {coupler.Position()}");
            }
        }

        /// <summary>
        /// Toggle air hose visibility for a specific coupler.
        /// For Schafenberg couplers, air hoses are always hidden on all trains.
        /// For steam locomotives, air hoses are hidden only on front couplers when the disable setting is enabled.
        /// </summary>
        public static void ToggleAirHose(Coupler coupler, bool visible)
        {
            if (coupler?.train?.gameObject == null)
                return;

            var trainGameObject = coupler.train.gameObject;

            // For profiles that always hide air hoses (e.g., Schaku), enforce it
            if (CouplerProfiles.Current?.Options.AlwaysHideAirHoses == true)
            {
                ToggleAirHoseOnAllTrainTypes(trainGameObject, coupler, false);
                return;
            }

            // Original logic for steam locomotives when the disable setting is enabled
            if (!Main.settings.disableFrontCouplersOnSteamLocos)
                return;

            // Only process steam locomotives (S282A and S060)
            if (!IsSteamLocomotive(coupler))
                return;

            // Only process front couplers (rear couplers on steam locomotives keep their air hoses)
            if (!coupler.isFrontCoupler)
                return;

            ToggleAirHoseOnSteamLocomotive(trainGameObject, coupler, visible);
        }

        /// <summary>
        /// Toggle air hose visibility on all train types (for Schafenberg couplers).
        /// Uses the same proven approach as steam locomotive air hose handling.
        /// </summary>
        private static void ToggleAirHoseOnAllTrainTypes(GameObject trainGameObject, Coupler coupler, bool visible)
        {
            // Deterministic: only disable/enable both direct "hoses" children under the interior
            var interior = coupler.train?.interior;
            if (interior == null)
                return;

            for (int i = 0; i < interior.childCount; i++)
            {
                var child = interior.GetChild(i);
                if (child != null && child.name == "hoses")
                {
                    child.gameObject.SetActive(visible);
                    if (!visible) HoseHider.Attach(child);
                }
            }
        }

        /// <summary>
        /// Toggle air hose visibility on steam locomotives (original logic).
        /// </summary>
        private static void ToggleAirHoseOnSteamLocomotive(GameObject trainGameObject, Coupler coupler, bool visible)
        {
            var trainName = trainGameObject.name;

            // For steam locomotives, look for their interior objects
            var interiorName = $"{trainName} [interior]";

            // Find all matching interior objects, not just the first one
            var allGameObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            var matchingInteriors = new List<GameObject>();

            foreach (var obj in allGameObjects)
            {
                if (obj.name == interiorName)
                {
                    matchingInteriors.Add(obj);
                }
            }

            if (matchingInteriors.Count == 0)
            {
                // Silent if no interior found; fall back to hierarchy search
                return;
            }

            // Process all matching interior objects
            foreach (var interiorGameObject in matchingInteriors)
            {
                // Toggle all direct hose children (there are usually two)
                SetActiveForChildrenNamed(interiorGameObject.transform, "hoses", visible, recursive: false);
                // and recursive as a safety net
                SetActiveForChildrenNamed(interiorGameObject.transform, "hoses", visible, recursive: true);
                if (!visible)
                {
                    foreach (var t in FindAllTransformsByName(interiorGameObject.transform, "hoses", recursive: true))
                        HoseHider.Attach(t);
                }
            }

            // Fallback 1: try to find the coupler hierarchy (original logic)
            var couplerHierarchy = coupler.isFrontCoupler ? "[coupler_front]" : "[coupler_rear]";
            var couplerTransform = trainGameObject.transform.Find(couplerHierarchy);
            if (couplerTransform != null)
            {
                foreach (var t in FindAllTransformsByName(couplerTransform, "hoseAndCock"))
                {
                    var renderers = t.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (var renderer in renderers)
                        renderer.enabled = visible;
                    if (!visible) HoseHider.Attach(t);
                }
            }

            // Fallback 2: search for air hose related objects in the entire train hierarchy
            var airHoseObjects = new string[]
            {
                "hoseAndCock",
                $"hoseAndCock_{(coupler.isFrontCoupler ? "front" : "rear")}",
                "AirHose",
                "Hose",
                $"{(coupler.isFrontCoupler ? "Front" : "Rear")}AirHose",
                $"AirHose{(coupler.isFrontCoupler ? "Front" : "Rear")}"
            };

            foreach (var hoseObjectName in airHoseObjects)
                foreach (var t in FindAllTransformsByName(trainGameObject.transform, hoseObjectName))
                {
                    var renderers = t.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (var renderer in renderers)
                        renderer.enabled = visible;
                    if (!visible) HoseHider.Attach(t);
                }
        }

        /// <summary>
        /// Recursively find a transform by name.
        /// </summary>
        private static Transform? FindTransformRecursive(Transform parent, string name)
        {
            if (parent.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindTransformRecursive(parent.GetChild(i), name);
                if (result != null)
                    return result;
            }

            return null;
        }

        /// <summary>
        /// Find all child transforms (optionally recursive) whose name equals the provided name (case-insensitive).
        /// </summary>
        private static IEnumerable<Transform> FindAllTransformsByName(Transform root, string name, bool recursive = true)
        {
            if (root == null)
                yield break;

            var comparison = StringComparison.OrdinalIgnoreCase;

            if (!recursive)
            {
                for (int i = 0; i < root.childCount; i++)
                {
                    var child = root.GetChild(i);
                    if (child.name.Equals(name, comparison))
                        yield return child;
                }
                yield break;
            }

            // Recursive traversal
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                for (int i = 0; i < current.childCount; i++)
                {
                    var child = current.GetChild(i);
                    if (child.name.Equals(name, comparison))
                        yield return child;
                    stack.Push(child);
                }
            }
        }

        /// <summary>
        /// Toggle GameObject active for all children with the given name. Returns how many were toggled.
        /// </summary>
        private static int SetActiveForChildrenNamed(Transform parent, string name, bool active, bool recursive)
        {
            int count = 0;
            foreach (var t in FindAllTransformsByName(parent, name, recursive))
            {
                if (t != null && t.gameObject != null)
                {
                    t.gameObject.SetActive(active);
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Helper to enable/disable all renderers under a transform and set its GameObject active.
        /// </summary>
        private static void SetRenderersAndObjectActive(Transform t, bool visible)
        {
            if (t == null)
                return;
            var renderers = t.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
                r.enabled = visible;
            t.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Toggle HookPlate_F visibility for S060 locomotive.
        /// </summary>
        private static void ToggleHookPlate(GameObject trainGameObject, bool visible)
        {
            // Look for HookPlate_F in the [buffers] hierarchy
            var buffersTransform = trainGameObject.transform.Find("[buffers]");
            if (buffersTransform != null)
            {
                var hookPlate = FindHookPlateRecursive(buffersTransform);
                if (hookPlate != null)
                {
                    var renderer = hookPlate.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.enabled = visible;
                    }
                }
            }

            // Also check in the main hierarchy as backup
            var mainHookPlate = FindHookPlateRecursive(trainGameObject.transform);
            if (mainHookPlate != null)
            {
                var renderer = mainHookPlate.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = visible;
                }
            }
        }

        /// <summary>
        /// Recursively find HookPlate_F transform.
        /// </summary>
        private static Transform? FindHookPlateRecursive(Transform parent)
        {
            if (parent.name == "HookPlate_F")
                return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindHookPlateRecursive(parent.GetChild(i));
                if (result != null)
                    return result;
            }

            return null;
        }

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

            // Check if this coupler should be disabled based on settings
            if (ShouldDisableCoupler(chainScript.couplerAdapter.coupler))
            {
                // Hide coupler hardware (air hose, mounting brackets) for disabled couplers
                ToggleCouplerHardware(chainScript.couplerAdapter.coupler, false);
                return;
            }

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

            // Ensure coupler hardware is visible for enabled couplers
            ToggleCouplerHardware(coupler, true);
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

            // Set initial position with offsets
            var basePosition = PivotLength * Vector3.forward;
            var finalPosition = basePosition;

            // Apply profile-specified offsets
            var options = CouplerProfiles.Current?.Options;
            if (options != null)
                finalPosition += new Vector3(options.HookLateralOffsetX, 0f, 0f) + options.HookAdditionalOffset;

            // Apply height offset for LocoS282A front coupler
            if (IsFrontCouplerOnLocoS282A(coupler))
            {
                // Move front coupler on LocoS282A down by 0.05 units
                finalPosition += new Vector3(0f, -0.05f, 0f);
            }

            hook.transform.localPosition = finalPosition;

            // Use the existing colliders from the prefab; no automatic creation
            var interactionCollider = hook.GetComponent<BoxCollider>();
            if (interactionCollider != null)
            {
                interactionCollider.isTrigger = true; // Ensure it's a trigger for interaction

                // Restore solid walkable collider like v1.2.2: create a child with a non-trigger BoxCollider
                // so the coupler head has physical collision for players while keeping interaction as trigger.
                var existingWalkable = hook.transform.Find("walkable");
                if (existingWalkable == null)
                {
                    var colliderHost = new GameObject("walkable");
                    colliderHost.layer = LayerMask.NameToLayer("Train_Walkable");
                    colliderHost.transform.SetParent(hook.transform, worldPositionStays: false);

                    var walkableCollider = colliderHost.AddComponent<BoxCollider>();
                    walkableCollider.center = interactionCollider.center;
                    walkableCollider.size = interactionCollider.size;
                    walkableCollider.isTrigger = false;
                }
                else
                {
                    // Ensure any existing walkable collider is configured properly
                    existingWalkable.gameObject.layer = LayerMask.NameToLayer("Train_Walkable");
                    if (existingWalkable.GetComponent<BoxCollider>() is BoxCollider wc)
                    {
                        wc.isTrigger = false;
                        wc.center = interactionCollider.center;
                        wc.size = interactionCollider.size;
                    }
                }
            }
            else
            {
                // Prefab has no BoxCollider; skip walkable collider creation
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
            }

            // If this coupler should be disabled, hide its hardware
            if (chainScript.couplerAdapter?.coupler != null && ShouldDisableCoupler(chainScript.couplerAdapter.coupler))
            {
                ToggleCouplerHardware(chainScript.couplerAdapter.coupler, false);
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

                // Calculate horizontal rotation (yaw)
                var horizontalAngle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;

                // Vertical articulation is profile-driven
                if (CouplerProfiles.Current?.Options.SupportsVerticalArticulation == true)
                {
                    // Calculate vertical rotation (pitch) for Schafenberg couplers
                    var horizontalDistance = Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
                    var verticalAngle = -Mathf.Atan2(offset.y, horizontalDistance) * Mathf.Rad2Deg;

                    // Apply both horizontal and vertical rotations
                    pivot.localEulerAngles = new Vector3(verticalAngle, horizontalAngle, 0);
                }
                else
                {
                    // Other coupler types only rotate horizontally
                    pivot.localEulerAngles = new Vector3(0, horizontalAngle, 0);
                }

                // Keep the Y component for distance calculation but don't zero it out for positioning
                var distance = offset.magnitude;
                var hook = pivot.Find("hook") ?? pivot.Find("hook_open") ?? pivot.Find("SA3_closed") ?? pivot.Find("SA3_open") ?? pivot.Find("Schaku_closed") ?? pivot.Find("Schaku_open");
                if (hook != null && hook.gameObject != null)
                {
                    // Base position at half distance
                    var basePosition = distance / 2 * Vector3.forward;

                    // Start with base position
                    var finalPosition = basePosition;
                    var options = CouplerProfiles.Current?.Options;
                    if (options != null)
                        finalPosition += new Vector3(options.HookLateralOffsetX, 0f, 0f) + options.HookAdditionalOffset;

                    // Apply height offset for LocoS282A front coupler
                    var coupler = pivot.GetComponentInParent<Coupler>();
                    if (IsFrontCouplerOnLocoS282A(coupler))
                    {
                        // Move front coupler on LocoS282A down by 0.05 units
                        finalPosition += new Vector3(0f, -0.05f, 0f);
                    }

                    hook.localPosition = finalPosition;

                    // Intentionally not logging per-frame positioning
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
                if (couplerType == CouplerType.AARKnuckle || couplerType == CouplerType.SA3Knuckle || couplerType == CouplerType.Schafenberg)
                {
                    SwapHookVisualIfNeeded(chainScript, coupler);
                }

                // Determine the correct interaction text based on coupler state
                var pivot = GetPivot(chainScript);
                var hook = pivot?.Find("hook") ?? pivot?.Find("hook_open") ?? pivot?.Find("SA3_closed") ?? pivot?.Find("SA3_open") ?? pivot?.Find("Schaku_closed") ?? pivot?.Find("Schaku_open");
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
                            // Base position when parked/disconnected
                            var basePosition = PivotLength * Vector3.forward;

                            // Start with base position and apply profile offsets
                            var finalPosition = basePosition;
                            var options = CouplerProfiles.Current?.Options;
                            if (options != null)
                                finalPosition += new Vector3(options.HookLateralOffsetX, 0f, 0f) + options.HookAdditionalOffset;

                            // Apply height offset for LocoS282A front coupler
                            if (IsFrontCouplerOnLocoS282A(coupler))
                            {
                                // Move front coupler on LocoS282A down by 0.05 units
                                finalPosition += new Vector3(0f, -0.05f, 0f);
                            }

                            hook.localPosition = finalPosition;
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

            // Find hook by name - use profile names with fallbacks
            var options = CouplerProfiles.Current?.Options;
            var openName = options?.HookOpenChildName ?? "hook_open";
            var closedName = options?.HookClosedChildName ?? "hook";
            var hookOpen = pivot.Find(openName) ?? pivot.Find("hook_open") ?? pivot.Find("SA3_open") ?? pivot.Find("Schaku_open");
            var hookClosed = pivot.Find(closedName) ?? pivot.Find("hook") ?? pivot.Find("SA3_closed") ?? pivot.Find("Schaku_closed");
            var hook = hookOpen ?? hookClosed;

            // Collect child names for potential diagnostics
            var childNames = new System.Collections.Generic.List<string>();
            for (int i = 0; i < pivot.childCount; i++)
            {
                var child = pivot.GetChild(i);
                if (child.name.Contains("hook") || child.name.Contains("SA3") || child.name.Contains("Schaku"))
                    childNames.Add(child.name);
            }

            if (hook == null)
            {
                return;
            }

            var isParked = coupler.state == ChainCouplerInteraction.State.Parked;
            var profile = CouplerProfiles.Current;
            bool shouldUseOpenHook = profile?.Options.HasOpenVariant == true && isParked && profile.GetOpenPrefab() != null;

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

                if (profile != null)
                {
                    newHookPrefab = shouldUseOpenHook ? profile.GetOpenPrefab() : profile.GetClosedPrefab();
                    desiredName = shouldUseOpenHook ? (options?.HookOpenChildName ?? "hook_open") : (options?.HookClosedChildName ?? "hook");
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
                    // Check if this coupler should be disabled
                    if (!ShouldDisableCoupler(car.frontCoupler))
                    {
                        // Removed routine coupler creation log
                        CreateHook(frontChainScript, hookPrefab);
                        created++;
                    }
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