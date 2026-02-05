using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DV.ThingTypes;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Visuals;
using UnityEngine;

namespace DvMod.ZCouplers.Physics;

public static class BufferCollisionManager
{
	/// <summary>
	/// Apply buffer stem collider management for a specific train car
	/// </summary>
	public static void ApplyBufferCollidersForCar(TrainCar car)
	{
		if (car == null) return;

		bool visible = BufferVisualManager.BuffersCurrentlyVisible;
		string liveryId = car.carLivery.id;

		// Use the TrainCar's interior Transform reference - this points to the actual interior GameObject
		Transform interiorTransform = car.interior;

		if (car._isCaboose == true)
		{
			Main.DebugLog(() => $"Skipping caboose {car.ID}");
			return;
		}
		if (interiorTransform != null)
		{
			ProcessInteriorObject(interiorTransform.gameObject, car.carLivery.id, visible);
		}
		else
		{
			Main.DebugLog(() => $"No car.interior found for car {car.ID} ({liveryId})");
		}
	}

    /// <summary>
    /// Process a single interior GameObject for buffer stem collider management
    /// </summary>
    private static void ProcessInteriorObject(GameObject interiorGO, string liveryId, bool visible)
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

                // Enable/disable all MeshColliders
                foreach (var collider in allMeshColliders)
                {
	                if (collider.transform == oldGroup || IsChildOf(collider.transform, oldGroup)) continue;
	                collider.enabled = visible;
                }

                // Enable/disable all Exterior GameObjects
                foreach (var exterior in exteriorObjects)
                {
	                if (exterior == oldGroup || IsChildOf(exterior, oldGroup)) continue;
	                exterior.gameObject.SetActive(visible);
                }

                // Find all Transform objects that might be buffer-related by name patterns
                var bufferRelatedObjects = walkable.GetComponentsInChildren<Transform>(true)
	                .Where(t => IsBufferRelatedName(t.name));

                // Special handling for DE6 (but not DE6Slug)
                if (liveryId.Contains("LocoDE6") && !liveryId.Contains("DE6Slug"))
                {
                    // DE6 has Capsule 1-4 instead of 4-7
                    bufferRelatedObjects = walkable.GetComponentsInChildren<Transform>(true)
                        .Where(t => Regex.IsMatch(t.name, @"capsule \([1-4]\)", RegexOptions.IgnoreCase))
                        .ToList();
                }


                // Process buffer-related objects by name
                var relatedObjects = bufferRelatedObjects as Transform[] ?? bufferRelatedObjects.ToArray();
                foreach (var bufferObj in relatedObjects)
                {
	                // Skip if not at least two buffers were found
	                if (relatedObjects.Length <= 1) continue;

	                if (!IsChildOf(bufferObj, oldGroup)) continue;

	                // Handle buffer objects that have any type of colliders (MeshCollider, CapsuleCollider, BoxCollider, etc.)
	                var bufferColliders = bufferObj.GetComponentsInChildren<Collider>(true);

	                foreach (var collider in bufferColliders)
	                {
		                collider.enabled = visible;
		                Main.DebugLog(() =>
			                $"Toggled collider '{collider.name}' (type: {collider.GetType().Name}) to {visible} for {liveryId} by name pattern");
	                }

	                // Handle buffer GameObjects themselves
	                bufferObj.gameObject.SetActive(visible);
	                foundColliders = true;
                }

	            // Special handling for DH4 since it has no buffer colliders
                if (!foundColliders)
                {
	                // Skip for LocoDH4 since it has no buffer colliders
	                if (liveryId.Contains("LocoDH4")) return;
	                var allColliders = oldGroup.GetComponentsInChildren<Collider>(true)
		                .Where(c => c is not MeshCollider) // Exclude mesh colliders
		                .ToList();
                }
            }

			// Process CCL cars separately
            if (isCCL(TrainCar.Resolve(interiorGO)))
            {
	            // Find all Transform objects that might be buffer-related by name patterns
	            var bufferRelatedObjects = walkable.GetComponentsInChildren<Transform>(true)
		            .Where(t => IsBufferRelatedName(t.name));

	            var relatedObjects = bufferRelatedObjects as Transform[] ?? bufferRelatedObjects.ToArray();

	            foreach (var bufferObj in relatedObjects)
	            {
		            // Skip if not at least two buffers were found
		            if (relatedObjects.Length <= 1) continue;

		            // Handle buffer objects that have any type of colliders (MeshCollider, CapsuleCollider, BoxCollider, etc.)
		            var bufferColliders = bufferObj.GetComponentsInChildren<Collider>(true);

		            foreach (var collider in bufferColliders)
		            {
			            collider.enabled = visible;
			            Main.DebugLog(() =>
				            $"Toggled collider '{collider.name}' (type: {collider.GetType().Name}) to {visible} for {liveryId} by name pattern");
		            }

		            // Handle buffer GameObjects themselves
		            bufferObj.gameObject.SetActive(visible);
		            foundColliders = true;
	            }
            }

            if (!foundOldGroup && !foundColliders)
            {
                Main.DebugLog(() => $"No [old] group or relevant colliders found under interior/[walkable]/ for {liveryId}");
            }
        }
        catch (Exception ex)
        {
            Main.ErrorLog(() => $"Error in ProcessInteriorObject for {liveryId}: {ex.Message}");
        }
    }

    private static bool isCCL(TrainCar car)
    {
	    if (Main.IsCCLLoaded)
	    {
		    return car.carLivery is CCL.Importer.Types.CCL_CarVariant;
	    }
	    return false;
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

        return Regex.IsMatch(nameLower, @"capsule \([4-7]\)") || // Match "capsule (4-7)"
               Regex.IsMatch(nameLower, @"\bbuffers?\b|^buffer|buffer$",
	               RegexOptions.IgnoreCase) || // Match "buffers", "buffer"
               (nameLower.Contains("hood") && nameLower.Contains("buffer")) || // BE2: "hood F/Buffer", "hood R/Buffer"
               nameLower == "buffers" || // DM1U case
               (nameLower.Contains("exterior") && nameLower.Contains("buffer")); // S060/S282A case
    }

    /// <summary>
    /// Identifies colliders that form a rectangular pattern based on local position.
    /// The 4 buffer colliders have: same Y height, two X values (left/right), two Z values (front/back).
    /// Example: (0.867, 1.050, 6.936), (-0.862, 1.050, 6.936), (0.867, 1.050, -6.936), (-0.862, 1.050, -6.936)
    /// X values: must have exactly 2 pairs of identical values (e.g., 0.867, 0.867, -0.862, -0.862)
    /// Z values: must have exactly 2 pairs of identical values (e.g., 9.056, 9.056, -9.029, -9.029)
    /// </summary>
    private static List<Collider> IdentifyRectangularColliderPattern(List<Collider> colliders)
    {
        if (colliders.Count == 0)
            return [];

        const float heightTolerance = 0f; // Enforce same height
        const float pairTolerance = 0.1f; // floating point precision error

        // Group colliders by their Y position (height)
        var heightGroups = new Dictionary<float, List<Collider>>();

        foreach (var collider in colliders)
        {
            if (collider == null) continue;

            float y = collider.transform.localPosition.y;
            // Limit check to standard height. Custom heights need to be named correctly
            var yLimit = collider.transform.localPosition.y is >= 0.995f and <= 1.1f;

            // Find existing group with similar Y or create new one
            bool addedToGroup = false;
            foreach (var kvp in heightGroups.Where(kvp => Mathf.Abs(kvp.Key - y) <= heightTolerance))
            {
	            if (!yLimit) continue;
	            kvp.Value.Add(collider);
	            addedToGroup = true;
	            break;
            }

            if (!addedToGroup)
            {
                heightGroups[y] = [collider];
            }
        }

        // Look for groups with exactly 4 colliders at the same height
        foreach (var heightGroup in heightGroups.Values)
        {
            if (heightGroup.Count != 4) continue;

            // Extract X and Z positions
            var xPositions = heightGroup.Select(c => c.transform.localPosition.x).OrderBy(x => x).ToList();
            var zPositions = heightGroup.Select(c => c.transform.localPosition.z).OrderBy(z => z).ToList();

            // Check X values: should be [x1, x1, x2, x2] when sorted
            bool xHasTwoPairs = Mathf.Abs(xPositions[0] - xPositions[1]) <= pairTolerance &&
                                Mathf.Abs(xPositions[2] - xPositions[3]) <= pairTolerance &&
                                Mathf.Abs(xPositions[0] - xPositions[2]) > pairTolerance;

            // Check Z values: should be [z1, z1, z2, z2] when sorted
            bool zHasTwoPairs = Mathf.Abs(zPositions[0] - zPositions[1]) <= pairTolerance &&
                                Mathf.Abs(zPositions[2] - zPositions[3]) <= pairTolerance &&
                                Mathf.Abs(zPositions[0] - zPositions[2]) > pairTolerance;

            // Valid rectangular pattern: X has two pairs and Z has two pairs
            if (xHasTwoPairs && zHasTwoPairs)
            {
                Main.DebugLog(() => $"Found rectangular pattern: Y={heightGroup[0].transform.localPosition.y:F3}, " +
                                   $"X pairs=({xPositions[0]:F3}, {xPositions[1]:F3}) & ({xPositions[2]:F3}, {xPositions[3]:F3}), " +
                                   $"Z pairs=({zPositions[0]:F3}, {zPositions[1]:F3}) & ({zPositions[2]:F3}, {zPositions[3]:F3})");
                return heightGroup;
            }
        }

        return [];
    }
}
