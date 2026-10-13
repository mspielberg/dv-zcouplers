using DvMod.ZCouplers.Core;
using UnityEngine;

namespace DvMod.ZCouplers.Physics
{
    /// <summary>
    /// Brings a coupling joint's limit down to its target without yanking the cars together. A joint
    /// starts wide enough to cover where the cars are when it is made: pressed together or still apart,
    /// or, on an MP client, metres apart because the client's copy of the cars lags the host's. Each
    /// FixedUpdate the limit follows the actual axial offset down, and narrows at least at a walking pace
    /// so cars left standing apart are drawn together gently, until it reaches the target.
    /// </summary>
    public class JointLimitRatchet : MonoBehaviour
    {
        // About the speed vanilla's own joint adaptation closes a gap at
        private const float MinNarrowingSpeed = 0.25f; // m/s

        private ConfigurableJoint? joint;
        private float target;
        private float startTime;

        /// <summary>Narrow the joint's limit to the target; a later call (e.g. with the MP host's target) replaces it.</summary>
        public static void Attach(ConfigurableJoint joint, float target)
        {
            if (joint == null || target <= 0f) return;
            foreach (var existing in joint.gameObject.GetComponents<JointLimitRatchet>())
            {
                if (existing.joint == joint)
                {
                    existing.enabled = false;
                    Destroy(existing);
                }
            }
            if (joint.linearLimit.limit <= target + 1e-3f)
            {
                joint.linearLimit = new SoftJointLimit { limit = target };
                return;
            }
            Main.DebugLog(() => $"Joint limit {joint.linearLimit.limit:F3} m, bringing it down to {target:F3} m");
            var ratchet = joint.gameObject.AddComponent<JointLimitRatchet>();
            ratchet.joint = joint;
            ratchet.target = target;
            ratchet.startTime = Time.time;
        }

        private void FixedUpdate()
        {
            if (joint == null || joint.connectedBody == null)
            {
                Destroy(this);
                return;
            }

            var offset = Mathf.Abs(JointManager.AxialOffset(joint));
            var narrowed = joint.linearLimit.limit - MinNarrowingSpeed * Time.fixedDeltaTime;

            var limit = Mathf.Max(target, Mathf.Min(narrowed, offset));
            if (limit <= target + 1e-3f)
            {
                joint.linearLimit = new SoftJointLimit { limit = target };
                Main.DebugLog(() => $"Joint limit reached {target:F3} m after {Time.time - startTime:F1} s");
                Destroy(this);
                return;
            }
            joint.linearLimit = new SoftJointLimit { limit = limit };
        }
    }
}
