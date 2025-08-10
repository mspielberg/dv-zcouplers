using System;
using HarmonyLib;
using UnityModManagerNet;

namespace DvMod.ZCouplers;

[EnableReloading]
public static class Main
{
	public static UnityModManager.ModEntry? mod;

	public static Settings settings = new Settings();

	public static bool Load(UnityModManager.ModEntry modEntry)
	{
		mod = modEntry;
		try
		{
			Settings settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
			if (settings != null)
			{
				Main.settings = settings;
				modEntry.Logger.Log("Loaded existing settings");
			}
			else
			{
				Main.settings = new Settings();
				modEntry.Logger.Log("Created new settings (no existing file)");
			}
		}
		catch (Exception ex)
		{
			Main.settings = new Settings();
			modEntry.Logger.Log("Failed to load settings, using defaults: " + ex.Message);
		}
		modEntry.OnGUI = Main.settings.Draw<Settings>;
		modEntry.OnSaveGUI = Main.settings.Save;
		AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
		{
			if (e.ExceptionObject is Exception ex2)
			{
				string? stackTrace = ex2.StackTrace;
				if (stackTrace != null && stackTrace.Contains("LateUpdate_Attached"))
				{
					modEntry.Logger.Log("Caught LateUpdate_Attached exception: " + ex2.Message);
				}
			}
		};
		new Harmony(modEntry.Info.Id).PatchAll();
		KnuckleCouplers.Initialize();
		mod.Logger.Log($"Loaded {Main.settings.couplerType}");
		return true;
	}

	public static void DebugLog(TrainCar car, Func<string> message)
	{
		if (car == PlayerManager.Car)
		{
			DebugLog(message);
		}
	}

	public static void DebugLog(Func<string> message)
	{
		if (settings.enableLogging)
		{
			mod?.Logger.Log(message());
		}
	}

	public static void ErrorLog(Func<string> message)
	{
		mod?.Logger.Log(message());
	}

	public static string GetSafeCarID(TrainCar car)
	{
		if (car?.logicCar == null)
		{
			return "[uninit]";
		}
		return car.ID;
	}
}
