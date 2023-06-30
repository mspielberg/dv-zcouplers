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
        public void FixedUpdate()
        {
            if (joint == null)
            {
                Object.Destroy(this);
                return;
            }
            var scaledForce = Vector3.Scale(joint.currentForce, StressScaler).magnitude;
            System.Array.Copy(recentStress, 0, recentStress, 1, recentStress.Length - 1);
            recentStress[0] = scaledForce;
            jointStress = recentStress.Max();
            // jointStress = ((1f - Alpha) * jointStress) + (Alpha * scaledForce);
            // Main.DebugLog(TrainCar.Resolve(gameObject), () => $"custom coupler: currentForce={joint.currentForce.magnitude},scaledForce={scaledForce},recentStress={string.Join(",", recentStress)},jointStress={jointStress}");
            var couplerStrength = Main.settings.GetCouplerStrength();
            if (couplerStrength > 0f && recentStress.All(s => s > couplerStrength) && Random.value < PerFrameBreakChance)
            {
                Main.DebugLog(() => $"Breaking coupler: currentForce={scaledForce},recentStress={string.Join(",", recentStress)}");
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