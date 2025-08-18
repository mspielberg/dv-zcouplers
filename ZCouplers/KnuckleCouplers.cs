using DV.CabControls;

using UnityEngine;

namespace DvMod.ZCouplers
{
    public class KnuckleCouplers
    {
        public static KnuckleCouplers? Instance { get; private set; }

        // Temporarily match the old working code exactly
        public static bool enabled => true; // Always enabled like the old code

        public KnuckleCouplers()
        {
            Instance = this;
            // Initialize asset manager
            AssetManager.LoadAssets();
        }

        // Asset management delegation
        public static GameObject? GetHookPrefab() => AssetManager.GetAARClosedPrefab();

        // Hook management delegation
        public static void CreateHook(ChainCouplerInteraction chainCoupler) => HookManager.CreateHook(chainCoupler, GetHookPrefab());
        public static void DestroyHook(ChainCouplerInteraction chainCoupler) => HookManager.DestroyHook(chainCoupler);
        public static void UpdateCouplerVisualState(Coupler coupler, bool locked) => KnuckleCouplerState.UpdateCouplerVisualState(coupler, locked);
        public static void EnsureKnuckleCouplersForTrain(TrainCar car) => HookManager.EnsureKnuckleCouplersForTrain(car, GetHookPrefab());

        // Coupler state management delegation
        public static bool IsUnlocked(Coupler coupler) => KnuckleCouplerState.IsUnlocked(coupler);
        public static bool IsReadyToCouple(Coupler coupler) => KnuckleCouplerState.IsReadyToCouple(coupler);
        public static void UnlockCoupler(Coupler coupler, bool viaChainInteraction) => KnuckleCouplerState.UnlockCoupler(coupler, viaChainInteraction);
        public static void ReadyCoupler(Coupler coupler) => KnuckleCouplerState.ReadyCoupler(coupler);
        public static void SetCouplerLocked(Coupler coupler, bool locked) => KnuckleCouplerState.SetCouplerLocked(coupler, locked);
        public static bool HasUnlockedCoupler(Trainset trainset) => KnuckleCouplerState.HasUnlockedCoupler(trainset);

        public static void OnSettingsChanged()
        {
            if (Instance == null)
            {
                new KnuckleCouplers();
            }
            BufferVisualManager.ToggleBuffers(Main.settings.showBuffersWithKnuckles);

            // Recreate all couplers to apply disable settings
            RecreateAllCouplers();
        }

        /// <summary>
        /// Recreate all knuckle couplers to apply settings changes
        /// </summary>
        private static void RecreateAllCouplers()
        {
            if (CarSpawner.Instance?.allCars == null)
                return;

            var hookPrefab = GetHookPrefab();

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                // Handle front coupler
                if (car.frontCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    var frontChainScript = car.frontCoupler.visualCoupler.chainAdapter.chainScript;
                    var shouldHaveHook = !HookManager.ShouldDisableCoupler(car.frontCoupler);
                    var hasHook = HookManager.GetPivot(frontChainScript) != null;

                    if (shouldHaveHook && !hasHook)
                    {
                        // Should have hook but doesn't - create it
                        HookManager.CreateHook(frontChainScript, hookPrefab);
                    }
                    else if (!shouldHaveHook && hasHook)
                    {
                        // Shouldn't have hook but does - destroy it
                        HookManager.DestroyHook(frontChainScript);
                    }
                    else if (!shouldHaveHook && !hasHook)
                    {
                        // No hook and shouldn't have one - ensure hardware is hidden (only for disabled couplers)
                        if (HookManager.ShouldDisableCoupler(car.frontCoupler))
                        {
                            HookManager.ToggleCouplerHardware(car.frontCoupler, false);
                        }
                    }
                }

                // Handle rear coupler
                if (car.rearCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    var rearChainScript = car.rearCoupler.visualCoupler.chainAdapter.chainScript;
                    var shouldHaveHook = !HookManager.ShouldDisableCoupler(car.rearCoupler);
                    var hasHook = HookManager.GetPivot(rearChainScript) != null;

                    if (shouldHaveHook && !hasHook)
                    {
                        // Should have hook but doesn't - create it
                        HookManager.CreateHook(rearChainScript, hookPrefab);
                    }
                    else if (!shouldHaveHook && hasHook)
                    {
                        // Shouldn't have hook but does - destroy it
                        HookManager.DestroyHook(rearChainScript);
                    }
                    else if (!shouldHaveHook && !hasHook)
                    {
                        // No hook and shouldn't have one - ensure hardware is hidden (only for disabled couplers)
                        if (HookManager.ShouldDisableCoupler(car.rearCoupler))
                        {
                            HookManager.ToggleCouplerHardware(car.rearCoupler, false);
                        }
                    }
                }
            }
        }

        // Called from Main.Load()
        public static void Initialize()
        {
            if (Instance == null)
            {
                new KnuckleCouplers();
            }
        }

    }
}