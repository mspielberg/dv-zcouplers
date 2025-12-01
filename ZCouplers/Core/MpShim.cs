using System;
using System.IO;
using System.Reflection;

namespace DvMod.ZCouplers.Core
{
	/// <summary>
	/// Lightweight runtime bridge between core and the optional Multiplayer integration.
	/// No compile-time references to the MP API.
	/// </summary>
	public static class MpShim
	{
		// Set by the optional MP assembly at runtime
		public static bool IsClientActive { get; private set; }
		public static bool ClientAllowsJointOps { get; private set; }

		public static void SetIsClientActive(bool active) => IsClientActive = active;
		public static void SetClientAllowsJointOps(bool allowed) => ClientAllowsJointOps = allowed;
		private static MethodInfo? couplerToggleRequest;

		/// <summary>
		/// Try to initialize the optional MP integration by loading ZCouplers.MP.dll
		/// only when MultiplayerAPI is present and loaded.
		/// </summary>
		public static void TryInitialize(UnityModManagerNet.UnityModManager.ModEntry mod)
		{
			try
			{
				mod?.Logger.Log("Checking for MultiplayerAPI and optional MP plugin...");
				var mpApiType = Type.GetType("MPAPI.MultiplayerAPI, MultiplayerAPI", throwOnError: false);
				if (mpApiType == null)
				{
					mod?.Logger.Log("MultiplayerAPI not found. Skipping optional MP integration.");
					return; // MP not installed
				}

				// Check IsMultiplayerLoaded == true
				var prop = mpApiType.GetProperty("IsMultiplayerLoaded", BindingFlags.Public | BindingFlags.Static);
				if (prop == null || !(prop.GetValue(null) is bool isLoaded) || !isLoaded)
				{
					mod?.Logger.Log("MultiplayerAPI present but not loaded. Skipping optional MP integration.");
					return; // MP not loaded
				}

				// Load companion assembly
				if (mod == null) return;
				var mpDll = Path.Combine(mod.Path, "ZCouplers.MP.dll");
				if (!File.Exists(mpDll))
				{
					mod?.Logger.Log($"Optional MP plugin not found at: {mpDll}. Skipping MP integration.");
					return; // companion not present
				}

				var asm = Assembly.LoadFrom(mpDll);
				var bootstrap = asm.GetType("DvMod.ZCouplers.Bootstrap", throwOnError: true);
				var init = bootstrap?.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
				init?.Invoke(null, null);
				mod?.Logger.Log("Multiplayer detected. Optional MP plugin loaded successfully.");
				var type = Type.GetType("DvMod.ZCouplers.MultiplayerIntegration, ZCouplers.MP", throwOnError: true);
				couplerToggleRequest = type?.GetMethod("SendCouplerToggleRequest", BindingFlags.Public | BindingFlags.Static);
			}
			catch (Exception ex)
			{
				mod?.Logger.Log($"Optional MP initialization failed (continuing without MP): {ex.Message}");
				mod?.Logger.Log($"Stack trace: {ex.StackTrace}");
			}
		}

		/// <summary>
		/// Try to route a coupler lock/unlock request to the MP integration if present.
		/// </summary>
		public static void TrySendCouplerToggleRequest(Coupler coupler, bool locked)
		{
			couplerToggleRequest?.Invoke(null, [coupler, locked]);
		}
	}
}
