using System.Linq;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Helpers;
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
            this.GetComponent<Coupler>().Uncoupled += OnUncoupled;
        }

        private const float PerFrameBreakChance = 0.01f;
        private const float BaseSpringRate = 2e6f; // 2 MN/m baseline spring rate for force normalization

        public void FixedUpdate()
        {
            if (joint == null)
            {
                Object.Destroy(this);
                return;
            }

            // Use the full force magnitude instead of artificially scaling it down
            var rawForce = joint.currentForce.magnitude;

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
                var coupler = GetComponent<Coupler>();
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

        public void OnUncoupled(object coupler, UncoupleEventArgs args)
        {
            // Handle LAP coupler link destruction when uncoupling
            if (Main.settings.couplerType == CouplerType.LAPCoupler && coupler is Coupler thisCoupler)
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
