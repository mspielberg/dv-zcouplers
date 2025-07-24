using DV;
using DV.CabControls;
using DV.CabControls.Spec;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.ThingTypes;
using UnityEngine;

namespace DvMod.ZCouplers
{
    public static class KnuckleCouplers
    {
        public static readonly bool enabled = true; // Always enabled since BufferAndChain is removed
        private static readonly GameObject? hookPrefab;

        static KnuckleCouplers()
        {
            if (!enabled)
            {
                // don't even try to load assets if knuckle couplers are disabled
                return;
            }
            var bundleStream = typeof(KnuckleCouplers).Assembly.GetManifestResourceStream(typeof(Main), "ZCouplers.assetbundle");
            var bundle = AssetBundle.LoadFromStream(bundleStream);
            CouplerType couplerType = Main.settings.couplerType;
            hookPrefab = bundle.LoadAsset<GameObject>(couplerType.ToString());
            bundle.Unload(false);
            ToggleBuffers(Main.settings.showBuffersWithKnuckles);
        }

        public static void ToggleBuffers(bool visible)
        {
            Main.DebugLog(() => $"Toggling buffer visibility {(visible ? "on" : "off")}");
            // Modify prefabs for any new cars that are instantiated.
            foreach (TrainCarLivery livery in Globals.G.Types.Liveries) 
                ToggleBuffers(livery.prefab, livery, visible);
            // Modify existing cars so the setting can update in real-time.
            if (CarSpawner.Instance == null) return;
            foreach (TrainCar car in CarSpawner.Instance.allCars)
                ToggleBuffers(car.gameObject, car.carLivery, visible);
            
            // Ensure all existing trains have knuckle couplers
            EnsureKnuckleCouplersForAllTrains();
            
            // Force rendering system update to ensure changes are visible immediately
            ForceGlobalRenderingUpdate();
        }

        private static void ToggleBuffers(GameObject root, TrainCarLivery livery, bool visible)
        {
            // Handle modern [buffers] hierarchy
            Transform buffers = root.transform.Find("[buffers]");
            if (buffers != null)
            {
                ToggleBufferVisuals(buffers, livery, visible);
            }
            else
            {
                // Fallback for cars without [buffers] hierarchy
                Main.DebugLog(() => $"Using fallback buffer method for {livery.id} (no [buffers] hierarchy found)");
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
                int fallbackToggles = 0;
                
                foreach (MeshRenderer renderer in renderers)
                {
                    if (IsZCouplersObject(renderer.transform))
                        continue;
                    
                    if (renderer.name.StartsWith("Buffer_") || renderer.name.Replace("_", "").ToLowerInvariant().Contains("bufferstem"))
                    {
                        renderer.enabled = visible;
                        ForceRendererUpdate(renderer);
                        fallbackToggles++;
                        Main.DebugLog(() => $"Fallback toggled: {renderer.name} on {livery.id}");
                    }
                }
                
                if (fallbackToggles == 0)
                {
                    Main.DebugLog(() => $"No buffer elements found via fallback method for {livery.id}");
                }
            }

            ToggleSpecialLocoBufferStems(root, livery, visible);
        }

        private static void ToggleBufferVisuals(Transform buffers, TrainCarLivery livery, bool visible)
        {
            int toggledVisuals = 0;
            MeshRenderer[] allRenderers = buffers.GetComponentsInChildren<MeshRenderer>();
            
            foreach (MeshRenderer renderer in allRenderers)
            {
                if (IsZCouplersObject(renderer.transform))
                    continue;
                
                // Preserve coupling mechanism
                if (renderer.name == "BuffersAndChainRig")
                    continue;
                
                // Hide buffer visual elements
                if (renderer.name.StartsWith("CabooseExteriorBufferStems") || 
                    renderer.name.StartsWith("Buffer_") || 
                    renderer.name.Replace("_", "").ToLowerInvariant().Contains("bufferstem"))
                {
                    renderer.enabled = visible;
                    ForceRendererUpdate(renderer);
                    toggledVisuals++;
                    Main.DebugLog(() => $"Toggled buffer visual: {renderer.name} on {livery.id}");
                }
            }

            if (toggledVisuals > 0)
            {
                Main.DebugLog(() => $"Toggled {toggledVisuals} buffer visual elements on {livery.id}");
            }
        }

        private static bool IsZCouplersObject(Transform transform)
        {
            Transform current = transform;
            while (current != null)
            {
                string name = current.name;
                
                if (name.StartsWith("ZCouplers pivot") || 
                    name == "hook" || 
                    (name == "walkable" && current.parent != null && current.parent.name == "hook"))
                {
                    return true;
                }
                
                current = current.parent;
            }
            return false;
        }

        private static void ToggleSpecialLocoBufferStems(GameObject root, TrainCarLivery livery, bool visible)
        {
            Transform bufferStemsTransform = null;
            string stemName = "";

            switch (livery.id)
            {
                case "LocoS282A":
                    bufferStemsTransform = root.transform.Find("LocoS282A_Body/Static_LOD0/s282_buffer_stems");
                    stemName = "s282_buffer_stems";
                    break;

                case "LocoS282B":
                    bufferStemsTransform = root.transform.Find("LocoS282B_Body/LOD0/s282_tender_buffer_stems");
                    stemName = "s282_tender_buffer_stems";
                    
                    // Handle LOD1 version
                    Transform lod1Transform = root.transform.Find("LocoS282B_Body/LOD1/s282_tender_buffer_stems_LOD1");
                    if (lod1Transform != null)
                    {
                        MeshRenderer lod1Renderer = lod1Transform.GetComponent<MeshRenderer>();
                        if (lod1Renderer != null)
                        {
                            lod1Renderer.enabled = visible;
                            ForceRendererUpdate(lod1Renderer);
                            Main.DebugLog(() => $"Toggled s282_tender_buffer_stems_LOD1 on {livery.id} to {visible}");
                        }
                    }
                    break;

                case "LocoS060":
                    bufferStemsTransform = root.transform.Find("LocoS060_Body/Static/s060_buffer_stems");
                    stemName = "s060_buffer_stems";
                    break;

                case "LocoMicroshunter":
                    bufferStemsTransform = root.transform.Find("LocoMicroshunter_Body/microshunter_buffer_stems");
                    stemName = "microshunter_buffer_stems";
                    break;

                default:
                    return;
            }

            if (bufferStemsTransform != null)
            {
                Main.DebugLog(() => $"Found {stemName} transform on {livery.id}");
                
                MeshRenderer meshRenderer = bufferStemsTransform.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = visible;
                    ForceRendererUpdate(meshRenderer);
                    Main.DebugLog(() => $"Toggled {stemName} MeshRenderer on {livery.id} to {visible}");
                }
                
                SkinnedMeshRenderer skinnedRenderer = bufferStemsTransform.GetComponent<SkinnedMeshRenderer>();
                if (skinnedRenderer != null)
                {
                    skinnedRenderer.enabled = visible;
                    ForceRendererUpdate(skinnedRenderer);
                    Main.DebugLog(() => $"Toggled {stemName} SkinnedMeshRenderer on {livery.id} to {visible}");
                }
                
                // Handle child renderers
                MeshRenderer[] childMeshRenderers = bufferStemsTransform.GetComponentsInChildren<MeshRenderer>();
                SkinnedMeshRenderer[] childSkinnedRenderers = bufferStemsTransform.GetComponentsInChildren<SkinnedMeshRenderer>();
                
                foreach (MeshRenderer childRenderer in childMeshRenderers)
                {
                    if (childRenderer.transform != bufferStemsTransform)
                    {
                        childRenderer.enabled = visible;
                        ForceRendererUpdate(childRenderer);
                        Main.DebugLog(() => $"Toggled child MeshRenderer {childRenderer.name} of {stemName} on {livery.id} to {visible}");
                    }
                }
                
                foreach (SkinnedMeshRenderer childRenderer in childSkinnedRenderers)
                {
                    if (childRenderer.transform != bufferStemsTransform)
                    {
                        childRenderer.enabled = visible;
                        ForceRendererUpdate(childRenderer);
                        Main.DebugLog(() => $"Toggled child SkinnedMeshRenderer {childRenderer.name} of {stemName} on {livery.id} to {visible}");
                    }
                }
                
                Component[] allComponents = bufferStemsTransform.GetComponents<Component>();
                Main.DebugLog(() => $"Components on {stemName}: {string.Join(", ", allComponents.Select(c => c.GetType().Name))}");
                
                if (meshRenderer == null && skinnedRenderer == null && childMeshRenderers.Length == 0 && childSkinnedRenderers.Length == 0)
                {
                    Main.DebugLog(() => $"No renderers found on {stemName} for {livery.id}");
                }
            }
            else
            {
                Main.DebugLog(() => $"Could not find {stemName} buffer stems on {livery.id}");
            }
        }

        private static string GetTransformPath(Transform transform, Transform root)
        {
            if (transform == root)
                return root.name;
            
            var path = transform.name;
            var current = transform.parent;
            
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            
            return root.name + "/" + path;
        }

        private static void ForceRendererUpdate(Renderer renderer)
        {
            if (renderer == null) return;
            
            try
            {
                bool currentState = renderer.enabled;
                renderer.enabled = false;
                renderer.enabled = currentState;
                renderer.transform.hasChanged = true;
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error in ForceRendererUpdate: {ex.Message}");
            }
        }

        private static void ForceGlobalRenderingUpdate()
        {
            try
            {
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    mainCamera.Render();
                }
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error in ForceGlobalRenderingUpdate: {ex.Message}");
            }
        }

        private static readonly HashSet<Coupler> unlockedCouplers = new HashSet<Coupler>();

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Enabled))]
        public static class Entry_EnabledPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                CreateHook(__instance);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Enabled))]
        public static class Exit_EnabledPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                GameObject.Destroy(GetPivot(__instance)?.gameObject);
                pivots.Remove(__instance);
            }
        }

        private static void AdjustPivot(Transform pivot, Transform target)
        {
            pivot.localEulerAngles = Vector3.zero;
            var offset = pivot.InverseTransformPoint(target.position);
            var angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            pivot.localEulerAngles = new Vector3(0, angle, 0);

            offset.y = 0f;
            var distance = offset.magnitude;
            var hook = pivot.Find("hook");
            hook.localPosition = distance / 2 * Vector3.forward;
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Attached))]
        public static class Entry_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;

                __instance.attachedTo = __instance.couplerAdapter.coupler.coupledTo.visualCoupler.chain.GetComponent<ChainCouplerInteraction>();
                __instance.attachedTo.attachedTo = __instance;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.LateUpdate_Attached))]
        public static class LateUpdate_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                var pivot = GetPivot(__instance);
                var otherPivot = GetPivot(__instance.attachedTo);
                if (pivot != null && otherPivot != null)
                {
                    AdjustPivot(pivot, otherPivot);
                }
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Attached))]
        public static class Exit_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                var pivot = GetPivot(__instance);
                if (pivot != null && __instance.couplerAdapter.coupler != null)
                {
                    pivot.localEulerAngles = __instance.couplerAdapter.coupler.transform.localEulerAngles;
                    var hook = pivot.Find("hook");
                    hook.localPosition = PivotLength * Vector3.forward;
                }
                if (__instance.attachedTo != null)
                {
                    __instance.attachedTo.attachedTo = null;
                    __instance.attachedTo = null;
                }
            }
        }

        private static readonly Dictionary<ChainCouplerInteraction, Transform> pivots = new Dictionary<ChainCouplerInteraction, Transform>();

        private static Transform? GetPivot(ChainCouplerInteraction chainScript)
        {
            if (chainScript == null)
                return null;
            pivots.TryGetValue(chainScript, out var pivot);
            return pivot;
        }

        private const float PivotLength = 1.0f;
        private const float HeightOffset = -0.067f;
        private static void CreateHook(ChainCouplerInteraction chainScript)
        {
            if (chainScript == null)
                return;
                
            // Check if hook already exists
            if (GetPivot(chainScript) != null)
            {
                Main.DebugLog(() => $"Knuckle coupler already exists for {chainScript.couplerAdapter?.coupler?.train?.ID}, skipping creation");
                return;
            }
            
            var coupler = chainScript.couplerAdapter.coupler;
            var pivot = new GameObject(coupler.isFrontCoupler ? "ZCouplers pivot front" : "ZCouplers pivot rear");
            pivot.transform.SetParent(coupler.transform, false);
            pivot.transform.localPosition = new Vector3(0, HeightOffset, -PivotLength);
            pivot.transform.parent = coupler.train.interior;
            pivots.Add(chainScript, pivot.transform);

            if (hookPrefab == null)
            {
                Main.DebugLog(() => "Hook prefab is null, cannot create knuckle coupler hook");
                return;
            }

            var hook = GameObject.Instantiate(hookPrefab);
            hook.SetActive(false); // defer Awake() until all components are added and initialized
            hook.name = "hook";
            hook.layer = LayerMask.NameToLayer("Interactable");
            hook.transform.SetParent(pivot.transform, false);
            hook.transform.localPosition = PivotLength * Vector3.forward;

            var interactionCollider = hook.GetComponent<BoxCollider>();
            interactionCollider.isTrigger = true;

            var colliderHost = new GameObject("walkable");
            colliderHost.layer = LayerMask.NameToLayer("Train_Walkable");
            colliderHost.transform.SetParent(hook.transform, worldPositionStays: false);
            var walkableCollider = colliderHost.AddComponent<BoxCollider>();
            walkableCollider.center = interactionCollider.center;
            walkableCollider.size = interactionCollider.size;

            var buttonSpec = hook.AddComponent<Button>();
            buttonSpec.createRigidbody = false;
            buttonSpec.useJoints = false;
            buttonSpec.colliderGameObjects = new GameObject[] { hook };

            var infoArea = hook.AddComponent<InfoArea>();
            infoArea.infoType = unlockedCouplers.Contains(coupler) ? KnuckleCouplerLock : KnuckleCouplerUnlock;
            hook.SetActive(true); // this should create an actual Button through excuting
            hook.GetComponent<ButtonBase>().Used += () => OnButtonPressed(chainScript);
        }

        public static InteractionInfoType KnuckleCouplerUnlock = (InteractionInfoType)23000;
        public static InteractionInfoType KnuckleCouplerLock = (InteractionInfoType)23001;

        public static void UnlockCoupler(Coupler coupler, bool viaChainInteraction)
        {
            if (!enabled)
                return;
            if (unlockedCouplers.Contains(coupler))
                return;
            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;
            if (unlockedCouplers.Add(coupler))
                chainScript.PlaySound(chainScript.attachSound, chainScript.transform.position);
            if (GetPivot(chainScript)?.Find("hook")?.GetComponent<InfoArea>() is InfoArea infoArea)
                infoArea.infoType = KnuckleCouplerLock;

            coupler.Uncouple(
                playAudio: true,
                calledOnOtherCoupler: false,
                dueToBrokenCouple: false,
                viaChainInteraction);
        }

        public static void ReadyCoupler(Coupler coupler)
        {
            if (!enabled)
                return;
            if (!unlockedCouplers.Contains(coupler))
                return;
            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;
            if (unlockedCouplers.Remove(coupler))
                chainScript.PlaySound(chainScript.parkSound, chainScript.transform.position);
            if (GetPivot(chainScript)?.Find("hook")?.GetComponent<InfoArea>() is InfoArea infoArea)
                infoArea.infoType = KnuckleCouplerUnlock;
        }

        // Update visual state only without triggering actual uncoupling
        public static void UpdateCouplerVisualState(Coupler coupler, bool locked)
        {
            if (!enabled)
                return;
                
            var chainScript = coupler.visualCoupler?.chainAdapter?.chainScript;
            if (chainScript == null)
                return;
                
            if (locked)
            {
                // Coupler should show as locked (ready to unlock)
                unlockedCouplers.Remove(coupler);
                if (GetPivot(chainScript)?.Find("hook")?.GetComponent<InfoArea>() is InfoArea infoArea)
                    infoArea.infoType = KnuckleCouplerUnlock;
            }
            else
            {
                // Coupler should show as unlocked (ready to couple)
                unlockedCouplers.Add(coupler);
                if (GetPivot(chainScript)?.Find("hook")?.GetComponent<InfoArea>() is InfoArea infoArea)
                    infoArea.infoType = KnuckleCouplerLock;
                    
                // Manually trigger visual disconnection for knuckle couplers
                var pivot = GetPivot(chainScript);
                if (pivot != null)
                {
                    pivot.localEulerAngles = coupler.transform.localEulerAngles;
                    var hook = pivot.Find("hook");
                    if (hook != null)
                    {
                        hook.localPosition = PivotLength * Vector3.forward;
                        Main.DebugLog(() => $"Reset knuckle coupler hook position for {coupler.train.ID} {coupler.Position()}");
                    }
                }
                
                // Clear the attached reference if it exists
                if (chainScript.attachedTo != null)
                {
                    chainScript.attachedTo.attachedTo = null;
                    chainScript.attachedTo = null;
                }
            }
        }

        private static void OnButtonPressed(ChainCouplerInteraction chainScript)
        {
            var coupler = chainScript.couplerAdapter.coupler;
            if (unlockedCouplers.Contains(coupler))
                ReadyCoupler(coupler);
            else
                UnlockCoupler(coupler, viaChainInteraction: true);
        }

        public static bool IsReadyToCouple(Coupler coupler)
        {
            return !unlockedCouplers.Contains(coupler);
        }

        public static bool HasUnlockedCoupler(Trainset trainset)
        {
            if (!enabled)
                return false;
            foreach (var car in trainset.cars)
            {
                if (unlockedCouplers.Contains(car.frontCoupler) || unlockedCouplers.Contains(car.rearCoupler))
                    return true;
            }

            return false;
       }
       
        /// Ensures all active train cars have knuckle couplers. 
        public static void EnsureKnuckleCouplersForAllTrains()
        {
            if (!enabled)
                return;

            if (CarSpawner.Instance == null)
                return;

            int created = 0;
            foreach (TrainCar car in CarSpawner.Instance.allCars)
            {
                if (car?.gameObject == null)
                    continue;

                // Check front coupler
                if (car.frontCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    var frontChainScript = car.frontCoupler.visualCoupler.chainAdapter.chainScript;
                    if (GetPivot(frontChainScript) == null && frontChainScript.enabled)
                    {
                        Main.DebugLog(() => $"Creating missing knuckle coupler for {car.ID} front");
                        CreateHook(frontChainScript);
                        created++;
                    }
                }

                // Check rear coupler
                if (car.rearCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
                {
                    var rearChainScript = car.rearCoupler.visualCoupler.chainAdapter.chainScript;
                    if (GetPivot(rearChainScript) == null && rearChainScript.enabled)
                    {
                        Main.DebugLog(() => $"Creating missing knuckle coupler for {car.ID} rear");
                        CreateHook(rearChainScript);
                        created++;
                    }
                }
            }

            if (created > 0)
            {
                Main.DebugLog(() => $"Created {created} missing knuckle couplers for existing trains");
            }
        }

        /// Ensures a specific train car has knuckle couplers on both ends.
        /// Returns the number of knuckle couplers created.
        public static int EnsureKnuckleCouplersForTrain(TrainCar car)
        {
            if (!enabled || car?.gameObject == null)
                return 0;
                
            int created = 0;
            
            // Check front coupler
            if (car.frontCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
            {
                var frontChainScript = car.frontCoupler.visualCoupler.chainAdapter.chainScript;
                if (GetPivot(frontChainScript) == null && frontChainScript.enabled)
                {
                    Main.DebugLog(() => $"Creating missing knuckle coupler for {car.ID} front");
                    CreateHook(frontChainScript);
                    created++;
                }
            }
            
            // Check rear coupler
            if (car.rearCoupler?.visualCoupler?.chainAdapter?.chainScript != null)
            {
                var rearChainScript = car.rearCoupler.visualCoupler.chainAdapter.chainScript;
                if (GetPivot(rearChainScript) == null && rearChainScript.enabled)
                {
                    Main.DebugLog(() => $"Creating missing knuckle coupler for {car.ID} rear");
                    CreateHook(rearChainScript);
                    created++;
                }
            }
            
            return created;
        }

        [HarmonyPatch(typeof(InteractionText), nameof(InteractionText.GetText))]
        public static class GetTextPatch
        {
            public static bool Prefix(InteractionInfoType infoType, ref string __result)
            {
                if (infoType == KnuckleCouplerUnlock)
                {
                    __result = $"Press {InteractionText.Instance.BtnUse} to unlock coupler";
                    return false;
                }
                if (infoType == KnuckleCouplerLock)
                {
                    __result = $"Press {InteractionText.Instance.BtnUse} to ready coupler";
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerVisibilityOptimizer), nameof(ChainCouplerVisibilityOptimizer.Enable))]
        public static class ChainCouplerVisibilityOptimizerEnablePatch
        {
            public static void Postfix(ChainCouplerVisibilityOptimizer __instance)
            {
                if (!enabled)
                    return;
                var chainTransform = __instance.chain.transform;
                for (int i = 0; i < chainTransform.childCount; i++)
                    chainTransform.GetChild(i).gameObject.SetActive(false);
                
                // Check if this coupler needs a knuckle coupler created
                var chainScript = __instance.chain.GetComponent<ChainCouplerInteraction>();
                if (chainScript != null && chainScript.enabled && GetPivot(chainScript) == null)
                {
                    var coupler = chainScript.couplerAdapter?.coupler;
                    if (coupler != null)
                    {
                        Main.DebugLog(() => $"Creating missing knuckle coupler for {coupler.train.ID} {coupler.Position()} during visibility enable");
                        CreateHook(chainScript);
                    }
                }
            }
        }

        /// Patch to catch train cars when they're being set up, including teleported trains.
        [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.Start))]
        public static class TrainCarStartPatch
        {
            public static void Postfix(TrainCar __instance)
            {
                if (!enabled)
                    return;
                    
                // Delay the check to ensure the train car is fully initialized
                __instance.StartCoroutine(DelayedKnuckleCouplerCheck(__instance));
            }
            
            private static System.Collections.IEnumerator DelayedKnuckleCouplerCheck(TrainCar trainCar)
            {
                // Wait a frame for the train car to be fully set up
                yield return null;
                
                if (trainCar != null)
                {
                    int created = EnsureKnuckleCouplersForTrain(trainCar);
                    if (created > 0)
                    {
                        Main.DebugLog(() => $"Created {created} knuckle couplers for train {trainCar.ID} during TrainCar.Start");
                    }
                }
            }
        }

        /// Patch to catch all train spawning, including teleported trains.
        [HarmonyPatch(typeof(CarSpawner), nameof(CarSpawner.SpawnCar))]
        public static class CarSpawnerSpawnCarPatch
        {
            public static void Postfix(TrainCar __result)
            {
                if (!enabled || __result == null)
                    return;
                    
                // Delay the check to ensure the train car is fully set up
                __result.StartCoroutine(DelayedSpawnKnuckleCouplerCheck(__result));
            }
            
            private static System.Collections.IEnumerator DelayedSpawnKnuckleCouplerCheck(TrainCar trainCar)
            {
                // Wait a bit longer for spawned cars to be fully initialized
                yield return new WaitForSeconds(0.5f);
                
                if (trainCar != null)
                {
                    int created = EnsureKnuckleCouplersForTrain(trainCar);
                    if (created > 0)
                    {
                        Main.DebugLog(() => $"Created {created} knuckle couplers for spawned train {trainCar.ID}");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.CoupleBrokenExternally))]
        public static class CoupleBrokenExternallyPatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance)
            {
                __instance.UncoupledExternally();
                return false;
            }
        }

        [HarmonyPatch]
        public static class ChainCouplerInteractionSkipPatches
        {
            private static readonly string[] MethodNames = new string[]
            {
                nameof(ChainCouplerInteraction.Entry_Enabled),
                nameof(ChainCouplerInteraction.Exit_Enabled),

                nameof(ChainCouplerInteraction.Entry_Attached),
                nameof(ChainCouplerInteraction.Exit_Attached),
                nameof(ChainCouplerInteraction.LateUpdate_Attached),

                nameof(ChainCouplerInteraction.Entry_Attached_Tight),
                nameof(ChainCouplerInteraction.Exit_Attached_Tight),

                nameof(ChainCouplerInteraction.Entry_Parked),
                nameof(ChainCouplerInteraction.Exit_Parked),
                nameof(ChainCouplerInteraction.Update_Parked),
            };
            public static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var name in MethodNames)
                    yield return AccessTools.Method(typeof(ChainCouplerInteraction), name);
            }

            public static bool Prefix()
            {
                return !enabled;
            }
        }

        private static readonly HashSet<string> synchronizedCouplings = new HashSet<string>();

        [HarmonyPatch(typeof(ChainCouplerCouplerAdapter), nameof(ChainCouplerCouplerAdapter.OnCoupled))]
        public static class OnCoupledPatch
        {
            public static void Postfix(ChainCouplerCouplerAdapter __instance, CoupleEventArgs e)
            {
                if (!enabled)
                    return;
                    
                Main.DebugLog(() => $"Knuckle OnCoupled: {e.thisCoupler.train.ID}<=>{e.otherCoupler.train.ID},viaChain={e.viaChainInteraction}");
                
                // Update knuckle coupler visual state to show coupled (locked) without triggering uncoupling
                UpdateCouplerVisualState(e.thisCoupler, locked: true);
                UpdateCouplerVisualState(e.otherCoupler, locked: true);
                
                // Ensure both coupler state machines are synchronized for external coupling
                // During UI coupling, only one OnCoupled event may fire, so we need to ensure 
                // both couplers have their states properly updated
                if (!e.viaChainInteraction)
                {
                    // Create a unique coupling ID to prevent duplicate synchronization
                    var couplingId = $"{e.thisCoupler.train.ID}-{e.otherCoupler.train.ID}";
                    var reverseCouplingId = $"{e.otherCoupler.train.ID}-{e.thisCoupler.train.ID}";
                    
                    if (!synchronizedCouplings.Contains(couplingId) && !synchronizedCouplings.Contains(reverseCouplingId))
                    {
                        synchronizedCouplings.Add(couplingId);
                        
                        // Force state machine re-evaluation for both couplers by disabling and re-enabling
                        var thisChainScript = e.thisCoupler.visualCoupler?.chain?.GetComponent<ChainCouplerInteraction>();
                        var otherChainScript = e.otherCoupler.visualCoupler?.chain?.GetComponent<ChainCouplerInteraction>();
                        
                        if (thisChainScript != null && otherChainScript != null)
                        {
                            // Temporarily disable and re-enable to force state refresh
                            thisChainScript.enabled = false;
                            otherChainScript.enabled = false;
                            thisChainScript.enabled = true;
                            otherChainScript.enabled = true;
                            
                            Main.DebugLog(() => $"Forced state synchronization for external coupling: {e.thisCoupler.train.ID} and {e.otherCoupler.train.ID}");
                        }
                        
                        // Clean up the synchronization record after a short delay
                        __instance.StartCoroutine(CleanupSynchronizationRecord(couplingId));
                    }
                }
            }
            
            private static System.Collections.IEnumerator CleanupSynchronizationRecord(string couplingId)
            {
                yield return new UnityEngine.WaitForSeconds(1.0f);
                synchronizedCouplings.Remove(couplingId);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.DetermineNextState))]
        public static class DetermineNextStatePatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance, ref ChainCouplerInteraction.State __result)
            {
                if (!enabled)
                    return true;

                // Check if we need to create a knuckle coupler for this chain script
                if (GetPivot(__instance) == null && __instance.couplerAdapter?.coupler != null)
                {
                    var coupler = __instance.couplerAdapter.coupler;
                    Main.DebugLog(() => $"Creating missing knuckle coupler for {coupler.train.ID} {coupler.Position()} via DetermineNextState");
                    CreateHook(__instance);
                }

                if (__instance.couplerAdapter.IsCoupled())
                {
                    var partner = __instance.couplerAdapter.coupler?.coupledTo?.visualCoupler?.chain?.GetComponent<ChainCouplerInteraction>();
                    if (partner == null)
                    {
                        __result = ChainCouplerInteraction.State.Disabled;
                        return false;
                    }
                    __result = ChainCouplerInteraction.State.Attached_Tight;
                }
                else
                {
                    __result = ChainCouplerInteraction.State.Parked;
                }

                return false;
            }
        }
    }
}
