using HarmonyLib;

using UnityEngine;
using System.Collections.Generic;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles collision physics and buffer responses between train cars
    /// </summary>
    public static class CollisionHandler
    {
        // Track fake buffer colliders for each coupler
        private static readonly Dictionary<Coupler, BoxCollider> fakeBufferColliders = new Dictionary<Coupler, BoxCollider>();

        /// <summary>
        /// Create or get fake buffer collider for a coupler (mimics game's approach)
        /// </summary>
        public static BoxCollider? GetOrCreateFakeBufferCollider(Coupler coupler)
        {
            if (coupler == null)
                return null;

            if (fakeBufferColliders.TryGetValue(coupler, out var existingCollider) && existingCollider != null)
                return existingCollider;

            try
            {
                // Create fake buffer collider like the game does
                var fakeCollider = coupler.gameObject.AddComponent<BoxCollider>();
                
                // Use same dimensions as game: 2.178126f x 0.45f x 1f
                fakeCollider.size = new Vector3(2.178126f, 0.45f, 1f);
                fakeCollider.center = new Vector3(0f, 0f, -0.6f);
                
                // Set to Train_Big_Collider layer like the game
                fakeCollider.gameObject.layer = LayerMask.NameToLayer("Train_Big_Collider");
                
                // Start disabled - will be enabled when needed
                fakeCollider.enabled = false;
                
                fakeBufferColliders[coupler] = fakeCollider;
                
                Main.DebugLog(() => $"Created fake buffer collider for {coupler.train.ID} {coupler.Position()}");
                return fakeCollider;
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error creating fake buffer collider for {coupler.train.ID}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Enable fake buffer colliders for uncoupled couplers (like game's loose mode)
        /// </summary>
        public static void EnableFakeBufferColliders(Coupler coupler1, Coupler coupler2)
        {
            if (coupler1 != null)
            {
                var collider1 = GetOrCreateFakeBufferCollider(coupler1);
                if (collider1 != null)
                {
                    collider1.enabled = true;
                    Main.DebugLog(() => $"Enabled fake buffer collider for {coupler1.train.ID}");
                }
            }

            if (coupler2 != null)
            {
                var collider2 = GetOrCreateFakeBufferCollider(coupler2);
                if (collider2 != null)
                {
                    collider2.enabled = true;
                    Main.DebugLog(() => $"Enabled fake buffer collider for {coupler2.train.ID}");
                }
            }
        }

        /// <summary>
        /// Disable fake buffer colliders for coupled couplers (like game's tight mode)
        /// </summary>
        public static void DisableFakeBufferColliders(Coupler coupler1, Coupler coupler2)
        {
            if (coupler1 != null && fakeBufferColliders.TryGetValue(coupler1, out var collider1) && collider1 != null)
            {
                collider1.enabled = false;
                Main.DebugLog(() => $"Disabled fake buffer collider for {coupler1.train.ID}");
            }

            if (coupler2 != null && fakeBufferColliders.TryGetValue(coupler2, out var collider2) && collider2 != null)
            {
                collider2.enabled = false;
                Main.DebugLog(() => $"Disabled fake buffer collider for {coupler2.train.ID}");
            }
        }

        /// <summary>
        /// Clean up fake buffer colliders (called during mod unload)
        /// </summary>
        public static void CleanupFakeBufferColliders()
        {
            foreach (var collider in fakeBufferColliders.Values)
            {
                if (collider != null)
                    UnityEngine.Object.Destroy(collider);
            }
            fakeBufferColliders.Clear();
        }

        /// <summary>
        /// Destroy all joints between two specific cars while preserving buffer joints
        /// </summary>
        public static void DestroyJointsBetweenCars(TrainCar car1, TrainCar car2)
        {
            if (car1?.gameObject == null || car2?.gameObject == null)
                return;

            try
            {
                Main.DebugLog(() => $"Destroying coupling joints between {car1.ID} and {car2.ID}");

                // Check all joints on car1 that connect to car2
                var jointsOnCar1 = car1.GetComponents<Joint>();
                foreach (var joint in jointsOnCar1)
                {
                    if (joint?.connectedBody != null && joint.connectedBody.gameObject == car2.gameObject)
                    {
                        Main.DebugLog(() => $"Destroying {joint.GetType().Name} on {car1.ID} connecting to {car2.ID}");
                        UnityEngine.Object.DestroyImmediate(joint);
                    }
                }

                // Check all joints on car2 that connect to car1
                var jointsOnCar2 = car2.GetComponents<Joint>();
                foreach (var joint in jointsOnCar2)
                {
                    if (joint?.connectedBody != null && joint.connectedBody.gameObject == car1.gameObject)
                    {
                        Main.DebugLog(() => $"Destroying {joint.GetType().Name} on {car2.ID} connecting to {car1.ID}");
                        UnityEngine.Object.DestroyImmediate(joint);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error destroying joints between {car1.ID} and {car2.ID}: {ex.Message}");
            }
        }

        /// <summary>
        /// Log remaining joints on a car for debugging
        /// </summary>
        public static void LogRemainingJoints(TrainCar car, string context)
        {
            if (car?.gameObject == null)
                return;

            try
            {
                var allJoints = car.GetComponents<ConfigurableJoint>();
                var fixedJoints = car.GetComponents<FixedJoint>();
                var springJoints = car.GetComponents<SpringJoint>();
                var hingeJoints = car.GetComponents<HingeJoint>();

                Main.DebugLog(() => $"Joint summary {context} - {car.ID}: Configurable={allJoints.Length}, Fixed={fixedJoints.Length}, Spring={springJoints.Length}, Hinge={hingeJoints.Length}");

                foreach (var joint in allJoints)
                {
                    if (joint?.connectedBody != null)
                    {
                        var connectedCar = TrainCar.Resolve(joint.connectedBody.gameObject);
                        // Detailed joint listing intentionally omitted to reduce log volume
                    }
                }

                foreach (var joint in fixedJoints)
                {
                    if (joint?.connectedBody != null)
                    {
                        var connectedCar = TrainCar.Resolve(joint.connectedBody.gameObject);
                        // Detailed joint listing intentionally omitted to reduce log volume
                    }
                }
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error logging joints for {car.ID}: {ex.Message}");
            }
        }

        /// <summary>
        /// Enhanced collision response for train car collisions
        /// </summary>
        [HarmonyPatch(typeof(TrainCarCollisions), nameof(TrainCarCollisions.OnCollisionEnter))]
        public static class TrainCarCollisionsPatch
        {
            public static void Postfix(TrainCarCollisions __instance, Collision collision)
            {
                try
                {
                    // Check if this is a collision between two train cars
                    var thisCar = TrainCar.Resolve(__instance.gameObject);
                    var otherCar = TrainCar.Resolve(collision.gameObject);

                    if (thisCar == null || otherCar == null || thisCar == otherCar)
                        return;

                    // Only apply buffer forces if cars are not coupled (to avoid interfering with coupling physics)
                    bool areCoupled = (thisCar.frontCoupler?.IsCoupled() == true && thisCar.frontCoupler.coupledTo?.train == otherCar) ||
                                    (thisCar.rearCoupler?.IsCoupled() == true && thisCar.rearCoupler.coupledTo?.train == otherCar);

                    if (areCoupled)
                        return;

                    ApplySimpleBufferResponse(__instance, thisCar, otherCar, collision);
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Error in collision patch: {ex.Message}");
                }
            }

            private static void ApplySimpleBufferResponse(TrainCarCollisions collisionComponent, TrainCar thisCar, TrainCar otherCar, Collision collision)
            {
                var thisRigidbody = thisCar.GetComponent<Rigidbody>();
                var otherRigidbody = otherCar.GetComponent<Rigidbody>();

                if (thisRigidbody == null || otherRigidbody == null)
                    return;

                // Get collision info
                var contact = collision.contacts[0];
                var collisionNormal = contact.normal;
                var relativeVelocity = collision.relativeVelocity;
                var velocityMagnitude = relativeVelocity.magnitude;

                // Simple approach: just add a small additional repelling force proportional to collision severity
                // This enhances Unity's natural collision response without overriding it
                // Use damper rate since we're applying force proportional to velocity (F = c * v)
                float additionalForce = velocityMagnitude * Main.settings.GetDamperRate() * 0.001f; // Small multiplier for damping effect

                // Apply the additional force at the collision point
                Vector3 forceVector = collisionNormal * additionalForce;
                thisRigidbody.AddForceAtPosition(forceVector, contact.point);

                Main.DebugLog(() => $"Enhanced collision response: {thisCar.ID} <-> {otherCar.ID}, force={additionalForce:F1}, v={velocityMagnitude:F2}");
            }
        }

        /// <summary>
        /// Clean up all collision handler resources
        /// </summary>
        public static void Cleanup()
        {
            CleanupFakeBufferColliders();
        }
    }
}