using System.Collections;
using System.Collections.Generic;

using HarmonyLib;

using Newtonsoft.Json.Linq;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles saving and loading of knuckle coupler states
    /// </summary>
    public static class SaveManager
    {
        private const string SaveKey = "DvMod.ZCouplers";
        private const string FrontCouplerLockedKey = "frontCouplerLocked";
        private const string RearCouplerLockedKey = "rearCouplerLocked";

        // Store coupler states that need to be applied after physics stabilizes
        private static readonly Dictionary<TrainCar, (bool frontLocked, bool rearLocked)> pendingCouplerStates =
            new Dictionary<TrainCar, (bool, bool)>();
        private static bool isLoadingFromSave = false;
        private static float saveLoadStartTime = 0f;
        private const float SaveLoadGracePeriod = 3.0f; // 3 seconds after first car loaded

        // Save knuckle coupler states alongside the game's native coupler states (don't override them)
        [HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.GetCarSaveData))]
        public static class GetCarSaveDataPatch
        {
            public static void Postfix(TrainCar car, JObject __result)
            {
                try
                {
                    // Only save knuckle coupler data if knuckle couplers are enabled
                    if (car?.frontCoupler != null && car?.rearCoupler != null)
                    {
                        // Save knuckle coupler locked states as separate data
                        __result[SaveKey] = new JObject(
                            new JProperty(FrontCouplerLockedKey, KnuckleCouplers.IsReadyToCouple(car.frontCoupler)),
                            new JProperty(RearCouplerLockedKey, KnuckleCouplers.IsReadyToCouple(car.rearCoupler)));
                    }
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Error saving coupler data for car {car?.ID}: {ex.Message}");
                }
            }
        }

        // Read knuckle coupler states from custom save data and let the game handle native states
        [HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.InstantiateCarFromSavegame))]
        public static class InstantiateCarPatch
        {
            public static void Postfix(JObject carData, RailTrack[] tracks, TrainCar __result)
            {
                try
                {
                    if (__result == null)
                        return;

                    // Only set loading state on the first car to avoid constantly resetting it
                    if (pendingCouplerStates.Count == 0)
                    {
                        isLoadingFromSave = true;
                        saveLoadStartTime = Time.time;
                    }

                    // Read knuckle coupler states from our custom save data
                    bool frontLocked = false;
                    bool rearLocked = false;

                    if (carData?.TryGetValue(SaveKey, out var data) == true && data is JObject obj)
                    {
                        frontLocked = obj.TryGetValue(FrontCouplerLockedKey, out var frontToken) && frontToken.Value<bool>();
                        rearLocked = obj.TryGetValue(RearCouplerLockedKey, out var rearToken) && rearToken.Value<bool>();
                    }
                    else
                    {
                        // No knuckle coupler save data - use defaults (locked/ready by default for knuckle couplers)
                        frontLocked = true;
                        rearLocked = true;
                    }

                    pendingCouplerStates[__result] = (frontLocked, rearLocked);

                    // Start deferred application only for the first car
                    if (pendingCouplerStates.Count == 1)
                    {
                        __result.StartCoroutine(TriggerDeferredApplicationCoroutine());
                    }
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Error processing coupler save data for car {__result.ID}: {ex.Message}");
                    // Use safe defaults
                    pendingCouplerStates[__result] = (true, true);
                }
            }
        }

        /// <summary>
        /// Trigger coroutine to handle deferred application of coupler states
        /// </summary>
        private static IEnumerator TriggerDeferredApplicationCoroutine()
        {
            // Wait for all cars to be loaded and physics to stabilize
            yield return new WaitForSeconds(2.0f);

            // Wait for additional physics frames to ensure save loading is complete
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Reset the loading state to allow normal joint creation
            isLoadingFromSave = false;

            // Start deferred application using the dedicated applicator
            var statesToApply = new Dictionary<TrainCar, (bool, bool)>(pendingCouplerStates);
            pendingCouplerStates.Clear();
            DeferredStateApplicator.StartDeferredCouplerApplication(statesToApply);
        }

        /// <summary>
        /// Check if we're currently in the save loading grace period
        /// </summary>
        public static bool IsLoadingFromSave => isLoadingFromSave && (Time.time - saveLoadStartTime) < SaveLoadGracePeriod;

        /// <summary>
        /// Check if a car has pending states to be applied
        /// </summary>
        public static bool HasPendingStates(TrainCar car) => pendingCouplerStates.ContainsKey(car);

        /// <summary>
        /// Clear pending states for manually uncoupled cars
        /// </summary>
        public static void ClearPendingStatesForCar(TrainCar car)
        {
            if (pendingCouplerStates.ContainsKey(car))
            {
                pendingCouplerStates.Remove(car);
            }
        }
    }
}