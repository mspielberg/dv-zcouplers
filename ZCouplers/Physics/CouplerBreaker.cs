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

        public void FixedUpdate()
        {
            if (joint == null)
            {
                Object.Destroy(this);
                return;
            }

            // Couplers fail in draft. In buff the coupler faces are pushed together and the load goes
            // through the drawgear into the frame, so run-ins and hard couplings don't break them.
            var coupler = GetComponent<Coupler>();
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
                    coupler.Uncouple();
                    RecouplingPrevention.CleanupOldRecords(coupler);
                }
            }
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
