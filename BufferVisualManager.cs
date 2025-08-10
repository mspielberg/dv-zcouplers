using System;
using System.Linq;
using DV;
using DV.ThingTypes;
using DV.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DvMod.ZCouplers;

public static class BufferVisualManager
{
	public static void ToggleBuffers(bool visible)
	{
		Main.DebugLog(() => "Toggling buffer visibility " + (visible ? "on" : "off"));
		foreach (TrainCarLivery livery in Globals.G.Types.Liveries)
		{
			ToggleBuffers(livery.prefab, livery, visible);
		}
		if (SingletonBehaviour<CarSpawner>.Instance == null)
		{
			return;
		}
		foreach (TrainCar allCar in SingletonBehaviour<CarSpawner>.Instance.allCars)
		{
			ToggleBuffers(allCar.gameObject, allCar.carLivery, visible);
		}
		ForceGlobalRenderingUpdate();
	}

	private static void ToggleBuffers(GameObject root, TrainCarLivery livery, bool visible)
	{
		Transform transform = root.transform.Find("[buffers]");
		if (transform != null)
		{
			ToggleBufferVisuals(transform, livery, visible);
		}
		else
		{
			Main.DebugLog(() => "Using fallback buffer method for " + livery.id + " (no [buffers] hierarchy found)");
			MeshRenderer[] componentsInChildren = root.GetComponentsInChildren<MeshRenderer>();
			int num = 0;
			MeshRenderer[] array = componentsInChildren;
			foreach (MeshRenderer renderer in array)
			{
				if (!IsZCouplersObject(renderer.transform) && (renderer.name.StartsWith("Buffer_") || renderer.name.Replace("_", "").ToLowerInvariant().Contains("bufferstem")))
				{
					renderer.enabled = visible;
					ForceRendererUpdate(renderer);
					num++;
					Main.DebugLog(() => "Fallback toggled: " + renderer.name + " on " + livery.id);
				}
			}
			if (num == 0)
			{
				Main.DebugLog(() => "No buffer elements found via fallback method for " + livery.id);
			}
		}
		ToggleSpecialLocoBufferStems(root, livery, visible);
	}

	private static void ToggleBufferVisuals(Transform buffers, TrainCarLivery livery, bool visible)
	{
		int toggledVisuals = 0;
		MeshRenderer[] componentsInChildren = buffers.GetComponentsInChildren<MeshRenderer>();
		foreach (MeshRenderer renderer in componentsInChildren)
		{
			if (!IsZCouplersObject(renderer.transform) && !(renderer.name == "BuffersAndChainRig") && (renderer.name.StartsWith("CabooseExteriorBufferStems") || renderer.name.StartsWith("Buffer_") || renderer.name.Replace("_", "").ToLowerInvariant().Contains("bufferstem")))
			{
				renderer.enabled = visible;
				ForceRendererUpdate(renderer);
				int num = toggledVisuals;
				toggledVisuals = num + 1;
				Main.DebugLog(() => "Toggled buffer visual: " + renderer.name + " on " + livery.id);
			}
		}
		if (toggledVisuals > 0)
		{
			Main.DebugLog(() => $"Toggled {toggledVisuals} buffer visual elements on {livery.id}");
		}
	}

	private static bool IsZCouplersObject(Transform transform)
	{
		Transform transform2 = transform;
		while (transform2 != null)
		{
			string name = transform2.name;
			if (name.StartsWith("ZCouplers pivot") || name == "hook" || (name == "walkable" && transform2.parent != null && transform2.parent.name == "hook"))
			{
				return true;
			}
			transform2 = transform2.parent;
		}
		return false;
	}

	private static void ToggleSpecialLocoBufferStems(GameObject root, TrainCarLivery livery, bool visible)
	{
		Transform transform = null;
		string stemName = "";
		string id = livery.id;
		if (id == null)
		{
			return;
		}
		switch (id.Length)
		{
		case 9:
			switch (id[8])
			{
			default:
				return;
			case 'A':
				if (!(id == "LocoS282A"))
				{
					return;
				}
				transform = root.transform.Find("LocoS282A_Body/Static_LOD0/s282_buffer_stems");
				stemName = "s282_buffer_stems";
				break;
			case 'B':
			{
				if (!(id == "LocoS282B"))
				{
					return;
				}
				transform = root.transform.Find("LocoS282B_Body/LOD0/s282_tender_buffer_stems");
				stemName = "s282_tender_buffer_stems";
				Transform transform2 = root.transform.Find("LocoS282B_Body/LOD1/s282_tender_buffer_stems_LOD1");
				if (transform2 != null)
				{
					MeshRenderer component = transform2.GetComponent<MeshRenderer>();
					if (component != null)
					{
						component.enabled = visible;
						ForceRendererUpdate(component);
						Main.DebugLog(() => $"Toggled s282_tender_buffer_stems_LOD1 on {livery.id} to {visible}");
					}
				}
				Transform transform3 = root.transform.Find("[colliders]/LocoS282B_Body/LOD0/s282_tender_buffer_stems");
				MeshRenderer[] array;
				SkinnedMeshRenderer[] array2;
				if (transform3 != null)
				{
					Main.DebugLog(() => "Found s282_tender_buffer_stems in colliders hierarchy on " + livery.id);
					MeshRenderer component2 = transform3.GetComponent<MeshRenderer>();
					if (component2 != null)
					{
						component2.enabled = visible;
						ForceRendererUpdate(component2);
						Main.DebugLog(() => $"Toggled s282_tender_buffer_stems MeshRenderer in colliders on {livery.id} to {visible}");
					}
					SkinnedMeshRenderer component3 = transform3.GetComponent<SkinnedMeshRenderer>();
					if (component3 != null)
					{
						component3.enabled = visible;
						ForceRendererUpdate(component3);
						Main.DebugLog(() => $"Toggled s282_tender_buffer_stems SkinnedMeshRenderer in colliders on {livery.id} to {visible}");
					}
					MeshRenderer[] componentsInChildren = transform3.GetComponentsInChildren<MeshRenderer>();
					SkinnedMeshRenderer[] componentsInChildren2 = transform3.GetComponentsInChildren<SkinnedMeshRenderer>();
					array = componentsInChildren;
					foreach (MeshRenderer childRenderer in array)
					{
						if (childRenderer.transform != transform3)
						{
							childRenderer.enabled = visible;
							ForceRendererUpdate(childRenderer);
							Main.DebugLog(() => $"Toggled child MeshRenderer {childRenderer.name} of s282_tender_buffer_stems in colliders on {livery.id} to {visible}");
						}
					}
					array2 = componentsInChildren2;
					foreach (SkinnedMeshRenderer childRenderer2 in array2)
					{
						if (childRenderer2.transform != transform3)
						{
							childRenderer2.enabled = visible;
							ForceRendererUpdate(childRenderer2);
							Main.DebugLog(() => $"Toggled child SkinnedMeshRenderer {childRenderer2.name} of s282_tender_buffer_stems in colliders on {livery.id} to {visible}");
						}
					}
				}
				Transform transform4 = root.transform.Find("[colliders]/LocoS282B_Body/LOD1/s282_tender_buffer_stems_LOD1");
				if (!(transform4 != null))
				{
					break;
				}
				MeshRenderer component4 = transform4.GetComponent<MeshRenderer>();
				if (component4 != null)
				{
					component4.enabled = visible;
					ForceRendererUpdate(component4);
					Main.DebugLog(() => $"Toggled s282_tender_buffer_stems_LOD1 in colliders on {livery.id} to {visible}");
				}
				SkinnedMeshRenderer component5 = transform4.GetComponent<SkinnedMeshRenderer>();
				if (component5 != null)
				{
					component5.enabled = visible;
					ForceRendererUpdate(component5);
					Main.DebugLog(() => $"Toggled s282_tender_buffer_stems_LOD1 SkinnedMeshRenderer in colliders on {livery.id} to {visible}");
				}
				MeshRenderer[] componentsInChildren3 = transform4.GetComponentsInChildren<MeshRenderer>();
				SkinnedMeshRenderer[] componentsInChildren4 = transform4.GetComponentsInChildren<SkinnedMeshRenderer>();
				array = componentsInChildren3;
				foreach (MeshRenderer childRenderer3 in array)
				{
					if (childRenderer3.transform != transform4)
					{
						childRenderer3.enabled = visible;
						ForceRendererUpdate(childRenderer3);
						Main.DebugLog(() => $"Toggled child MeshRenderer {childRenderer3.name} of s282_tender_buffer_stems_LOD1 in colliders on {livery.id} to {visible}");
					}
				}
				array2 = componentsInChildren4;
				foreach (SkinnedMeshRenderer childRenderer4 in array2)
				{
					if (childRenderer4.transform != transform4)
					{
						childRenderer4.enabled = visible;
						ForceRendererUpdate(childRenderer4);
						Main.DebugLog(() => $"Toggled child SkinnedMeshRenderer {childRenderer4.name} of s282_tender_buffer_stems_LOD1 in colliders on {livery.id} to {visible}");
					}
				}
				break;
			}
			}
			break;
		case 8:
			switch (id[4])
			{
			default:
				return;
			case 'S':
				if (!(id == "LocoS060"))
				{
					return;
				}
				transform = root.transform.Find("LocoS060_Body/Static/s060_buffer_stems");
				stemName = "s060_buffer_stems";
				break;
			case 'D':
				if (!(id == "LocoDM1U"))
				{
					return;
				}
				transform = root.transform.Find("LocoDM1U_Body/buffer_stems");
				stemName = "buffer_stems";
				break;
			}
			break;
		case 7:
			switch (id[6])
			{
			default:
				return;
			case '3':
				if (!(id == "LocoDM3"))
				{
					return;
				}
				transform = root.transform.Find("LocoDM3_Body/buffer_stems");
				stemName = "buffer_stems";
				break;
			case '4':
				if (!(id == "LocoDH4"))
				{
					return;
				}
				transform = root.transform.Find("LocoDH4_Body/dh4_buffer_stems");
				stemName = "dh4_buffer_stems";
				break;
			case '6':
				if (!(id == "LocoDE6"))
				{
					return;
				}
				transform = root.transform.Find("LocoDE6_Body/BufferStems");
				stemName = "BufferStems";
				break;
			case '2':
				if (!(id == "LocoDE2"))
				{
					return;
				}
				transform = root.transform.Find("LocoDE2_Body/BufferStems");
				stemName = "BufferStems";
				break;
			case '5':
				return;
			}
			break;
		case 16:
			if (!(id == "LocoMicroshunter"))
			{
				return;
			}
			transform = root.transform.Find("LocoMicroshunter_Body/microshunter_buffer_stems");
			stemName = "microshunter_buffer_stems";
			break;
		case 11:
			if (!(id == "LocoDE6Slug"))
			{
				return;
			}
			transform = root.transform.Find("LocoDE6Slug_Body/de6_slug_buffer_stems");
			stemName = "de6_slug_buffer_stems";
			break;
		default:
			return;
		}
		if (transform != null)
		{
			Main.DebugLog(() => "Found " + stemName + " transform on " + livery.id);
			MeshRenderer component6 = transform.GetComponent<MeshRenderer>();
			if (component6 != null)
			{
				component6.enabled = visible;
				ForceRendererUpdate(component6);
				Main.DebugLog(() => $"Toggled {stemName} MeshRenderer on {livery.id} to {visible}");
			}
			SkinnedMeshRenderer component7 = transform.GetComponent<SkinnedMeshRenderer>();
			if (component7 != null)
			{
				component7.enabled = visible;
				ForceRendererUpdate(component7);
				Main.DebugLog(() => $"Toggled {stemName} SkinnedMeshRenderer on {livery.id} to {visible}");
			}
			MeshRenderer[] componentsInChildren5 = transform.GetComponentsInChildren<MeshRenderer>();
			SkinnedMeshRenderer[] componentsInChildren6 = transform.GetComponentsInChildren<SkinnedMeshRenderer>();
			MeshRenderer[] array = componentsInChildren5;
			foreach (MeshRenderer childRenderer5 in array)
			{
				if (childRenderer5.transform != transform)
				{
					childRenderer5.enabled = visible;
					ForceRendererUpdate(childRenderer5);
					Main.DebugLog(() => $"Toggled child MeshRenderer {childRenderer5.name} of {stemName} on {livery.id} to {visible}");
				}
			}
			SkinnedMeshRenderer[] array2 = componentsInChildren6;
			foreach (SkinnedMeshRenderer childRenderer6 in array2)
			{
				if (childRenderer6.transform != transform)
				{
					childRenderer6.enabled = visible;
					ForceRendererUpdate(childRenderer6);
					Main.DebugLog(() => $"Toggled child SkinnedMeshRenderer {childRenderer6.name} of {stemName} on {livery.id} to {visible}");
				}
			}
			Component[] allComponents = transform.GetComponents<Component>();
			Main.DebugLog(() => "Components on " + stemName + ": " + string.Join(", ", allComponents.Select((Component c) => c.GetType().Name)));
			if (component6 == null && component7 == null && componentsInChildren5.Length == 0 && componentsInChildren6.Length == 0)
			{
				Main.DebugLog(() => "No renderers found on " + stemName + " for " + livery.id);
			}
		}
		else
		{
			Main.DebugLog(() => "Could not find " + stemName + " buffer stems on " + livery.id);
		}
	}

	private static void ForceRendererUpdate(Renderer renderer)
	{
		if (renderer == null)
		{
			return;
		}
		try
		{
			bool enabled = renderer.enabled;
			renderer.enabled = false;
			renderer.enabled = enabled;
			renderer.transform.hasChanged = true;
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Main.DebugLog(() => "Error in ForceRendererUpdate: " + ex3.Message);
		}
	}

	private static void ForceGlobalRenderingUpdate()
	{
		try
		{
			SceneManager.GetActiveScene();
			Camera main = Camera.main;
			if (main != null)
			{
				main.Render();
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Main.DebugLog(() => "Error in ForceGlobalRenderingUpdate: " + ex3.Message);
		}
	}
}
