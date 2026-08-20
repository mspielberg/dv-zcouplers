using System;
using System.Collections;
using System.Collections.Generic;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.ThingTypes;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Core.Profiles;
using DvMod.ZCouplers.Core.Utils;
using UnityEngine;

namespace DvMod.ZCouplers.Visuals
{
    /// <summary>
    /// Manages creation and lifecycle of knuckle coupler hook visuals.
    /// </summary>
    public static class HookManager
    {
        private static readonly Dictionary<ChainCouplerInteraction, Transform> pivots = new Dictionary<ChainCouplerInteraction, Transform>();
        private const float PivotLength = 1.0f;
        private const float HeightOffset = -0.067f;

        public static InteractionInfoType KnuckleCouplerReady = (InteractionInfoType)23000;
        public static InteractionInfoType KnuckleCouplerUnlocked = (InteractionInfoType)23001;
        public static InteractionInfoType KnuckleCouplerCoupled = (InteractionInfoType)23002;

        /// <summary>
        /// Clean up all hook pivots and clear tracking dictionary.
        /// Called during mod unload.
        /// </summary>
        public static void Cleanup()
        {
            // Destroy all hook pivots
            foreach (var pivot in pivots.Values)
            {
                if (pivot != null)
                    UnityEngine.Object.Destroy(pivot.gameObject);
            }

            // Clear tracking dictionary
            pivots.Clear();
        }

        /// <summary>
        /// Check if the given coupler is the front coupler of a LocoS282A (LocoSteamHeavy).
        /// </summary>
        private static bool IsFrontCouplerOnSteamLoco(Coupler coupler)
        {
	        return coupler.train?.carLivery?.id == "LocoS282A" && coupler.isFrontCoupler;
        }

        /// <summary>
        /// Check if the coupler should be disabled based on settings and locomotive type.
        /// </summary>
        public static bool ShouldDisableCoupler(Coupler coupler)
        {
	        return Main.settings.disableFrontCouplersOnSteamLocos && IsFrontCouplerOnSteamLoco(coupler);
        }

        /// <summary>
        /// Toggle air hoses and coupler mounting hardware for disabled couplers.
        /// Also toggles the coupler component functionality.
        /// For Scharfenberg couplers, air hoses are always deactivated.
        /// </summary>
        public static void ToggleCouplerHardware(Coupler coupler, bool visible)
        {
            if (coupler?.train?.gameObject == null)
                return;

            Main.DebugLog(() => $"ToggleCouplerHardware called for {coupler.train.ID} {coupler.Position()}: visible={visible}, isSteamLoco={CarTypes.IsSteamLocomotive(coupler.train?.carLivery)}, disableSetting={Main.settings.disableFrontCouplersOnSteamLocos}");

            // Toggle the coupler component functionality
            ToggleCouplerComponent(coupler, visible);

            // Hide or show the HookPlate for this specific coupler
            ToggleHookPlate(coupler, visible);

            bool isSteamLoco = Main.settings.disableFrontCouplersOnSteamLocos && coupler.train?.carLivery?.id == "LocoS282A";

            if (isSteamLoco)
            {
				// Disabling front coupler on steam loco - hide all air hoses on this train
	            ToggleAirHose(coupler, false);
            }
            else
            {
                // Normal trains: toggle air hoses based on visible parameter
                ToggleAirHose(coupler, visible);
            }

            // Ensure replacement socket plates are present (for enabled couplers)
            if (visible && coupler.train != null)
            {
	            EnsureSocketPlates(coupler.train, visible);
            }

            // Summary debug
            Main.DebugLog(() => $"Coupler hardware toggled for {coupler.train?.ID} {coupler.Position()}: visible={visible}");
        }

        /// <summary>
        /// Toggle the visibility of the HookPlate for a specific coupler (front or rear).
        /// Also handles ZC_Socket plates created by EnsureSocketPlates.
        /// When a socket exists, the original HookPlate should remain hidden.
        /// </summary>
        private static void ToggleHookPlate(Coupler coupler, bool visible)
        {
            if (coupler?.train?.gameObject == null)
                return;

            var buffers = coupler.train.gameObject.transform.Find("[buffers]");
            if (buffers == null)
            {
                buffers = FindTransformRecursive(coupler.train.gameObject.transform, "[buffers]");
                if (buffers == null)
                {
                    buffers = coupler.train.gameObject.transform;
                    Main.DebugLog(() => $"Using car root as buffers container for {coupler.train.ID}");
                }
            }

            // Determine which HookPlate and socket to toggle based on coupler position
            string hookPlateName = coupler.isFrontCoupler ? "HookPlate_F" : "HookPlate_R";
            string socketName = coupler.isFrontCoupler ? "ZC_Socket_F" : "ZC_Socket_R";

            // Check if a socket exists for this coupler
            bool hasSocket = false;
            foreach (var socket in FindAllTransformsByName(buffers, socketName, recursive: true))
            {
                if (socket != null)
                {
                    hasSocket = true;
                    socket.gameObject.SetActive(visible);
                    Main.DebugLog(() => $"{socketName} on {coupler.train.ID} set to visible={visible}");
                }
            }

            // Only toggle the original HookPlate if there's NO socket
            // If a socket exists, the original HookPlate should remain hidden
            if (!hasSocket)
            {
                int foundCount = 0;
                foreach (var hookPlate in FindAllTransformsByName(buffers, hookPlateName, recursive: true))
                {
                    if (hookPlate != null)
                    {
                        foundCount++;
                        hookPlate.gameObject.SetActive(visible);
                        Main.DebugLog(() => $"{hookPlateName} on {coupler.train.ID} set to visible={visible}");
                    }
                }

                if (foundCount == 0)
                {
                    Main.DebugLog(() => $"WARNING: No {hookPlateName} found on {coupler.train.ID}");
                }
            }
            else
            {
                // Socket exists, ensure original HookPlate stays hidden
                foreach (var hookPlate in FindAllTransformsByName(buffers, hookPlateName, recursive: true))
                {
                    if (hookPlate != null && hookPlate.gameObject.activeSelf)
                    {
                        hookPlate.gameObject.SetActive(false);
                        Main.DebugLog(() => $"Keeping {hookPlateName} hidden on {coupler.train.ID} (socket present)");
                    }
                }
            }
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
        /// For Scharfenberg couplers, air hoses are always hidden on all trains.
        /// For steam locomotives, air hoses are hidden only on front couplers when the disable setting is enabled.
        /// For all other cases, air hoses follow the visible parameter.
        /// </summary>
        public static void ToggleAirHose(Coupler coupler, bool visible)
        {
            if (coupler.train?.gameObject == null)
                return;

            // For profiles that always hide air hoses (e.g., Schaku), enforce it
            if (CouplerProfiles.Current?.Options.AlwaysHideAirHoses == true)
            {
                ToggleAirHoseVisibility(coupler, false);
                return;
            }

            // Disable air hose on S282A
            if (Main.settings.disableFrontCouplersOnSteamLocos && coupler.train?.carLivery?.id == "LocoS282A")
            {
	            ToggleAirHoseVisibility(coupler, false);
	            return;
            }

            // Default behavior: show/hide air hoses based on the visible parameter
            // This handles all normal trains and non-disabled couplers
            ToggleAirHoseVisibility(coupler, visible);
        }

        /// <summary>
        /// Toggle air hose visibility.
        /// </summary>
        private static void ToggleAirHoseVisibility(Coupler coupler, bool visible)
        {
            // Deterministic: only disable/enable both direct "hoses" children under the interior
            var interior = coupler.train?.interior;
            if (interior == null)
                return;

            for (int i = 0; i < interior.childCount; i++)
            {
                var child = interior.GetChild(i);
                if (child == null || child.name != "hoses") continue;
                if (!visible)
                {
	                child.gameObject.SetActive(false);
	                GameObjHider.Attach(child);
                }
                else
                {
	                GameObjHider.Detach(child);

	                // Temporarily disable CouplingHoseDelayedEnable components to prevent NullReferenceException
	                // during activation (they try to access PlayerManager.ActiveCamera which may not be ready yet)
	                var delayedEnableComponents = child.GetComponentsInChildren<CouplingHoseDelayedEnable>(true);
	                foreach (var component in delayedEnableComponents)
	                {
		                if (component != null)
		                {
			                component.enabled = false;
		                }
	                }

	                child.gameObject.SetActive(true);

	                // Re-enable the components after PlayerManager.ActiveCamera is available
	                if (delayedEnableComponents.Length > 0 && coupler.train != null)
	                {
		                coupler.train.StartCoroutine(ReEnableHoseComponentsWhenCameraReady(delayedEnableComponents));
	                }
                }
            }
        }

        /// <summary>
        /// Coroutine to re-enable CouplingHoseDelayedEnable components after PlayerManager.ActiveCamera is ready.
        /// This prevents NullReferenceException when the component tries to access the camera during its OnEnable.
        /// </summary>
        private static IEnumerator ReEnableHoseComponentsWhenCameraReady(CouplingHoseDelayedEnable[] components)
        {
            // Wait until PlayerManager.ActiveCamera is available
            // This is much more reliable than waiting for a fixed time
            while (PlayerManager.ActiveCamera == null)
            {
                yield return null; // Wait one frame
            }

            // Camera is now ready, safe to re-enable the components
            foreach (var component in components)
            {
                if (component != null)
                {
                    try
                    {
                        component.enabled = true;
                    }
                    catch (System.Exception ex)
                    {
                        // Suppress any exceptions during re-enable
                        if (Main.settings.enableLogging)
                            Main.DebugLog(() => $"Exception re-enabling CouplingHoseDelayedEnable: {ex.Message}");
                    }
                }
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

        private static void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                var child = obj.transform.GetChild(i)?.gameObject;
                if (child != null)
                    SetLayerRecursively(child, layer);
            }
        }

        private static void ForceRendererRefresh(Renderer r)
        {
            if (r == null) return;
            try
            {
                bool was = r.enabled;
                r.enabled = false;
                r.enabled = was || true; // ensure on
                r.transform.hasChanged = true;
            }
            catch { }
        }

        /// <summary>
        /// Ensure we have ZCouplers socket plates instantiated for the current coupler type on this car.
        /// New sockets are placed at the original HookPlate_F/R local position, plus profile-specific offset.
        /// </summary>
        private static void EnsureSocketPlates(TrainCar car, bool visible = true)
        {
            if (car?.gameObject == null || !visible)
                return;

            // Try direct find first
            var buffers = car.gameObject.transform.Find("[buffers]");
            if (buffers == null)
            {
                // Fallback: search recursively for a transform literally named "[buffers]"
                buffers = FindTransformRecursive(car.gameObject.transform, "[buffers]");
                if (buffers == null)
                {
                    // Last resort: some cars might not have a [buffers] container; we'll search the whole car
                    buffers = car.gameObject.transform;
                    if (Main.settings.enableLogging)
                        Main.DebugLog(() => $"'[buffers]' not found on {car.ID}; falling back to full-car search");
                }
            }

            // Get socket prefab from current profile (modular system)
            var profile = CouplerProfiles.Current;
            GameObject? socketPrefab = profile?.GetSocketPrefab();

            if (socketPrefab == null)
            {
                if (Main.settings.enableLogging)
                    Main.DebugLog(() => "Socket prefab is null for current coupler type; skipping creation");
                return; // Nothing to create for this coupler type
            }

            // Helper to create one socket at the position of an original plate (with offset)
            void CreateSocketIfMissing(string originalName, string newName)
            {
                bool isFrontPlate = originalName.EndsWith("_F", StringComparison.OrdinalIgnoreCase);
                // Avoid duplicates (search recursively)
                foreach (var existing in FindAllTransformsByName(buffers, newName, recursive: true))
                {
                    if (existing != null)
                        return; // already present somewhere under [buffers]
                }

                // Find the original plate transform (may be inactive)
                Transform? original = null;
                foreach (var t in FindAllTransformsByName(buffers, originalName, recursive: true))
                {
                    original = t;
                    break;
                }

                if (original == null)
                {
                    if (Main.settings.enableLogging)
                        Main.DebugLog(() => $"Original plate '{originalName}' not found on {car.ID}; skipping {newName}");
                    return; // No anchor found
                }

                var parentTransform = original.parent;
                var originalLocalPos = original.localPosition;

                // Get transform data from profile (modular system)
                Vector3 offset;
                Quaternion rotation;
                Vector3 scale;
                profile!.GetSocketTransform(isFrontPlate, out offset, out rotation, out scale);

                // Apply the prefab's original rotation to the profile rotation
                Quaternion prefabLocalRot = rotation * socketPrefab.transform.localRotation;
                Vector3 prefabScale = Vector3.Scale(scale, socketPrefab.transform.localScale);

                // Hide the original plate instead of destroying it
                original.gameObject.SetActive(false);

                var instance = GameObject.Instantiate(socketPrefab);
                if (instance == null)
                    return;

                instance.name = newName;
                instance.transform.SetParent(parentTransform, worldPositionStays: false);
                instance.transform.localPosition = originalLocalPos + offset;
                instance.transform.localRotation = prefabLocalRot;
                instance.transform.localScale = prefabScale;

                // Put sockets on the car root layer for consistent rendering
                int targetLayer = car.gameObject.layer;
                SetLayerRecursively(instance, targetLayer);

                // Ensure visible by default (all renderer types)
                var rends = instance.GetComponentsInChildren<Renderer>(true);
                foreach (var r in rends)
                {
                    r.enabled = true;
                    ForceRendererRefresh(r);
                }
                instance.SetActive(true);
                Main.DebugLog(() => $"Created socket '{instance.name}' on {car.ID} with position {instance.transform.localPosition} and scale {instance.transform.localScale}");
            }

            CreateSocketIfMissing("HookPlate_F", "ZC_Socket_F");
            CreateSocketIfMissing("HookPlate_R", "ZC_Socket_R");
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

        /// <summary>
        /// Decide whether the visual should use the "open" hook prefab based on coupler type and current state.
        /// Now delegates to the profile's ShouldUseOpenVisual method for full modularity.
        /// </summary>
        private static bool ShouldUseOpenHook(Coupler coupler)
        {
            if (coupler == null)
                return false;

            var profile = CouplerProfiles.Current;
            if (profile == null || profile.Options.HasOpenVariant != true)
                return false;

            // Delegate to profile's visual state logic
            return profile.ShouldUseOpenVisual(coupler.state);
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

            // If we already have a tracked pivot, nothing to do
            if (GetPivot(chainScript) != null)
                return;

            // Attempt to rebind to an existing ZCouplers pivot/hook left in the scene
            // from a previous mod load (static dictionary would have been cleared).
            if (TryRebindExistingPivot(chainScript))
            {
                // Ensure visuals/components are wired and hardware visible
                var existingUpdater = chainScript.gameObject.GetComponent<CouplerVisualUpdater>();
                if (existingUpdater == null)
                    chainScript.gameObject.AddComponent<CouplerVisualUpdater>();

                var existingCoupler = chainScript.couplerAdapter?.coupler;
                if (existingCoupler != null)
                {
                    ToggleCouplerHardware(existingCoupler, true);
                    UpdateHookVisualStateFromCouplerState(existingCoupler);
                }
                return; // Successfully rebound; do not create a duplicate
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

            // Determine which hook prefab to use based on coupler type and state (new mapping)
            var profile = CouplerProfiles.Current;
            GameObject? actualHookPrefab = null;
            string desiredName = profile?.Options.HookClosedChildName ?? "hook";
            bool initShouldUseOpenHook = ShouldUseOpenHook(coupler);
            if (profile != null)
            {
                actualHookPrefab = initShouldUseOpenHook ? profile.GetOpenPrefab() : profile.GetClosedPrefab();
                desiredName = initShouldUseOpenHook ? (profile.Options.HookOpenChildName ?? "hook_open")
                                                    : (profile.Options.HookClosedChildName ?? "hook");
            }

            if (actualHookPrefab == null)
            {
                // Fallback to provided prefab if profile-provided prefabs are missing
                actualHookPrefab = fallbackHookPrefab;
            }

            if (actualHookPrefab == null)
            {
                Main.ErrorLog(() => $"Hook prefab is null for coupler type {CouplerProfiles.Current}, state={coupler.state}, cannot create knuckle coupler hook");
                return;
            }

            if (!ValidateHookPrefab(actualHookPrefab))
            {
                Main.ErrorLog(() => $"Hook prefab validation failed for {actualHookPrefab.name}, cannot create knuckle coupler hook");
                return;
            }

            // Name the initial hook child according to the mapped state so swap detection works correctly
            CreateHookInstance(pivot.transform, actualHookPrefab, chainScript, coupler, desiredName);

            // Debug logging for successful LAP hook creation
            if (CouplerProfiles.Current == CouplerProfiles.GetById("LAP"))
            {
                Main.DebugLog(() => $"Successfully created LAP hook for {coupler.train.ID} {coupler.Position()}, using {(initShouldUseOpenHook ? "open" : "closed")} variant");
            }

            // Add the visual updater component to ensure rotation works
            if (chainScript.gameObject.GetComponent<CouplerVisualUpdater>() == null)
            {
                chainScript.gameObject.AddComponent<CouplerVisualUpdater>();
            }

            // Ensure coupler hardware is visible for enabled couplers
            ToggleCouplerHardware(coupler, true);
        }

        /// <summary>
        /// Try to find an existing ZCouplers pivot in the scene for this chainScript/coupler and rebind it
        /// to our runtime dictionary. Returns true if successful.
        /// </summary>
        private static bool TryRebindExistingPivot(ChainCouplerInteraction chainScript)
        {
            if (chainScript?.couplerAdapter?.coupler == null)
                return false;

            var coupler = chainScript.couplerAdapter.coupler;
            var pivot = FindExistingPivotForCoupler(coupler);
            if (pivot == null)
                return false;

            // Validate the pivot still has a hook child; if it does not, treat as not reboundable
            var hook = pivot.Find("hook") ?? pivot.Find("hook_open") ?? pivot.Find("SA3_closed") ?? pivot.Find("SA3_open") ?? pivot.Find("Schaku_closed") ?? pivot.Find("Schaku_open") ?? pivot.Find("LaP_closed") ?? pivot.Find("LaP_open");
            if (hook == null)
                return false;

            // Rebind in dictionary
            pivots[chainScript] = pivot;
            return true;
        }

        /// <summary>
        /// Locate an existing pivot Transform for a coupler created by ZCouplers in a previous load.
        /// Prefers exact names "ZCouplers pivot front/rear" under the train interior; falls back to any
        /// transform whose name contains "ZCouplers pivot" and picks the closest to the coupler.
        /// </summary>
        private static Transform? FindExistingPivotForCoupler(Coupler coupler)
        {
            var interior = coupler?.train?.interior;
            if (interior == null)
                return null;

            // Use a non-null local after guard to keep nullable analysis happy
            Transform interiorTf = interior;

            // 1) Try exact expected name first
            var isFront = coupler!.isFrontCoupler; // Unity coupler component exists here
            var expectedName = isFront ? "ZCouplers pivot front" : "ZCouplers pivot rear";
            var exact = FindTransformRecursive(interiorTf, expectedName);
            if (exact != null)
                return exact;

            // 2) Fallback: search for any transform containing our prefix
            Transform? best = null;
            float bestDist = float.MaxValue;

            var stack = new Stack<Transform>();
            stack.Push(interiorTf);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t != null)
                {
                    if (t.name.IndexOf("ZCouplers pivot", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // If front/rear keyword matches, consider it first by distance
                        bool nameMatchesSide = isFront ? t.name.IndexOf("front", StringComparison.OrdinalIgnoreCase) >= 0
                                                       : t.name.IndexOf("rear", StringComparison.OrdinalIgnoreCase) >= 0;
                        var couplerPos = coupler!.transform!.position;
                        var dist = Vector3.Distance(couplerPos, t.position);
                        // Prefer matching side, otherwise still eligible
                        var score = nameMatchesSide ? dist : dist + 1000f; // penalize non-matching side
                        if (score < bestDist)
                        {
                            bestDist = score;
                            best = t;
                        }
                    }

                    for (int i = 0; i < t.childCount; i++)
                        stack.Push(t.GetChild(i));
                }
            }

            // Only accept a candidate if it's reasonably close to this coupler (avoid binding front to rear, etc.)
            const float maxAcceptableDistance = 3.0f; // meters
            if (best != null && bestDist < maxAcceptableDistance)
                return best;

            return null;
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

            var hook = GameObject.Instantiate(hookPrefab, pivot, false);
            if (hook == null)
            {
                Main.ErrorLog(() => "Failed to instantiate hook from prefab");
                return;
            }

            hook.SetActive(false); // Defer Awake() until all components are added and initialized
            hook.name = desiredName; // Use the desired name instead of always "hook"
            hook.layer = LayerMask.NameToLayer("Interactable");

            // Set initial position with offsets
            var basePosition = PivotLength * Vector3.forward;
            var finalPosition = basePosition;

            // Apply profile-specified offsets
            var options = CouplerProfiles.Current?.Options;
            if (options != null)
                finalPosition += new Vector3(options.HookLateralOffsetX, 0f, 0f) + options.HookAdditionalOffset;

            // Apply height offset for LocoS282A front coupler
            if (coupler.train?.carLivery?.id == "LocoS282A" && coupler.isFrontCoupler)
            {
                // Move front coupler on LocoS282A down by 0.05 units
                finalPosition += new Vector3(0f, -0.05f, 0f);
            }

            hook.transform.localPosition = finalPosition;

            // Use the existing colliders from the prefab; support both BoxCollider and MeshCollider
            var interactionCollider = hook.GetComponent<BoxCollider>() ?? (Collider)hook.GetComponent<MeshCollider>();
            if (interactionCollider != null)
            {
                interactionCollider.isTrigger = true; // Ensure it's a trigger for interaction

                // Restore solid walkable collider like v1.2.2: create a child with a non-trigger collider
                // so the coupler head has physical collision for players while keeping interaction as trigger.
                var existingWalkable = hook.transform.Find("walkable");
                if (existingWalkable == null)
                {
                    var colliderHost = new GameObject("walkable");
                    colliderHost.layer = LayerMask.NameToLayer("Train_Walkable");
                    colliderHost.transform.SetParent(hook.transform, worldPositionStays: false);

                    // Create walkable collider matching the original type
                    if (interactionCollider is BoxCollider boxCollider)
                    {
                        var walkableCollider = colliderHost.AddComponent<BoxCollider>();
                        walkableCollider.center = boxCollider.center;
                        walkableCollider.size = boxCollider.size;
                        walkableCollider.isTrigger = false;
                    }
                    else if (interactionCollider is MeshCollider meshCollider)
                    {
                        var walkableCollider = colliderHost.AddComponent<MeshCollider>();
                        walkableCollider.sharedMesh = meshCollider.sharedMesh;
                        walkableCollider.convex = true; // Required for non-trigger MeshColliders
                        walkableCollider.isTrigger = false;
                    }
                }
                else
                {
                    // Ensure any existing walkable collider is configured properly
                    existingWalkable.gameObject.layer = LayerMask.NameToLayer("Train_Walkable");
                    if (existingWalkable.GetComponent<BoxCollider>() is BoxCollider wc)
                    {
                        wc.isTrigger = false;
                        if (interactionCollider is BoxCollider bc)
                        {
                            wc.center = bc.center;
                            wc.size = bc.size;
                        }
                    }
                    else if (existingWalkable.GetComponent<MeshCollider>() is MeshCollider wmc)
                    {
                        wmc.isTrigger = false;
                        wmc.convex = true;
                        if (interactionCollider is MeshCollider mc)
                        {
                            wmc.sharedMesh = mc.sharedMesh;
                        }
                    }
                }

                var existingItems = hook.transform.Find("items");
                if (existingItems == null)
                {
                    var colliderHost = new GameObject("items");
                    colliderHost.layer = LayerMask.NameToLayer("Train_Interior");
                    colliderHost.transform.SetParent(hook.transform, worldPositionStays: false);

                    // Create items collider matching the original type
                    if (interactionCollider is BoxCollider boxCollider)
                    {
                        var itemCollider = colliderHost.AddComponent<BoxCollider>();
                        itemCollider.center = boxCollider.center;
                        itemCollider.size = boxCollider.size;
                        itemCollider.isTrigger = false;
                    }
                    else if (interactionCollider is MeshCollider meshCollider)
                    {
                        var itemCollider = colliderHost.AddComponent<MeshCollider>();
                        itemCollider.sharedMesh = meshCollider.sharedMesh;
                        itemCollider.convex = true; // Required for non-trigger MeshColliders
                        itemCollider.isTrigger = false;
                    }
                }
                else
                {
                    // Ensure any existing item collider is configured properly
                    existingItems.gameObject.layer = LayerMask.NameToLayer("Train_Interior");
                    if (existingItems.GetComponent<BoxCollider>() is BoxCollider ic)
                    {
                        ic.isTrigger = false;
                        if (interactionCollider is BoxCollider bc)
                        {
                            ic.center = bc.center;
                            ic.size = bc.size;
                        }
                    }
                    else if (existingItems.GetComponent<MeshCollider>() is MeshCollider imc)
                    {
                        imc.isTrigger = false;
                        imc.convex = true;
                        if (interactionCollider is MeshCollider mc)
                        {
                            imc.sharedMesh = mc.sharedMesh;
                        }
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
            // Prevent VRTK NRE in VR: ControlImplBase.GenerateHandPoses() accesses
            // spec.handPosesOverride which is null by default on dynamically created buttons
            buttonSpec.handPosesOverride = new DV.Interaction.InteractionHandPoses();

            var infoArea = hook.AddComponent<InfoArea>();
            // Set initial interaction type based on coupler state
            if (coupler.IsCoupled() && coupler.state == ChainCouplerInteraction.State.Attached_Tight)
            {
                infoArea.infoType = KnuckleCouplerCoupled;
            }
            else
            {
                infoArea.infoType = KnuckleCouplerState.IsUnlocked(coupler) ? KnuckleCouplerUnlocked : KnuckleCouplerReady;
            }
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
                    // Calculate vertical rotation (pitch) for Scharfenberg couplers
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
                var hook = pivot.Find("hook") ?? pivot.Find("hook_open") ?? pivot.Find("SA3_closed") ?? pivot.Find("SA3_open") ?? pivot.Find("Schaku_closed") ?? pivot.Find("Schaku_open") ?? pivot.Find("LaP_closed") ?? pivot.Find("LaP_open");
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
                    if (coupler?.train?.carLivery?.id == "LocoS282A" && coupler.isFrontCoupler)
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

        /// <summary>
        /// Update hook visual state based on current coupler state.
        /// Uses immediate hook swapping for proper visual synchronization during loading.
        /// </summary>
        public static void UpdateHookVisualStateFromCouplerState(Coupler? coupler)
        {
            if (coupler?.visualCoupler?.chainAdapter?.chainScript == null)
                return;

            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;

            // Use immediate hook swapping instead of the deferred UpdateHookVisualState
            UpdateHookVisualStateImmediate(chainScript, coupler);

            // Update LAP link visibility if using LAP couplers
            if (CouplerProfiles.Current != CouplerProfiles.GetById("LAP") || !coupler.IsCoupled()) return;
            var otherCoupler = coupler.coupledTo;
            if (otherCoupler != null)
            {
	            LAPLinkManager.CreateOrShowLink(coupler, otherCoupler);
            }
        }

        /// <summary>
        /// Update hook visual state with immediate hook swapping - safe for loading and normal contexts
        /// </summary>
        private static void UpdateHookVisualStateImmediate(ChainCouplerInteraction chainScript, Coupler coupler)
        {
            if (chainScript == null || coupler == null)
                return;

            // Get pivot and hook
            var pivot = GetPivot(chainScript);
            var options = CouplerProfiles.Current?.Options;
            var hookOpen = pivot?.Find(options?.HookOpenChildName);
            var hookClosed = pivot?.Find(options?.HookClosedChildName);
            var hook = hookOpen ?? hookClosed;

            try
            {
                SwapHookVisualImmediately(chainScript, coupler);

                // Determine the correct interaction text based on coupler state
                if (hook?.GetComponent<InfoArea>() is { } infoArea)
                {
                    // Base the text on the actual coupler state, not just the locked flag
                    switch (coupler.state)
                    {
	                    case ChainCouplerInteraction.State.Parked:
		                    // Parked = coupler is unlocked, but for Scharfenberg or auto-coupling mode, show "ready"
		                    if (CouplerProfiles.Current != null && (CouplerProfiles.Current.Options.EnforceAutoCoupling || Main.settings.autoCouplingMode))
		                    {
			                    infoArea.infoType = KnuckleCouplerReady; // "Coupler is ready"
		                    }
		                    else
		                    {
			                    infoArea.infoType = KnuckleCouplerUnlocked; // "Coupler is unlocked"
		                    }
                            break;

                        case ChainCouplerInteraction.State.Attached_Tight:
                            // Attached_Tight = coupler is coupled to another coupler
	                        infoArea.infoType = KnuckleCouplerCoupled; // "Coupler is coupled"
                            break;

                        case ChainCouplerInteraction.State.Dangling:
                            // Dangling = coupler is ready but not coupled
                            infoArea.infoType = KnuckleCouplerReady; // "Coupler is ready"
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
                    Main.ErrorLog(() => $"Exception in UpdateHookVisualStateImmediate: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Immediate hook visual swap - safe when not in button interaction context
        /// </summary>
        private static void SwapHookVisualImmediately(ChainCouplerInteraction chainScript, Coupler coupler)
        {
            var pivot = GetPivot(chainScript);
            if (pivot == null)
            {
                return;
            }

            // Find hook by name
            var options = CouplerProfiles.Current?.Options;
            var hookOpen = pivot.Find(options?.HookOpenChildName);
            var hookClosed = pivot.Find(options?.HookClosedChildName);
            var hook = hookOpen ?? hookClosed;

            if (hook == null)
            {
                return;
            }

            // New mapping
            bool shouldUseOpenHook = ShouldUseOpenHook(coupler);

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
                // Prefetch the replacement prefab; if unavailable (e.g., assets not yet loaded), skip swapping
                GameObject? newHookPrefab = null;
                string? desiredName = "";
                var profile = CouplerProfiles.Current;
                if (profile != null)
                {
                    newHookPrefab = shouldUseOpenHook ? profile.GetOpenPrefab() : profile.GetClosedPrefab();
                    desiredName = shouldUseOpenHook ? (options?.HookOpenChildName) : (options?.HookClosedChildName);
                }

                if (newHookPrefab == null || pivot == null)
                {
                    // Don't destroy existing hook if we can't replace it yet
                    return;
                }

                // Immediate swap - safe when not called during button interaction
                Main.DebugLog(() => $"Hook visual swapped immediately for {coupler.train.ID} {coupler.Position()} -> {(shouldUseOpenHook ? "open" : "closed")} state");

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

                // Remove old hook and create replacement immediately
                // Don't destroy the currently-used Button GameObject inside its Use() call.
                // Rename and deactivate it to hide and avoid name collisions, create the new hook now,
                // and destroy the old one deferred (end of frame) to prevent NREs.
                var oldGo = hook.gameObject;
                oldGo.name = oldGo.name + "__old";
                oldGo.SetActive(false);

                // Create replacement immediately so visuals update this frame
                if (desiredName != null) CreateHookInstance(pivot, newHookPrefab, chainScript, coupler, desiredName);

                // Destroy old after this frame; safe vs. ButtonBase.Use stack
                GameObject.Destroy(oldGo);
            }
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
            UpdateHookVisualStateFromCouplerState(coupler);
        }

        /// Ensures a specific train car has knuckle couplers on both ends.
        /// Returns the number of knuckle couplers created.
        public static int EnsureKnuckleCouplersForTrain(TrainCar car, GameObject? hookPrefab)
        {
            if (car?.gameObject == null)
                return 0;
            var options = CouplerProfiles.Current?.Options;
            if (options == null || options.HasSocketPlates)
            {
                // Add ZCouplers socket plates
                EnsureSocketPlates(car);
            }

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
                    // Check if this coupler should be disabled
                    if (!ShouldDisableCoupler(car.rearCoupler))
                    {
                        // Removed routine coupler creation log
                        CreateHook(rearChainScript, hookPrefab);
                        created++;
                    }
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
                EnsureKnuckleCouplersForTrain(trainCar, hookPrefab);
            }
        }

        /// <summary>
        /// Restore original HookPlates to visible state when switching to coupler types that don't need custom sockets.
        /// </summary>
        public static void RestoreOriginalHookPlates(TrainCar car)
        {
            if (car?.gameObject == null)
                return;

            var buffers = car.gameObject.transform.Find("[buffers]");
            if (buffers == null)
            {
                buffers = FindTransformRecursive(car.gameObject.transform, "[buffers]");
                if (buffers == null)
                {
                    buffers = car.gameObject.transform;
                }
            }

            // Restore HookPlate_F
            foreach (var hookPlate in FindAllTransformsByName(buffers, "HookPlate_F", recursive: true))
            {
                if (hookPlate != null)
                {
                    hookPlate.gameObject.SetActive(true);
                    Main.DebugLog(() => $"Restored original HookPlate_F visibility on {car.ID}");
                }
            }

            // Restore HookPlate_R
            foreach (var hookPlate in FindAllTransformsByName(buffers, "HookPlate_R", recursive: true))
            {
                if (hookPlate != null)
                {
                    hookPlate.gameObject.SetActive(true);
                    Main.DebugLog(() => $"Restored original HookPlate_R visibility on {car.ID}");
                }
            }
        }

        /// <summary>
        /// Clean up ZCouplers socket instances when switching to coupler types that don't need them.
        /// </summary>
        public static void CleanupZCouplersSockets(TrainCar car)
        {
            if (car?.gameObject == null)
                return;

            var buffers = car.gameObject.transform.Find("[buffers]");
            if (buffers == null)
            {
                buffers = FindTransformRecursive(car.gameObject.transform, "[buffers]");
                if (buffers == null)
                {
                    buffers = car.gameObject.transform;
                }
            }

            // Remove ZC_Socket_F instances
            var frontSockets = FindAllTransformsByName(buffers, "ZC_Socket_F", recursive: true);
            foreach (var socket in frontSockets)
            {
                if (socket != null)
                {
                    GameObject.Destroy(socket.gameObject);
                    Main.DebugLog(() => $"Cleaned up ZC_Socket_F on {car.ID}");
                }
            }

            // Remove ZC_Socket_R instances
            var rearSockets = FindAllTransformsByName(buffers, "ZC_Socket_R", recursive: true);
            foreach (var socket in rearSockets)
            {
                if (socket != null)
                {
                    GameObject.Destroy(socket.gameObject);
                    Main.DebugLog(() => $"Cleaned up ZC_Socket_R on {car.ID}");
                }
            }
        }

        /// <summary>
        /// Clean up all HookPlate-related objects for runtime coupler switching.
        /// </summary>
        public static void CleanupHookPlatesForTypeSwitch(TrainCar car)
        {
            if (car?.gameObject == null)
                return;

            // Clean up existing ZCouplers sockets
            CleanupZCouplersSockets(car);

            // Restore original HookPlates to visible state so they can be managed by the new coupler type
            RestoreOriginalHookPlates(car);

            Main.DebugLog(() => $"Cleaned up HookPlates for type switch on {car.ID}");
        }
    }
}
