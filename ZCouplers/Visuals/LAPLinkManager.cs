using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DvMod.ZCouplers
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
            if (coupler1 == null || coupler2 == null || !coupler1.IsCoupled() || !coupler2.IsCoupled())
                return;

            if (Main.settings.couplerType != CouplerType.LAPCoupler)
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
            var linkPrefab = AssetManager.GetLAPLinkPrefab();
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
            UpdateLinkTransform(linkObject, coupler1, coupler2);

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
            foreach (var linkObject in couplerLinks.Values)
            {
                if (linkObject != null)
                    Object.Destroy(linkObject);
            }
            couplerLinks.Clear();
            linkToCouplers.Clear();
        }

        /// <summary>
        /// Updates link visibility for all LAP couplers based on their current states.
        /// Call this when coupler states change.
        /// </summary>
        public static void UpdateAllLinkVisibility()
        {
            if (Main.settings.couplerType != CouplerType.LAPCoupler)
                return;

            if (CarSpawner.Instance?.allCars == null)
                return;

            // Check all cars for LAP couplers
            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car?.frontCoupler != null && car.frontCoupler.IsCoupled())
                {
                    var otherCoupler = car.frontCoupler.coupledTo;
                    if (otherCoupler != null)
                    {
                        CreateOrShowLink(car.frontCoupler, otherCoupler);
                    }
                }

                if (car?.rearCoupler != null && car.rearCoupler.IsCoupled())
                {
                    var otherCoupler = car.rearCoupler.coupledTo;
                    if (otherCoupler != null)
                    {
                        CreateOrShowLink(car.rearCoupler, otherCoupler);
                    }
                }
            }
            
            // Update all link transforms after ensuring they exist
            UpdateAllLinkTransforms();
        }

        /// <summary>
        /// Updates the position and rotation of a link to connect two couplers.
        /// The link is anchored to coupler1's locking pin and rotated to point toward coupler2's pin.
        /// </summary>
        private static void UpdateLinkTransform(GameObject linkObject, Coupler coupler1, Coupler coupler2)
        {
            if (linkObject == null || coupler1 == null || coupler2 == null)
                return;

            // Anchor the link to coupler1's locking pin position
            var pin1Position = coupler1.transform.position;
            var pin2Position = coupler2.transform.position;
            
            // Get the hook offset from the current coupler profile
            var hookOffset = CouplerProfiles.Current?.Options?.HookAdditionalOffset ?? Vector3.zero;
            
            // Combine hook offset with custom link offset
            var totalOffset = hookOffset + customLinkOffset;
            
            // Apply offset to the link position relative to coupler1's orientation
            var offsetPosition = pin1Position + coupler1.transform.TransformDirection(totalOffset);
            
            // Position the link at the offset anchor point
            linkObject.transform.position = offsetPosition;

            // Start with coupler1's orientation as the base
            var baseRotation = coupler1.transform.rotation;
            var prefabRotationCorrection = Quaternion.Euler(90f, 0f, 0f);

            // Calculate direction from coupler1's pin to coupler2's pin
            // Thanks ierdna100 for the improved direction calculation
            var directionToOtherPin = (pin1Position + ((-pin2Position - pin1Position) / 2)) * -1;

            // Check if couplers are too close to avoid erratic rotation
            if (directionToOtherPin.sqrMagnitude < 1e-8f)
            {
                // When too close, just use coupler1's orientation with prefab correction
                linkObject.transform.rotation = Quaternion.Slerp(
                    linkObject.transform.rotation,
                    baseRotation * prefabRotationCorrection,
                    Time.deltaTime * 5f
                );
                return;
            }

            // Calculate a small adjustment based on the direction to the other pin
            var localDirection = coupler1.transform.InverseTransformDirection(directionToOtherPin.normalized);

            // Only apply small rotational adjustments (max ±10 degrees per axis)
            var adjustmentX = Mathf.Clamp(-localDirection.y * 5f, -5f, 5f); // Pitch: up/down movement
            var adjustmentY = Mathf.Clamp(localDirection.x * 15f, -30f, 30f);  // Yaw: left/right movement
            var adjustmentZ = 0f; // No roll adjustment for LAP links

            // Apply the small adjustment to the base rotation, then apply prefab correction
            var rotationAdjustment = Quaternion.Euler(adjustmentX, adjustmentY, adjustmentZ);
            var targetRotation = baseRotation * rotationAdjustment * prefabRotationCorrection;

            // Smooth the rotation to prevent jumping
            linkObject.transform.rotation = Quaternion.Slerp(
                linkObject.transform.rotation,
                targetRotation,
                Time.deltaTime * 15f
            );
        }

        /// <summary>
        /// Updates all existing LAP links to maintain proper positioning and rotation.
        /// Should be called regularly to handle train movement and curves.
        /// </summary>
        public static void UpdateAllLinkTransforms()
        {
            if (Main.settings.couplerType != CouplerType.LAPCoupler)
                return;

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
                        UpdateLinkTransform(linkObject, coupler1, coupler2);
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