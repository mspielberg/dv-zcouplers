using System;
using DvMod.ZCouplers.Core;
using HarmonyLib;
using System.Linq;
using System.Reflection;

namespace DvMod.ZCouplers
{
	public static class Bootstrap
	{
		public static void Initialize()
		{
			// Apply all Harmony patches in this assembly with detailed error handling
			try
			{
				var harmony = new Harmony("DvMod.ZCouplers.MP");

				// Get all types in this assembly that have HarmonyPatch attributes
				var assembly = typeof(Bootstrap).Assembly;
				var patchedTypes = assembly.GetTypes();

				Main.DebugLog(() => $"[MP] Found {patchedTypes.Length} types in ZCouplers.MP assembly");

				foreach (var type in patchedTypes)
				{
					var harmonyAttributes = type.GetCustomAttributes<HarmonyPatch>();
					if (harmonyAttributes.Any())
					{
						try
						{
							Main.DebugLog(() => $"[MP] Attempting to patch type: {type.Name}");
							harmony.CreateClassProcessor(type).Patch();
							Main.DebugLog(() => $"[MP] Successfully patched: {type.Name}");
						}
						catch (Exception ex)
						{
							Main.ErrorLog(() => $"[MP] Failed to patch {type.Name}: {ex.Message}");
							Main.ErrorLog(() => $"[MP] Stack trace: {ex.StackTrace}");
						}
					}
				}

				Main.DebugLog(() => "[MP] Completed Harmony patch application");
			}
			catch (Exception ex)
			{
				Main.ErrorLog(() => "[MP] Failed to apply Harmony patches for ZCouplers.MP");
				Main.ErrorLog(() => $"[MP] Failed to apply Harmony patches for ZCouplers.MP: {ex.Message}");
				Main.ErrorLog(() => $"[MP] Stack trace: {ex.StackTrace}");
			}

			// Wire core shim flags from MP state
			MultiplayerIntegration.Initialize();
		}
	}
}
