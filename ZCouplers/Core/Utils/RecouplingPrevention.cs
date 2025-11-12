using System.Collections;
using System.Collections.Generic;
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
        private static readonly HashSet<CouplerPair> blockedPairs = new HashSet<CouplerPair>();

        /// <summary>
        /// Reference to the running distance check coroutine.
        /// </summary>
        private static Coroutine? distanceCheckCoroutine;

        /// <summary>
        /// MonoBehaviour instance for running coroutines.
        /// </summary>
        private static MonoBehaviour? coroutineRunner;

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
        private struct CouplerPair
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
        private static void EnsureInitialized()
        {
            // Check if we need to (re)initialize
            bool needsInit = coroutineRunner == null || distanceCheckCoroutine == null;

            // Also check if the coroutine runner object is destroyed/invalid
            if (coroutineRunner != null)
            {
                try
                {
                    // Try to access the gameObject - if it's destroyed, this will throw
                    var _ = coroutineRunner.gameObject;
                }
                catch
                {
                    Main.DebugLog(() => "Coroutine runner is invalid, reinitializing");
                    coroutineRunner = null;
                    distanceCheckCoroutine = null;
                    needsInit = true;
                }
            }

            if (needsInit)
            {
                // Stop old coroutine if it exists
                if (coroutineRunner != null && distanceCheckCoroutine != null)
                {
                    try
                    {
                        coroutineRunner.StopCoroutine(distanceCheckCoroutine);
                    }
                    catch { }
                    distanceCheckCoroutine = null;
                }

                // Find any available MonoBehaviour to run our coroutine
                coroutineRunner = UnityEngine.Object.FindObjectOfType<CouplingScanner>();
                if (coroutineRunner == null)
                {
                    coroutineRunner = UnityEngine.Object.FindObjectOfType<CarSpawner>();
                }
                if (coroutineRunner == null)
                {
                    coroutineRunner = UnityEngine.Object.FindObjectOfType<MonoBehaviour>();
                }

                if (coroutineRunner != null)
                {
                    distanceCheckCoroutine = coroutineRunner.StartCoroutine(DistanceCheckCoroutine());
                    Main.DebugLog(() => "Coroutine started successfully");
                }
                else
                {
                    Main.ErrorLog(() => "Failed to find MonoBehaviour for coroutine runner!");
                }
            }
        }

        /// <summary>
        /// Stop the distance checking coroutine and clean up.
        /// </summary>
        public static void Shutdown()
        {
            if (coroutineRunner != null && distanceCheckCoroutine != null)
            {
                coroutineRunner.StopCoroutine(distanceCheckCoroutine);
                distanceCheckCoroutine = null;
            }
            recentlyUncoupled.Clear();
            blockedPairs.Clear();
            coroutineRunner = null;
        }

        /// <summary>
        /// Coroutine that checks distances every second and updates blocked pairs.
        /// </summary>
        private static IEnumerator DistanceCheckCoroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(1f);
                UpdateBlockedPairs();
            }
        }

        /// <summary>
        /// Update the blocked pairs based on current distances.
        /// </summary>
        private static void UpdateBlockedPairs()
        {
            if (recentlyUncoupled.Count == 0)
                return; // No need to log if there's nothing to check

            Main.DebugLog(() => $"Checking {recentlyUncoupled.Count} uncoupled pair(s)");

            var toRemove = new List<CouplerPair>();

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
                        coupler1.train.ID == "" ||
                        coupler2.train.ID == "")
                    {
                        toRemove.Add(pair);
                        Main.DebugLog(() => "Coupler pair has null coupler/train, removing");
                        continue;
                    }

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
                catch
                {
                    // If there's any issue accessing the couplers, remove the record
                    toRemove.Add(pair);
                    blockedPairs.Remove(pair);
                    Main.DebugLog(() => "Exception accessing coupler pair, removing");
                }
            }

            // Remove records that are no longer needed
            foreach (var pair in toRemove)
            {
                recentlyUncoupled.Remove(pair);
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
        }

        /// <summary>
        /// Check if two couplers are allowed to recouple.
        /// </summary>
        public static bool CanRecouple(Coupler coupler1, Coupler coupler2)
        {
            if (coupler1 == null || coupler2 == null)
                return true;

            var pair = new CouplerPair(coupler1, coupler2);
            return !blockedPairs.Contains(pair);
        }

        /// <summary>
        /// Clean up uncoupling records for a specific coupler when it's being destroyed.
        /// </summary>
        public static void CleanupOldRecords(Coupler coupler)
        {
            if (coupler == null)
                return;

            var toRemove = new List<CouplerPair>();

            foreach (var pair in recentlyUncoupled.Keys)
            {
                try
                {
                    // Check if this pair involves the specified coupler
                    var other = pair.GetOther(coupler);
                    if (other == coupler || other == null) // Either it's the same coupler or the pair involves this coupler
                    {
                        toRemove.Add(pair);
                    }
                }
                catch
                {
                    // If there's any issue accessing the pair, mark it for removal
                    toRemove.Add(pair);
                }
            }

            foreach (var key in toRemove)
            {
                recentlyUncoupled.Remove(key);
                blockedPairs.Remove(key); // Also remove from blocked pairs
            }

            if (toRemove.Count > 0)
            {
                Main.DebugLog(() => $"Cleaned up {toRemove.Count} uncoupling records for coupler {coupler.train.ID}");
            }
        }

        /// <summary>
        /// Clear all blocking records for a specific coupler.
        /// This is useful when the coupler is reset to Parked state.
        /// </summary>
        public static void ClearRecordsForCoupler(Coupler coupler)
        {
            if (coupler == null)
                return;

            var toRemove = new List<CouplerPair>();

            foreach (var pair in recentlyUncoupled.Keys)
            {
                try
                {
                    if (pair.GetCoupler1() == coupler || pair.GetCoupler2() == coupler)
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
                Main.DebugLog(() => $"Cleared {toRemove.Count} blocking records for coupler {coupler.train.ID}");
            }
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

            if (recordCount > 0)
            {
                Main.DebugLog(() => $"Cleared all {recordCount} uncoupling/blocking records");
            }
        }
    }
}
