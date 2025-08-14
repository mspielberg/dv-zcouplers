using System.Collections;
using System.Collections.Generic;

using DV;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles deferred application of coupler states after save loading
    /// </summary>
    public static class DeferredStateApplicator
    {
        /// <summary>
        /// Start the deferred application process for coupler states
        /// </summary>
        public static void StartDeferredCouplerApplication(Dictionary<TrainCar, (bool frontLocked, bool rearLocked)> statesToApply)
        {
            if (statesToApply.Count > 0)
            {
                Main.DebugLog(() => $"Starting deferred application of {statesToApply.Count} coupler states");

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
                    deferredApplier.Initialize(statesToApply);
                }
                else
                {
                    Main.DebugLog(() => "Could not create host for deferred application - applying immediately");
                    // Fallback: apply immediately if we can't create a host
                    ApplyStatesImmediately(statesToApply);
                }
            }
        }

        /// <summary>
        /// Apply coupler states immediately as a fallback
        /// </summary>
        private static void ApplyStatesImmediately(Dictionary<TrainCar, (bool frontLocked, bool rearLocked)> statesToApply)
        {
            Main.DebugLog(() => "Applying pending coupler states immediately as fallback");

            foreach (var kvp in statesToApply)
            {
                var car = kvp.Key;
                var (frontLocked, rearLocked) = kvp.Value;

                if (car != null && car.gameObject != null)
                {
                    try
                    {
                        CouplerStateManager.ApplyCouplerState(car.frontCoupler, frontLocked);
                        CouplerStateManager.ApplyCouplerState(car.rearCoupler, rearLocked);
                        // Removed verbose state application log
                    }
                    catch (System.Exception ex)
                    {
                        Main.ErrorLog(() => $"Error applying coupler states for car {car.ID}: {ex.Message}");
                    }
                }
            }

            // Removed verbose completion log
        }

        /// <summary>
        /// Component to handle deferred coupler state application
        /// </summary>
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
                // Wait longer for the native save system to restore coupler states
                yield return new WaitForSeconds(3.0f);

                // Additional wait for physics frames to ensure everything is stable
                for (int i = 0; i < 30; i++)
                {
                    yield return new WaitForFixedUpdate();
                }

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
                                CouplerStateManager.ApplyCouplerState(car.frontCoupler, frontLocked);
                                CouplerStateManager.ApplyCouplerState(car.rearCoupler, rearLocked);

                                // Removed verbose state application log
                            }
                            catch (System.Exception ex)
                            {
                                Main.ErrorLog(() => $"Error applying coupler states for car {car.ID}: {ex.Message}");
                            }
                        }
                    }

                    // Synchronize all coupling pair states after individual applications
                    yield return new WaitForSeconds(0.5f);
                    CouplerStateManager.SynchronizeAllCouplingStates();

                    // Second pass: ensure all coupled cars have proper joints
                    yield return new WaitForSeconds(1.0f);

                    foreach (var kvp in statesToApply)
                    {
                        var car = kvp.Key;
                        if (car != null && car.gameObject != null)
                        {
                            try
                            {
                                // Ensure tension joints exist for coupled cars
                                if (car.frontCoupler?.IsCoupled() == true)
                                {
                                    Couplers.ForceCreateTensionJoint(car.frontCoupler);
                                }
                                if (car.rearCoupler?.IsCoupled() == true)
                                {
                                    Couplers.ForceCreateTensionJoint(car.rearCoupler);
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Main.ErrorLog(() => $"Error creating joints for car {car.ID}: {ex.Message}");
                            }
                        }
                    }

                    // Final validation to catch any missed joints
                    yield return new WaitForSeconds(1.0f);
                    CouplerStateManager.ValidateAllJointsAfterLoading();
                }

                // Removed verbose completion log

                // Self-destruct after completion
                Destroy(this);
            }
        }
    }
}