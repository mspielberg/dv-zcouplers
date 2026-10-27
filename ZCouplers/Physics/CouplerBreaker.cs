using System.Linq;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Profiles;
using DvMod.ZCouplers.Core.Utils;
using DvMod.ZCouplers.Visuals;
using UnityEngine;

namespace DvMod.ZCouplers.Physics
{
    public class CouplerBreaker : MonoBehaviour
    {
        public ConfigurableJoint? joint;
        public float jointStress;
        public float[] recentStress = new float[10];

        public void Start()
        {
            GetComponent<Coupler>().Uncoupled += OnUncoupled;
        }

        private float PerFrameBreakChance = 0.5f;

        // The joint leaves sideways motion free and the breaker only sees long-axis force, so a train
        // split by a switch thrown under it never loads the joint. Beyond these, no curve or crossover
        // can hold the coupler faces, so the pair is treated as torn apart.
        private const float MaxLateralOffset = 1.5f;
        private const float MaxYawDegrees = 45f;
        private const int SplitFramesToBreak = 10;
        private int splitFrames;

        public void FixedUpdate()
        {
            if (joint == null)
            {
                Object.Destroy(this);
                return;
            }

            var coupler = GetComponent<Coupler>();

            if (coupler != null && CheckSplit(coupler))
                return;

            // Couplers fail in draft. In buff the coupler faces are pushed together and the load goes
            // through the drawgear into the frame, so run-ins and hard couplings don't break them.
            var rawForce = coupler != null && IsInDraft(coupler) ? joint.currentForce.magnitude : 0f;

            var currentSpringRate = Main.settings.GetSpringRate();

            System.Array.Copy(recentStress, 0, recentStress, 1, recentStress.Length - 1);
            recentStress[0] = rawForce;
            jointStress = recentStress.Max();

            var couplerStrength = Main.settings.GetCouplerStrength();
            var stressMN = rawForce / 1e6f; // Convert to MN for readability
            var stressRatio = couplerStrength > 0f ? rawForce / couplerStrength : 0f;

            // DebugLog when stress exceeds 90% of breaking threshold
            if (stressRatio > 0.9f)
            {
                var car = GetComponent<Coupler>()?.train;
                var carName = car?.ID ?? "Unknown";

                Main.DebugLog(() => $"[{carName}] High stress: {stressMN:F2}MN ({stressRatio:P1} of limit)");
            }

            if (couplerStrength > 0f && recentStress.All(s => s > couplerStrength) && Random.value < PerFrameBreakChance)
            {
                var otherCoupler = coupler?.coupledTo;

                Main.DebugLog(() => $"Breaking coupler: normForce={rawForce:F1}, spring={currentSpringRate:E2}");

                // Use the proper uncoupling method which handles all cleanup automatically
                if (coupler != null && coupler.IsCoupled())
                {
                    Main.DebugLog(() => $"Force uncoupling {coupler.train?.ID} and {otherCoupler?.train?.ID} due to coupler break");
                    BreakCoupling(coupler);
                    RecouplingPrevention.CleanupOldRecords(coupler);
                }
            }
        }

        /// <summary>
        /// Uncouples when the partner's face has drifted sideways or the cars have turned away from each
        /// other for <see cref="SplitFramesToBreak"/> frames in a row. Returns true if it uncoupled.
        /// </summary>
        private bool CheckSplit(Coupler coupler)
        {
            var other = coupler.coupledTo;
            if (other == null || coupler.train == null || other.train == null)
            {
                splitFrames = 0;
                return false;
            }

            var carTransform = coupler.train.transform;
            var lateral = Mathf.Abs(carTransform.InverseTransformDirection(other.transform.position - coupler.transform.position).x);
            // Coupled cars can face either way, so only the angle between their long axes counts
            var angle = Vector3.Angle(carTransform.forward, other.train.transform.forward);
            var yaw = Mathf.Min(angle, 180f - angle);

            var splitRatio = Mathf.Max(lateral / MaxLateralOffset, yaw / MaxYawDegrees);
            if (splitRatio > 0.75f)
            {
                Main.DebugLog(() => $"[{coupler.train.ID}] Split from {other.train.ID}: lateral={lateral:F2}m, yaw={yaw:F1}° ({splitRatio:P0} of limit)");
            }

            splitFrames = splitRatio > 1f ? splitFrames + 1 : 0;
            if (splitFrames < SplitFramesToBreak || !coupler.IsCoupled())
                return false;

            Main.DebugLog(() => $"Force uncoupling {coupler.train.ID} and {other.train.ID}: cars split apart (lateral={lateral:F2}m, yaw={yaw:F1}°)");
            BreakCoupling(coupler);
            RecouplingPrevention.CleanupOldRecords(coupler);
            return true;
        }

        /// <summary>
        /// A plain Uncouple() is a manual uncoupling and closes both angle cocks. A broken coupling tears
        /// the hoses apart with the cocks still open, so the brake pipe vents and the cars brake.
        /// </summary>
        private static void BreakCoupling(Coupler coupler)
        {
            coupler.Uncouple(playAudio: true, calledOnOtherCoupler: false, dueToBrokenCouple: true);
        }

        /// <summary>
        /// Whether the coupled faces are pulled apart (draft) rather than pushed together (buff): the
        /// partner's coupler lies beyond this one, seen from this car's centre.
        /// </summary>
        private static bool IsInDraft(Coupler coupler)
        {
            var other = coupler.coupledTo;
            if (other == null) return false;
            var outward = coupler.transform.position - coupler.train.transform.position;
            return Vector3.Dot(other.transform.position - coupler.transform.position, outward) > 0f;
        }

        public void OnUncoupled(object coupler, UncoupleEventArgs args)
        {
            // Handle LAP coupler link destruction when uncoupling
            if (CouplerProfiles.Current?.ProfileId == "LAP" && coupler is Coupler thisCoupler)
            {
                var otherCoupler = args.otherCoupler;
                if (otherCoupler != null)
                {
                    LAPLinkManager.HideOrDestroyLink(thisCoupler, otherCoupler);
                }
            }

            Component.Destroy(this);
        }

        public void OnDestroy()
        {
            var coupler = this.GetComponent<Coupler>();
            if (coupler)
                coupler.Uncoupled -= OnUncoupled;
        }
    }
}
