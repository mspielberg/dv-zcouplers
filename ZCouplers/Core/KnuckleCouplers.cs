using System;
using System.Collections.Generic;

using DV.CabControls;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace DvMod.ZCouplers
{
    public class KnuckleCouplers
    {
        public static KnuckleCouplers? Instance { get; private set; }
        private static bool sceneLoadHooked;

        // Cache for tracking deactivated air hoses to avoid redundant operations
        private static readonly HashSet<int> deactivatedAirHoses = new HashSet<int>();

        // Temporarily match the old working code exactly
        public static bool enabled => true; // Always enabled like the old code

        public KnuckleCouplers()
        {
            Instance = this;
            // Initialize asset manager
            AssetManager.LoadAssets();
        }

        // Asset management delegation
        public static GameObject? GetHookPrefab()
        {
            var profile = CouplerProfiles.Current;
            if (profile == null)
                return AssetManager.GetAARClosedPrefab();
            return profile.GetClosedPrefab();
        }

        // Hook management delegation
        public static void CreateHook(ChainCouplerInteraction chainCoupler) => HookManager.CreateHook(chainCoupler, GetHookPrefab());
        public static void DestroyHook(ChainCouplerInteraction chainCoupler) => HookManager.DestroyHook(chainCoupler);
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

            // Air hose handling may be profile-driven
            if (CouplerProfiles.Current?.Options.AlwaysHideAirHoses == true)
                DeactivateAllAirHoses();

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

        /// <summary>
        /// Deactivate all air hoses on all trains when using Scharfenberg couplers.
        /// </summary>
        private static void DeactivateAllAirHoses()
        {
            if (CarSpawner.Instance?.allCars == null)
                return;

            Main.DebugLog(() => "Deactivating all air hoses for Scharfenberg couplers");

            int processedCars = 0;
            int processedCouplers = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                processedCars++;

                // Deactivate air hoses on front coupler
                if (car.frontCoupler != null)
                {
                    DeactivateAirHoseForCoupler(car.frontCoupler);
                    processedCouplers++;
                }

                // Deactivate air hoses on rear coupler
                if (car.rearCoupler != null)
                {
                    DeactivateAirHoseForCoupler(car.rearCoupler);
                    processedCouplers++;
                }
            }

            Main.DebugLog(() => $"Processed air hoses: {processedCars} cars, {processedCouplers} couplers");
        }

        /// <summary>
        /// Directly deactivate air hoses for a specific coupler, bypassing the conditional logic in HookManager.
        /// Uses the same proven approach as the steam locomotive air hose deactivation.
        /// </summary>
        public static void DeactivateAirHoseForCoupler(Coupler coupler)
        {
            if (coupler?.train?.gameObject == null)
                return;

            var interior = coupler.train.interior;
            if (interior == null)
                return;

            // Use interior instance ID as cache key to track already processed interiors
            int interiorId = interior.GetInstanceID();

            // Early exit if we've already processed this interior
            if (deactivatedAirHoses.Contains(interiorId))
                return;

            // Mark as processed before doing the work
            deactivatedAirHoses.Add(interiorId);

            // Use Transform.Find for direct lookup instead of iterating all children
            var hosesTransform = interior.Find("hoses");
            if (hosesTransform != null)
            {
                hosesTransform.gameObject.SetActive(false);
                GameObjHider.Attach(hosesTransform);
            }
        }

        /// <summary>
        /// Clear the air hose deactivation cache. Call when cars are spawned/despawned.
        /// </summary>
        public static void ClearAirHoseCache()
        {
            deactivatedAirHoses.Clear();
        }

        /// <summary>
        /// Recursively find a transform by name.
        /// </summary>
    // No longer used: deterministic interior/hoses-only

        // Called from Main.Load()
        public static void Initialize()
        {
            if (Instance == null)
            {
                new KnuckleCouplers();
            }

            // Apply buffer visibility immediately based on current settings
            BufferVisualManager.ToggleBuffers(Main.settings.showBuffersWithKnuckles);

            // Also re-apply shortly after load to catch already spawned cars - use ForceRefresh here
            UnityEngine.Object.FindObjectOfType<CarSpawner>()?.StartCoroutine(DelayedBufferVisualUpdate());

            // Ensure we re-apply on future scene loads (e.g., entering game)
            if (!sceneLoadHooked)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                sceneLoadHooked = true;
            }

            // If the current profile requires always hiding air hoses, do it after a small delay
            if (CouplerProfiles.Current?.Options.AlwaysHideAirHoses == true)
                UnityEngine.Object.FindObjectOfType<CarSpawner>()?.StartCoroutine(DelayedAirHoseDeactivation());
        }

        /// <summary>
        /// Coroutine to deactivate air hoses with a small delay to ensure cars are loaded.
        /// </summary>
        private static System.Collections.IEnumerator DelayedAirHoseDeactivation()
        {
            yield return new UnityEngine.WaitForSeconds(1.0f);
            DeactivateAllAirHoses();
        }

        /// <summary>
        /// Delay to ensure cars are present before applying buffer visibility to instances.
        /// </summary>
        private static System.Collections.IEnumerator DelayedBufferVisualUpdate()
        {
            yield return new UnityEngine.WaitForSeconds(1.0f);
            // Use ForceRefresh here since new cars may have spawned
            BufferVisualManager.ForceRefreshBuffers(Main.settings.showBuffersWithKnuckles);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Only clear air hose cache for meaningful scene changes that could affect car spawning
            // Don't clear on UI scene loads or other non-gameplay scenes
            if (mode == LoadSceneMode.Single)
            {
                ClearAirHoseCache();
                Main.DebugLog(() => "Cleared air hose cache for scene load " + scene.name);
            }
            else
            {
	            Main.DebugLog(() => $"Not clearing air hose cache for scene load {scene.name} with mode {mode}");
            }

            BufferVisualManager.ToggleBuffers(Main.settings.showBuffersWithKnuckles);

            if (CouplerProfiles.Current?.Options.AlwaysHideAirHoses == true)
                UnityEngine.Object.FindObjectOfType<CarSpawner>()?.StartCoroutine(DelayedAirHoseDeactivation());
        }

    }
}
