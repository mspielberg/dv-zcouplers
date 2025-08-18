using System.Collections;

using HarmonyLib;

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
                        __instance.StartCoroutine(InitializeNewCar(__instance));
                    }
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Error in TrainCar Awake patch: {ex.Message}");
                }
            }

            /// <summary>
            /// Initialize proper coupler states for a newly spawned car
            /// </summary>
            private static IEnumerator DelayedVisualStateUpdate(TrainCar car)
            {
                // Wait a bit for hooks to be created
                yield return new WaitForSeconds(0.2f);

                // Update visual states to show correct interaction prompts
                if (car?.frontCoupler != null && !car.frontCoupler.IsCoupled())
                {
                    KnuckleCouplerState.UpdateCouplerVisualState(car.frontCoupler, locked: true);
                }
                if (car?.rearCoupler != null && !car.rearCoupler.IsCoupled())
                {
                    KnuckleCouplerState.UpdateCouplerVisualState(car.rearCoupler, locked: true);
                }
            }

            /// <summary>
            /// Re-apply hardware hiding for steam locomotives after spawn/initialization
            /// </summary>
            private static IEnumerator DelayedHardwareToggle(TrainCar car)
            {
                // Wait longer than visual updates to ensure game initialization is complete
                yield return new WaitForSeconds(0.5f);

                if (car?.frontCoupler != null && HookManager.ShouldDisableCoupler(car.frontCoupler))
                {
                    Main.DebugLog(() => $"Re-applying hardware hiding for steam locomotive {car.ID} front coupler");
                    HookManager.ToggleCouplerHardware(car.frontCoupler, false);
                }
            }

            private static IEnumerator InitializeNewCar(TrainCar car)
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
                        car.frontCoupler.state = ChainCouplerInteraction.State.Dangling;
                    }
                    if (!car.rearCoupler.IsCoupled())
                    {
                        car.rearCoupler.state = ChainCouplerInteraction.State.Dangling;
                    }

                    // For steam locomotives, re-apply hardware hiding after initialization
                    // because the game may have re-enabled air hoses during initialization
                    if (Main.settings.disableFrontCouplersOnSteamLocos)
                    {
                        if (HookManager.ShouldDisableCoupler(car.frontCoupler))
                        {
                            // Wait a bit longer to ensure the game has finished initializing everything
                            car.StartCoroutine(DelayedHardwareToggle(car));
                        }
                    }

                    // Update visual states after a small delay to ensure hooks are created
                    car.StartCoroutine(DelayedVisualStateUpdate(car));

                    Main.DebugLog(() => $"Initialized knuckle coupler states for new car {car.ID}");
                }
                else
                {
                    Main.DebugLog(() => $"Coupler initialization skipped - car not ready after {attempts} attempts");
                }
            }
        }
    }
}