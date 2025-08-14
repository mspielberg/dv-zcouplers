using System.Linq;

using UnityEngine;

namespace DvMod.ZCouplers
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

        private const float PerFrameBreakChance = 0.001f;
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
            var couplerStrength = Main.settings.GetCouplerStrength();
            if (couplerStrength > 0f && recentStress.All(s => s > couplerStrength) && Random.value < PerFrameBreakChance)
            {
                Main.DebugLog(() => $"Breaking coupler: scaledForce={scaledForce},normalizedForce={normalizedForce},springRate={currentSpringRate},normalizationFactor={normalizationFactor},recentStress={string.Join(",", recentStress)}");
                joint!.gameObject.SendMessage("OnJointBreak", jointStress);
                Component.Destroy(joint);
            }
        }

        public void OnUncoupled(object coupler, UncoupleEventArgs args)
        {
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