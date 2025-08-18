using DV.RemoteControls;

using HarmonyLib;

namespace DvMod.ZCouplers
{
    public static class LocoRemote
    {
        [HarmonyPatch(typeof(RemoteControllerModule), nameof(RemoteControllerModule.IsCouplerInRange))]
        public static class IsCouplerInRangePatch
        {
            public static bool Prefix(RemoteControllerModule __instance, ref bool __result)
            {
                var brakeset = __instance.car.brakeSystem.brakeset;
                var trainset = __instance.car.trainset;
                if (brakeset.cars.Count < trainset.cars.Count || KnuckleCouplers.HasUnlockedCoupler(trainset))
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(RemoteControllerModule), nameof(RemoteControllerModule.RemoteControllerCouple))]
        public static class RemoteControllerCouplePatch
        {
            public static void Postfix(RemoteControllerModule __instance)
            {
                var car = __instance.car.trainset.firstCar;
                var coupler = car.frontCoupler.IsCoupled() ? car.frontCoupler : car.rearCoupler;
                KnuckleCouplers.ReadyCoupler(coupler.GetOppositeCoupler());

                while (coupler.coupledTo is Coupler partner)
                {
                    coupler.ConnectAirHose(partner, true);
                    coupler.IsCockOpen = true;
                    partner.IsCockOpen = true;

                    // Force create joints for remote coupling
                    Couplers.ForceCreateTensionJoint(coupler);

                    coupler = partner.GetOppositeCoupler();
                }

                KnuckleCouplers.ReadyCoupler(coupler);
            }
        }

        [HarmonyPatch(typeof(RemoteControllerModule), nameof(RemoteControllerModule.Uncouple))]
        public static class UncouplePatch
        {
            public static void Postfix(RemoteControllerModule __instance, int selectedCoupler)
            {
                var coupler = selectedCoupler > 0
                    ? CouplerLogic.GetNthCouplerFrom(__instance.car.frontCoupler, selectedCoupler - 1)
                    : CouplerLogic.GetNthCouplerFrom(__instance.car.rearCoupler, -selectedCoupler - 1);
                if (coupler != null)
                    KnuckleCouplers.UnlockCoupler(coupler, viaChainInteraction: false);
            }
        }
    }
}