using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Core.Profiles;
using DvMod.ZCouplers.Core.Utils;
using DvMod.ZCouplers.Physics;
using DvMod.ZCouplers.Visuals;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DvMod.ZCouplers.Core
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
            return profile?.GetClosedPrefab();
        }

        // Hook management delegation
        public static void CreateHook(ChainCouplerInteraction chainCoupler) => HookManager.CreateHook(chainCoupler, GetHookPrefab());

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

            // Safety check: Don't process air hoses if profiles aren't registered yet
            var currentProfile = CouplerProfiles.Current;
            if (currentProfile == null)
            {
                Main.DebugLog(() => $"OnSettingsChanged called but no profile available for {currentProfile}, skipping air hose processing");
                return;
            }

            Main.DebugLog(() => $"OnSettingsChanged called: couplerProfileId={currentProfile.ProfileId}, Current profile={currentProfile.DisplayName}");

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

        /// <summary>
        /// Deactivate all air hoses on all trains when using Scharfenberg couplers.
        /// </summary>
        private static void DeactivateAllAirHoses()
        {
	        if (CarSpawner.Instance?.allCars == null)
		        return;

	        Main.DebugLog(() => "Deactivating all air hoses");

	        int processedCars = 0;
	        int processedCouplers = 0;

	        foreach (var car in CarSpawner.Instance.allCars)
	        {
		        if (car == null) continue;
		        HookManager.ToggleAirHose(car.frontCoupler, false);
		        HookManager.ToggleAirHose(car.rearCoupler, false);
		        processedCars += 2;
	        }

	        Main.DebugLog(() => $"Processed air hoses: {processedCars} cars, {processedCouplers} couplers");
        }

        /// <summary>
        /// Restore all air hoses when switching from profiles that hide them.
        /// </summary>
        private static void RestoreAllAirHoses()
        {
            if (CarSpawner.Instance?.allCars == null)
                return;

            Main.DebugLog(() => "Restoring all air hoses");

            int processedCars = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;
                HookManager.ToggleAirHose(car.frontCoupler, true);
                HookManager.ToggleAirHose(car.rearCoupler, true);
                processedCars++;
            }

            Main.DebugLog(() => $"Restored air hoses for {processedCars} cars");
        }

        /// <summary>
        /// Clean up and shutdown all systems.
        /// Called during mod unload.
        /// </summary>
        public static void Cleanup()
        {
            // Unsubscribe from scene events
            if (sceneLoadHooked)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                sceneLoadHooked = false;
            }

            // Clear air hose cache
            deactivatedAirHoses.Clear();

            // Reset instance
            Instance = null;
        }

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

            // Apply buffer colliders after cars are loaded
            UnityEngine.Object.FindObjectOfType<CarSpawner>()?.StartCoroutine(DelayedBufferColliderUpdate());
            // Ensure we re-apply on future scene loads (e.g., entering game)
            if (!sceneLoadHooked)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                sceneLoadHooked = true;
            }

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

        /// <summary>
        /// Delay to ensure cars and interior objects are fully loaded before applying buffer collider management.
        /// </summary>
        private static System.Collections.IEnumerator DelayedBufferColliderUpdate()
        {
            yield return new WaitUntil(() => AStartGameData.carsAndJobsLoadingFinished);

            // Additional wait for physics frames to ensure everything is stable
            for (int i = 0; i < 30; i++)
            {
                yield return new UnityEngine.WaitForFixedUpdate();
            }

            if (CarSpawner.Instance == null) yield break;
            foreach (var car in CarSpawner.Instance.allCars)
				BufferCollisionManager.ApplyBufferCollidersForCar(car);
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Only clear air hose cache for meaningful scene changes that could affect car spawning
            // Don't clear on UI scene loads or other non-gameplay scenes
            if (mode == LoadSceneMode.Single)
            {
                BufferVisualManager.ToggleBuffers(Main.settings.showBuffersWithKnuckles);

                // Apply buffer colliders after scene load
                UnityEngine.Object.FindObjectOfType<CarSpawner>()?.StartCoroutine(DelayedBufferColliderUpdate());
                // Apply air hose visibility after scene load
                UnityEngine.Object.FindObjectOfType<CarSpawner>()?.StartCoroutine(DelayedAirHoseUpdate());
            }
        }

        /// <summary>
        /// Delay to ensure cars and interior objects are fully loaded before applying air hose visibility.
        /// This handles the case where Scharfenberg is already selected when the game loads.
        /// </summary>
        private static System.Collections.IEnumerator DelayedAirHoseUpdate()
        {
            yield return new WaitUntil(() => AStartGameData.carsAndJobsLoadingFinished);

            // Additional wait for interior objects to be fully loaded
            for (int i = 0; i < 10; i++)
            {
                yield return new UnityEngine.WaitForFixedUpdate();
            }

            // Check if current coupler type requires air hoses to be hidden
            var currentProfile = CouplerProfiles.Current;
            if (currentProfile?.Options.AlwaysHideAirHoses != true) yield break;
            Main.DebugLog(() => $"{currentProfile.DisplayName} selected - hiding air hoses");

            if (CarSpawner.Instance?.allCars == null) yield break;
            foreach (var car in CarSpawner.Instance.allCars.OfType<TrainCar>())
            {
	            HookManager.ToggleAirHose(car.frontCoupler, false);
	            HookManager.ToggleAirHose(car.rearCoupler, false);
            }
            Main.DebugLog(() => $"Hidden air hoses on {CarSpawner.Instance.allCars.Count} cars");
        }

        /// <summary>
        /// Switch coupler types at runtime without requiring a restart.
        /// This method starts a coroutine to handle the complex multi-phase switching process.
        /// </summary>
        public static void SwitchCouplerTypeAtRuntime(ICouplerProfile oldType, ICouplerProfile newType)
        {
            Main.DebugLog(() => $"Starting runtime coupler switch from {oldType} to {newType}");

            // Start the coroutine-based switching process
            var carSpawner = UnityEngine.Object.FindObjectOfType<CarSpawner>();
            if (carSpawner != null)
            {
                carSpawner.StartCoroutine(SwitchCouplerTypeCoroutine(oldType, newType));
            }
            else
            {
                Main.ErrorLog(() => "CarSpawner not found, cannot perform runtime coupler switch");
            }
        }

        /// <summary>
        /// Coroutine that handles the runtime coupler type switching with proper timing and validation.
        /// </summary>
        private static System.Collections.IEnumerator SwitchCouplerTypeCoroutine(ICouplerProfile oldProfile, ICouplerProfile newProfile)
        {
            Main.DebugLog(() => $"Phase 1: Pre-loading assets for {newProfile.DisplayName}");

            // Phase 1: Get profile and load assets
            if (newProfile == null)
            {
                Main.ErrorLog(() => $"No profile found for coupler type {newProfile}, aborting switch");
                yield break;
            }

            AssetManager.LoadAssetsForProfile(newProfile);

            // Wait a frame for asset loading to complete
            yield return null;

            // Verify assets loaded successfully
            if (!AssetManager.AreAssetsLoaded(newProfile))
            {
                Main.ErrorLog(() => $"Failed to load assets for profile {newProfile}, aborting switch");
                yield break;
            }

            // Verify prefabs are accessible
            GameObject? closedPrefab = null;
            GameObject? openPrefab = null;

            try
            {
                closedPrefab = newProfile.GetClosedPrefab();
                openPrefab = newProfile.GetOpenPrefab();

                if (closedPrefab == null || (newProfile.Options.HasOpenVariant && openPrefab == null))
                {
                    Main.ErrorLog(() => $"Profile for {newProfile} returned null prefabs (closed: {closedPrefab?.name ?? "null"}, open: {openPrefab?.name ?? "null"})");
                    yield break;
                }

                Main.DebugLog(() => $"Assets verified for {newProfile}: closed={closedPrefab.name}, open={openPrefab?.name ?? "N/A"}");
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error accessing prefabs for {newProfile}: {ex.Message}");
                yield break;
            }

            // Phase 2: Clean up existing couplers
            Main.DebugLog(() => "Phase 2: Cleaning up existing couplers");

            try
            {
                CleanupAllCouplersForTypeSwitch();
                LAPLinkManager.Cleanup();
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error during cleanup phase: {ex.Message}");
                yield break;
            }

            // Wait multiple frames for Unity to process GameObject destruction
            yield return null;
            yield return null;
            yield return new UnityEngine.WaitForEndOfFrame();

            // Validate cleanup completed
            if (!ValidateCleanupCompleted())
            {
                Main.ErrorLog(() => "Cleanup validation failed, but continuing anyway");
                // Continue anyway - some hooks might remain but we'll recreate everything
            }

            Main.DebugLog(() => "Cleanup phase completed successfully");

            // Phase 3: Wait for physics to settle
            Main.DebugLog(() => "Phase 3: Waiting for physics to settle");

            // Wait for physics updates to process the cleanup
            for (int i = 0; i < 5; i++)
            {
                yield return new UnityEngine.WaitForFixedUpdate();
            }

            // Phase 4: Recreate couplers with new type
            Main.DebugLog(() => "Phase 4: Recreating couplers with new type");

            try
            {
                RecreateAllCouplersForNewType();
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error during recreation phase: {ex.Message}");
                Main.ErrorLog(() => "Starting recovery process due to recreation failure");
                // Can't yield in catch, so just log error and continue - recovery will happen later if validation fails
            }

            // Wait for creation to complete
            yield return null;
            yield return new UnityEngine.WaitForEndOfFrame();

            // Validate recreation completed
            if (!ValidateRecreationCompleted(newProfile.ProfileId))
            {
                Main.ErrorLog(() => "Recreation validation failed, attempting simple recovery");
                // Attempt a simple recreation retry
                RecreateAllCouplersForNewType();
                yield return null;

                if (!ValidateRecreationCompleted(newProfile.ProfileId))
                {
                    Main.ErrorLog(() => "Recovery failed, couplers may be in inconsistent state");
                    // Continue anyway - user can change coupler type again to fix
                }
            }

            Main.DebugLog(() => "Recreation phase completed successfully");

            // Phase 5: Apply type-specific settings
            Main.DebugLog(() => "Phase 5: Applying type-specific settings");

            try
            {
                ApplyProfileSpecificSettings(oldProfile, newProfile);
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error applying type-specific settings: {ex.Message}");
                // Continue anyway, this is not critical enough to abort
            }

            // Wait for settings application
            yield return null;

            // Phase 6: Update physics joints
            Main.DebugLog(() => "Phase 6: Updating physics joints");

            try
            {
                UpdateAllJointsForNewType();
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error updating physics joints: {ex.Message}");
                // Continue anyway, joints will be updated as needed
            }

            // Final physics settle time
            for (int i = 0; i < 3; i++)
            {
                yield return new UnityEngine.WaitForFixedUpdate();
            }

            Main.DebugLog(() => $"Successfully completed runtime coupler switch to {newProfile}");
        }

        /// <summary>
        /// Clean up all existing coupler visuals and physics in preparation for type switch
        /// </summary>
        private static void CleanupAllCouplersForTypeSwitch()
        {
            if (CarSpawner.Instance?.allCars == null)
            {
                Main.ErrorLog(() => "CarSpawner.Instance or allCars is null during cleanup");
                return;
            }

            Main.DebugLog(() => "Cleaning up existing couplers for type switch");

            int processedCars = 0;
            int cleanedHooks = 0;
            int totalHooks = 0;
            int cleanedGameObjHiders = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                processedCars++;

                try
                {
                    // Clean up HookPlates and socket plates for this car first
                    HookManager.CleanupHookPlatesForTypeSwitch(car);

                    // Clean up GameObjHider components from air hoses to ensure proper visibility restoration
                    if (car.interior != null)
                    {
                        for (int i = 0; i < car.interior.childCount; i++)
                        {
                            var child = car.interior.GetChild(i);
                            if (child != null && child.name == "hoses")
                            {
                                var hiders = child.GetComponentsInChildren<GameObjHider>(true);
                                foreach (var hider in hiders)
                                {
                                    if (hider != null)
                                    {
                                        Object.Destroy(hider);
                                        cleanedGameObjHiders++;
                                    }
                                }
                            }
                        }
                    }

                    // Clean up front coupler
                    if (car.frontCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                    {
                        var chainScript = car.frontCoupler.visualCoupler.chainAdapter.chainScript;
                        totalHooks++;

                        // Verify hook exists before attempting to destroy
                        if (HookManager.GetPivot(chainScript) != null)
                        {
                            HookManager.DestroyHook(chainScript);
                            cleanedHooks++;
                        }

                        // Clean up any existing joints
                        try
                        {
                            JointManager.CleanupCouplerJoints(car.frontCoupler);
                        }
                        catch (System.Exception ex)
                        {
                            Main.ErrorLog(() => $"Error cleaning front coupler joints for car {car.ID}: {ex.Message}");
                        }
                    }

                    // Clean up rear coupler
                    if (car.rearCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                    {
                        var chainScript = car.rearCoupler.visualCoupler.chainAdapter.chainScript;
                        totalHooks++;

                        // Verify hook exists before attempting to destroy
                        if (HookManager.GetPivot(chainScript) != null)
                        {
                            HookManager.DestroyHook(chainScript);
                            cleanedHooks++;
                        }

                        // Clean up any existing joints
                        try
                        {
                            JointManager.CleanupCouplerJoints(car.rearCoupler);
                        }
                        catch (System.Exception ex)
                        {
                            Main.ErrorLog(() => $"Error cleaning rear coupler joints for car {car.ID}: {ex.Message}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Error during cleanup for car {car.ID}: {ex.Message}");
                }
            }

            Main.DebugLog(() => $"Cleanup completed: {processedCars} cars processed, {cleanedHooks}/{totalHooks} hooks destroyed, {cleanedGameObjHiders} GameObjHider components removed");
        }

        /// <summary>
        /// Recreate all couplers with the new coupler type
        /// </summary>
        private static void RecreateAllCouplersForNewType()
        {
            if (CarSpawner.Instance?.allCars == null)
            {
                Main.ErrorLog(() => "CarSpawner.Instance or allCars is null during recreation");
                return;
            }

            // Verify we have a valid profile and prefabs before starting
            var profile = CouplerProfiles.Current;

            var hookPrefab = GetHookPrefab();
            if (hookPrefab == null)
            {
                Main.ErrorLog(() => $"Hook prefab is null for coupler type {profile?.DisplayName}");
                return;
            }

            Main.DebugLog(() => $"Recreating all couplers with new type {profile?.DisplayName}, prefab: {hookPrefab.name}");

            int processedCars = 0;
            int successfulHooks = 0;
            int expectedHooks = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                processedCars++;

                try
                {
                    // Recreate front coupler
                    if (car.frontCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                    {
                        var frontChainScript = car.frontCoupler.visualCoupler.chainAdapter.chainScript;
                        var shouldHaveHook = !HookManager.ShouldDisableCoupler(car.frontCoupler);

                        if (shouldHaveHook)
                        {
                            expectedHooks++;

                            // Verify the chain script is still valid
                            if (frontChainScript.gameObject != null && frontChainScript.couplerAdapter != null)
                            {
                                HookManager.CreateHook(frontChainScript, hookPrefab);

                                // Verify the hook was actually created
                                if (HookManager.GetPivot(frontChainScript) != null)
                                {
                                    successfulHooks++;
                                    HookManager.UpdateHookVisualStateFromCouplerState(car.frontCoupler);

                                    // Air hose visibility is handled in ApplyTypeSpecificSettings, not here
                                }
                                else
                                {
                                    Main.ErrorLog(() => $"Failed to create front hook for car {car.ID}");
                                }
                            }
                        }
                        else
                        {
                            HookManager.ToggleCouplerHardware(car.frontCoupler, false);
                        }
                    }

                    // Recreate rear coupler
                    if (car.rearCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                    {
                        var rearChainScript = car.rearCoupler.visualCoupler.chainAdapter.chainScript;
                        var shouldHaveHook = !HookManager.ShouldDisableCoupler(car.rearCoupler);

                        if (shouldHaveHook)
                        {
                            expectedHooks++;

                            // Verify the chain script is still valid
                            if (rearChainScript.gameObject != null && rearChainScript.couplerAdapter != null)
                            {
                                HookManager.CreateHook(rearChainScript, hookPrefab);

                                // Verify the hook was actually created
                                if (HookManager.GetPivot(rearChainScript) != null)
                                {
                                    successfulHooks++;
                                    HookManager.UpdateHookVisualStateFromCouplerState(car.rearCoupler);

                                    // Air hose visibility is handled in ApplyTypeSpecificSettings, not here
                                }
                                else
                                {
                                    Main.ErrorLog(() => $"Failed to create rear hook for car {car.ID}");
                                }
                            }
                        }
                        else
                        {
                            HookManager.ToggleCouplerHardware(car.rearCoupler, false);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Error recreating couplers for car {car.ID}: {ex.Message}");
                }
            }

            Main.DebugLog(() => $"Recreation completed: {processedCars} cars processed, {successfulHooks}/{expectedHooks} hooks created");

            if (expectedHooks > 0 && successfulHooks < expectedHooks)
            {
                Main.ErrorLog(() => $"Recreation incomplete: only {successfulHooks}/{expectedHooks} hooks were successfully created");
            }
        }

        /// <summary>
        /// Apply profile-specific settings like air hoses and buffers when switching coupler profiles.
        /// Only handles air hoses when switching to/from profiles that hide air hoses.
        /// </summary>
        private static void ApplyProfileSpecificSettings(ICouplerProfile oldProfile, ICouplerProfile newProfile)
        {
            Main.DebugLog(() => $"Applying profile-specific settings for switch from {oldProfile.DisplayName} to {newProfile.DisplayName}");

            // Only handle air hoses when switching to/from profiles that hide air hoses (e.g., Scharfenberg)
            bool oldProfileHidesAirHoses = oldProfile?.Options.AlwaysHideAirHoses == true;
            bool newProfileHidesAirHoses = newProfile.Options.AlwaysHideAirHoses;

            if (oldProfileHidesAirHoses != newProfileHidesAirHoses)
            {
                // Only change air hose state when there's actually a difference
                if (newProfileHidesAirHoses)
                {
                    Main.DebugLog(() => $"Switching to {newProfile.DisplayName} - hiding air hoses");
                    DeactivateAllAirHoses();
                }
                else
                {
                    Main.DebugLog(() => $"Switching from {oldProfile?.DisplayName} to {newProfile.DisplayName} - restoring air hoses");
                    RestoreAllAirHoses();
                }
            }
            else
            {
                Main.DebugLog(() => $"Air hose visibility unchanged for switch from {oldProfile?.DisplayName} to {newProfile.DisplayName}");
            }

            // Update buffer visibility (this may have changed with coupler profile)
            BufferVisualManager.ToggleBuffers(Main.settings.showBuffersWithKnuckles);
        }

        /// <summary>
        /// Validate that cleanup completed successfully by checking for remaining hooks
        /// </summary>
        private static bool ValidateCleanupCompleted()
        {
            if (CarSpawner.Instance?.allCars == null)
                return true; // No cars, nothing to validate

            int remainingHooks = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                if (car.frontCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    if (HookManager.GetPivot(car.frontCoupler.visualCoupler.chainAdapter.chainScript) != null)
                        remainingHooks++;
                }

                if (car.rearCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    if (HookManager.GetPivot(car.rearCoupler.visualCoupler.chainAdapter.chainScript) != null)
                        remainingHooks++;
                }
            }

            if (remainingHooks > 0)
            {
                Main.ErrorLog(() => $"Cleanup validation failed: {remainingHooks} hooks still exist");
                return false;
            }

            Main.DebugLog(() => "Cleanup validation passed: all hooks removed");
            return true;
        }

        /// <summary>
        /// Validate that recreation completed successfully
        /// </summary>
        private static bool ValidateRecreationCompleted(string expectedProfileId)
        {
            if (CarSpawner.Instance?.allCars == null)
                return true; // No cars, nothing to validate

            var profile = CouplerProfiles.GetById(expectedProfileId);
            if (profile == null)
            {
                Main.ErrorLog(() => $"Profile {expectedProfileId} not found during recreation validation");
                return false;
            }

            int expectedHooks = 0;
            int actualHooks = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                // Check front coupler
                if (car.frontCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    if (!HookManager.ShouldDisableCoupler(car.frontCoupler))
                    {
                        expectedHooks++;
                        if (HookManager.GetPivot(car.frontCoupler.visualCoupler.chainAdapter.chainScript) != null)
                            actualHooks++;
                    }
                }

                // Check rear coupler
                if (car.rearCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    if (!HookManager.ShouldDisableCoupler(car.rearCoupler))
                    {
                        expectedHooks++;
                        if (HookManager.GetPivot(car.rearCoupler.visualCoupler.chainAdapter.chainScript) != null)
                            actualHooks++;
                    }
                }
            }

            if (actualHooks < expectedHooks)
            {
                Main.ErrorLog(() => $"Recreation validation failed: only {actualHooks}/{expectedHooks} hooks created");
                return false;
            }

            Main.DebugLog(() => $"Recreation validation passed: {actualHooks}/{expectedHooks} hooks created");
            return true;
        }

        /// <summary>
        /// Update all physics joints for the new coupler type
        /// </summary>
        private static void UpdateAllJointsForNewType()
        {
            if (CarSpawner.Instance?.allCars == null)
                return;

            Main.DebugLog(() => "Updating physics joints for new coupler type");

            int updatedJoints = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                // Update joints for front coupler if coupled
                if (car.frontCoupler?.IsCoupled() == true)
                {
                    try
                    {
                        JointManager.UpdateJointForCoupler(car.frontCoupler);
                        updatedJoints++;
                    }
                    catch (System.Exception ex)
                    {
                        Main.ErrorLog(() => $"Error updating front joint for car {car.ID}: {ex.Message}");
                    }
                }

                // Update joints for rear coupler if coupled
                if (car.rearCoupler?.IsCoupled() == true)
                {
                    try
                    {
                        JointManager.UpdateJointForCoupler(car.rearCoupler);
                        updatedJoints++;
                    }
                    catch (System.Exception ex)
                    {
                        Main.ErrorLog(() => $"Error updating rear joint for car {car.ID}: {ex.Message}");
                    }
                }
            }

            Main.DebugLog(() => $"Updated {updatedJoints} physics joints");
        }
    }
}
