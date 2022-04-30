using HarmonyLib;

namespace DvMod.ZCouplers
{
    public static class LocoRemote
    {
        [HarmonyPatch(typeof(LocoControllerBase), nameof(LocoControllerBase.IsCouplerInRange))]
        public static class IsCouplerInRangePatch
        {
            public static bool Prefix(LocoControllerBase __instance, ref bool __result)
            {
                var brakeset = __instance.train.brakeSystem.brakeset;
                var trainset = __instance.train.trainset;
                if (brakeset.cars.Count < trainset.cars.Count || KnuckleCouplers.HasUnlockedCoupler(trainset))
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(LocoControllerBase), nameof(LocoControllerBase.RemoteControllerCouple))]
        public static class RemoteControllerCouplePatch
        {
            public static void Postfix(LocoControllerBase __instance)
            {
                var car = __instance.train.trainset.firstCar;
                var coupler = car.frontCoupler.IsCoupled() ? car.frontCoupler : car.rearCoupler;
                KnuckleCouplers.ReadyCoupler(coupler.GetOppositeCoupler());

                while (coupler.coupledTo is Coupler partner)
                {
                    coupler.ConnectAirHose(partner, true);
                    coupler.IsCockOpen = true;
                    partner.IsCockOpen = true;
                    coupler = partner.GetOppositeCoupler();
                }

                KnuckleCouplers.ReadyCoupler(coupler);
            }
        }

        [HarmonyPatch(typeof(LocoControllerBase), nameof(LocoControllerBase.Uncouple))]
        public static class UncouplePatch
        {
            public static void Postfix(LocoControllerBase __instance, int selectedCoupler)
            {
                var coupler = selectedCoupler > 0
                    ? CouplerLogic.GetNthCouplerFrom(__instance.train.frontCoupler, selectedCoupler - 1)
                    : CouplerLogic.GetNthCouplerFrom(__instance.train.rearCoupler, -selectedCoupler - 1);
                if (coupler != null)
                    KnuckleCouplers.UnlockCoupler(coupler, viaChainInteraction: false);
            }
        }
    }
}