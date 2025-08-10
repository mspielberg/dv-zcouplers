using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Manages creation, destruction, and tracking of physics joints between train cars
    /// </summary>
    public static class JointManager
    {
        // Custom tension joint management
        private static readonly Dictionary<Coupler, ConfigurableJoint> customTensionJoints = new Dictionary<Coupler, ConfigurableJoint>();
        
        // Track when joints were last created to prevent rapid recreation
        private static readonly Dictionary<Coupler, float> lastJointCreationTime = new Dictionary<Coupler, float>();
        private const float MinJointCreationInterval = 2.0f; // Seconds between joint creation attempts
        
        // Buffer joint tracking
        internal static readonly Dictionary<Coupler, (Coupler otherCoupler, ConfigurableJoint joint)> bufferJoints =
            new Dictionary<Coupler, (Coupler otherCoupler, ConfigurableJoint joint)>();
        
        private const float LooseChainLength = 1.1f;
        private const float TightChainLength = 1.0f;
        private const float BufferTravel = 0.25f;

        /// <summary>
        /// Get tension joint for a coupler (used by CouplerBreaker)
        /// </summary>
        public static ConfigurableJoint? GetTensionJoint(Coupler coupler)
        {
            return coupler != null && customTensionJoints.TryGetValue(coupler, out var joint) ? joint : null;
        }

        /// <summary>
        /// Check if tension joint exists for a coupler
        /// </summary>
        public static bool HasTensionJoint(Coupler coupler)
        {
            return coupler != null && customTensionJoints.ContainsKey(coupler);
        }

        /// <summary>
        /// Force create tension joint (used by SaveManager)
        /// </summary>
        public static void ForceCreateTensionJoint(Coupler coupler)
        {
            if (coupler == null || !coupler.IsCoupled() || coupler.coupledTo == null)
                return;
                
            if (customTensionJoints.ContainsKey(coupler))
                return; // Already exists
                
            CreateTensionJoint(coupler);
            
            // Also create compression joint if needed
            if (coupler.rigidCJ == null && coupler.coupledTo.rigidCJ == null)
                CreateCompressionJoint(coupler, coupler.coupledTo);
        }

        /// <summary>
        /// Create tension joint between two coupled cars
        /// </summary>
        public static void CreateTensionJoint(Coupler coupler)
        {
            var coupledTo = coupler.coupledTo;
            
            // Calculate actual distance between couplers for debugging
            var actualDistance = Vector3.Distance(coupler.transform.position, coupledTo.transform.position);
            var desiredDistance = TightChainLength;
            
            // Use desired distance for anchor offset - this is what sets the target separation
            var anchorOffset = Vector3.forward * desiredDistance * (coupler.isFrontCoupler ? -1f : 1f);

            var cj = coupler.train.gameObject.AddComponent<ConfigurableJoint>();
            cj.autoConfigureConnectedAnchor = false;
            cj.anchor = coupler.transform.localPosition + anchorOffset;
            cj.connectedBody = coupler.coupledTo.train.gameObject.GetComponent<Rigidbody>();
            cj.connectedAnchor = coupler.coupledTo.transform.localPosition;

            // Configure joint motion constraints
            cj.xMotion = ConfigurableJointMotion.Limited;
            cj.yMotion = ConfigurableJointMotion.Limited;
            cj.zMotion = ConfigurableJointMotion.Limited;
            cj.angularXMotion = ConfigurableJointMotion.Limited;
            cj.angularYMotion = ConfigurableJointMotion.Limited;
            cj.angularZMotion = ConfigurableJointMotion.Limited;

            // Set angular limits
            cj.lowAngularXLimit = new SoftJointLimit { limit = 5f };
            cj.highAngularXLimit = new SoftJointLimit { limit = 5f };
            cj.angularYLimit = new SoftJointLimit { limit = 30f };
            cj.angularZLimit = new SoftJointLimit { limit = 5 };

            // Configure spring forces
            cj.angularXLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };
            cj.angularYZLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };

            cj.linearLimit = new SoftJointLimit { limit = desiredDistance };
            cj.linearLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };
            cj.enableCollision = false;
            cj.breakForce = float.PositiveInfinity;
            cj.breakTorque = float.PositiveInfinity;

            // Store tension joint
            customTensionJoints[coupler] = cj;
            
            // Set the joint to the desired tight length immediately
            cj.linearLimit = new SoftJointLimit { limit = TightChainLength };
        }

        /// <summary>
        /// Create compression joint between two couplers
        /// </summary>
        public static void CreateCompressionJoint(Coupler a, Coupler b)
        {
            if (a?.coupledTo != b || b?.coupledTo != a)
            {
                Main.DebugLog(() => $"Skipping compression joint creation - couplers not properly coupled: {a?.train?.ID} to {b?.train?.ID}");
                return;
            }
            // Only log if debug logging is enabled
            Main.DebugLog(() => $"Creating compression joint between {TrainCar.Resolve(a.gameObject)?.ID} and {TrainCar.Resolve(b.gameObject)?.ID}");

            // Create rigid (bottoming out) joint
            var bottomedCj = a.train.gameObject.AddComponent<ConfigurableJoint>();
            bottomedCj.autoConfigureConnectedAnchor = false;
            bottomedCj.anchor = a.transform.localPosition + (2 * (a.isFrontCoupler ? Vector3.forward : Vector3.back));
            bottomedCj.connectedBody = b.train.gameObject.GetComponent<Rigidbody>();
            bottomedCj.connectedAnchor = b.transform.localPosition;
            bottomedCj.zMotion = ConfigurableJointMotion.Limited;

            bottomedCj.linearLimit = new SoftJointLimit { limit = BufferTravel + 2f };
            bottomedCj.linearLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };
            bottomedCj.enableCollision = false;
            bottomedCj.breakForce = float.PositiveInfinity;
            bottomedCj.breakTorque = float.PositiveInfinity;

            a.rigidCJ = bottomedCj;

            // Create buffer joint
            var bufferCj = a.train.gameObject.AddComponent<ConfigurableJoint>();
            bufferCj.autoConfigureConnectedAnchor = false;
            bufferCj.anchor = a.transform.localPosition + (2 * (a.isFrontCoupler ? Vector3.forward : Vector3.back));
            bufferCj.connectedBody = b.train.gameObject.GetComponent<Rigidbody>();
            bufferCj.connectedAnchor = b.transform.localPosition;
            bufferCj.zMotion = ConfigurableJointMotion.Limited;

            bufferCj.linearLimit = new SoftJointLimit { limit = 2f };
            bufferCj.linearLimitSpring = new SoftJointLimitSpring
            {
                spring = Main.settings.GetSpringRate(),
                damper = Main.settings.GetDamperRate(),
            };
            bufferCj.enableCollision = false;
            bufferCj.breakForce = float.PositiveInfinity;
            bufferCj.breakTorque = float.PositiveInfinity;

            bufferJoints.Add(a, (b, bufferCj));
            bufferJoints.Add(b, (a, bufferCj));
            
            // If both couplers are ready (locked) but showing as Dangling, update them to Attached_Tight
            // This handles the case where compression joints are created after deferred state application
            UpdateCouplerStatesAfterCompressionJoint(a, b);
        }
        
        /// <summary>
        /// Update coupler states to Attached_Tight when compression joints are created for ready couplers
        /// </summary>
        private static void UpdateCouplerStatesAfterCompressionJoint(Coupler a, Coupler b)
        {
            // Only update if both couplers are ready (locked) and in Dangling state
            if (KnuckleCouplers.IsReadyToCouple(a) && KnuckleCouplers.IsReadyToCouple(b))
            {
                bool aWasDangling = a.state == ChainCouplerInteraction.State.Dangling;
                bool bWasDangling = b.state == ChainCouplerInteraction.State.Dangling;
                
                if (aWasDangling || bWasDangling)
                {
                    Main.DebugLog(() => $"Updating coupler states after compression joint creation: {a.train.ID} {a.Position()} (was {a.state}) and {b.train.ID} {b.Position()} (was {b.state})");
                    
                    // Update both couplers to Attached_Tight since they're both ready and have compression joints
                    // The actual coupling and tension joint creation will be handled by MasterCoro
                    if (aWasDangling)
                    {
                        a.state = ChainCouplerInteraction.State.Attached_Tight;
                        // Removed verbose state update log
                    }
                    
                    if (bWasDangling)
                    {
                        b.state = ChainCouplerInteraction.State.Attached_Tight;
                        // Removed verbose state update log
                    }
                }
            }
        }

        /// <summary>
        /// Destroy tension joint for a coupler
        /// </summary>
        public static void DestroyTensionJoint(Coupler coupler)
        {
            if (coupler == null)
                return;
                
            try
            {
                // Try to find tension joint on this coupler first
                if (customTensionJoints.TryGetValue(coupler, out var tensionJoint))
                {
                    if (tensionJoint != null)
                    {
                        // Removed verbose tension joint destruction logs
                        UnityEngine.Object.Destroy(tensionJoint);
                    }
                    customTensionJoints.Remove(coupler);
                    lastJointCreationTime.Remove(coupler);
                    // Removed verbose joint destruction log
                    return;
                }
                
                // If not found on this coupler, try to find it on the partner coupler
                if (coupler.coupledTo != null && customTensionJoints.TryGetValue(coupler.coupledTo, out tensionJoint))
                {
                    if (tensionJoint != null)
                    {
                        // Removed verbose partner joint destruction log
                        UnityEngine.Object.Destroy(tensionJoint);
                    }
                    customTensionJoints.Remove(coupler.coupledTo);
                    lastJointCreationTime.Remove(coupler.coupledTo);
                    // Removed verbose partner destruction log
                    return;
                }
                
                // Keep important warning about missing joints for debugging
                Main.DebugLog(() => $"TENSION JOINT: No tension joint found to destroy for {coupler.train.ID} {coupler.Position()} or its partner");
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error destroying tension joint: {ex.Message}");
                // Clean up dictionaries to prevent memory leaks
                customTensionJoints.Remove(coupler);
                lastJointCreationTime.Remove(coupler);
                if (coupler.coupledTo != null)
                {
                    customTensionJoints.Remove(coupler.coupledTo);
                    lastJointCreationTime.Remove(coupler.coupledTo);
                }
            }
        }

        /// <summary>
        /// Destroy compression joint for a coupler
        /// </summary>
        public static void DestroyCompressionJoint(Coupler coupler, string caller = "unknown")
        {
            if (coupler == null || !bufferJoints.TryGetValue(coupler, out var result))
                return;

            try
            {
                // Only log destruction when debug logging is enabled
                Main.DebugLog(() => $"Destroying compression joint between {TrainCar.Resolve(coupler.gameObject)?.ID} and {TrainCar.Resolve(result.otherCoupler.gameObject)?.ID} - called from: {caller}");
                
                // Destroy the joint
                if (result.joint != null)
                    Component.Destroy(result.joint);

                foreach (var c in new Coupler[] { coupler, result.otherCoupler })
                {
                    if (c != null)
                    {
                        try
                        {
                            if (c.jointCoroRigid != null)
                            {
                                c.StopCoroutine(c.jointCoroRigid);
                                c.jointCoroRigid = null;
                            }
                            if (c.rigidCJ != null)
                            {
                                Component.Destroy(c.rigidCJ);
                                c.rigidCJ = null;
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Main.ErrorLog(() => $"Error cleaning up coupler {c?.train?.ID}: {ex.Message}");
                        }
                    }
                }

                bufferJoints.Remove(coupler);
                bufferJoints.Remove(result.otherCoupler);
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error destroying compression joint: {ex.Message}");
                // Clean up dictionaries to prevent memory leaks
                bufferJoints.Remove(coupler);
                if (result.otherCoupler != null)
                    bufferJoints.Remove(result.otherCoupler);
            }
        }

        /// <summary>
        /// Convert compression joint to use game's collision system instead
        /// </summary>
        public static void ConvertCompressionJointToBufferOnly(Coupler coupler)
        {
            if (coupler?.coupledTo == null)
                return;
                
            try
            {
                // Removed verbose collision system conversion logs
                
                // Destroy any existing compression joints - we'll use the game's collision system instead
                if (bufferJoints.TryGetValue(coupler, out var result))
                {
                    if (result.joint != null)
                    {
                        Component.Destroy(result.joint);
                        // Removed verbose collision system success log
                    }
                    
                    // Remove from tracking
                    bufferJoints.Remove(coupler);
                    bufferJoints.Remove(result.otherCoupler);
                }
                else
                {
                    // Removed verbose conversion failure log
                }
                
                // Clear the rigidCJ references so the game doesn't think cars are rigidly coupled
                if (coupler.rigidCJ != null)
                {
                    coupler.rigidCJ = null;
                    // Removed verbose reference cleanup logs
                }
                if (coupler.coupledTo.rigidCJ != null)
                {
                    coupler.coupledTo.rigidCJ = null;
                    // Removed verbose reference cleanup logs
                }
                
                // Clear coroutines
                if (coupler.jointCoroRigid != null)
                {
                    coupler.StopCoroutine(coupler.jointCoroRigid);
                    coupler.jointCoroRigid = null;
                }
                if (coupler.coupledTo.jointCoroRigid != null)
                {
                    coupler.coupledTo.StopCoroutine(coupler.coupledTo.jointCoroRigid);
                    coupler.coupledTo.jointCoroRigid = null;
                }
                
                // Removed verbose success log
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error converting to collision system: {ex.Message}");
            }
        }

        /// <summary>
        /// Tighten chain between coupled cars
        /// </summary>
        public static void TightenChain(Coupler coupler)
        {
            if (!customTensionJoints.TryGetValue(coupler, out var tensionJoint))
            {
                Main.DebugLog(() => $"TIGHTEN CHAIN: No tension joint found for {coupler.train.ID} {coupler.Position()}, checking partner");
                if (coupler.coupledTo != null && customTensionJoints.ContainsKey(coupler.coupledTo))
                    TightenChain(coupler.coupledTo);
                return;
            }
            
            var oldLimit = tensionJoint.linearLimit.limit;
            tensionJoint.linearLimit = new SoftJointLimit { limit = TightChainLength };
            Main.DebugLog(() => $"TIGHTEN CHAIN: Changed limit for {coupler.train.ID} {coupler.Position()} from {oldLimit} to {TightChainLength}");
        }

        /// <summary>
        /// Loosen chain between coupled cars
        /// </summary>
        public static void LoosenChain(Coupler coupler)
        {
            if (!customTensionJoints.TryGetValue(coupler, out var tensionJoint))
            {
                if (coupler.coupledTo != null && customTensionJoints.ContainsKey(coupler.coupledTo))
                    LoosenChain(coupler.coupledTo);
                return;
            }
            tensionJoint.linearLimit = new SoftJointLimit { limit = LooseChainLength };
        }

        /// <summary>
        /// Update all compression joints with current settings
        /// </summary>
        public static void UpdateAllCompressionJoints()
        {
            if (bufferJoints.Count == 0)
                return;

            var springRate = Main.settings.GetSpringRate();
            var damperRate = Main.settings.GetDamperRate();

            var firstJoint = bufferJoints.Values.FirstOrDefault().joint;
            if (firstJoint == null || (firstJoint.linearLimitSpring.spring == springRate && firstJoint.linearLimitSpring.damper == damperRate))
                return;

            foreach (var joint in bufferJoints.Values.Select(x => x.joint))
            {
                joint.linearLimitSpring = new SoftJointLimitSpring
                {
                    spring = springRate,
                    damper = damperRate,
                };
            }
        }

        /// <summary>
        /// Check if joint creation should be allowed based on timing
        /// </summary>
        public static bool CanCreateJoint(Coupler coupler)
        {
            var currentTime = Time.time;
            if (lastJointCreationTime.TryGetValue(coupler, out var lastTime) && (currentTime - lastTime) < MinJointCreationInterval)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Record that a joint was created for timing purposes
        /// </summary>
        public static void RecordJointCreation(Coupler coupler)
        {
            var currentTime = Time.time;
            lastJointCreationTime[coupler] = currentTime;
            if (coupler.coupledTo != null)
                lastJointCreationTime[coupler.coupledTo] = currentTime;
        }

        /// <summary>
        /// Log joint states for debugging
        /// </summary>
        public static void LogJointStates(string context)
        {
            Main.DebugLog(() => $"JOINT STATES ({context}): Tension joints: {customTensionJoints.Count}");
            foreach (var kvp in customTensionJoints.ToList())
            {
                var coupler = kvp.Key;
                var joint = kvp.Value;
                if (coupler?.train != null && joint != null)
                {
                    var distance = joint.connectedBody != null ? 
                        Vector3.Distance(coupler.transform.position, joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)) : 0f;
                    Main.DebugLog(() => $"  {coupler.train.ID} {coupler.Position()} -> {coupler.coupledTo?.train.ID}, distance: {distance:F2}m, joint valid: {joint != null}");
                }
            }
        }
    }
}
