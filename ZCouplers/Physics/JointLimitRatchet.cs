using DvMod.ZCouplers.Core;
using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Brings a client's tension joint limit down to the host's without yanking the cars together.
    /// The client's copy of the cars lags behind the host's, so when JointCreate arrives they can still
    /// be metres apart and the joint is created that long. Each FixedUpdate the limit follows the actual
    /// anchor distance down (never below the host's limit) until it reaches the host's value.
    /// </summary>
    public class JointLimitRatchet : MonoBehaviour
    {
        private const float GiveUpAfterSeconds = 30f;

        private ConfigurableJoint? joint;
        private float target;
        private float startTime;

        public static void Attach(ConfigurableJoint joint, float target)
        {
            if (joint == null || target <= 0f) return;
            if (joint.linearLimit.limit <= target) return;
            Main.DebugLog(() => $"[MP] Client joint limit {joint.linearLimit.limit:F3} m, bringing it down to the host's {target:F3} m");
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

            var anchor = joint.transform.TransformPoint(joint.anchor);
            var connected = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
            var distance = Vector3.Distance(anchor, connected);
            var timedOut = Time.time - startTime > GiveUpAfterSeconds;

            var limit = timedOut ? target : Mathf.Max(target, Mathf.Min(joint.linearLimit.limit, distance));
            if (!Mathf.Approximately(limit, joint.linearLimit.limit))
                joint.linearLimit = new SoftJointLimit { limit = limit };
            if (limit <= target + 1e-3f)
            {
                Main.DebugLog(() => $"[MP] Client joint limit reached the host's {target:F3} m after {Time.time - startTime:F1} s{(timedOut ? " (timed out)" : "")}");
                Destroy(this);
            }
        }
    }
}
