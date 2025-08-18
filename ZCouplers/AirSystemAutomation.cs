using System;
using System.Reflection;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Utilities for automatically connecting/disconnecting air systems in Full Automatic Mode.
    /// Kept conservative and reflection-based to avoid tight coupling to DV internals.
    /// </summary>
    internal static class AirSystemAutomation
    {
        /// <summary>
        /// Try to disconnect air hoses and close angle cocks on both couplers.
        /// Safe to call even if already disconnected or parts are missing.
        /// </summary>
        public static void TryAutoDisconnect(Coupler a, Coupler b)
        {
            if (a == null || b == null)
                return;

            if (!Main.settings.EffectiveFullAutomaticMode)
                return;

            try
            {
                var h1 = a.hoseAndCock;
                var h2 = b.hoseAndCock;

                // If neither side has hoses, nothing to do.
                if (h1 == null && h2 == null)
                    return;

                // Determine if either side reports a connection
                bool connected = false;
                try { connected |= (h1 != null && h1.IsHoseConnected); } catch { }
                try { connected |= (h2 != null && h2.IsHoseConnected); } catch { }

                // Attempt to invoke known/likely disconnection APIs via reflection.
                // Try methods on Coupler first (common in DV builds)
                if (connected)
                {
                    if (!InvokeMethodIfExists(a, "DisconnectAirHose", new object[] { b, true }) &&
                        !InvokeMethodIfExists(a, "DisconnectAirHose", new object[] { true }) &&
                        !InvokeMethodIfExists(a, "DisconnectAirHose", Array.Empty<object>()))
                    {
                        // Try on the other coupler too
                        if (!InvokeMethodIfExists(b, "DisconnectAirHose", new object[] { a, true }) &&
                            !InvokeMethodIfExists(b, "DisconnectAirHose", new object[] { true }) &&
                            !InvokeMethodIfExists(b, "DisconnectAirHose", Array.Empty<object>()))
                        {
                            // Try hose object fallbacks
                            InvokeMethodIfExists(h1, "DisconnectHose", Array.Empty<object>());
                            InvokeMethodIfExists(h2, "DisconnectHose", Array.Empty<object>());
                            InvokeMethodIfExists(h1, "Disconnect", Array.Empty<object>());
                            InvokeMethodIfExists(h2, "Disconnect", Array.Empty<object>());
                        }
                    }
                }

                // Close both angle cocks when decoupling
                try { if (a.IsCockOpen) a.IsCockOpen = false; } catch { }
                try { if (b.IsCockOpen) b.IsCockOpen = false; } catch { }

                Main.DebugLog(() => $"Auto-disconnected air systems between {a.train.ID} and {b.train.ID}");
            }
            catch (Exception ex)
            {
                Main.ErrorLog(() => $"Error in TryAutoDisconnect: {ex.Message}");
            }
        }

        private static bool InvokeMethodIfExists(object target, string methodName, object[] args)
        {
            if (target == null)
                return false;
            try
            {
                var t = target.GetType();
                var types = GetArgTypes(args);
                var mi = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, types, null)
                         ?? t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null)
                    return false;
                mi.Invoke(target, mi.GetParameters().Length == args.Length ? args : null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Type[] GetArgTypes(object[] args)
        {
            if (args == null || args.Length == 0)
                return Type.EmptyTypes;
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                types[i] = args[i]?.GetType() ?? typeof(object);
            return types;
        }
    }
}
