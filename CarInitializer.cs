using HarmonyLib;
using System.Collections;
using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles initialization of newly spawned cars (not from save files)
    /// </summary>
    public static class CarInitializer
    {
        /// <summary>
        /// Patch to handle newly spawned cars (not from save) to ensure proper initial states
        /// </summary>
        [HarmonyPatch(typeof(TrainCar), "Awake")]
        public static class TrainCarAwakePatch
        {
            public static void Postfix(TrainCar __instance)
            {
                try
                {
                    if (__instance == null)
                        return;
                        
                    // Only handle cars that are NOT being loaded from save
                    if (!SaveManager.IsLoadingFromSave && !SaveManager.HasPendingStates(__instance))
                    {
                        // This is a newly spawned car, ensure proper knuckle coupler initial states
                        __instance.StartCoroutine(InitializeNewCarCouplerStates(__instance));
                    }
                }
                catch (System.Exception ex)
                {
                    Main.DebugLog(() => $"Error in TrainCar Awake patch: {ex.Message}");
                }
            }
            
            /// <summary>
            /// Initialize proper coupler states for a newly spawned car
            /// </summary>
            private static IEnumerator InitializeNewCarCouplerStates(TrainCar car)
            {
                // Wait a frame for the car to be fully initialized
                yield return new WaitForEndOfFrame();
                
                // Wait until the car's logicCar is properly set up
                int attempts = 0;
                while ((car?.logicCar == null || string.IsNullOrEmpty(car.ID)) && attempts < 10)
                {
                    yield return new WaitForEndOfFrame();
                    attempts++;
                }
                
                if (car?.frontCoupler != null && car?.rearCoupler != null && !string.IsNullOrEmpty(car.ID))
                {
                    // Set knuckle couplers to locked (ready to couple) by default for new cars
                    KnuckleCouplers.SetCouplerLocked(car.frontCoupler, true);
                    KnuckleCouplers.SetCouplerLocked(car.rearCoupler, true);
                    
                    // Ensure proper native states for uncoupled new cars
                    if (!car.frontCoupler.IsCoupled())
                    {
                        car.frontCoupler.state = ChainCouplerInteraction.State.Parked;
                    }
                    if (!car.rearCoupler.IsCoupled())
                    {
                        car.rearCoupler.state = ChainCouplerInteraction.State.Parked;
                    }
                    
                    Main.DebugLog(() => $"Initialized knuckle coupler states for new car {car.ID}");
                }
                else
                {
                    Main.DebugLog(() => $"Failed to initialize coupler states - car not ready after {attempts} attempts");
                }
            }
        }
    }
}
