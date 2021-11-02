using System;
using System.Linq;
using UnityEngine;
using UnityModManagerNet;
using Formatter = System.Func<float, string>;
using Provider = System.Func<TrainCar, float?>;
using Pusher = System.Action<TrainCar, float>;

namespace DvMod.ZCouplers
{
    internal sealed class HeadsUpDisplayBridge
    {
        public static HeadsUpDisplayBridge? instance;

        public static void Init()
        {
            // just here to force the static initializer to run
        }

        static HeadsUpDisplayBridge()
        {
            try
            {
                var hudMod = UnityModManager.FindMod("HeadsUpDisplay");
                if (hudMod?.Loaded != true)
                    return;
                instance = new HeadsUpDisplayBridge(hudMod);
            }
            catch (System.IO.FileNotFoundException)
            {
            }
        }

        private static readonly Type[] RegisterPullArgumentTypes = new Type[]
        {
            typeof(string),
            typeof(Provider),
            typeof(Formatter),
            typeof(IComparable),
            typeof(bool),
        };

        private HeadsUpDisplayBridge(UnityModManager.ModEntry hudMod)
        {
            void RegisterPull(string label, Provider provider, Formatter formatter, IComparable? order = null, bool hidden = false)
            {
                hudMod.Invoke(
                    "DvMod.HeadsUpDisplay.Registry.RegisterPull",
                    out var _,
                    new object?[] { label, provider, formatter, order, hidden },
                    RegisterPullArgumentTypes);
            }

            RegisterPull(
                "Front coupler",
                car => car.frontCoupler.GetComponent<CouplerBreaker>()?.jointStress,
                v => $"{v / Main.settings.GetCouplerStrength() / 1e6f:P0}",
                hidden: true);

            // RegisterPull(
            //     "Front coupler Z",
            //     car => JointDelta(car.frontCoupler)?.z,
            //     v => $"{v * 1e3f:F3} mm");

            // RegisterPull(
            //     "Front coupler length",
            //     car => JointDelta(car.frontCoupler)?.magnitude,
            //     v => $"{v * 1e3f:F3} mm");

            RegisterPull(
                "Rear coupler",
                car => car.rearCoupler.GetComponent<CouplerBreaker>()?.jointStress,
                v => $"{v / Main.settings.GetCouplerStrength() / 1e6f:P0}",
                hidden: true);

            // RegisterPull(
            //     "Rear coupler Z",
            //     car => JointDelta(car.rearCoupler)?.z,
            //     v => $"{v * 1e3f:F3} mm");

            // RegisterPull(
            //     "Rear coupler length",
            //     car => JointDelta(car.rearCoupler)?.magnitude,
            //     v => $"{v * 1e3f:F3} mm");
        }

        private static Vector3? JointDelta(Coupler coupler)
        {
            if (coupler.springyCJ == null)
                return null;
            return JointDelta(coupler.springyCJ, coupler.isFrontCoupler);
        }

        private static Vector3 JointDelta(Joint joint, bool isFrontCoupler)
        {
            var delta = joint.transform.InverseTransformPoint(joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)) - joint.anchor;
            return isFrontCoupler ? delta : -delta;
        }
    }
}