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
        public static InteractionInfoType KnuckleCouplerCoupled = (InteractionInfoType)23002;

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
            // Disable front coupler on the S282A
            return liveryId == "LocoS282A";
        }

        /// <summary>
        /// Check whether a locomotive should use steam-specific front hose handling.
        /// </summary>
        private static bool IsSteamLocomotive(Coupler? coupler)
        {
            var liveryId = coupler?.train?.carLivery?.id;
            return liveryId == "LocoS282A";
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

            // Toggle the actual coupler component functionality
            ToggleCouplerComponent(coupler, visible);

            // Profile-driven hose policy (e.g., Schaku hides hoses regardless of visible)
            var profile = CouplerProfiles.Current;
            bool shouldShowAirHose = visible && (profile?.Options.AlwaysHideAirHoses != true);

            // Toggle air hose
            ToggleAirHose(coupler, shouldShowAirHose);

            // Ensure replacement socket plates are present (destroys originals)
            EnsureSocketPlates(coupler.train);

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

            // Only process heavy steam locomotive (S282A)
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
                    if (!visible)
                        GameObjHider.Attach(child);
                    else
                        GameObjHider.Detach(child);
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
                foreach (var t in FindAllTransformsByName(interiorGameObject.transform, "hoses", recursive: true))
                {
                    if (!visible) GameObjHider.Attach(t);
                    else GameObjHider.Detach(t);
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
                    if (!visible) GameObjHider.Attach(t);
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
                    if (!visible) GameObjHider.Attach(t);
                    else GameObjHider.Detach(t);
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
        /// Ensure we have ZCouplers socket plates instantiated for the current coupler type (AAR/SA3) on this car.
        /// New sockets are placed at the original HookPlate_F/R local position, plus a small type-specific offset, under the same parent.
        /// </summary>
        private static void EnsureSocketPlates(TrainCar car)
        {
            if (car?.gameObject == null)
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
                        Main.DebugLog(() => $"[Sockets] '[buffers]' not found on {car.ID}; falling back to full-car search");
                }
            }

            // Pick prefab based on current coupler type
            GameObject? socketPrefab = null;
            switch (Main.settings.couplerType)
            {
                case CouplerType.AARKnuckle:
                    socketPrefab = AssetManager.GetAARSocketPrefab();
                    break;
                case CouplerType.SA3Knuckle:
                    socketPrefab = AssetManager.GetSA3SocketPrefab();
                    break;
                default:
                    socketPrefab = null;
                    break;
            }

            if (socketPrefab == null)
            {
                if (Main.settings.enableLogging)
                    Main.DebugLog(() => "[Sockets] Socket prefab is null for current coupler type; skipping creation");
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
                        Main.DebugLog(() => $"[Sockets] Original plate '{originalName}' not found on {car.ID}; skipping {newName}");
                    return; // No anchor found
                }

                var parentTransform = original.parent;
                var originalLocalPos = original.localPosition;
                
                // Type-specific local offset relative to the stock HookPlate position
                Vector3 offset = Vector3.zero;
                Quaternion prefabLocalRot = socketPrefab.transform.localRotation; // Default to prefab rotation
                Vector3 prefabScale = socketPrefab.transform.localScale;
                if (isFrontPlate)
                {
                    switch (Main.settings.couplerType)
                    {
                        case CouplerType.AARKnuckle:
                            offset = new Vector3(0f, -0.01f, 0.01f);
                            //prefabScale += new Vector3(-1f, 0f, 10f);
                            break;
                        case CouplerType.SA3Knuckle:
                            offset = new Vector3(-0.02f, 0.04f, 0.01f);
                            break;
                        default:
                            break;
                    }
                }
                // Rear sockets need other offsets and rotation
                else if (!isFrontPlate)
                {
                    switch (Main.settings.couplerType)
                    {
                        case CouplerType.AARKnuckle:
                            offset = new Vector3(0f, -0.01f, -0.01f);
                            prefabLocalRot = Quaternion.Euler(0f, 180f, 0f) * socketPrefab.transform.localRotation;
                            //prefabScale += new Vector3(-1f, 0f, 10f);
                            break;
                        case CouplerType.SA3Knuckle:
                            offset = new Vector3(0.02f, 0.04f, -0.01f);
                            prefabLocalRot = Quaternion.Euler(0f, 180f, 0f) * socketPrefab.transform.localRotation;
                            break;
                        default:
                            break;
                    }
                }

                // Destroy the original plate completely
                GameObject.Destroy(original.gameObject);

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
                Main.DebugLog(() => $"[Sockets] Created socket '{instance.name}' on {car.ID} with position {instance.transform.localPosition} and scale {instance.transform.localScale}");
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

            // Name the initial hook child according to the state so swap detection works correctly
            var options = CouplerProfiles.Current?.Options;
            var profile = CouplerProfiles.Current;
            bool initialShouldUseOpenHook = profile?.Options.HasOpenVariant == true && isParked && profile.GetOpenPrefab() != null;
            string desiredName = initialShouldUseOpenHook ? (options?.HookOpenChildName ?? "hook_open")
                                                          : (options?.HookClosedChildName ?? "hook");

            CreateHookInstance(pivot.transform, actualHookPrefab, chainScript, coupler, desiredName);

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

            // Validate the pivot still has a hook child; if it doesn’t, treat as not reboundable
            var hook = pivot.Find("hook") ?? pivot.Find("hook_open") ?? pivot.Find("SA3_closed") ?? pivot.Find("SA3_open") ?? pivot.Find("Schaku_closed") ?? pivot.Find("Schaku_open");
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
            // Set initial interaction type based on coupler state
            if (coupler.IsCoupled() && coupler.state == ChainCouplerInteraction.State.Attached_Tight)
            {
                infoArea.infoType = KnuckleCouplerCoupled;
            }
            else
            {
                infoArea.infoType = KnuckleCouplerState.IsUnlocked(coupler) ? KnuckleCouplerLock : KnuckleCouplerUnlock;
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

                        case ChainCouplerInteraction.State.Attached_Tight:
                            // Attached_Tight = coupler is coupled to another coupler
                            if (coupler.IsCoupled())
                            {
                                infoArea.infoType = KnuckleCouplerCoupled; // "Coupler is coupled"
                            }
                            else
                            {
                                infoArea.infoType = KnuckleCouplerUnlock; // "Press to unlock coupler"
                            }
                            break;

                        case ChainCouplerInteraction.State.Dangling:
                        case ChainCouplerInteraction.State.Being_Dragged:
                        case ChainCouplerInteraction.State.Attached_Loose:
                            // These states = coupler is ready/locked but not coupled, can be unlocked
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
                // Prefetch the replacement prefab; if unavailable (e.g., assets not yet loaded), skip swapping
                GameObject? newHookPrefab = null;
                string desiredName = "";
                if (profile != null)
                {
                    newHookPrefab = shouldUseOpenHook ? profile.GetOpenPrefab() : profile.GetClosedPrefab();
                    desiredName = shouldUseOpenHook ? (options?.HookOpenChildName ?? "hook_open") : (options?.HookClosedChildName ?? "hook");
                }

                if (newHookPrefab == null || pivot == null)
                {
                    // Don’t destroy existing hook if we can’t replace it yet
                    return;
                }

                // Remove old hook and create replacement - defer to avoid NRE during button interaction
                chainScript.StartCoroutine(DelayedHookSwap(chainScript, coupler, shouldUseOpenHook));
            }
        }

        /// <summary>
        /// Delayed hook visual swap to avoid NRE during button interaction processing
        /// </summary>
        private static System.Collections.IEnumerator DelayedHookSwap(ChainCouplerInteraction chainScript, Coupler coupler, bool shouldUseOpenHook)
        {
            // Wait a frame to allow button interaction to complete
            yield return null;

            var pivot = GetPivot(chainScript);
            if (pivot == null)
                yield break;

            // Re-find the hook after waiting a frame
            var options = CouplerProfiles.Current?.Options;
            var openName = options?.HookOpenChildName ?? "hook_open";
            var closedName = options?.HookClosedChildName ?? "hook";
            var hookOpen = pivot.Find(openName) ?? pivot.Find("hook_open") ?? pivot.Find("SA3_open") ?? pivot.Find("Schaku_open");
            var hookClosed = pivot.Find(closedName) ?? pivot.Find("hook") ?? pivot.Find("SA3_closed") ?? pivot.Find("Schaku_closed");
            var hook = hookOpen ?? hookClosed;

            if (hook == null)
                yield break;

            // Prefetch the replacement prefab
            GameObject? newHookPrefab = null;
            string desiredName = "";
            var profile = CouplerProfiles.Current;
            if (profile != null)
            {
                newHookPrefab = shouldUseOpenHook ? profile.GetOpenPrefab() : profile.GetClosedPrefab();
                desiredName = shouldUseOpenHook ? (options?.HookOpenChildName ?? "hook_open") : (options?.HookClosedChildName ?? "hook");
            }

            if (newHookPrefab == null || pivot == null)
                yield break;

            Main.DebugLog(() => $"Hook visual swapped for {coupler.train.ID} {coupler.Position()} -> {(shouldUseOpenHook ? "open" : "closed")} state");

            // Play appropriate sound for the state change
            var isCurrentlyOpen = hook.name.Contains("open");
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

            // Remove old hook and create replacement
            GameObject.DestroyImmediate(hook.gameObject);
            CreateHookInstance(pivot, newHookPrefab, chainScript, coupler, desiredName);
        }

        /// <summary>
        /// Update hook visual state based on current coupler state.
        /// Uses immediate hook swapping for proper visual synchronization during loading.
        /// </summary>
        public static void UpdateHookVisualStateFromCouplerState(Coupler coupler)
        {
            if (coupler?.visualCoupler?.chainAdapter?.chainScript == null)
                return;

            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;

            // Use immediate hook swapping instead of the deferred UpdateHookVisualState
            UpdateHookVisualStateImmediate(chainScript, coupler);
        }

        /// <summary>
        /// Update hook visual state with immediate hook swapping - safe for loading and normal contexts
        /// </summary>
        private static void UpdateHookVisualStateImmediate(ChainCouplerInteraction chainScript, Coupler coupler)
        {
            if (chainScript == null || coupler == null)
                return;

            try
            {
                // Check if we need to swap the hook visual for couplers that support multiple states
                var couplerType = Main.settings.couplerType;
                if (couplerType == CouplerType.AARKnuckle || couplerType == CouplerType.SA3Knuckle || couplerType == CouplerType.Schafenberg)
                {
                    SwapHookVisualImmediately(chainScript, coupler);
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

                        case ChainCouplerInteraction.State.Attached_Tight:
                            // Attached_Tight = coupler is coupled to another coupler
                            if (coupler.IsCoupled())
                            {
                                infoArea.infoType = KnuckleCouplerCoupled; // "Coupler is coupled"
                            }
                            else
                            {
                                infoArea.infoType = KnuckleCouplerUnlock; // "Press to unlock coupler"
                            }
                            break;

                        case ChainCouplerInteraction.State.Dangling:
                        case ChainCouplerInteraction.State.Being_Dragged:
                        case ChainCouplerInteraction.State.Attached_Loose:
                            // These states = coupler is ready/locked but not coupled, can be unlocked
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

            // Find hook by name - use profile names with fallbacks
            var options = CouplerProfiles.Current?.Options;
            var openName = options?.HookOpenChildName ?? "hook_open";
            var closedName = options?.HookClosedChildName ?? "hook";
            var hookOpen = pivot.Find(openName) ?? pivot.Find("hook_open") ?? pivot.Find("SA3_open") ?? pivot.Find("Schaku_open");
            var hookClosed = pivot.Find(closedName) ?? pivot.Find("hook") ?? pivot.Find("SA3_closed") ?? pivot.Find("Schaku_closed");
            var hook = hookOpen ?? hookClosed;

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
                // Prefetch the replacement prefab; if unavailable (e.g., assets not yet loaded), skip swapping
                GameObject? newHookPrefab = null;
                string desiredName = "";
                if (profile != null)
                {
                    newHookPrefab = shouldUseOpenHook ? profile.GetOpenPrefab() : profile.GetClosedPrefab();
                    desiredName = shouldUseOpenHook ? (options?.HookOpenChildName ?? "hook_open") : (options?.HookClosedChildName ?? "hook");
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
                GameObject.DestroyImmediate(hook.gameObject);
                CreateHookInstance(pivot, newHookPrefab, chainScript, coupler, desiredName);
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
        }

        /// Ensures a specific train car has knuckle couplers on both ends.
        /// Returns the number of knuckle couplers created.
        public static int EnsureKnuckleCouplersForTrain(TrainCar car, GameObject? hookPrefab)
        {
            if (car?.gameObject == null)
                return 0;

            // Add ZCouplers socket plates for AAR/SA3 (destroys originals)
            EnsureSocketPlates(car);

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