using System.Linq;
using DvMod.ZCouplers.Core;
using UnityEngine;

namespace DvMod.ZCouplers.Physics
{
    public class CouplerBreaker : MonoBehaviour
    {
        public ConfigurableJoint? joint;
        public float jointStress;
        public float[] recentStress = new float[10];
        private static readonly Vector3 StressScaler = new Vector3(0.1f, 0.1f, 1.0f);

        public void Start()
        {
            this.GetComponent<Coupler>().Uncoupled += OnUncoupled;
        }

        private const float PerFrameBreakChance = 0.01f;
        private const float BaseSpringRate = 2e6f; // 2 MN/m baseline spring rate for force normalization
        private const float MinNormalizationFactor = 0.5f; // Don't reduce forces below 50% of original

        public void FixedUpdate()
        {
            if (joint == null)
            {
                Object.Destroy(this);
                return;
            }
            var scaledForce = Vector3.Scale(joint.currentForce, StressScaler).magnitude;

            // Normalize force by spring rate to maintain consistent breaking behavior
            // But don't let forces get too weak to prevent unrealistic behavior
            var currentSpringRate = Main.settings.GetSpringRate();
            var normalizationFactor = Mathf.Max(BaseSpringRate / currentSpringRate, MinNormalizationFactor);
            var normalizedForce = scaledForce * normalizationFactor;

            System.Array.Copy(recentStress, 0, recentStress, 1, recentStress.Length - 1);
            recentStress[0] = normalizedForce;
            jointStress = recentStress.Max();
            
            // Debug logging for elevated stress levels (avoid spam for normal operation)
            var couplerStrength = Main.settings.GetCouplerStrength();
            var stressMN = normalizedForce / 1e6f; // Convert to MN for readability
            var strengthMN = couplerStrength / 1e6f;
            var stressRatio = couplerStrength > 0f ? normalizedForce / couplerStrength : 0f;
            
            // Log when stress exceeds 25% of breaking threshold, or when above 0.5 MN
            if (stressMN > 0.5f || stressRatio > 0.25f)
            {
                var rawForceMN = joint.currentForce.magnitude / 1e6f;
                var car = GetComponent<Coupler>()?.train;
                var carName = car?.ID ?? "Unknown";
                
                Main.DebugLog(() => $"[{carName}] High stress: {stressMN:F2}MN ({stressRatio:P1} of limit), " +
                                   $"raw={rawForceMN:F2}MN, scaled={scaledForce/1e6f:F2}MN, " +
                                   $"spring={currentSpringRate/1e6f:F1}MN/m, norm={normalizationFactor:F2}");
            }
            
            if (couplerStrength > 0f && recentStress.All(s => s > couplerStrength) && Random.value < PerFrameBreakChance)
            {
                Main.DebugLog(() => $"Breaking coupler: normForce={normalizedForce:F1}, spring={currentSpringRate:E2}");
                joint!.gameObject.SendMessage("OnJointBreak", jointStress);
                Component.Destroy(joint);
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
