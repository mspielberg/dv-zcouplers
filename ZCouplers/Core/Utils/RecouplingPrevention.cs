using System;
using System.Collections;
using System.Collections.Generic;
using DvMod.ZCouplers.Core.Helpers;
using UnityEngine;

namespace DvMod.ZCouplers.Core.Utils
{
    /// <summary>
    /// Tracks recently uncoupled coupler pairs to prevent immediate recoupling
    /// until they've achieved minimum separation distance.
    /// </summary>
    public static class RecouplingPrevention
    {
        /// <summary>
        /// Tracks coupler pairs that recently uncoupled and their uncoupling positions.
        /// </summary>
        private static readonly Dictionary<CouplerPair, UncouplingRecord> recentlyUncoupled =
            new Dictionary<CouplerPair, UncouplingRecord>();

        /// <summary>
        /// Tracks coupler pairs that are currently blocked from recoupling.
        /// </summary>
        public static readonly HashSet<CouplerPair> blockedPairs = new HashSet<CouplerPair>();

        /// <summary>
        /// Coupler pairs that hit above the safe coupling speed and are still in contact.
        /// </summary>
        private static readonly HashSet<CouplerPair> overspeedBlocked = new HashSet<CouplerPair>();

        /// <summary>
        /// Reference to the running distance check coroutine.
        /// </summary>
        private static Coroutine? distanceCheckCoroutine;

        /// <summary>
        /// Flag to track if the coroutine is actively running.
        /// </summary>
        private static bool isCoroutineRunning;

        /// <summary>
        /// Record of an uncoupling event.
        /// </summary>
        private struct UncouplingRecord
        {
            public float InitialDistance;

            public UncouplingRecord(float initialDistance)
            {
                InitialDistance = initialDistance;
            }
        }

        /// <summary>
        /// Represents a pair of couplers (order-independent).
        /// </summary>
        public struct CouplerPair
        {
            private readonly Coupler coupler1;
            private readonly Coupler coupler2;

            public CouplerPair(Coupler c1, Coupler c2)
            {
                // Ensure consistent ordering for dictionary key
                if (c1.GetHashCode() < c2.GetHashCode())
                {
                    coupler1 = c1;
                    coupler2 = c2;
                }
                else
                {
                    coupler1 = c2;
                    coupler2 = c1;
                }
            }

            public Coupler GetOther(Coupler coupler)
            {
                return coupler == coupler1 ? coupler2 : coupler1;
            }

            public Coupler GetCoupler1() => coupler1;
            public Coupler GetCoupler2() => coupler2;

            public override bool Equals(object obj)
            {
                return obj is CouplerPair other &&
                       coupler1 == other.coupler1 &&
                       coupler2 == other.coupler2;
            }

            public override int GetHashCode()
            {
                return coupler1.GetHashCode() ^ coupler2.GetHashCode();
            }
        }

        /// <summary>
        /// Initialize the recoupling prevention system with a MonoBehaviour for running coroutines.
        /// Called lazily when first needed.
        /// </summary>
        public static void EnsureInitialized()
        {
	        if (isCoroutineRunning)
	        {
		        return; // Already running
	        }
	        
	        if (CoroutineManager.Instance == null)
	        {
		        Main.ErrorLog(() => "CoroutineManager.Instance is null, cannot start distance check coroutine");
		        return;
	        }
	        
	        try
	        {
		        distanceCheckCoroutine = CoroutineManager.Instance.StartCoroutine(DistanceCheckCoroutine());
		        isCoroutineRunning = true;
		        Main.DebugLog(() => "Distance check coroutine started successfully");
	        }
	        catch (Exception ex)
	        {
		        Main.ErrorLog(() => $"Failed to start distance check coroutine: {ex.Message}");
		        isCoroutineRunning = false;
	        }
        }

        /// <summary>
        /// Stop the distance checking coroutine and clean up.
        /// </summary>
        public static void Shutdown()
        {
	        try
	        {
		        CoroutineManager.Instance.StopCoroutine(distanceCheckCoroutine);
		        distanceCheckCoroutine = null;
	        }
	        catch (Exception e)
	        {
		        Main.ErrorLog((() => "Error stopping distance check coroutine: " + e.Message));
	        }
	        finally
	        {
		        isCoroutineRunning = false;
		        recentlyUncoupled.Clear();
		        blockedPairs.Clear();
		        overspeedBlocked.Clear();
	        }
        }

        /// <summary>
        /// Check if the coroutine is still running and restart if needed.
        /// Called from RecordUncoupling to ensure the coroutine is alive.
        /// </summary>
        private static void EnsureCoroutineHealth()
        {
            if (!isCoroutineRunning && recentlyUncoupled.Count > 0)
            {
                Main.ErrorLog(() => "Detected stopped coroutine with active records, restarting...");
                EnsureInitialized();
            }
        }

        /// <summary>
        /// Coroutine that checks distances every second and updates blocked pairs.
        /// </summary>
        private static IEnumerator DistanceCheckCoroutine()
        {
            try
            {
                Main.DebugLog(() => "Distance check coroutine loop started");
                while (true)
                {
                    yield return new WaitForSeconds(1f);
                    UpdateBlockedPairs();
                }
            }
            finally
            {
                // Always clear the running flag when coroutine exits
                isCoroutineRunning = false;
                Main.DebugLog(() => "Distance check coroutine stopped, flag cleared");
            }
        }

        /// <summary>
        /// Update the blocked pairs based on current distances.
        /// </summary>
        public static void UpdateBlockedPairs()
        {
            try
            {
                if (recentlyUncoupled.Count == 0)
                    return; // No need to log if there's nothing to check

                var toRemove = new List<CouplerPair>();
                int checkedCount = 0;

                foreach (var kvp in recentlyUncoupled)
                {
                    var pair = kvp.Key;
                    var record = kvp.Value;

                    try
                    {
                        var coupler1 = pair.GetCoupler1();
                        var coupler2 = pair.GetCoupler2();

                        if (coupler1 == null ||
                            coupler2 == null ||
                            coupler1.train == null ||
                            coupler2.train == null ||
                            coupler1.train.ID == "" ||
                            coupler2.train.ID == "")
                        {
                            toRemove.Add(pair);
                            Main.DebugLog(() => "Coupler pair has null coupler/train, removing");
                            continue;
                        }

                        // Derailed cars can't separate on their own; keep the pair blocked until rerailed
                        if (coupler1.train.derailed || coupler2.train.derailed)
                        {
                            blockedPairs.Add(pair);
                            continue;
                        }
                        checkedCount++;

                        // Calculate current separation distance between the couplers
                        var currentDistance = Vector3.Distance(coupler1.transform.position, coupler2.transform.position);

                        Main.DebugLog(() => $"{coupler1.train.ID} <-> {coupler2.train.ID}: current={currentDistance:F3}m, required={Main.settings.minimumSeparationDistance:F3}m");

                        // Check if couplers are far enough apart (absolute distance check)
                        if (currentDistance >= Main.settings.minimumSeparationDistance)
                        {
                            // Remove from both tracking dictionaries
                            toRemove.Add(pair);
                            blockedPairs.Remove(pair);
                            Main.DebugLog(() => $"Separation requirement met: {coupler1.train.ID} <-> {coupler2.train.ID}, distance: {currentDistance:F3}m");
                        }
                        else
                        {
                            // Still blocked - couplers are too close
                            blockedPairs.Add(pair);
                        }
                    }
                    catch (Exception ex)
                    {
                        // If there's any issue accessing the couplers, remove the record
                        toRemove.Add(pair);
                        blockedPairs.Remove(pair);
                        Main.ErrorLog(() => $"Exception accessing coupler pair: {ex.Message}\n{ex.StackTrace}");
                    }
                }

                // Remove records that are no longer needed
                foreach (var pair in toRemove)
                {
                    recentlyUncoupled.Remove(pair);
                }

                if (checkedCount > 0)
                    Main.DebugLog(() => $"Checked {checkedCount} of {recentlyUncoupled.Count + toRemove.Count} uncoupled pair(s)");
            }
            catch (Exception ex)
            {
                Main.ErrorLog(() => $"Critical error in UpdateBlockedPairs: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Record that two couplers have just uncoupled.
        /// </summary>
        public static void RecordUncoupling(Coupler coupler1, Coupler coupler2)
        {
            if (coupler1 == null || coupler2 == null)
                return;

            var pair = new CouplerPair(coupler1, coupler2);
            var initialDistance = Vector3.Distance(coupler1.transform.position, coupler2.transform.position);
            var record = new UncouplingRecord(initialDistance);

            recentlyUncoupled[pair] = record;
            blockedPairs.Add(pair); // Initially blocked

            Main.DebugLog(() => $"Recorded uncoupling: {coupler1.train.ID} <-> {coupler2.train.ID} at distance {initialDistance:F3}m");

            // Ensure coroutine is running
            EnsureInitialized();
            EnsureCoroutineHealth();
        }

        /// <summary>
        /// Check if two couplers are allowed to recouple.
        /// </summary>
        public static bool CanRecouple(Coupler coupler1, Coupler coupler2)
        {
            if (coupler1 == null || coupler2 == null)
                return true;

            // Fallback: if the coroutine died but there are still records, restart it
            if (!isCoroutineRunning && recentlyUncoupled.Count > 0)
            {
                Main.ErrorLog(() => "CanRecouple: detected dead coroutine with active records, attempting restart");
                EnsureInitialized();
            }

            var pair = new CouplerPair(coupler1, coupler2);
            return !blockedPairs.Contains(pair);
        }

        /// <summary>
        /// Relative speed between the two couplers' cars in m/s.
        /// </summary>
        public static float GetClosingSpeed(Coupler coupler1, Coupler coupler2)
        {
            var velocity1 = coupler1.train.GetComponent<Rigidbody>()?.velocity ?? Vector3.zero;
            var velocity2 = coupler2.train.GetComponent<Rigidbody>()?.velocity ?? Vector3.zero;
            return (velocity1 - velocity2).magnitude;
        }

        /// <summary>
        /// Check if the couplers latch on this impact. impactSpeed is the closing speed sampled before contact;
        /// the higher of it and the current closing speed is used. An impact above the profile's safe coupling
        /// speed keeps the pair blocked until ReleaseOverspeedBlock is called once contact is released.
        /// </summary>
        public static bool CanLatch(Coupler coupler1, Coupler coupler2, float impactSpeed)
        {
            if (coupler1 == null || coupler2 == null)
                return true;

            var pair = new CouplerPair(coupler1, coupler2);
            if (overspeedBlocked.Contains(pair))
                return false;

            var safeSpeed = Main.settings.GetSafeCouplingSpeed();
            var closingSpeed = Mathf.Max(impactSpeed, GetClosingSpeed(coupler1, coupler2));
            if (closingSpeed <= safeSpeed)
                return true;

            overspeedBlocked.Add(pair);
            Main.DebugLog(() => $"Coupling denied, impact too fast: {coupler1.train.ID} <-> {coupler2.train.ID} at {closingSpeed:F1} m/s (safe {safeSpeed:F1} m/s)");
            return false;
        }

        /// <summary>
        /// Allow the coupler to couple again after an over-speed impact. Called once it stops pressing against another coupler.
        /// </summary>
        public static void ReleaseOverspeedBlock(Coupler coupler)
        {
            if (coupler == null || overspeedBlocked.Count == 0)
                return;

            if (overspeedBlocked.RemoveWhere(pair => pair.GetCoupler1() == coupler || pair.GetCoupler2() == coupler) > 0)
                Main.DebugLog(() => $"Contact released after over-speed impact: {coupler.train.ID} {coupler.Position()}");
        }

        /// <summary>
        /// Remove all blocking/recoupling records for a specific coupler.
        /// Called when the user manually readies a coupler (state change to ready),
        /// indicating intent to recouple.
        /// </summary>
        public static void RemoveBlockingFor(Coupler coupler)
        {
            if (coupler == null)
                return;

            var toRemove = new List<CouplerPair>();

            foreach (var pair in recentlyUncoupled.Keys)
            {
                try
                {
                    var c1 = pair.GetCoupler1();
                    var c2 = pair.GetCoupler2();
                    if (c1 == coupler || c2 == coupler)
                    {
                        toRemove.Add(pair);
                    }
                }
                catch
                {
                    toRemove.Add(pair);
                }
            }

            foreach (var key in toRemove)
            {
                recentlyUncoupled.Remove(key);
                blockedPairs.Remove(key);
            }

            if (toRemove.Count > 0)
            {
                Main.DebugLog(() => $"Removed {toRemove.Count} recoupling block(s) for coupler {coupler.train.ID} (manual state change)");
            }
        }

        /// <summary>
        /// Clean up uncoupling records for a specific coupler when it's being destroyed.
        /// </summary>
        public static void CleanupOldRecords(Coupler coupler)
        {
            if (coupler == null)
                return;

            Main.DebugLog(() => $"CleanupOldRecords called for coupler {coupler.train.ID}, {recentlyUncoupled.Count} record(s) in recentlyUncoupled");

            var toRemove = new List<CouplerPair>();

            foreach (var pair in recentlyUncoupled.Keys)
            {
                try
                {
                    var c1 = pair.GetCoupler1();
                    var c2 = pair.GetCoupler2();
                    var other = pair.GetOther(coupler);

                    Main.DebugLog(() => $"CleanupOldRecords: evaluating pair {c1?.train?.ID} <-> {c2?.train?.ID}, coupler={coupler.train.ID}, GetOther={other?.train?.ID}, other==coupler:{other == coupler}");

                    // Check if this pair involves the specified coupler
                    if (c1 == coupler || c2 == coupler)
                    {
                        toRemove.Add(pair);
                        Main.DebugLog(() => $"CleanupOldRecords: marked pair for removal (coupler is member)");
                    }
                    else if (other == null)
                    {
                        toRemove.Add(pair);
                        Main.DebugLog(() => $"CleanupOldRecords: marked pair for removal (GetOther returned null)");
                    }
                }
                catch
                {
                    // If there's any issue accessing the pair, mark it for removal
                    toRemove.Add(pair);
                    Main.DebugLog(() => "CleanupOldRecords: marked pair for removal (exception)");
                }
            }

            foreach (var key in toRemove)
            {
                recentlyUncoupled.Remove(key);
                blockedPairs.Remove(key); // Also remove from blocked pairs
            }
            overspeedBlocked.RemoveWhere(pair => pair.GetCoupler1() == coupler || pair.GetCoupler2() == coupler);

            Main.DebugLog(() => $"CleanupOldRecords: removed {toRemove.Count} of {toRemove.Count + recentlyUncoupled.Count} record(s) for coupler {coupler.train.ID}");
        }

        /// <summary>
        /// Clear all uncoupling records.
        /// Called during save loading to prevent stale records from persisting.
        /// </summary>
        public static void ClearAllRecords()
        {
            int recordCount = recentlyUncoupled.Count;
            recentlyUncoupled.Clear();
            blockedPairs.Clear();
            overspeedBlocked.Clear();

            if (recordCount > 0)
            {
                Main.DebugLog(() => $"Cleared all {recordCount} uncoupling/blocking records");
            }
        }
    }
}
