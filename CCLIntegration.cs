/*
using DVCustomCarLoader;
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.ZCouplers
{
    public static class CCLIntegration
    {
        private static UnityModManager.ModEntry? _cclMod;
        private static UnityModManager.ModEntry? CCLMod
        {
            get
            {
                return _cclMod ??= UnityModManager.FindMod("DVCustomCarLoader");
            }
        }

        public static void Initialize()
        {
            if (!(CCLMod?.Active ?? false))
                return;
            if (KnuckleCouplers.enabled)
                Internal.HideBuffers();
        }

        private static class Internal
        {
            public static void HideBuffers()
            {
                foreach (var customCar in CustomCarManager.CustomCarTypes)
                {
                    var buffersRootObj = customCar.CarPrefab.transform.Find("[ZCouplers buffers]")?.gameObject;
                    if (buffersRootObj == null)
                    {
                        Main.DebugLog(() => $"No [ZCouplers buffers] transform in {customCar.identifier}.");
                        continue;
                    }

                    if (buffersRootObj.activeSelf)
                        buffersRootObj.SetActive(false);

                    var buffersRoot = customCar.CarPrefab.transform.Find("[buffers]");
                    foreach (var child in buffersRoot.GetComponentsInChildren<Transform>())
                    {
                        if (child.name.StartsWith("buffer anchor "))
                            child.gameObject.SetActive(false);
                    }

                    Main.DebugLog(() => "Disabled [ZCouplers buffers] transform.");
                }
            }
        }
    }
}
*/