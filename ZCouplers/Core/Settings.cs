using System;
using System.Linq;
using System.Xml.Serialization;
using DvMod.ZCouplers.Core.Profiles;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.ZCouplers.Core
{
    public enum strengthPreset { Custom, Recommended }

    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        // Property to get the actual profile object from the selected ID
        [XmlIgnore]
        public ICouplerProfile? couplerProfile => CouplerProfiles.GetById(selectedCoupler);

        public string selectedCoupler = "";
        public bool showBuffersWithKnuckles = false;
        public strengthPreset strengthValues = strengthPreset.Recommended;
        public float knuckleStrength = 1.78f;
        public float drawgearSpringRate = 2f; // 2 MN/m = 2e6 N/m
        public float drawgearDamperRate = 100f;
        public float autoCoupleThreshold = 20f;
        public float minimumSeparationDistance = 1.0f;

        public bool autoAirAndMuMode = false;
        public bool autoCouplingMode = false;

        /// <summary>
        /// Gets the effective Auto Air & MU Mode setting, considering Scharfenberg coupler requirements.
        /// Scharfenberg couplers automatically force Auto Air & MU Mode to be active.
        /// </summary>
        [XmlIgnore]
        public bool EffectiveAutoAirAndMuMode => autoAirAndMuMode || couplerProfile?.ProfileId == "Scharfenberg";

        public bool disableFrontCouplersOnSteamLocos = false;

        public bool enableLogging = false;
        public bool enableErrorLogging = true;

        [XmlIgnore]
        public readonly string? version = Main.mod?.Info.Version;

        // Override OnGUI to draw all settings manually
        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            // When in multiplayer, lock all settings - host and clients cannot change values
            if (MpShim.IsMultiplayerActive)
            {
                GUILayout.Label("Settings are locked during multiplayer sessions.", GUILayout.ExpandWidth(false));
                GUILayout.Label("Only the host can change settings in singleplayer.", GUILayout.ExpandWidth(false));
                GUILayout.Space(10);

                // Display current values as read-only
                GUILayout.Label($"Coupler Profile: {couplerProfile?.DisplayName ?? "None"}");
                GUILayout.Label($"Buffers Visible: {showBuffersWithKnuckles}");
                GUILayout.Label($"Strength Mode: {strengthValues}");
                if (strengthValues == strengthPreset.Custom)
                {
                    GUILayout.Label($"Coupler Strength: {knuckleStrength:F2} MN");
                    GUILayout.Label($"Spring Rate: {drawgearSpringRate:F2} MN/m");
                    GUILayout.Label($"Damper Rate: {drawgearDamperRate:F2} kN*s/m");
                }
                GUILayout.Label($"Auto Couple Threshold: {autoCoupleThreshold:F1} mm");
                GUILayout.Label($"Min Separation Distance: {minimumSeparationDistance:F2} m");
                GUILayout.Label($"Auto Air & MU Mode: {autoAirAndMuMode}");
                GUILayout.Label($"Auto Coupling Mode: {autoCouplingMode}");
                GUILayout.Label($"Disable Front Couplers on S282: {disableFrontCouplersOnSteamLocos}");

                if (!string.IsNullOrEmpty(version))
                {
                    GUILayout.Label($"Version: {version}");
                }
                return;
            }

            // Draw coupler profile popup dropdown
            GUILayout.BeginHorizontal();
            GUILayout.Label("Coupler Profile: ", GUILayout.Width(120));

            var couplerProfiles = CouplerProfiles.GetAllProfiles().ToArray();
            var names = couplerProfiles.Select(p => p.DisplayName).ToArray();
            var ids = couplerProfiles.Select(p => p.ProfileId).ToArray();

            int currentIndex = Array.FindIndex(ids, id => id == selectedCoupler);
            if (currentIndex < 0 && ids.Length > 0) currentIndex = 0;

            // Use PopupToggleGroup for a dropdown (requires ref parameter)
            int newIndex = currentIndex;
            GUILayout.BeginVertical(GUILayout.Width(150));
            UnityModManager.UI.PopupToggleGroup(ref newIndex, names);
            GUILayout.EndVertical();

            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ids.Length)
            {
                selectedCoupler = ids[newIndex];
                OnChange();
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            // Toggle Buffers Visuals
            var newShowBuffers = GUILayout.Toggle(showBuffersWithKnuckles, "Toggle Buffers Visuals");
            if (newShowBuffers != showBuffersWithKnuckles)
            {
                showBuffersWithKnuckles = newShowBuffers;
                OnChange();
            }

            GUILayout.Space(5);

            // Strength preset toggle group
            GUILayout.BeginHorizontal();
            GUILayout.Label("Strength Values: ", GUILayout.ExpandWidth(false));
            var newPreset = (strengthPreset)GUILayout.SelectionGrid((int)strengthValues,
                new[] { "Custom", "Recommended" }, 2, GUILayout.ExpandWidth(false));
            if (newPreset != strengthValues)
            {
                strengthValues = newPreset;
                OnChange();
            }
            GUILayout.EndHorizontal();

            // Custom strength values (only visible when Custom is selected)
            if (strengthValues == strengthPreset.Custom)
            {
                GUILayout.Space(5);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Coupler strength (MN): ", GUILayout.Width(200));
                var strengthStr = GUILayout.TextField(knuckleStrength.ToString("F2"), GUILayout.Width(100));
                if (float.TryParse(strengthStr, out float newStrength) && newStrength >= 0.1f && Math.Abs(newStrength - knuckleStrength) > 0.001f)
                {
                    knuckleStrength = newStrength;
                    OnChange();
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Tension spring rate (MN/m): ", GUILayout.Width(200));
                var springStr = GUILayout.TextField(drawgearSpringRate.ToString("F2"), GUILayout.Width(100));
                if (float.TryParse(springStr, out float newSpring) && newSpring >= 0f && Math.Abs(newSpring - drawgearSpringRate) > 0.001f)
                {
                    drawgearSpringRate = newSpring;
                    OnChange();
                }
                GUILayout.Label(" (Needs recoupling or restart)", GUILayout.ExpandWidth(false));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Compression damper rate (kN*s/m): ", GUILayout.Width(200));
                var damperStr = GUILayout.TextField(drawgearDamperRate.ToString("F2"), GUILayout.Width(100));
                if (float.TryParse(damperStr, out float newDamper) && newDamper >= 0f && Math.Abs(newDamper - drawgearDamperRate) > 0.001f)
                {
                    drawgearDamperRate = newDamper;
                    OnChange();
                }
                GUILayout.Label(" (Needs recoupling or restart)", GUILayout.ExpandWidth(false));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);

            // Auto couple threshold
            GUILayout.BeginHorizontal();
            GUILayout.Label("Auto couple threshold (mm): ", GUILayout.Width(200));
            var thresholdStr = GUILayout.TextField(autoCoupleThreshold.ToString("F1"), GUILayout.Width(100));
            if (float.TryParse(thresholdStr, out float newThreshold) && newThreshold >= 0f && Math.Abs(newThreshold - autoCoupleThreshold) > 0.001f)
            {
                autoCoupleThreshold = newThreshold;
                OnChange();
            }
            GUILayout.EndHorizontal();

            // Minimum separation distance
            GUILayout.BeginHorizontal();
            GUILayout.Label("Minimum separation distance (m): ", GUILayout.Width(200));
            var separationStr = GUILayout.TextField(minimumSeparationDistance.ToString("F2"), GUILayout.Width(100));
            if (float.TryParse(separationStr, out float newSeparation) && newSeparation >= 0.1f && Math.Abs(newSeparation - minimumSeparationDistance) > 0.001f)
            {
                minimumSeparationDistance = newSeparation;
                OnChange();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Auto Air & MU Mode
            var newAutoAir = GUILayout.Toggle(autoAirAndMuMode, "Auto Air & MU Mode (Automatically connect air hoses, open brake valves, and connect MU cables when coupling. Enforced by Scharfenberg couplers.)");
            if (newAutoAir != autoAirAndMuMode)
            {
                autoAirAndMuMode = newAutoAir;
                OnChange();
            }

            // Auto Coupling Mode
            var newAutoCoupling = GUILayout.Toggle(autoCouplingMode, "Auto Coupling Mode (Automatically couple even when couplers are not ready. Enforced by Scharfenberg couplers.)");
            if (newAutoCoupling != autoCouplingMode)
            {
                autoCouplingMode = newAutoCoupling;
                OnChange();
            }

            // Disable Front Couplers on S282
            var newDisableFront = GUILayout.Toggle(disableFrontCouplersOnSteamLocos, "Disable Front Couplers on S282");
            if (newDisableFront != disableFrontCouplersOnSteamLocos)
            {
                disableFrontCouplersOnSteamLocos = newDisableFront;
                OnChange();
            }

            GUILayout.Space(10);

            // Enable debug logging
            var newDebugLog = GUILayout.Toggle(enableLogging, "Enable debug logging");
            if (newDebugLog != enableLogging)
            {
                enableLogging = newDebugLog;
                OnChange();
            }

            // Enable error logging
            var newErrorLog = GUILayout.Toggle(enableErrorLogging, "Enable error logging");
            if (newErrorLog != enableErrorLogging)
            {
                enableErrorLogging = newErrorLog;
                OnChange();
            }

            GUILayout.Space(10);

            // Display version
            if (!string.IsNullOrEmpty(version))
            {
                GUILayout.Label($"Version: {version}");
            }
        }

        public override void Save(UnityModManager.ModEntry entry)
        {
            Save(this, entry);
        }

        public void OnChange()
        {
            Couplers.UpdateAllCompressionJoints();
            KnuckleCouplers.OnSettingsChanged();

            // Handle coupler type change at runtime
            HandleCouplerTypeChange();
        }

        [XmlIgnore]
        public ICouplerProfile? lastCouplerProfile = null;

        /// <summary>
        /// Handle runtime coupler type changes by recreating all coupler visuals and physics
        /// </summary>
        private void HandleCouplerTypeChange()
        {
            var currentProfile = couplerProfile;
            if (lastCouplerProfile != null && lastCouplerProfile != currentProfile)
            {
                Main.DebugLog(() => $"Coupler type changed from {lastCouplerProfile.DisplayName} to {currentProfile?.DisplayName}, switching at runtime");
                if (currentProfile != null)
                    KnuckleCouplers.SwitchCouplerTypeAtRuntime(lastCouplerProfile, currentProfile);
            }
            lastCouplerProfile = currentProfile;
        }

        public float GetCouplerStrength()
        {
            if (strengthValues == strengthPreset.Recommended)
            {
                // Use profile default values
                var profile = couplerProfile;
                return profile?.Options.CouplerStrength ?? 1.78e6f;
            }
            else
            {
                // Use custom override value
                return knuckleStrength * 1e6f; // Convert MN to N
            }
        }

        public float GetSpringRate()
        {
            if (strengthValues == strengthPreset.Recommended)
            {
                // Use profile default values
                var profile = couplerProfile;
                return profile?.Options.SpringRate ?? 2e6f;
            }
            else
            {
                // Use custom override value
                return drawgearSpringRate * 1e6f; // Convert MN/m to N/m
            }
        }

        public float GetDamperRate()
        {
            if (strengthValues == strengthPreset.Recommended)
            {
                // Use profile default values
                var profile = couplerProfile;
                return profile?.Options.DamperRate ?? 100e3f;
            }
            else
            {
                // Use custom override value
                return drawgearDamperRate * 1e3f; // Convert kN*s/m to N*s/m
            }
        }
    }
}
