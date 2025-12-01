using System.Collections.Generic;
using System.Diagnostics;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Profiles;
using UnityEngine;

namespace DvMod.ZCouplers.Visuals
{
    /// <summary>
    /// Manages Link and Pin coupler links - the connecting piece between two LAP couplers.
    /// LAP couplers themselves don't move, the link is what connects them.
    /// The link rotates dynamically to maintain visual connection in curves.
    /// </summary>
    public static class LAPLinkManager
    {
        // Track created links by coupler pair (order-independent)
        private static readonly Dictionary<CouplerPair, GameObject> couplerLinks = new Dictionary<CouplerPair, GameObject>();

        // Track coupler pairs for each link to enable updates
        private static readonly Dictionary<GameObject, CouplerPair> linkToCouplers = new Dictionary<GameObject, CouplerPair>();

        // Custom offset for link positioning (in addition to hook offset)
        private static readonly Vector3 customLinkOffset = new Vector3(0f, -0.1f, 0f);

        /// <summary>
        /// Creates or shows a LAP link between two coupled couplers that are both in Attached_Tight state.
        /// </summary>
        public static void CreateOrShowLink(Coupler coupler1, Coupler coupler2)
        {
            if (CouplerProfiles.Current?.ProfileId != "LAP")
                return;

            if (coupler1 == null || coupler2 == null || !coupler1.IsCoupled() || !coupler2.IsCoupled())
                return;

            // Only create link when both couplers are in Attached_Tight state
            if (coupler1.state != ChainCouplerInteraction.State.Attached_Tight ||
                coupler2.state != ChainCouplerInteraction.State.Attached_Tight)
            {
                // If not both Attached_Tight, ensure any existing link is hidden
                var tempPair = new CouplerPair(coupler1, coupler2);
                if (couplerLinks.TryGetValue(tempPair, out var tempLink) && tempLink != null)
                {
                    tempLink.SetActive(false);
                }
                return;
            }

            var pair = new CouplerPair(coupler1, coupler2);

            // If link already exists, just ensure it's visible
            if (couplerLinks.TryGetValue(pair, out var existingLink))
            {
                if (existingLink != null)
                {
                    existingLink.SetActive(true);
                    return;
                }
                else
                {
                    // Link was destroyed, remove from dictionary
                    couplerLinks.Remove(pair);
                }
            }

            // Create new link
            var linkPrefab = AssetManager.GetPrefabForProfile(CouplerProfiles.GetById("LAP"), "link");
            if (linkPrefab == null)
            {
                Main.ErrorLog(() => "LAP link prefab not available, cannot create link");
                return;
            }

            // Position the link between the two couplers
            var midpoint = (coupler1.transform.position + coupler2.transform.position) * 0.5f;
            var linkObject = Object.Instantiate(linkPrefab, midpoint, Quaternion.identity);

            // Parent to the first coupler's train interior for cleanup
            linkObject.transform.SetParent(coupler1.train.interior, true);

            // Orient the link to connect the couplers
            UpdateLinkTransformOptimized(linkObject, coupler1, coupler2);

            couplerLinks[pair] = linkObject;
            linkToCouplers[linkObject] = pair;
            Main.DebugLog(() => $"Created LAP link between {coupler1.train.ID} and {coupler2.train.ID}");
            Main.DebugLog(() => $"Created LAP link between {coupler1.train.ID} and {coupler2.train.ID}");
        }

        /// <summary>
        /// Hides or destroys the LAP link between two couplers when they uncouple.
        /// </summary>
        public static void HideOrDestroyLink(Coupler coupler1, Coupler coupler2)
        {
            if (coupler1 == null || coupler2 == null)
                return;

            var pair = new CouplerPair(coupler1, coupler2);

            if (couplerLinks.TryGetValue(pair, out var linkObject))
            {
                if (linkObject != null)
                {
                    linkToCouplers.Remove(linkObject);
                    Object.Destroy(linkObject);
                    Main.DebugLog(() => $"Destroyed LAP link between {coupler1.train.ID} and {coupler2.train.ID}");
                }
                couplerLinks.Remove(pair);
            }
        }

        /// <summary>
        /// Clean up all LAP links. Called during mod unload.
        /// </summary>
        public static void Cleanup()
        {
            if (CouplerProfiles.Current?.ProfileId != "LAP")
                return;

            foreach (var linkObject in couplerLinks.Values)
            {
                if (linkObject != null)
                    Object.Destroy(linkObject);
            }
            couplerLinks.Clear();
            linkToCouplers.Clear();
        }

        /// <summary>
        /// Optimized version of UpdateLinkTransform with reduced allocations and faster math operations.
        /// Uses direct vector/quaternion calculations instead of expensive Transform operations.
        /// </summary>
        private static void UpdateLinkTransformOptimized(GameObject linkObject, Coupler coupler1, Coupler coupler2)
        {
	        if (linkObject == null || coupler1 == null || coupler2 == null)
		        return;

	        var transform1 = coupler1.transform;
	        var transform2 = coupler2.transform;
	        var linkTransform = linkObject.transform;

	        // Cache all positions/rotations at once to minimize property access overhead
	        var pin1Position = transform1.position;
	        var pin2Position = transform2.position;
	        var coupler1Rotation = transform1.rotation;
	        var coupler2Forward = transform2.forward;

	        // Get offsets - these should be cached at class level if they don't change
	        var hookOffset = CouplerProfiles.Current?.Options?.HookAdditionalOffset ?? Vector3.zero;
	        var totalOffset = hookOffset + customLinkOffset;

	        // Apply offset using cached rotation (avoid TransformDirection call)
	        var offsetPosition = pin1Position + coupler1Rotation * totalOffset;

	        // Calculate direction from link position to target
	        var toTarget = pin2Position - offsetPosition;
	        var sqrDistance = toTarget.x * toTarget.x + toTarget.y * toTarget.y + toTarget.z * toTarget.z;

	        // Early return for too-close case
	        if (sqrDistance < 1e-8f)
	        {
		        var prefabCorrection = Quaternion.Euler(90f, 0f, 0f);
		        linkTransform.SetPositionAndRotation(
			        offsetPosition,
			        Quaternion.Slerp(linkTransform.rotation, coupler1Rotation * prefabCorrection, Time.deltaTime * 5f)
		        );
		        return;
	        }

	        // Extend target behind coupler2
	        var extendedTarget = pin2Position - coupler2Forward * 0.5f;
	        var lookDirection = extendedTarget - offsetPosition;

	        // Fast magnitude calculation
	        var lookSqrMag = lookDirection.x * lookDirection.x + lookDirection.y * lookDirection.y + lookDirection.z * lookDirection.z;
	        if (lookSqrMag < 1e-8f)
	        {
		        var prefabCorrection = Quaternion.Euler(90f, 0f, 0f);
		        linkTransform.SetPositionAndRotation(
			        offsetPosition,
			        Quaternion.Slerp(linkTransform.rotation, coupler1Rotation * prefabCorrection, Time.deltaTime * 5f)
		        );
		        return;
	        }

	        // Normalize manually to avoid Vector3.normalized allocation
	        var invMag = 1f / Mathf.Sqrt(lookSqrMag);
	        var normX = lookDirection.x * invMag;
	        var normY = lookDirection.y * invMag;
	        var normZ = lookDirection.z * invMag;

	        // Clamp vertical angle to ±5°
	        //var clampedY = Mathf.Clamp(normY, -0.0871557f, 0.0871557f); // sin(5°) ≈ 0.0871557

	        // Project onto horizontal plane using plane projection: projected = direction - (direction · normal) * normal
	        // For horizontal plane (XZ), normal = (0, 1, 0), so: projected = (x, 0, z)
	        // Then scale to maintain unit length with clamped Y component
	        var horizontalScale = Mathf.Sqrt(1f - normY * normY);
	        var projectedX = normX * horizontalScale;
	        var projectedZ = normZ * horizontalScale;

	        // Create rotation from projected direction vector
	        var targetRotation = Quaternion.LookRotation(new Vector3(projectedX, normY, projectedZ)) * Quaternion.Euler(90f, 0f, 0f);

	        // Use SetPositionAndRotation to batch the updates
	        linkTransform.SetPositionAndRotation(
		        offsetPosition,
		        Quaternion.Slerp(linkTransform.rotation, targetRotation, Time.deltaTime * 15f)
	        );
        }

        /// <summary>
        /// Updates all existing LAP links to maintain proper positioning and rotation.
        /// Should be called regularly to handle train movement and curves.
        /// Only updates links within 20 meters of the camera for performance.
        /// </summary>
        public static void UpdateAllLinkTransforms()
        {
            if (CouplerProfiles.Current != CouplerProfiles.GetById("LAP"))
                return;

            // Get camera position for distance culling
            // In VR mode, ActiveCamera might be null, so fall back to PlayerCamera
            var camera = PlayerManager.ActiveCamera;
            if (camera == null)
            {
                camera = PlayerManager.PlayerCamera;
                if (camera == null)
                    return;
            }

            var cameraPosition = camera.transform.position;
            const float maxDistanceSqr = 25f * 25f; // 500 - squared distance for performance

            // Update all existing links
            var linksToUpdate = new List<GameObject>(linkToCouplers.Keys);
            foreach (var linkObject in linksToUpdate)
            {
                if (linkObject == null)
                {
                    // Link was destroyed, clean up tracking
                    if (linkObject != null)
                        linkToCouplers.Remove(linkObject);
                    continue;
                }

                if (linkToCouplers.TryGetValue(linkObject, out var pair))
                {
                    var coupler1 = pair.GetCoupler1();
                    var coupler2 = pair.GetCoupler2();

                    // Verify couplers are still valid and coupled
                    if (coupler1 != null && coupler2 != null &&
                        coupler1.IsCoupled() && coupler2.IsCoupled() &&
                        coupler1.state == ChainCouplerInteraction.State.Attached_Tight &&
                        coupler2.state == ChainCouplerInteraction.State.Attached_Tight)
                    {
                        // Only update links within 50 meters of camera
                        var linkPosition = linkObject.transform.position;
                        var dx = linkPosition.x - cameraPosition.x;
                        var dy = linkPosition.y - cameraPosition.y;
                        var dz = linkPosition.z - cameraPosition.z;
                        var distanceSqr = dx * dx + dy * dy + dz * dz;

                        if (distanceSqr <= maxDistanceSqr)
                        {
	                        UpdateLinkTransformOptimized(linkObject, coupler1, coupler2);
                        }
                    }
                    else
                    {
                        // Couplers are no longer properly coupled, hide the link
                        linkObject.SetActive(false);
                    }
                }
            }
        }

        /// <summary>
        /// Represents a pair of couplers (order-independent) for tracking links.
        /// </summary>
        private struct CouplerPair
        {
            private readonly Coupler coupler1;
            private readonly Coupler coupler2;

            public CouplerPair(Coupler c1, Coupler c2)
            {
                // Ensure consistent ordering for hash/equals
                if (c1.GetInstanceID() < c2.GetInstanceID())
                {
                    coupler1 = c1;
                    coupler2 = c2;
                }
                else
                {
                    coupler1 = c2;
                    coupler2 = c1;
                }
            }

            public Coupler GetCoupler1() => coupler1;
            public Coupler GetCoupler2() => coupler2;

            public override bool Equals(object obj)
            {
                if (obj is CouplerPair other)
                {
                    return coupler1 == other.coupler1 && coupler2 == other.coupler2;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return coupler1.GetHashCode() ^ coupler2.GetHashCode();
            }
        }
    }
}
