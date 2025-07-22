using HarmonyLib;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DvMod.ZCouplers
{
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
        private const float SaveLoadGracePeriod = 5.0f; // 5 seconds after first car loaded

        [HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.GetCarSaveData))]
        public static class GetCarSaveDataPatch
        {
            public static void Postfix(TrainCar car, JObject __result)
            {
                try
                {
                    // Only save coupler data if knuckle couplers are enabled
                    if (KnuckleCouplers.enabled && car?.frontCoupler != null && car?.rearCoupler != null)
                    {
                        __result[SaveKey] = new JObject(
                            new JProperty(FrontCouplerLockedKey, KnuckleCouplers.IsReadyToCouple(car.frontCoupler)),
                            new JProperty(RearCouplerLockedKey, KnuckleCouplers.IsReadyToCouple(car.rearCoupler)));
                        
                        Main.DebugLog(() => $"Saved coupler states for car {car.ID}");
                    }
                }
                catch (System.Exception ex)
                {
                    Main.DebugLog(() => $"Error saving coupler data for car {car?.ID}: {ex.Message}");
                    // Don't add any save data if there's an error
                }
            }
        }

        [HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.InstantiateCarFromSavegame))]
        public static class InstantiateCarPatch
        {
            public static void Postfix(JObject carData, RailTrack[] tracks, TrainCar __result)
            {
                try
                {
                    // Only set loading state on the first car to avoid constantly resetting it
                    if (pendingCouplerStates.Count == 0)
                    {
                        isLoadingFromSave = true;
                        saveLoadStartTime = Time.time;
                        Main.DebugLog(() => "Started save loading process");
                    }
                    
                    // Store coupler states for deferred application
                    if (carData?.TryGetValue(SaveKey, out var data) == true && data is JObject obj)
                    {
                        bool frontLocked = obj.TryGetValue(FrontCouplerLockedKey, out var frontToken) && frontToken.Value<bool>();
                        bool rearLocked = obj.TryGetValue(RearCouplerLockedKey, out var rearToken) && rearToken.Value<bool>();
                        
                        pendingCouplerStates[__result] = (frontLocked, rearLocked);
                        Main.DebugLog(() => $"Stored pending coupler states for car {__result.ID}: front={frontLocked}, rear={rearLocked}");
                    }
                    else
                    {
                        // No save data - use defaults (unlocked for knuckle couplers)
                        if (KnuckleCouplers.enabled)
                        {
                            pendingCouplerStates[__result] = (false, false);
                            Main.DebugLog(() => $"No coupler save data for car {__result.ID}, will use defaults");
                        }
                    }
                    
                    // Start deferred application only for the first car
                    if (pendingCouplerStates.Count == 1)
                    {
                        __result.StartCoroutine(TriggerDeferredApplicationCoroutine());
                    }
                }
                catch (System.Exception ex)
                {
                    Main.DebugLog(() => $"Error processing coupler save data for car {__result.ID}: {ex.Message}");
                    // Use safe defaults
                    if (KnuckleCouplers.enabled)
                        pendingCouplerStates[__result] = (false, false);
                }
            }
        }
        
        private static IEnumerator TriggerDeferredApplicationCoroutine()
        {
            // Wait a bit for all cars to be loaded
            yield return new WaitForSeconds(2.0f);
            
            // Reset the loading state to allow normal joint creation
            isLoadingFromSave = false;
            Main.DebugLog(() => "Save loading period ended - normal joint creation restored");
            
            // Trigger deferred application
            StartDeferredCouplerApplication();
        }
        
        private static void ApplyPendingStatesImmediately()
        {
            Main.DebugLog(() => "Applying pending coupler states immediately as fallback");
            
            foreach (var kvp in pendingCouplerStates)
            {
                var car = kvp.Key;
                var (frontLocked, rearLocked) = kvp.Value;
                
                if (car != null && car.gameObject != null)
                {
                    try
                    {
                        ApplyCouplerState(car.frontCoupler, frontLocked);
                        ApplyCouplerState(car.rearCoupler, rearLocked);
                        Main.DebugLog(() => $"Applied coupler states for car {car.ID}: front={frontLocked}, rear={rearLocked}");
                    }
                    catch (System.Exception ex)
                    {
                        Main.DebugLog(() => $"Error applying coupler states for car {car.ID}: {ex.Message}");
                    }
                }
            }
            
            pendingCouplerStates.Clear();
            isLoadingFromSave = false;
            Main.DebugLog(() => "Immediate application completed - normal joint creation restored");
        }
        
        // Simple deferred application system using MonoBehaviour
        public static void StartDeferredCouplerApplication()
        {
            if (pendingCouplerStates.Count > 0)
            {
                Main.DebugLog(() => $"Starting deferred application of {pendingCouplerStates.Count} coupler states");
                
                // Try multiple GameObject names to find a suitable host
                GameObject? host = null;
                string[] possibleHosts = { "GameManager", "WorldMover", "Player", "PlayerManager", "CarSpawner" };
                
                foreach (string hostName in possibleHosts)
                {
                    host = GameObject.Find(hostName);
                    if (host != null)
                    {
                        Main.DebugLog(() => $"Found host GameObject: {hostName}");
                        break;
                    }
                }
                
                // If we can't find any specific GameObject, create our own
                if (host == null)
                {
                    host = new GameObject("ZCouplers_DeferredApplier");
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    Main.DebugLog(() => "Created dedicated host GameObject for deferred application");
                }
                
                if (host != null)
                {
                    var deferredApplier = host.AddComponent<DeferredCouplerApplier>();
                    deferredApplier.Initialize(pendingCouplerStates);
                    pendingCouplerStates.Clear();
                }
                else
                {
                    Main.DebugLog(() => "Could not create host for deferred application - applying immediately");
                    // Fallback: apply immediately if we can't create a host
                    ApplyPendingStatesImmediately();
                }
            }
        }
        
        private static void ApplyCouplerState(Coupler coupler, bool locked)
        {
            if (coupler == null)
                return;
                
            Main.DebugLog(() => $"Applying coupler state for {coupler.train.ID} {coupler.Position()}: locked={locked}, currently coupled={coupler.IsCoupled()}");
                
            if (locked)
            {
                // If should be locked but not currently coupled, this indicates a problem
                if (!coupler.IsCoupled())
                {
                    Main.DebugLog(() => $"WARNING: {coupler.train.ID} {coupler.Position()} should be locked but is not coupled - save state inconsistency");
                }
                // Use visual state update only - don't trigger actual coupling/uncoupling during save loading
                KnuckleCouplers.UpdateCouplerVisualState(coupler, locked: true);
            }
            else
            {
                // Update visual state to show unlocked without triggering actual uncoupling
                KnuckleCouplers.UpdateCouplerVisualState(coupler, locked: false);
                Main.DebugLog(() => $"Set coupler {coupler.train.ID} {coupler.Position()} visual state to unlocked");
            }
        }
        
        // Prevent joint creation during save loading
        public static bool IsLoadingFromSave => isLoadingFromSave && (Time.time - saveLoadStartTime) < SaveLoadGracePeriod;
        
        // Clear pending states for manually uncoupled cars
        public static void ClearPendingStatesForCar(TrainCar car)
        {
            if (pendingCouplerStates.ContainsKey(car))
            {
                pendingCouplerStates.Remove(car);
                Main.DebugLog(() => $"Cleared pending coupler states for manually uncoupled car {car.ID}");
            }
        }
        
        private static void CreateMissingTensionJoints(Coupler coupler)
        {
            if (coupler == null || !coupler.IsCoupled() || coupler.coupledTo == null)
                return;
                
            // Check if tension joint already exists
            if (Couplers.HasTensionJoint(coupler))
            {
                Main.DebugLog(() => $"Tension joint already exists for {coupler.train.ID} {coupler.Position()}");
                return;
            }
            
            // Create tension joint using the Couplers system
            Main.DebugLog(() => $"Creating missing tension joint for {coupler.train.ID} {coupler.Position()} -> {coupler.coupledTo.train.ID} {coupler.coupledTo.Position()}");
            
            try
            {
                Couplers.ForceCreateTensionJoint(coupler);
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error creating tension joint for {coupler.train.ID}: {ex.Message}");
            }
        }

        // Component to handle deferred coupler state application

        public class DeferredCouplerApplier : MonoBehaviour
        {
            private Dictionary<TrainCar, (bool frontLocked, bool rearLocked)>? statesToApply;
            
            public void Initialize(Dictionary<TrainCar, (bool frontLocked, bool rearLocked)> states)
            {
                statesToApply = new Dictionary<TrainCar, (bool, bool)>(states);
                StartCoroutine(ApplyStatesAfterDelay());
            }
            
            private IEnumerator ApplyStatesAfterDelay()
            {
                // Wait for physics to stabilize
                yield return new WaitForSeconds(3.0f);
                
                Main.DebugLog(() => "Applying deferred coupler states after physics stabilization");
                
                if (statesToApply != null)
                {
                    foreach (var kvp in statesToApply)
                    {
                        var car = kvp.Key;
                        var (frontLocked, rearLocked) = kvp.Value;
                        
                        if (car != null && car.gameObject != null)
                        {
                            try
                            {
                                SaveManager.ApplyCouplerState(car.frontCoupler, frontLocked);
                                SaveManager.ApplyCouplerState(car.rearCoupler, rearLocked);
                                
                                // Create missing tension joints for coupled cars
                                SaveManager.CreateMissingTensionJoints(car.frontCoupler);
                                SaveManager.CreateMissingTensionJoints(car.rearCoupler);
                                
                                Main.DebugLog(() => $"Applied coupler states for car {car.ID}: front={frontLocked}, rear={rearLocked}");
                            }
                            catch (System.Exception ex)
                            {
                                Main.DebugLog(() => $"Error applying coupler states for car {car.ID}: {ex.Message}");
                            }
                        }
                    }
                }
                
                Main.DebugLog(() => "Finished applying all deferred coupler states");
                
                // Ensure loading state is reset
                SaveManager.isLoadingFromSave = false;
                Main.DebugLog(() => "Deferred application completed - normal joint creation restored");
                
                // Self-destruct after completion
                Destroy(this);
            }
        }
    }
}
