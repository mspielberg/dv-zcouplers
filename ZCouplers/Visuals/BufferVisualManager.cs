using System;
using System.Collections;
using System.Linq;

using DV;
using DV.ThingTypes;
using DV.Utils;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace DvMod.ZCouplers;

public static class BufferVisualManager
{
    private static bool? _lastVisibilityState = null;
    // Exposed (internal) read helper so helper components can query current state
    internal static bool BuffersCurrentlyVisible => _lastVisibilityState ?? true;

    public static void ToggleBuffers(bool visible)
    {
        // If the requested state is the same as the last applied state, skip re-toggling prefabs
        // but still refresh live cars to cover newly spawned instances.
        bool sameAsLast = _lastVisibilityState.HasValue && _lastVisibilityState.Value == visible;

        if (!sameAsLast)
        {
            Main.DebugLog(() => "Toggling buffer visibility " + (visible ? "on" : "off"));
            _lastVisibilityState = visible;

            foreach (TrainCarLivery livery in Globals.G.Types.Liveries)
            {
                ToggleBuffers(livery.prefab, livery, visible);
            }
        }
        else
        {
            // Keep logs quiet but indicate a refresh for instances when debug logging is on
            Main.DebugLog(() => $"Refreshing buffer visibility for live cars (state unchanged: {(visible ? "on" : "off")})");
        }

        var spawner = SingletonBehaviour<CarSpawner>.Instance;
        if (spawner != null)
        {
            foreach (TrainCar allCar in spawner.allCars)
            {
                ToggleBuffers(allCar.gameObject, allCar.carLivery, visible);
            }
        }
    }

    /// <summary>
    /// Force a refresh of buffer visibility, ignoring the cached state.
    /// Use this when new cars are spawned and need to have their buffer visibility updated.
    /// </summary>
    public static void ForceRefreshBuffers(bool visible)
    {
        _lastVisibilityState = null;
        ToggleBuffers(visible);
    }

    /// <summary>
    /// Reset the cached visibility state. This will cause the next call to ToggleBuffers to execute regardless of the previous state.
    /// </summary>
    public static void ResetVisibilityCache()
    {
        _lastVisibilityState = null;
    }

    private static void ToggleBuffers(GameObject root, TrainCarLivery livery, bool visible)
    {
        Transform transform = root.transform.Find("[buffers]");
        if (transform != null)
        {
            ToggleBufferVisuals(transform, livery, visible);
        }
        else
        {
            Main.DebugLog(() => "No [buffers] hierarchy for " + livery.id + "; applying fallback");
            MeshRenderer[] componentsInChildren = root.GetComponentsInChildren<MeshRenderer>();
            int num = 0;
            MeshRenderer[] array = componentsInChildren;
            foreach (MeshRenderer renderer in array)
            {
                if (!IsZCouplersObject(renderer.transform) && (renderer.name.StartsWith("Buffer_") || renderer.name.Replace("_", "").ToLowerInvariant().Contains("bufferstem")))
                {
                    renderer.enabled = visible;
                    num++;
                }
            }
            if (num == 0)
            {
                Main.DebugLog(() => "No buffer elements found via fallback method for " + livery.id);
            }
        }
        ToggleSpecialLocoBufferStems(root, livery, visible);
		// CCL: additionally support markers named "[BufferStems]" anywhere under the car hierarchy
		ToggleCCLBufferStemsByMarker(root, livery, visible);
		ToggleDamageBufferStems(root, livery, visible);
        // Apply buffer stem collider management
        ToggleBufferStemColliders(root, livery, visible);
    }

    private static void ToggleBufferVisuals(Transform buffers, TrainCarLivery livery, bool visible)
    {
        int toggledVisuals = 0;
        MeshRenderer[] componentsInChildren = buffers.GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer renderer in componentsInChildren)
        {
            if (!IsZCouplersObject(renderer.transform)
                && renderer.name != "BuffersAndChainRig"
                && (HasBufferNameInAncestry(renderer.transform)
                    || renderer.name.StartsWith("CabooseExteriorBufferStems")
                    || renderer.name.StartsWith("Buffer_")
                    || renderer.name.Replace("_", "").ToLowerInvariant().Contains("bufferstem")))
            {
                renderer.enabled = visible;
                int num = toggledVisuals;
                toggledVisuals = num + 1;
            }
        }
    }

    private static bool IsZCouplersObject(Transform transform)
    {
        Transform? transform2 = transform;
        while (transform2 != null)
        {
            string name = transform2.name;
            if (name.StartsWith("ZCouplers pivot") || name == "hook" || (name == "walkable" && transform2.parent != null && transform2.parent.name == "hook"))
            {
                return true;
            }
            transform2 = transform2.parent;
        }
        return false;
    }

    private static void ToggleSpecialLocoBufferStems(GameObject root, TrainCarLivery livery, bool visible)
    {
        Transform? transform = null;
        string stemName = "";
        string? id = livery.id;
        if (id == null)
        {
            return;
        }
        switch (id.Length)
        {
            case 9:
                switch (id[8])
                {
                    default:
                        return;
                    case 'A':
                        if (!(id == "LocoS282A"))
                        {
                            return;
                        }
                        transform = root.transform.Find("LocoS282A_Body/Static_LOD0/s282_buffer_stems");
                        stemName = "s282_buffer_stems";
                        break;
                    case 'B':
                        {
                            if (!(id == "LocoS282B"))
                            {
                                return;
                            }
                            transform = root.transform.Find("LocoS282B_Body/LOD0/s282_tender_buffer_stems");
                            stemName = "s282_tender_buffer_stems";
                            Transform transform2 = root.transform.Find("LocoS282B_Body/LOD1/s282_tender_buffer_stems_LOD1");
                            if (transform2 != null)
                            {
                                MeshRenderer component = transform2.GetComponent<MeshRenderer>();
                                if (component != null)
                                {
                                    component.enabled = visible;
                                }
                            }
                            Transform transform3 = root.transform.Find("[colliders]/LocoS282B_Body/LOD0/s282_tender_buffer_stems");
                            MeshRenderer[] array;
                            SkinnedMeshRenderer[] array2;
                            if (transform3 != null)
                            {
                                MeshRenderer component2 = transform3.GetComponent<MeshRenderer>();
                                if (component2 != null)
                                {
                                    component2.enabled = visible;
                                }
                                SkinnedMeshRenderer component3 = transform3.GetComponent<SkinnedMeshRenderer>();
                                if (component3 != null)
                                {
                                    component3.enabled = visible;
                                }
                                MeshRenderer[] componentsInChildren = transform3.GetComponentsInChildren<MeshRenderer>();
                                SkinnedMeshRenderer[] componentsInChildren2 = transform3.GetComponentsInChildren<SkinnedMeshRenderer>();
                                array = componentsInChildren;
                                foreach (MeshRenderer childRenderer in array)
                                {
                                    if (childRenderer.transform != transform3)
                                    {
                                        childRenderer.enabled = visible;
                                    }
                                }
                                array2 = componentsInChildren2;
                                foreach (SkinnedMeshRenderer childRenderer2 in array2)
                                {
                                    if (childRenderer2.transform != transform3)
                                    {
                                        childRenderer2.enabled = visible;
                                    }
                                }
                            }
                            Transform transform4 = root.transform.Find("[colliders]/LocoS282B_Body/LOD1/s282_tender_buffer_stems_LOD1");
                            if (!(transform4 != null))
                            {
                                break;
                            }
                            MeshRenderer component4 = transform4.GetComponent<MeshRenderer>();
                            if (component4 != null)
                            {
                                component4.enabled = visible;
                            }
                            SkinnedMeshRenderer component5 = transform4.GetComponent<SkinnedMeshRenderer>();
                            if (component5 != null)
                            {
                                component5.enabled = visible;
                            }
                            MeshRenderer[] componentsInChildren3 = transform4.GetComponentsInChildren<MeshRenderer>();
                            SkinnedMeshRenderer[] componentsInChildren4 = transform4.GetComponentsInChildren<SkinnedMeshRenderer>();
                            array = componentsInChildren3;
                            foreach (MeshRenderer childRenderer3 in array)
                            {
                                if (childRenderer3.transform != transform4)
                                {
                                    childRenderer3.enabled = visible;
                                }
                            }
                            array2 = componentsInChildren4;
                            foreach (SkinnedMeshRenderer childRenderer4 in array2)
                            {
                                if (childRenderer4.transform != transform4)
                                {
                                    childRenderer4.enabled = visible;
                                }
                            }
                            break;
                        }
                }
                break;
            case 8:
                switch (id[4])
                {
                    default:
                        return;
                    case 'S':
                        if (!(id == "LocoS060"))
                        {
                            return;
                        }
                        transform = root.transform.Find("LocoS060_Body/Static/s060_buffer_stems");
                        stemName = "s060_buffer_stems";
                        break;
                    case 'D':
                        if (!(id == "LocoDM1U"))
                        {
                            return;
                        }
                        transform = root.transform.Find("LocoDM1U_Body/buffer_stems");
                        stemName = "buffer_stems";
                        break;
                }
                break;
            case 7:
                switch (id[6])
                {
                    default:
                        return;
                    case '3':
                        if (!(id == "LocoDM3"))
                        {
                            return;
                        }
                        transform = root.transform.Find("LocoDM3_Body/buffer_stems");
                        stemName = "buffer_stems";
                        break;
                    case '4':
                        if (!(id == "LocoDH4"))
                        {
                            return;
                        }
                        transform = root.transform.Find("LocoDH4_Body/dh4_buffer_stems");
                        stemName = "dh4_buffer_stems";
                        break;
                    case '6':
                        if (!(id == "LocoDE6"))
                        {
                            return;
                        }
                        transform = root.transform.Find("LocoDE6_Body/BufferStems");
                        stemName = "BufferStems";
                        break;
                    case '2':
                        if (!(id == "LocoDE2"))
                        {
                            return;
                        }
                        transform = root.transform.Find("LocoDE2_Body/BufferStems");
                        stemName = "BufferStems";
                        break;
                    case '5':
                        return;
                }
                break;
            case 16:
                if (!(id == "LocoMicroshunter"))
                {
                    return;
                }
                transform = root.transform.Find("LocoMicroshunter_Body/microshunter_buffer_stems");
                stemName = "microshunter_buffer_stems";
                break;
            case 11:
                if (!(id == "LocoDE6Slug"))
                {
                    return;
                }
                transform = root.transform.Find("LocoDE6Slug_Body/de6_slug_buffer_stems");
                stemName = "de6_slug_buffer_stems";
                break;
            default:
                return;
        }
        if (transform != null)
        {
            MeshRenderer component6 = transform.GetComponent<MeshRenderer>();
            if (component6 != null)
            {
                component6.enabled = visible;
            }
            SkinnedMeshRenderer component7 = transform.GetComponent<SkinnedMeshRenderer>();
            if (component7 != null)
            {
                component7.enabled = visible;
            }
            MeshRenderer[] componentsInChildren5 = transform.GetComponentsInChildren<MeshRenderer>();
            SkinnedMeshRenderer[] componentsInChildren6 = transform.GetComponentsInChildren<SkinnedMeshRenderer>();
            MeshRenderer[] array = componentsInChildren5;
            foreach (MeshRenderer childRenderer5 in array)
            {
                if (childRenderer5.transform != transform)
                {
                    childRenderer5.enabled = visible;
                }
            }
            SkinnedMeshRenderer[] array2 = componentsInChildren6;
            foreach (SkinnedMeshRenderer childRenderer6 in array2)
            {
                if (childRenderer6.transform != transform)
                {
                    childRenderer6.enabled = visible;
                }
            }
        }
        else
        {
            Main.DebugLog(() => "Special buffer stems not found: " + stemName + " on " + livery.id);
        }
    }

    // CCL: Find any transforms named "[BufferStems]" and toggle all renderers beneath them for CCL trains/cars
    private static void ToggleCCLBufferStemsByMarker(GameObject root, TrainCarLivery livery, bool visible)
    {
        try
        {
            var all = root.GetComponentsInChildren<Transform>(includeInactive: true);
            var markers = all.Where(t => t != null && t.name == "[BufferStems]");
            int toggled = 0;
            foreach (var marker in markers)
            {
                toggled += ToggleRendererTreeCCL(marker, visible);
            }
            if (toggled > 0)
            {
                Main.DebugLog(() => $"CCL: Toggled {toggled} renderer(s) under [BufferStems] on {livery.id}");
            }
        }
        catch (Exception ex)
        {
            Main.ErrorLog(() => "Error in ToggleCCLBufferStemsByMarker: " + ex.Message);
        }
    }

    // CCL: Toggle renderers under a marker for CCL trains/cars
    private static int ToggleRendererTreeCCL(Transform root, bool visible)
    {
        int count = 0;
        if (root == null)
            return count;

        try
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                if (!IsZCouplersObject(r.transform))
                {
                    r.enabled = visible;
                    count++;
                }
            }
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                if (!IsZCouplersObject(r.transform))
                {
                    r.enabled = visible;
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            Main.ErrorLog(() => "Error in ToggleRendererTreeCCL: " + ex.Message);
        }

        return count;
    }

    private static bool HasBufferNameInAncestry(Transform t)
    {
	    var cur = t;
	    while (cur != null)
	    {
		    string n = cur.name;
		    // Skip generic rig container; we care about specific buffer nodes above/below it.
		    if (string.Equals(n, "BuffersAndChainRig", StringComparison.Ordinal))
		    {
			    cur = cur.parent;
			    continue;
		    }

		    // Common buffer naming patterns
		    if (n.StartsWith("Buffer_", StringComparison.OrdinalIgnoreCase))
			    return true;
		    if (n.Replace("_", "").ToLowerInvariant().Contains("bufferstem"))
			    return true;
		    if (n.StartsWith("CabooseExteriorBufferStems", StringComparison.OrdinalIgnoreCase))
			    return true;

		    cur = cur.parent;
	    }
	    return false;
    }


    // Disable anything named like "*BufferStems_damage*" under a "{car}Exploded" variant in the given wagon root
    /// <summary>
    /// Toggle damage buffer stems (nodes containing "BufferStems_damage") for a given car root.
    /// Made internal so explosion patches can refresh visuals right after an explosion model swap.
    /// </summary>
    internal static void ToggleDamageBufferStems(GameObject root, TrainCarLivery livery, bool visible)
    {
	    try
	    {
		    var all = root.GetComponentsInChildren<Transform>(includeInactive: true);
		    if (all == null || all.Length == 0)
			    return;

		    // First try to find exploded variants under the livery structure
		    Transform liveryTransform = root.transform.Find(livery.id);
		    Transform[] searchRoots;

		    if (liveryTransform != null)
		    {
			    // Search within the livery hierarchy
			    searchRoots = liveryTransform.GetComponentsInChildren<Transform>(includeInactive: true);
		    }
		    else
		    {
			    // Fallback to searching the entire car hierarchy
			    searchRoots = all;
		    }

		    foreach (var searchRoot in searchRoots)
		    {
			    if (searchRoot == null) continue;
			    var subtree = searchRoot.GetComponentsInChildren<Transform>(includeInactive: true);
			    var targets = subtree.Where(t => t != null && NameContains(t.name, "BufferStems_damage"));
			    foreach (var target in targets)
			    {
				    var r = target.GetComponent<MeshRenderer>();
				    if (r != null)
					    r.enabled = visible;
				    var s = target.GetComponent<SkinnedMeshRenderer>();
				    if (s != null)
					    s.enabled = visible;
			    }
		    }
	    }
	    catch (Exception ex)
	    {
		    Main.ErrorLog(() => "Error in ToggleDamageBufferStems: " + ex.Message);
	    }
    }

    private static bool NameContains(string name, string needle)
    {
	    return name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Toggle buffer stem colliders to prevent collision when buffers are hidden.
    /// Disables MeshCollider/Exterior components under [interior]/[walkable]/ when [old] group exists,
    /// and disables Capsule (4-7) components under [walkable].
    /// </summary>
    private static void ToggleBufferStemColliders(GameObject root, TrainCarLivery livery, bool visible)
    {
        try
        {
            // Skip collider management for prefabs - only handle live cars in the scene
            if (root.scene.name == null || root.scene.name.Length == 0)
            {
                // This is a prefab, not a live car in the scene
                return;
            }

            // Get the TrainCar component to access its interior Transform reference
            TrainCar trainCar = root.GetComponent<TrainCar>();

            if (trainCar?.interior != null)
            {
                ProcessSingleInteriorObject(trainCar.interior.gameObject, livery.id, visible);
            }
            else
            {
                Main.DebugLog(() => $"No TrainCar component or interior found for {livery.id} - skipping collider management");
                return;
            }
        }
        catch (Exception ex)
        {
            Main.ErrorLog(() => $"Error in ToggleBufferStemColliders for {livery.id}: {ex.Message}");
        }
    }

    /// <summary>
    /// Process a single interior GameObject for buffer stem collider management
    /// </summary>
    private static void ProcessSingleInteriorObject(GameObject interiorGO, string liveryId, bool visible)
    {
        try
        {
            Transform interior = interiorGO.transform;

            // Find the walkable transform under interior
            Transform walkable = interior.Find("[walkable]");
            if (walkable == null)
            {
                Main.DebugLog(() => $"No [walkable] found under interior for {liveryId} - skipping");
                return;
            }

            bool foundOldGroup = false;
            bool foundColliders = false;

            // Check if there's an [old] group under walkable
            Transform oldGroup = walkable.Find("[old]");
            if (oldGroup != null)
            {
                foundOldGroup = true;
                // Enable/disable the [old] group based on buffer visibility state
                // When buffers are hidden (!visible), enable the [old] group
                bool oldGroupNewState = !visible;
                oldGroup.gameObject.SetActive(oldGroupNewState);

                // Look for MeshCollider, Exterior, and any buffer-related components under walkable to disable
                var allMeshColliders = walkable.GetComponentsInChildren<MeshCollider>(true); // Include inactive
                var exteriorObjects = walkable.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.Equals("Exterior", StringComparison.OrdinalIgnoreCase));

                // Find all Transform objects that might be buffer-related by name patterns
                var bufferRelatedObjects = walkable.GetComponentsInChildren<Transform>(true)
                    .Where(t => IsBufferRelatedName(t.name));

                foreach (var collider in allMeshColliders)
                {
                    if (oldGroup == null || (collider.transform != oldGroup && !IsChildOf(collider.transform, oldGroup)))
                    {
                        collider.enabled = visible;
                        foundColliders = true;
                    }
                }

                foreach (var exterior in exteriorObjects)
                {
                    if (oldGroup == null || (exterior != oldGroup && !IsChildOf(exterior, oldGroup)))
                    {
                        exterior.gameObject.SetActive(visible);
                        foundColliders = true;
                    }
                }

                // Process buffer-related objects (like DM1U buffers in "[old]/Buffers", Microshunter "hood F/Buffer", etc.)
                foreach (var bufferObj in bufferRelatedObjects)
                {
                    if (oldGroup == null || IsChildOf(bufferObj, oldGroup))
                    {
                        // Handle buffer objects that have any type of colliders (MeshCollider, CapsuleCollider, BoxCollider, etc.)
                        var bufferColliders = bufferObj.GetComponentsInChildren<Collider>(true);
                        foreach (var collider in bufferColliders)
                        {
                            collider.enabled = visible;
                        }

                        // Handle buffer GameObjects themselves
                        bufferObj.gameObject.SetActive(visible);
                        foundColliders = true;
                    }
                }
            }

            // Disable Capsule (4) to Capsule (7) components under [walkable]
            var capsuleColliders = walkable.GetComponentsInChildren<CapsuleCollider>(true); // Include inactive

            foreach (var capsule in capsuleColliders)
            {
                string name = capsule.name;
                if (name.ToLower().StartsWith("capsule"))
                {

                    bool wasEnabled = capsule.enabled;
                    capsule.enabled = visible;
                    foundColliders = true;

                }
            }

            if (!foundOldGroup && !foundColliders)
            {
                Main.ErrorLog(() => $"No [old] group or relevant colliders found under interior/[walkable]/ for {liveryId}");
            }
        }
        catch (Exception ex)
        {
            Main.ErrorLog(() => $"Error in ProcessSingleInteriorObject for {liveryId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Helper method to check if a transform is a child of another transform
    /// </summary>
    private static bool IsChildOf(Transform child, Transform parent)
    {
        if (child == null || parent == null) return false;

        Transform current = child.parent;
        while (current != null)
        {
            if (current == parent) return true;
            current = current.parent;
        }
        return false;
    }

    /// <summary>
    /// Helper method to get the full path of a GameObject in the hierarchy
    /// </summary>
    private static string GetGameObjectPath(GameObject obj)
    {
        if (obj == null) return "null";

        string path = obj.name;
        Transform parent = obj.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    /// <summary>
    /// Apply buffer stem collider management for a specific train car
    /// </summary>
    public static void ApplyBufferCollidersForCar(TrainCar car)
    {
        if (car == null) return;

        bool visible = BuffersCurrentlyVisible;
        string liveryId = car.carLivery.id;
        bool isLocomotive = liveryId.StartsWith("Loco");

        // Use the TrainCar's interior Transform reference - this points to the actual interior GameObject
        Transform interiorTransform = car.interior;

        if (interiorTransform != null)
        {
            ProcessSingleInteriorObject(interiorTransform.gameObject, car.carLivery.id, visible);
        }
        else
        {
            Main.DebugLog(() => $"No car.interior found for car {car.ID} ({liveryId})");
        }
    }

    /// <summary>
    /// Apply buffer stem collider management for all cars in the scene
    /// </summary>
    public static void ApplyBufferCollidersForAllCars()
    {
        var spawner = SingletonBehaviour<CarSpawner>.Instance;
        if (spawner == null) return;

        bool visible = BuffersCurrentlyVisible;
        int totalCars = spawner.allCars.Count;
        int processedCars = 0;
        int skippedCars = 0;

        // First, process all TrainCar instances
        foreach (TrainCar car in spawner.allCars)
        {
            if (car != null)
            {
                try
                {
                    ApplyBufferCollidersForCar(car);
                    processedCars++;
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Error processing car {car.ID} ({car.carLivery.id}): {ex.Message}");
                    skippedCars++;
                }
            }
            else
            {
                skippedCars++;
            }
        }

        Main.DebugLog(() => $"TrainCar processing completed: {processedCars} processed, {skippedCars} skipped out of {totalCars} total");

        // CRITICAL: Also process ALL interior objects directly to catch any missed instances
        ProcessAllInteriorObjects(visible);

        if (skippedCars > 0)
        {
            Main.ErrorLog(() => $"WARNING: {skippedCars} cars were skipped during buffer collider management!");
        }
    }

    /// <summary>
    /// Process ALL interior objects in the scene directly, regardless of TrainCar association
    /// This ensures we catch every single interior object, including orphaned ones
    /// </summary>
    private static void ProcessAllInteriorObjects(bool visible)
    {
        try
        {
            // Find ALL interior GameObjects in the scene
            GameObject[] allInteriors = UnityEngine.Object.FindObjectsOfType<GameObject>()
                .Where(go => go.name.Contains("(Clone) [interior]"))
                .ToArray();

            int processedInteriors = 0;

            foreach (var interior in allInteriors)
            {
                // Extract livery ID from the name (e.g., "LocoDH4(Clone) [interior]" -> "LocoDH4")
                string[] parts = interior.name.Split('(');
                if (parts.Length > 0)
                {
                    string liveryId = parts[0];
                    ProcessSingleInteriorObject(interior, liveryId, visible);
                    processedInteriors++;
                }
            }
        }
        catch (System.Exception ex)
        {
            Main.ErrorLog(() => $"Error in ProcessAllInteriorObjects: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if a GameObject name suggests it's buffer-related
    /// </summary>
    private static bool IsBufferRelatedName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        string nameLower = name.ToLowerInvariant();

        // Common buffer-related name patterns based on different train types:
        // - DM1U: "[old]/Buffers"
        // - S060: "[old]/Exterior/buffer (4-7)"
        // - S282A: "[old]/Exterior/buffer (4-5)"
        // - Microshunter: "[old]/hood F/Buffer", "[old]/hood F/Buffer (1)", "[old]/hood R/Buffer", "[old]/hood R/Buffer (1)"

        return nameLower.Contains("buffer") ||
               nameLower.Contains("buffers") ||
               nameLower.StartsWith("buffer") ||
               nameLower.EndsWith("buffer") ||
               // Pattern for named buffer groups like "hood F/Buffer", "hood R/Buffer"
               (nameLower.Contains("hood") && nameLower.Contains("buffer")) ||
               // Additional patterns for different locomotive types
               nameLower == "buffers" || // DM1U case
               (nameLower.Contains("exterior") && nameLower.Contains("buffer")); // S060/S282A case
    }
}
