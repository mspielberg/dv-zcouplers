using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Manages creation, destruction, and tracking of physics joints between train cars.
    /// </summary>
    public static class JointManager
    {
        // Custom tension joint management
        private static readonly Dictionary<Coupler, ConfigurableJoint> customTensionJoints = new Dictionary<Coupler, ConfigurableJoint>();

        // Track when joints were last created to prevent rapid recreation
        private static readonly Dictionary<Coupler, float> lastJointCreationTime = new Dictionary<Coupler, float>();
        private const float MinJointCreationInterval = 2.0f; // Minimum seconds between joint creation attempts

        // Buffer joint tracking
        internal static readonly Dictionary<Coupler, (Coupler otherCoupler, ConfigurableJoint joint)> bufferJoints =
            new Dictionary<Coupler, (Coupler otherCoupler, ConfigurableJoint joint)>();

        private const float LooseChainLength = 1.0f;
        private const float TightChainLength = 1.0f;
        private const float BufferTravel = 0.25f;

        /// <summary>
        /// Calculate the actual distance between joint anchors (matching the game's JointDistance).
        /// Uses the measured distance after coupling for better compatibility across car types and configurations.
        /// </summary>
        private static float CalculateJointDistance(ConfigurableJoint joint)
        {
            Vector3 anchorWorldPos = joint.transform.TransformPoint(joint.anchor);
            Vector3 connectedAnchorWorldPos = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
            return Vector3.Distance(anchorWorldPos, connectedAnchorWorldPos);
        }

        /// <summary>
        /// Get tension joint for a coupler (used by CouplerBreaker).
        /// </summary>
        public static ConfigurableJoint? GetTensionJoint(Coupler coupler)
        {
            return coupler != null && customTensionJoints.TryGetValue(coupler, out var joint) ? joint : null;
        }

        /// <summary>
        /// Check whether a tension joint exists for a coupler.
        /// </summary>
        public static bool HasTensionJoint(Coupler coupler)
        {
            return coupler != null && customTensionJoints.ContainsKey(coupler);
        }

        /// <summary>
        /// Force-create tension joint (used by SaveManager).
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
        /// Create a tension joint between two coupled cars.
        /// </summary>
        public static void CreateTensionJoint(Coupler coupler)
        {
            var coupledTo = coupler.coupledTo;

            // Calculate anchor positions to match the game's approach
            var anchorOffset = Vector3.forward * TightChainLength * (coupler.isFrontCoupler ? -1f : 1f);

            var cj = coupler.train.gameObject.AddComponent<ConfigurableJoint>();
            cj.autoConfigureConnectedAnchor = false;
            cj.anchor = coupler.transform.localPosition + anchorOffset;
            cj.connectedBody = coupler.coupledTo.train.gameObject.GetComponent<Rigidbody>();
            cj.connectedAnchor = coupler.coupledTo.transform.localPosition;

            // Calculate actual joint distance like the game does
            var actualJointDistance = CalculateJointDistance(cj);
            var jointLimit = Mathf.Max(actualJointDistance, LooseChainLength);

            // Configure joint motion constraints
            cj.xMotion = ConfigurableJointMotion.Limited;
            cj.yMotion = ConfigurableJointMotion.Limited;
            cj.zMotion = ConfigurableJointMotion.Limited;
            cj.angularXMotion = ConfigurableJointMotion.Limited;
            cj.angularYMotion = ConfigurableJointMotion.Limited;
            cj.angularZMotion = ConfigurableJointMotion.Limited;

            // Set angular limits (looser when buffers are hidden to increase play)
            if (Main.settings.showBuffersWithKnuckles)
            {
                cj.lowAngularXLimit = new SoftJointLimit { limit = 5f };
                cj.highAngularXLimit = new SoftJointLimit { limit = 5f };
                cj.angularYLimit = new SoftJointLimit { limit = 30f };
                cj.angularZLimit = new SoftJointLimit { limit = 5 };
            }
            else
            {
                cj.lowAngularXLimit = new SoftJointLimit { limit = 20f };
                cj.highAngularXLimit = new SoftJointLimit { limit = 20f };
                cj.angularYLimit = new SoftJointLimit { limit = 80f };
                cj.angularZLimit = new SoftJointLimit { limit = 45f };
            }

            // Configure spring forces
            cj.angularXLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };
            cj.angularYZLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };

            cj.linearLimit = new SoftJointLimit { limit = jointLimit };
            cj.linearLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };
            cj.enableCollision = false;
            cj.breakForce = float.PositiveInfinity;
            cj.breakTorque = float.PositiveInfinity;

            // Store tension joint
            customTensionJoints[coupler] = cj;

            Main.DebugLog(() => $"Tension joint created: distance={actualJointDistance:F3}m, limit={jointLimit:F3}m for {coupler.train.ID}");
        }

        /// <summary>
        /// Create a compression joint between two couplers.
        /// </summary>
        public static void CreateCompressionJoint(Coupler a, Coupler b)
        {
            if (a?.coupledTo != b || b?.coupledTo != a)
            {
                Main.DebugLog(() => $"Skip compression joint: not properly coupled {a?.train?.ID} -> {b?.train?.ID}");
                return;
            }
            Main.DebugLog(() => $"Compression joint created between {TrainCar.Resolve(a.gameObject)?.ID} and {TrainCar.Resolve(b.gameObject)?.ID}");

            bool showBuffers = Main.settings.showBuffersWithKnuckles;
            // Always create a lightweight "rigid" joint so DV's native coupling logic (and audio) can run,
            // even when buffers are hidden and we use a tight guard joint for physics.
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
            bufferCj.connectedBody = b.train.gameObject.GetComponent<Rigidbody>();

            if (showBuffers)
            {
                // Full buffer behavior uses the historical anchor offset
                bufferCj.anchor = a.transform.localPosition + (2 * (a.isFrontCoupler ? Vector3.forward : Vector3.back));
                bufferCj.connectedAnchor = b.transform.localPosition;
                bufferCj.zMotion = ConfigurableJointMotion.Limited;
                // Full buffer behavior
                bufferCj.linearLimit = new SoftJointLimit { limit = 2f };
                bufferCj.linearLimitSpring = new SoftJointLimitSpring
                {
                    spring = Main.settings.GetSpringRate(),
                    damper = Main.settings.GetDamperRate(),
                };
                // Keep other motions at defaults (free lateral), let tension joint manage angulars
                bufferCj.angularXMotion = ConfigurableJointMotion.Free;
                bufferCj.angularYMotion = ConfigurableJointMotion.Free;
                bufferCj.angularZMotion = ConfigurableJointMotion.Free;
            }
            else
            {
                // Guard mode when buffers are hidden: spherical, tight limit about the actual coupler faces
                // Use coupler local positions for anchors to avoid introducing a baseline offset/gap
                bufferCj.anchor = a.transform.localPosition;
                bufferCj.connectedAnchor = b.transform.localPosition;

                // Limit only along the coupler axis (z); allow lateral (x/y) to avoid clamping in curves
                bufferCj.xMotion = ConfigurableJointMotion.Free;
                bufferCj.yMotion = ConfigurableJointMotion.Free;
                bufferCj.zMotion = ConfigurableJointMotion.Limited;
                bufferCj.linearLimit = new SoftJointLimit { limit = 0.03f }; // ~3 cm axial guard

                // Use user-configured spring/damper values directly
                bufferCj.linearLimitSpring = new SoftJointLimitSpring
                {
                    spring = Main.settings.GetSpringRate(),
                    damper = Main.settings.GetDamperRate(),
                };

                // Allow free angles; tension joint governs overall articulation
                bufferCj.angularXMotion = ConfigurableJointMotion.Free;
                bufferCj.angularYMotion = ConfigurableJointMotion.Free;
                bufferCj.angularZMotion = ConfigurableJointMotion.Free;
            }

            bufferCj.enableCollision = false;
            bufferCj.breakForce = float.PositiveInfinity;
            bufferCj.breakTorque = float.PositiveInfinity;

            bufferJoints.Add(a, (b, bufferCj));
            bufferJoints.Add(b, (a, bufferCj));

            // If both couplers are ready (locked) but showing as Dangling, update them to Attached_Tight.
            // Handles compression joints created after deferred state application.
            UpdateCouplerStatesAfterCompressionJoint(a, b);
        }

        /// <summary>
        /// Check if a compression (buffer) joint exists for this coupler.
        /// </summary>
        public static bool HasCompressionJoint(Coupler coupler)
        {
            return coupler != null && bufferJoints.ContainsKey(coupler);
        }

        /// <summary>
        /// Try get the compression joint for this coupler.
        /// </summary>
        public static bool TryGetCompressionJoint(Coupler coupler, out ConfigurableJoint joint)
        {
            joint = default!;
            if (coupler != null && bufferJoints.TryGetValue(coupler, out var tuple) && tuple.joint != null)
            {
                joint = tuple.joint;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Update coupler states to Attached_Tight when compression joints are created for ready couplers.
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
                    Main.DebugLog(() => $"Set Attached_Tight after compression joint: {a.train.ID} {a.Position()} (was {a.state}), {b.train.ID} {b.Position()} (was {b.state})");

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
        /// Destroy the tension joint for a coupler.
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
                        // Destroy found joint
                        UnityEngine.Object.Destroy(tensionJoint);
                    }
                    customTensionJoints.Remove(coupler);
                    lastJointCreationTime.Remove(coupler);
                    // Cleaned up tracking entries
                    return;
                }

                // If not found on this coupler, try to find it on the partner coupler
                if (coupler.coupledTo != null && customTensionJoints.TryGetValue(coupler.coupledTo, out tensionJoint))
                {
                    if (tensionJoint != null)
                    {
                        // Destroy partner's joint
                        UnityEngine.Object.Destroy(tensionJoint);
                    }
                    customTensionJoints.Remove(coupler.coupledTo);
                    lastJointCreationTime.Remove(coupler.coupledTo);
                    // Cleaned up partner tracking entries
                    return;
                }

                Main.DebugLog(() => $"Tension joint not found to destroy for {coupler.train.ID} {coupler.Position()} or its partner");
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
        /// Destroy the compression joint for a coupler.
        /// </summary>
        public static void DestroyCompressionJoint(Coupler coupler, string caller = "unknown")
        {
            if (coupler == null || !bufferJoints.TryGetValue(coupler, out var result))
                return;

            try
            {
                Main.DebugLog(() => $"Destroy compression joint between {TrainCar.Resolve(coupler.gameObject)?.ID} and {TrainCar.Resolve(result.otherCoupler.gameObject)?.ID} (caller: {caller})");

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
        /// Convert compression joint to use the game's collision system instead.
        /// </summary>
        public static void ConvertCompressionJointToBufferOnly(Coupler coupler)
        {
            if (coupler?.coupledTo == null)
                return;

            try
            {
                // Remove existing compression joints and use the game's collision system instead.

                // Destroy any existing compression joints - we'll use the game's collision system instead
                if (bufferJoints.TryGetValue(coupler, out var result))
                {
                    if (result.joint != null)
                    {
                        Component.Destroy(result.joint);
                        // Joint removed
                    }

                    // Remove from tracking
                    bufferJoints.Remove(coupler);
                    bufferJoints.Remove(result.otherCoupler);
                }
                else
                {
                    // No joint found; nothing to convert
                }

                // Clear rigidCJ references so the game doesn't think cars are rigidly coupled
                if (coupler.rigidCJ != null)
                {
                    coupler.rigidCJ = null;
                    // Cleared
                }
                if (coupler.coupledTo.rigidCJ != null)
                {
                    coupler.coupledTo.rigidCJ = null;
                    // Cleared
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
        /// Update all compression joints with current settings.
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
        /// Check whether joint creation should be allowed based on timing.
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
        /// Record that a joint was created for timing purposes.
        /// </summary>
        public static void RecordJointCreation(Coupler coupler)
        {
            var currentTime = Time.time;
            lastJointCreationTime[coupler] = currentTime;
            if (coupler.coupledTo != null)
                lastJointCreationTime[coupler.coupledTo] = currentTime;
        }

    }
}