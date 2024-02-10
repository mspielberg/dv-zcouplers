using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace DvMod.ZCouplers
{
    public static class SaveManager
    {
        private const string SaveKey = "DvMod.ZCouplers";
        private const string FrontCouplerLockedKey = "frontCouplerLocked";
        private const string RearCouplerLockedKey = "rearCouplerLocked";

        [HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.GetCarSaveData))]
        public static class GetCarSaveDataPatch
        {
            public static void Postfix(TrainCar car, JObject __result)
            {
                __result[SaveKey] = new JObject(
                    new JProperty(FrontCouplerLockedKey, KnuckleCouplers.IsReadyToCouple(car.frontCoupler)),
                    new JProperty(RearCouplerLockedKey, KnuckleCouplers.IsReadyToCouple(car.rearCoupler)));
            }
        }

        [HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.InstantiateCarFromSavegame))]
        public static class InstantiateCarPatch
        {
            public static void Postfix(JObject carData, RailTrack[] tracks, TrainCar __result)
            {
                static void SetupCoupler(Coupler coupler, bool locked)
                {
                    if (locked)
                        KnuckleCouplers.ReadyCoupler(coupler);
                    else
                        KnuckleCouplers.UnlockCoupler(coupler, true);
                }

                if ((carData?.TryGetValue(SaveKey, out var data) ?? false) && data is JObject obj)
                {
                    if (obj.TryGetValue(FrontCouplerLockedKey, out var frontLocked))
                        SetupCoupler(__result.frontCoupler, frontLocked.Value<bool>());
                    if (obj.TryGetValue(RearCouplerLockedKey, out var rearLocked))
                        SetupCoupler(__result.rearCoupler, rearLocked.Value<bool>());
                }
            }
        }
    }
}
