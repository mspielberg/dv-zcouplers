using System.Runtime.CompilerServices;
using DvMod.ZCouplers.Core;
using HarmonyLib;

namespace DvMod.ZCouplers.Patches
{
    /// <summary>
    /// Suspends coupling scanners while their car is derailed and resumes them on rerail.
    /// Without this, scanners keep pairing with neighbours and the master coroutine retries coupling every tick.
    /// </summary>
    public static class DerailScannerGuard
    {
        private static readonly ConditionalWeakTable<CouplingScanner, TrainCar> scannerCars = new();

        private static TrainCar? GetCar(CouplingScanner scanner)
        {
            if (scannerCars.TryGetValue(scanner, out var car) && car != null)
                return car;
            car = TrainCar.Resolve(scanner.gameObject);
            if (car != null)
            {
                scannerCars.Remove(scanner);
                scannerCars.Add(scanner, car);
            }
            return car;
        }

        public static bool IsDerailed(CouplingScanner? scanner)
        {
            if (scanner == null)
                return false;
            var car = GetCar(scanner);
            return car != null && car.derailed;
        }

        private static void OnDerailed(TrainCar car)
        {
            Main.DebugLog(() => $"{car.ID} derailed: suspending coupling scanners");
            Suspend(CouplingScannerPatches.GetScanner(car.frontCoupler));
            Suspend(CouplingScannerPatches.GetScanner(car.rearCoupler));
        }

        private static void OnRerailed(TrainCar car)
        {
            Main.DebugLog(() => $"{car.ID} rerailed: resuming coupling scanners");
            Resume(CouplingScannerPatches.GetScanner(car.frontCoupler));
            Resume(CouplingScannerPatches.GetScanner(car.rearCoupler));
        }

        private static void Suspend(CouplingScanner? scanner)
        {
            if (scanner == null)
                return;
            try
            {
                // Unpair kills the master coroutine on both sides and restarts their scan loops,
                // which the DoScan/Pair guards below keep idle until the car is rerailed.
                if (scanner.nearbyScanner != null)
                    scanner.Unpair(true);
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error suspending coupling scanner: {ex.Message}");
            }
        }

        private static void Resume(CouplingScanner? scanner)
        {
            if (scanner == null)
                return;
            try
            {
                if (scanner.nearbyScanner == null && scanner.scanCoro == null && scanner.isActiveAndEnabled)
                    scanner.StartScanning();
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error resuming coupling scanner: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(TrainCar), "Awake")]
        public static class TrainCarAwakePatch
        {
            public static void Postfix(TrainCar __instance)
            {
                var car = __instance;
                car.OnDerailed += OnDerailed;
                car.OnRerailed += () => OnRerailed(car);
            }
        }

        [HarmonyPatch(typeof(CouplingScanner), nameof(CouplingScanner.DoScan))]
        public static class DoScanPatch
        {
            public static bool Prefix(CouplingScanner __instance)
            {
                return !IsDerailed(__instance);
            }
        }

        /// <summary>
        /// Also covers a railed car's scanner raycasting into a derailed neighbour.
        /// </summary>
        [HarmonyPatch(typeof(CouplingScanner), nameof(CouplingScanner.Pair))]
        public static class PairPatch
        {
            public static bool Prefix(CouplingScanner __instance, CouplingScanner other)
            {
                return !IsDerailed(__instance) && !IsDerailed(other);
            }
        }
    }
}
