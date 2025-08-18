using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Utilities for automatically connecting/disconnecting air systems in Full Automatic Mode.
    /// Kept conservative and reflection-based to avoid tight coupling to DV internals.
    /// </summary>
    internal static class AirSystemAutomation
    {
        // --- DV MU integration (reflected) ---
        private static Type? muModuleType;
        private static MethodInfo? muConnectMethod; // ConnectCablesOfConnectedCouplersIfMultipleUnitSupported(Coupler, Coupler)
        private static MethodInfo? muDisconnectMethod; // DisconnectCablesIfMultipleUnitSupported(TrainCar, bool, bool)

        private static bool EnsureMuApi()
        {
            if (muModuleType != null)
                return true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("DV.MultipleUnit.MultipleUnitModule", throwOnError: false);
                    if (t != null)
                    {
                        muModuleType = t;
                        muConnectMethod = t.GetMethod("ConnectCablesOfConnectedCouplersIfMultipleUnitSupported", BindingFlags.Public | BindingFlags.Static);
                        muDisconnectMethod = t.GetMethod("DisconnectCablesIfMultipleUnitSupported", BindingFlags.Public | BindingFlags.Static);
                        Main.DebugLog(() => $"MU API found: {t.Assembly.GetName().Name}");
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Disconnect air hoses using the game's Coupler API and close angle cocks on both couplers.
        /// </summary>
        public static void TryAutoDisconnect(Coupler a, Coupler b)
        {
            if (a == null || b == null)
                return;

            if (!Main.settings.EffectiveFullAutomaticMode)
                return;

            try
            {
                // Use game's native disconnect; call on both sides to be explicit
                try { a.DisconnectAirHose(true); Main.DebugLog(() => "Invoked Coupler.DisconnectAirHose(bool) for Air (A)"); } catch { }
                try { b.DisconnectAirHose(true); Main.DebugLog(() => "Invoked Coupler.DisconnectAirHose(bool) for Air (B)"); } catch { }

                // Close both angle cocks when decoupling
                try { if (a.IsCockOpen) a.IsCockOpen = false; } catch { }
                try { if (b.IsCockOpen) b.IsCockOpen = false; } catch { }

                // Also disconnect MU/control cables if present
                TryAutoDisconnectMU(a, b);

                Main.DebugLog(() => $"Auto-disconnected air (and MU if present) between {a.train.ID} and {b.train.ID}");
            }
            catch (Exception ex)
            {
                Main.ErrorLog(() => $"Error in TryAutoDisconnect: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to connect MU/control cables after coupling in Full Automatic Mode.
        /// </summary>
        public static void TryAutoConnectMU(Coupler a, Coupler b)
        {
            if (a == null || b == null)
                return;
            if (!Main.settings.EffectiveFullAutomaticMode)
                return;
            try
            {
                Main.DebugLog(() => $"Trying MU auto-connect: {a.train.ID} {a.Position()} <-> {b.train.ID} {b.Position()}");

                // Prefer official DV MU API if available
                if (EnsureMuApi() && muConnectMethod != null)
                {
                    try
                    {
                        muConnectMethod.Invoke(null, new object[] { a, b });
                        Main.DebugLog(() => "Invoked MultipleUnitModule.ConnectCablesOfConnectedCouplersIfMultipleUnitSupported(Coupler, Coupler) for MU");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Main.ErrorLog(() => $"MU connect via MultipleUnitModule failed: {ex.Message}");
                    }
                }
                Main.DebugLog(() => "MU auto-connect: MultipleUnitModule API not found; skipping per no-fallback policy");
            }
            catch (Exception ex)
            {
                Main.ErrorLog(() => $"Error in TryAutoConnectMU: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to disconnect MU/control cables after decoupling in Full Automatic Mode.
        /// </summary>
        public static void TryAutoDisconnectMU(Coupler a, Coupler b)
        {
            if (a == null || b == null)
                return;
            if (!Main.settings.EffectiveFullAutomaticMode)
                return;
            try
            {
                Main.DebugLog(() => $"Trying MU auto-disconnect: {a.train.ID} {a.Position()} <-> {b.train.ID} {b.Position()}");
                // Prefer official DV MU API; no fallbacks
                if (EnsureMuApi() && muDisconnectMethod != null)
                {
                    try
                    {
                        var dfA = a.isFrontCoupler;
                        var drA = !a.isFrontCoupler;
                        var dfB = b.isFrontCoupler;
                        var drB = !b.isFrontCoupler;
                        muDisconnectMethod.Invoke(null, new object[] { a.train, dfA, drA });
                        muDisconnectMethod.Invoke(null, new object[] { b.train, dfB, drB });
                        Main.DebugLog(() => "Invoked MultipleUnitModule.DisconnectCablesIfMultipleUnitSupported(TrainCar, bool, bool) for MU");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Main.ErrorLog(() => $"MU disconnect via MultipleUnitModule failed: {ex.Message}");
                    }
                }
                else
                {
                    Main.DebugLog(() => "MU auto-disconnect: MultipleUnitModule API not found; skipping per no-fallback policy");
                }
            }
            catch (Exception ex)
            {
                Main.ErrorLog(() => $"Error in TryAutoDisconnectMU: {ex.Message}");
            }
        }
        // Helpers below kept minimal for MU API discovery only
        private static object? SafeGet(PropertyInfo p, object target)
        {
            try { return p.GetValue(target); } catch { return null; }
        }
    }
}