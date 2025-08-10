using HarmonyLib;
using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles collision physics and buffer responses between train cars
    /// </summary>
    public static class CollisionHandler
    {
        /// <summary>
        /// Destroy all joints between two specific cars while preserving buffer joints
        /// </summary>
        public static void DestroyJointsBetweenCars(TrainCar car1, TrainCar car2)
        {
            if (car1?.gameObject == null || car2?.gameObject == null)
                return;
                
            try
            {
                Main.DebugLog(() => $"CLEANUP: Destroying coupling joints between {car1.ID} and {car2.ID}");
                
                // Check all joints on car1 that connect to car2
                var jointsOnCar1 = car1.GetComponents<Joint>();
                foreach (var joint in jointsOnCar1)
                {
                    if (joint?.connectedBody != null && joint.connectedBody.gameObject == car2.gameObject)
                    {
                        Main.DebugLog(() => $"CLEANUP: Destroying coupling joint {joint.GetType().Name} on {car1.ID} connecting to {car2.ID}");
                        UnityEngine.Object.DestroyImmediate(joint);
                    }
                }
                
                // Check all joints on car2 that connect to car1
                var jointsOnCar2 = car2.GetComponents<Joint>();
                foreach (var joint in jointsOnCar2)
                {
                    if (joint?.connectedBody != null && joint.connectedBody.gameObject == car1.gameObject)
                    {
                        Main.DebugLog(() => $"CLEANUP: Destroying coupling joint {joint.GetType().Name} on {car2.ID} connecting to {car1.ID}");
                        UnityEngine.Object.DestroyImmediate(joint);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error destroying joints between {car1.ID} and {car2.ID}: {ex.Message}");
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
                
                Main.DebugLog(() => $"JOINT DEBUG {context} - {car.ID}: ConfigurableJoints={allJoints.Length}, FixedJoints={fixedJoints.Length}, SpringJoints={springJoints.Length}, HingeJoints={hingeJoints.Length}");
                
                foreach (var joint in allJoints)
                {
                    if (joint?.connectedBody != null)
                    {
                        var connectedCar = TrainCar.Resolve(joint.connectedBody.gameObject);
                        Main.DebugLog(() => $"  ConfigurableJoint: {car.ID} -> {connectedCar?.ID ?? "unknown"}");
                    }
                }
                
                foreach (var joint in fixedJoints)
                {
                    if (joint?.connectedBody != null)
                    {
                        var connectedCar = TrainCar.Resolve(joint.connectedBody.gameObject);
                        Main.DebugLog(() => $"  FixedJoint: {car.ID} -> {connectedCar?.ID ?? "unknown"}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error logging joints for {car.ID}: {ex.Message}");
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
                    Main.DebugLog(() => $"Error in collision patch: {ex.Message}");
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
                
                Main.DebugLog(() => $"Enhanced collision response between {thisCar.ID} and {otherCar.ID}: added force={additionalForce:F1}, velocity={velocityMagnitude:F2}");
            }
        }
    }
}
