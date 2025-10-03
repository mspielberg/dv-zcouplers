using System.Collections.Generic;
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
            if (profile == null)
                return AssetManager.GetAARClosedPrefab();
            return profile.GetClosedPrefab();
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
                Main.DebugLog(() => $"OnSettingsChanged called but no profile available for {Main.settings.couplerType}, skipping air hose processing");
                return;
            }
            
            Main.DebugLog(() => $"OnSettingsChanged called: couplerType={Main.settings.couplerType}, Current profile={currentProfile.Options.Name}");
            
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

            BufferVisualManager.ApplyBufferCollidersForAllCars();
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Only clear air hose cache for meaningful scene changes that could affect car spawning
            // Don't clear on UI scene loads or other non-gameplay scenes
            if (mode == LoadSceneMode.Single)
            {
                ClearAirHoseCache();
                Main.DebugLog(() => "Cleared air hose cache for scene load " + scene.name);
                BufferVisualManager.ToggleBuffers(Main.settings.showBuffersWithKnuckles);

                // Apply buffer colliders after scene load
                UnityEngine.Object.FindObjectOfType<CarSpawner>()?.StartCoroutine(DelayedBufferColliderUpdate());

                // Air hose handling is only done in OnSettingsChanged when switching coupler types
            }
        }

        /// <summary>
        /// Switch coupler types at runtime without requiring a restart.
        /// This method starts a coroutine to handle the complex multi-phase switching process.
        /// </summary>
        public static void SwitchCouplerTypeAtRuntime(CouplerType oldType, CouplerType newType)
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
        private static System.Collections.IEnumerator SwitchCouplerTypeCoroutine(CouplerType oldType, CouplerType newType)
        {
            Main.DebugLog(() => $"Phase 1: Pre-loading assets for {newType}");
            
            // Phase 1: Load assets FIRST before any cleanup
            AssetManager.LoadAssetsForCouplerType(newType);
            
            // Wait a frame for asset loading to complete
            yield return null;
            
            // Verify assets loaded successfully
            if (!AssetManager.AreAssetsLoadedForType(newType))
            {
                Main.ErrorLog(() => $"Failed to load assets for coupler type {newType}, aborting switch");
                yield break;
            }

            // Verify profile and prefabs are accessible
            var newProfile = CouplerProfiles.Get(newType);
            if (newProfile == null)
            {
                Main.ErrorLog(() => $"No profile found for coupler type {newType}, aborting switch");
                yield break;
            }

            GameObject? closedPrefab = null;
            GameObject? openPrefab = null;
            
            try
            {
                closedPrefab = newProfile.GetClosedPrefab();
                openPrefab = newProfile.GetOpenPrefab();
                
                if (closedPrefab == null || openPrefab == null)
                {
                    Main.ErrorLog(() => $"Profile for {newType} returned null prefabs (closed: {closedPrefab?.name ?? "null"}, open: {openPrefab?.name ?? "null"})");
                    yield break;
                }

                Main.DebugLog(() => $"Assets verified for {newType}: closed={closedPrefab.name}, open={openPrefab.name}");
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error accessing prefabs for {newType}: {ex.Message}");
                yield break;
            }

            // Phase 2: Clean up existing couplers
            Main.DebugLog(() => "Phase 2: Cleaning up existing couplers");
            
            try
            {
                CleanupAllCouplersForTypeSwitch();
                LAPLinkManager.Cleanup();
                ClearAirHoseCache();
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
            if (!ValidateRecreationCompleted(newType))
            {
                Main.ErrorLog(() => "Recreation validation failed, attempting simple recovery");
                // Attempt a simple recreation retry
                RecreateAllCouplersForNewType();
                yield return null;
                
                if (!ValidateRecreationCompleted(newType))
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
                ApplyTypeSpecificSettings(oldType, newType);
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

            Main.DebugLog(() => $"Successfully completed runtime coupler switch to {newType}");
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

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                processedCars++;

                try
                {
                    // Clean up HookPlates and socket plates for this car first
                    HookManager.CleanupHookPlatesForTypeSwitch(car);

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

            Main.DebugLog(() => $"Cleanup completed: {processedCars} cars processed, {cleanedHooks}/{totalHooks} hooks destroyed");
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
            if (profile == null)
            {
                Main.ErrorLog(() => $"No profile available for coupler type {Main.settings.couplerType}");
                return;
            }

            var hookPrefab = GetHookPrefab();
            if (hookPrefab == null)
            {
                Main.ErrorLog(() => $"Hook prefab is null for coupler type {Main.settings.couplerType}");
                return;
            }

            Main.DebugLog(() => $"Recreating all couplers with new type {Main.settings.couplerType}, prefab: {hookPrefab.name}");

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
        /// Apply type-specific settings like air hoses and buffers when switching coupler types.
        /// Only handles air hoses when switching to/from Scharfenberg profile.
        /// </summary>
        private static void ApplyTypeSpecificSettings(CouplerType oldType, CouplerType newType)
        {
            var oldProfile = CouplerProfiles.Get(oldType);
            var newProfile = CouplerProfiles.Get(newType);
            if (newProfile == null) return;

            Main.DebugLog(() => $"Applying type-specific settings for switch from {oldType} to {newType}");

            // Only handle air hoses when switching to/from Scharfenberg (which hides air hoses)
            bool oldTypeHidesAirHoses = oldProfile?.Options.AlwaysHideAirHoses == true;
            bool newTypeHidesAirHoses = newProfile.Options.AlwaysHideAirHoses;

            if (oldTypeHidesAirHoses != newTypeHidesAirHoses)
            {
                // Only change air hose state when there's actually a difference
                if (newTypeHidesAirHoses)
                {
                    Main.DebugLog(() => $"Switching to {newType} - hiding air hoses");
                    DeactivateAllAirHoses();
                }
                else
                {
                    Main.DebugLog(() => $"Switching from {oldType} to {newType} - restoring air hoses");
                    RestoreAllAirHoses();
                }
            }
            else
            {
                Main.DebugLog(() => $"Air hose visibility unchanged for switch from {oldType} to {newType}");
            }

            // Update buffer visibility (this may have changed with coupler type)
            BufferVisualManager.ToggleBuffers(Main.settings.showBuffersWithKnuckles);
        }

        /// <summary>
        /// Restore air hoses that may have been hidden by a previous coupler type
        /// </summary>
        private static void RestoreAllAirHoses()
        {
            if (CarSpawner.Instance?.allCars == null)
                return;

            Main.DebugLog(() => "Restoring air hoses for new coupler type");

            // Clear the cache first so hoses can be processed again
            deactivatedAirHoses.Clear();

            int restoredCars = 0;
            int processedCouplers = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car?.interior == null) continue;

                restoredCars++;

                // First, thoroughly clean up any GameObjHider components and restore at train level
                var hosesTransform = car.interior.Find("hoses");
                if (hosesTransform != null)
                {
                    // Remove ALL GameObjHider components that may be hiding the hoses (could be multiple)
                    // Use DestroyImmediate to ensure they're gone before we try to restore visibility
                    var hiders = hosesTransform.GetComponents<GameObjHider>();
                    foreach (var hider in hiders)
                    {
                        if (hider != null)
                        {
                            UnityEngine.Object.DestroyImmediate(hider);
                        }
                    }

                    // Also check and remove GameObjHiders from child objects
                    var childHiders = hosesTransform.GetComponentsInChildren<GameObjHider>(true);
                    foreach (var childHider in childHiders)
                    {
                        if (childHider != null)
                        {
                            // Store the gameObject reference before destroying the component
                            var childObj = childHider.gameObject;
                            UnityEngine.Object.DestroyImmediate(childHider);
                            
                            // Force the child object back to active state, but skip DEBUG objects
                            if (!childObj.name.Contains("DEBUG"))
                            {
                                childObj.SetActive(true);
                            }
                        }
                    }

                    // Restore visibility at train level and all child objects
                    hosesTransform.gameObject.SetActive(true);
                    
                    // Ensure all child objects are also active and renderers enabled
                    var renderers = hosesTransform.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in renderers)
                    {
                        if (renderer != null && !renderer.gameObject.name.Contains("DEBUG"))
                        {
                            renderer.gameObject.SetActive(true);
                            renderer.enabled = true;
                        }
                    }
                }

                // Air hose visibility is now restored by cleaning up GameObjHiders above
                // No need to call ToggleAirHose which has profile-based overrides
            }

            Main.DebugLog(() => $"Air hose restoration completed: {restoredCars} cars, {processedCouplers} couplers processed");
        }

        /// <summary>
        /// Update all existing physics joints to use parameters from the new coupler type
        /// </summary>
        private static void UpdateAllJointsForNewType()
        {
            Main.DebugLog(() => "Updating physics joints for new coupler type");

            // Update compression joints with new settings
            Couplers.UpdateAllCompressionJoints();

            // Update any existing tension joints
            JointManager.UpdateAllJointParameters();
        }

        /// <summary>
        /// Validates that the cleanup phase completed successfully by checking that old coupler visuals are removed.
        /// </summary>
        private static bool ValidateCleanupCompleted()
        {
            if (CarSpawner.Instance?.allCars == null)
                return true;

            int remainingHooks = 0;
            int totalCouplers = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                // Check front coupler
                if (car.frontCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    totalCouplers++;
                    var frontChainScript = car.frontCoupler.visualCoupler.chainAdapter.chainScript;
                    if (HookManager.GetPivot(frontChainScript) != null)
                        remainingHooks++;
                }

                // Check rear coupler
                if (car.rearCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    totalCouplers++;
                    var rearChainScript = car.rearCoupler.visualCoupler.chainAdapter.chainScript;
                    if (HookManager.GetPivot(rearChainScript) != null)
                        remainingHooks++;
                }
            }

            Main.DebugLog(() => $"Cleanup validation: {remainingHooks}/{totalCouplers} couplers still have hooks");
            return remainingHooks == 0;
        }

        /// <summary>
        /// Validates that the recreation phase completed successfully by checking that new coupler visuals are created.
        /// </summary>
        private static bool ValidateRecreationCompleted(CouplerType expectedType)
        {
            if (CarSpawner.Instance?.allCars == null)
                return true;

            var profile = CouplerProfiles.Get(expectedType);
            if (profile == null)
                return false;

            int expectedHooks = 0;
            int actualHooks = 0;
            int totalCouplers = 0;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                // Check front coupler
                if (car.frontCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    totalCouplers++;
                    var frontChainScript = car.frontCoupler.visualCoupler.chainAdapter.chainScript;
                    
                    if (!HookManager.ShouldDisableCoupler(car.frontCoupler))
                    {
                        expectedHooks++;
                        if (HookManager.GetPivot(frontChainScript) != null)
                            actualHooks++;
                    }
                }

                // Check rear coupler
                if (car.rearCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    totalCouplers++;
                    var rearChainScript = car.rearCoupler.visualCoupler.chainAdapter.chainScript;
                    
                    if (!HookManager.ShouldDisableCoupler(car.rearCoupler))
                    {
                        expectedHooks++;
                        if (HookManager.GetPivot(rearChainScript) != null)
                            actualHooks++;
                    }
                }
            }

            Main.DebugLog(() => $"Recreation validation: {actualHooks}/{expectedHooks} expected hooks created ({totalCouplers} total couplers)");
            
            // Allow some tolerance - if we got at least 80% of expected hooks, consider it successful
            return expectedHooks == 0 || (actualHooks >= (expectedHooks * 0.8f));
        }
    }
}
