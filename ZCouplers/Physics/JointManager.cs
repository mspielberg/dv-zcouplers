using System.Collections.Generic;
using System.Linq;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Visuals;
using UnityEngine;

namespace DvMod.ZCouplers.Physics
{
    /// <summary>
    /// Single-joint manager for knuckle couplers.
    /// One ConfigurableJoint per coupled pair is authoritative.
    /// </summary>
    public static class JointManager
    {
        private static readonly Dictionary<Coupler, ConfigurableJoint> customTensionJoints = new();

        // Kept as a mirrored map to avoid breaking callers while architecture is single-joint.
        public static readonly Dictionary<Coupler, (Coupler otherCoupler, ConfigurableJoint joint)> bufferJoints = new();

        private static readonly Dictionary<Coupler, float> lastJointCreationTime = new();
        private const float MinJointCreationInterval = 2.0f;

        private const float TightChainLength = 1.0f;

        private static float CalculateJointDistance(ConfigurableJoint joint)
        {
            Vector3 anchorWorldPos = joint.transform.TransformPoint(joint.anchor);
            Vector3 connectedAnchorWorldPos = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
            return Vector3.Distance(anchorWorldPos, connectedAnchorWorldPos);
        }

        public static ConfigurableJoint? GetTensionJoint(Coupler coupler)
            => coupler != null && customTensionJoints.TryGetValue(coupler, out var joint) ? joint : null;

        public static bool HasTensionJoint(Coupler coupler)
            => coupler != null && customTensionJoints.ContainsKey(coupler);

        public static void ForceCreateTensionJoint(Coupler coupler)
        {
            if (coupler == null || !coupler.IsCoupled() || coupler.coupledTo == null)
                return;
            if (customTensionJoints.ContainsKey(coupler))
                return;

            CreateTensionJoint(coupler);

            var existingBreaker = coupler.GetComponent<CouplerBreaker>();
            if (existingBreaker == null)
            {
                var breaker = coupler.gameObject.AddComponent<CouplerBreaker>();
                var tensionJoint = GetTensionJoint(coupler);
                if (tensionJoint != null)
                {
                    breaker.joint = tensionJoint;
                    Main.DebugLog(() => $"Added CouplerBreaker to {coupler.train.ID} during force joint creation");
                }
            }
        }

        public static void CreateTensionJoint(Coupler coupler)
        {
            if (coupler == null || coupler.coupledTo == null)
                return;

            if (HasTensionJoint(coupler) || HasTensionJoint(coupler.coupledTo))
                return;

            var coupledTo = coupler.coupledTo;
            var anchorOffset = Vector3.forward * TightChainLength * (coupler.isFrontCoupler ? -1f : 1f);

            var cj = coupler.train.gameObject.AddComponent<ConfigurableJoint>();
            cj.autoConfigureConnectedAnchor = false;
            cj.anchor = coupler.transform.localPosition + anchorOffset;
            cj.connectedBody = coupledTo.train.gameObject.GetComponent<Rigidbody>();
            cj.connectedAnchor = coupledTo.transform.localPosition;

            var actualJointDistance = CalculateJointDistance(cj);
            var jointLimit = Mathf.Max(actualJointDistance, 1.0f);

            cj.xMotion = ConfigurableJointMotion.Free;
            cj.yMotion = ConfigurableJointMotion.Free;
            cj.zMotion = ConfigurableJointMotion.Limited;

            if (Main.settings.showBuffersWithKnuckles)
            {
                cj.angularXMotion = ConfigurableJointMotion.Limited;
                cj.angularYMotion = ConfigurableJointMotion.Limited;
                cj.angularZMotion = ConfigurableJointMotion.Limited;
                cj.lowAngularXLimit = new SoftJointLimit { limit = 5f };
                cj.highAngularXLimit = new SoftJointLimit { limit = 5f };
                cj.angularYLimit = new SoftJointLimit { limit = 30f };
                cj.angularZLimit = new SoftJointLimit { limit = 5f };
                cj.enableCollision = true;
            }
            else
            {
                cj.angularXMotion = ConfigurableJointMotion.Limited;
                cj.angularYMotion = ConfigurableJointMotion.Limited;
                cj.angularZMotion = ConfigurableJointMotion.Limited;
                cj.lowAngularXLimit = new SoftJointLimit { limit = 20f };
                cj.highAngularXLimit = new SoftJointLimit { limit = 20f };
                cj.angularYLimit = new SoftJointLimit { limit = 80f };
                cj.angularZLimit = new SoftJointLimit { limit = 45f };
                cj.enableCollision = false;
            }

            cj.angularXLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };
            cj.angularYZLimitSpring = new SoftJointLimitSpring { spring = Main.settings.GetSpringRate() };
            cj.linearLimit = new SoftJointLimit { limit = jointLimit };
            cj.linearLimitSpring = new SoftJointLimitSpring
            {
                spring = Main.settings.GetSpringRate(),
                damper = Main.settings.GetDamperRate(),
            };
            cj.breakForce = float.PositiveInfinity;
            cj.breakTorque = float.PositiveInfinity;

            customTensionJoints[coupler] = cj;
            bufferJoints[coupler] = (coupledTo, cj);
            bufferJoints[coupledTo] = (coupler, cj);

            coupler.rigidCJ = cj;
            coupledTo.rigidCJ = cj;

            CollisionHandler.DisableFakeBufferColliders(coupler, coupledTo);
            UpdateCouplerStatesAfterCompressionJoint(coupler, coupledTo);

            var existingBreaker = coupler.GetComponent<CouplerBreaker>();
            if (existingBreaker == null)
            {
                var breaker = coupler.gameObject.AddComponent<CouplerBreaker>();
                breaker.joint = cj;
                Main.DebugLog(() => $"Added CouplerBreaker to {coupler.train.ID} during tension joint creation");
            }

            Main.DebugLog(() => $"Single joint created: distance={actualJointDistance:F3}m, limit={jointLimit:F3}m for {coupler.train.ID}");
        }

        private static void UpdateCouplerStatesAfterCompressionJoint(Coupler a, Coupler b)
        {
            if (KnuckleCouplers.IsReadyToCouple(a) && KnuckleCouplers.IsReadyToCouple(b))
            {
                bool aWasDangling = a.state == ChainCouplerInteraction.State.Dangling;
                bool bWasDangling = b.state == ChainCouplerInteraction.State.Dangling;

                if (aWasDangling || bWasDangling)
                {
                    Main.DebugLog(() => $"Set Attached_Tight after joint creation: {a.train.ID} {a.Position()} (was {a.state}), {b.train.ID} {b.Position()} (was {b.state})");
                    if (aWasDangling)
                    {
                        a.state = ChainCouplerInteraction.State.Attached_Tight;
                        HookManager.UpdateHookVisualStateFromCouplerState(a);
                    }
                    if (bWasDangling)
                    {
                        b.state = ChainCouplerInteraction.State.Attached_Tight;
                        HookManager.UpdateHookVisualStateFromCouplerState(b);
                    }
                }
            }
        }

        public static void DestroyTensionJoint(Coupler coupler)
        {
            if (coupler == null)
                return;

            try
            {
                Coupler? owner = coupler;
                if (!customTensionJoints.TryGetValue(owner, out var tensionJoint))
                {
                    owner = coupler.coupledTo;
                    if (owner == null || !customTensionJoints.TryGetValue(owner, out tensionJoint))
                    {
                        Main.DebugLog(() => $"Tension joint not found to destroy for {coupler.train.ID} {coupler.Position()} or its partner");
                        return;
                    }
                }

                var other = owner.coupledTo;

                if (tensionJoint != null)
                    Object.Destroy(tensionJoint);

                customTensionJoints.Remove(owner);
                lastJointCreationTime.Remove(owner);

                bufferJoints.Remove(owner);
                if (other != null) bufferJoints.Remove(other);

                if (owner.rigidCJ == tensionJoint) owner.rigidCJ = null;
                if (other != null && other.rigidCJ == tensionJoint) other.rigidCJ = null;

                if (other != null) CollisionHandler.EnableFakeBufferColliders(owner, other);
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error destroying tension joint: {ex.Message}");
                customTensionJoints.Remove(coupler);
                lastJointCreationTime.Remove(coupler);
                bufferJoints.Remove(coupler);
                if (coupler.coupledTo != null)
                {
                    customTensionJoints.Remove(coupler.coupledTo);
                    lastJointCreationTime.Remove(coupler.coupledTo);
                    bufferJoints.Remove(coupler.coupledTo);
                }
            }
        }

        public static void UpdateAllCompressionJoints()
        {
            UpdateAllJointParameters();
        }

        public static bool CanCreateJoint(Coupler coupler)
        {
            var currentTime = Time.time;
            if (lastJointCreationTime.TryGetValue(coupler, out var lastTime) && (currentTime - lastTime) < MinJointCreationInterval)
                return false;
            return true;
        }

        public static void RecordJointCreation(Coupler coupler)
        {
            var currentTime = Time.time;
            lastJointCreationTime[coupler] = currentTime;
            if (coupler.coupledTo != null)
                lastJointCreationTime[coupler.coupledTo] = currentTime;
        }

        public static void DestroyUntrackedJoints(GameObject gameObject)
        {
            if (gameObject == null) return;
            var joints = gameObject.GetComponents<ConfigurableJoint>();
            foreach (var joint in joints)
            {
                if (joint == null) continue;
                bool isCustom = customTensionJoints.Values.Any(v => v == joint);
                if (isCustom) continue;
                Object.Destroy(joint);
            }
        }

        public static void Cleanup()
        {
            var jointsToDestroy = new List<ConfigurableJoint>();
            foreach (var joint in customTensionJoints.Values)
            {
                if (joint != null)
                    jointsToDestroy.Add(joint);
            }
            foreach (var joint in jointsToDestroy)
            {
                if (joint != null)
                    Object.Destroy(joint);
            }

            customTensionJoints.Clear();
            lastJointCreationTime.Clear();
            bufferJoints.Clear();
            CollisionHandler.Cleanup();
        }

        public static void CleanupCouplerJoints(Coupler coupler)
        {
            if (coupler == null) return;

            try
            {
                DestroyTensionJoint(coupler);

                var breaker = coupler.GetComponent<CouplerBreaker>();
                if (breaker != null)
                    Object.DestroyImmediate(breaker);

                Main.DebugLog(() => $"Cleaned up joints for coupler {coupler.train.ID} {coupler.Position()}");
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error cleaning up coupler joints for {coupler.train.ID}: {ex.Message}");
            }
        }

        public static void UpdateAllJointParameters()
        {
            try
            {
                var springRate = Main.settings.GetSpringRate();
                var damperRate = Main.settings.GetDamperRate();

                Main.DebugLog(() => $"Updating joint parameters: spring={springRate}, damper={damperRate}");

                foreach (var kvp in customTensionJoints.ToList())
                {
                    var coupler = kvp.Key;
                    var joint = kvp.Value;

                    if (joint == null)
                    {
                        if (coupler != null)
                            customTensionJoints.Remove(coupler);
                        continue;
                    }

                    joint.angularXLimitSpring = new SoftJointLimitSpring { spring = springRate };
                    joint.angularYZLimitSpring = new SoftJointLimitSpring { spring = springRate };
                    joint.linearLimitSpring = new SoftJointLimitSpring { spring = springRate, damper = damperRate };

                    if (Main.settings.showBuffersWithKnuckles)
                    {
                        joint.lowAngularXLimit = new SoftJointLimit { limit = 5f };
                        joint.highAngularXLimit = new SoftJointLimit { limit = 5f };
                        joint.angularYLimit = new SoftJointLimit { limit = 30f };
                        joint.angularZLimit = new SoftJointLimit { limit = 5f };
                        joint.enableCollision = true;
                    }
                    else
                    {
                        joint.lowAngularXLimit = new SoftJointLimit { limit = 20f };
                        joint.highAngularXLimit = new SoftJointLimit { limit = 20f };
                        joint.angularYLimit = new SoftJointLimit { limit = 80f };
                        joint.angularZLimit = new SoftJointLimit { limit = 45f };
                        joint.enableCollision = false;
                    }
                }

                RecreateMissingTensionJoints();
                Main.DebugLog(() => $"Updated {customTensionJoints.Count} single joints");
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error updating joint parameters: {ex.Message}");
            }
        }

        private static void RecreateMissingTensionJoints()
        {
            if (CarSpawner.Instance?.allCars == null)
                return;

            int recreatedCount = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                if (car.frontCoupler != null && car.frontCoupler.IsCoupled() && !HasTensionJoint(car.frontCoupler))
                {
                    ForceCreateTensionJoint(car.frontCoupler);
                    recreatedCount++;
                }

                if (car.rearCoupler != null && car.rearCoupler.IsCoupled() && !HasTensionJoint(car.rearCoupler))
                {
                    ForceCreateTensionJoint(car.rearCoupler);
                    recreatedCount++;
                }
            }

            if (recreatedCount > 0)
                Main.DebugLog(() => $"Recreated {recreatedCount} missing tension joints");
        }

        public static void UpdateJointForCoupler(Coupler coupler)
        {
            if (coupler == null || !coupler.IsCoupled())
                return;

            try
            {
                if (HasTensionJoint(coupler))
                {
                    var joint = GetTensionJoint(coupler);
                    if (joint != null)
                    {
                        var springRate = Main.settings.GetSpringRate();
                        var damperRate = Main.settings.GetDamperRate();
                        joint.angularXLimitSpring = new SoftJointLimitSpring { spring = springRate };
                        joint.angularYZLimitSpring = new SoftJointLimitSpring { spring = springRate };
                        joint.linearLimitSpring = new SoftJointLimitSpring { spring = springRate, damper = damperRate };
                    }
                }
                else
                {
                    CreateTensionJoint(coupler);
                }
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error updating joint for coupler: {ex.Message}");
            }
        }
    }
}
