// Turns the OpenXR loader on, for every platform this study is run on.
//
//   Emotion Rooms > Set Up XR
//
// This is the step the documentation everywhere tells you to do by hand in Project
// Settings. It is three clicks, it has to be repeated on every machine and after some
// package updates, and forgetting it produces a headset that simply does nothing while
// the app looks like it is working -- so it is worth automating even though automating
// it costs reflection.
//
// Reflection because this file must compile whether or not the XR packages are present.
// A red console because an editor script references a package somebody has not installed
// yet is a worse failure than the one it is trying to prevent, and it blocks everything
// else in the project rather than only the XR path.
//
// If any step cannot be completed, it says exactly which manual click replaces it rather
// than failing quietly.

using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EmotionRooms.EditorTools
{
    public static class XRSetup
    {
        const string LoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string SettingsKey = "com.unity.xr.management.loader_settings";

        // Quest controllers. Without an interaction profile the runtime reports no
        // controller at all, which looks exactly like a flat battery.
        static readonly string[] Profiles =
        {
            "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile",
        };

        [MenuItem("Emotion Rooms/Set Up XR", priority = 4)]
        public static void Run()
        {
            var targets = new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android };
            var report = new System.Text.StringBuilder("Emotion Rooms XR setup\n");
            bool allGood = true;

            foreach (var group in targets)
            {
                string outcome = Configure(group);
                report.Append("  ").Append(group).Append(": ").Append(outcome).Append('\n');
                if (outcome != "ready") allGood = false;
            }

            if (allGood)
            {
                report.Append("\nPlug the headset in and press Play. The control panel " +
                              "reports whether it is tracking.");
                Debug.Log(report.ToString());
            }
            else
            {
                report.Append("\nAnything not 'ready' has to be done by hand:\n")
                      .Append("  Project Settings > XR Plug-in Management > tick OpenXR\n")
                      .Append("  under OpenXR > add Oculus Touch Controller Profile");
                Debug.LogWarning(report.ToString());
            }

            AssetDatabase.SaveAssets();
        }

        static string Configure(BuildTargetGroup group)
        {
            try
            {
                var perTarget = GetOrCreatePerBuildTargetSettings();
                if (perTarget == null) return "XR Management not installed";

                var settings = Invoke(perTarget, "SettingsForBuildTarget", group)
                               ?? CreateFor(perTarget, group);
                if (settings == null) return "could not create settings";

                var manager = GetProperty(settings, "Manager") ?? GetField(settings, "m_LoaderManagerInstance");
                if (manager == null) return "no loader manager";

                if (!AssignLoader(manager, group)) return "OpenXR package not installed";

                EnableProfiles(group);
                return "ready";
            }
            catch (Exception e)
            {
                return "failed (" + e.GetType().Name + ": " + e.Message + ")";
            }
        }

        static object GetOrCreatePerBuildTargetSettings()
        {
            var type = FindType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
            if (type == null) return null;

            UnityEngine.Object existing;
            if (EditorBuildSettings.TryGetConfigObject(SettingsKey, out existing) && existing != null)
                return existing;

            var created = ScriptableObject.CreateInstance(type);
            const string folder = "Assets/XR";
            System.IO.Directory.CreateDirectory(folder);
            AssetDatabase.CreateAsset(created, folder + "/XRGeneralSettings.asset");
            EditorBuildSettings.AddConfigObject(SettingsKey, created, true);
            return created;
        }

        static object CreateFor(object perTarget, BuildTargetGroup group)
        {
            Invoke(perTarget, "CreateDefaultManagerSettingsForBuildTarget", group);
            return Invoke(perTarget, "SettingsForBuildTarget", group);
        }

        static bool AssignLoader(object manager, BuildTargetGroup group)
        {
            var store = FindType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore");
            if (store == null) return false;

            var assign = store.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "AssignLoader" && m.GetParameters().Length == 3);
            if (assign == null) return false;

            var result = assign.Invoke(null, new[] { manager, LoaderType, (object)group });
            return result is bool ? (bool)result : true;
        }

        static void EnableProfiles(BuildTargetGroup group)
        {
            var settingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
            if (settingsType == null) return;

            var getFor = settingsType.GetMethod("GetSettingsForBuildTargetGroup",
                BindingFlags.Public | BindingFlags.Static);
            if (getFor == null) return;

            var settings = getFor.Invoke(null, new object[] { group });
            if (settings == null) return;

            var getFeature = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetFeature" && m.IsGenericMethod &&
                                     m.GetParameters().Length == 0);
            if (getFeature == null) return;

            foreach (var name in Profiles)
            {
                var profileType = FindType(name);
                if (profileType == null) continue;

                var feature = getFeature.MakeGenericMethod(profileType).Invoke(settings, null);
                if (feature == null) continue;

                var enabled = profileType.GetProperty("enabled",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (enabled != null && enabled.CanWrite)
                {
                    enabled.SetValue(feature, true);
                    EditorUtility.SetDirty((UnityEngine.Object)feature);
                }
            }
        }

        // ------------------------------------------------------------------ plumbing

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        static object Invoke(object target, string method, params object[] args)
        {
            var info = target.GetType().GetMethod(method,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.FlattenHierarchy);
            return info == null ? null : info.Invoke(target, args);
        }

        static object GetProperty(object target, string name)
        {
            var info = target.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return info == null ? null : info.GetValue(target);
        }

        static object GetField(object target, string name)
        {
            var info = target.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return info == null ? null : info.GetValue(target);
        }

        /// <summary>
        /// The OpenXR runtime actually in use, or "" if none.
        ///
        /// Worth reporting because a display subsystem being "running" does not mean a
        /// headset is on someone's face. macOS has no OpenXR runtime, so Unity falls back
        /// to a mock one that reports as tracking and renders an empty preview -- which
        /// looks exactly like a headset that is connected but showing nothing.
        /// </summary>
        public static string RuntimeName()
        {
            try
            {
                var type = FindType("UnityEngine.XR.OpenXR.OpenXRRuntime");
                if (type == null) return "";
                var name = type.GetProperty("name",
                    BindingFlags.Public | BindingFlags.Static);
                return name == null ? "" : (name.GetValue(null) as string) ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>False for the mock and the simulator, which are not a headset.</summary>
        public static bool IsRealRuntime(string runtime)
        {
            if (string.IsNullOrEmpty(runtime)) return false;
            string lower = runtime.ToLowerInvariant();
            return !lower.Contains("mock") && !lower.Contains("simulator") &&
                   !lower.Contains("unity");
        }

        /// <summary>
        /// Take OpenXR off the desktop target.
        ///
        /// On a Mac it can only ever load a mock runtime, and having it enabled means
        /// pressing Play opens an empty preview window that looks like a broken study.
        /// The Android setting is left alone: that is what a Quest build needs.
        /// </summary>
        [MenuItem("Emotion Rooms/Advanced/Turn OpenXR off for desktop", priority = 114)]
        public static void DisableDesktop()
        {
            try
            {
                UnityEngine.Object existing;
                if (!EditorBuildSettings.TryGetConfigObject(SettingsKey, out existing) ||
                    existing == null)
                    return;

                var settings = Invoke(existing, "SettingsForBuildTarget",
                                      BuildTargetGroup.Standalone);
                if (settings == null) return;

                var manager = GetProperty(settings, "Manager");
                if (manager == null) return;

                var store = FindType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore");
                var remove = store?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "RemoveLoader" && m.GetParameters().Length == 3);
                if (remove == null)
                {
                    Debug.LogWarning("Could not remove the loader automatically. Untick " +
                                     "OpenXR under Project Settings > XR Plug-in " +
                                     "Management, on the desktop tab.");
                    return;
                }

                remove.Invoke(null, new[] { manager, LoaderType, (object)BuildTargetGroup.Standalone });
                AssetDatabase.SaveAssets();
                Debug.Log("OpenXR is off for desktop. Play mode will no longer open a mock " +
                          "headset window. Android is untouched, so a Quest build still works.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not turn OpenXR off for desktop: " + e.Message);
            }
        }

        /// <summary>True when the OpenXR loader is assigned for this platform.</summary>
        public static bool IsConfigured(BuildTargetGroup group)
        {
            try
            {
                UnityEngine.Object existing;
                if (!EditorBuildSettings.TryGetConfigObject(SettingsKey, out existing) ||
                    existing == null)
                    return false;

                var settings = Invoke(existing, "SettingsForBuildTarget", group);
                if (settings == null) return false;

                var manager = GetProperty(settings, "Manager");
                if (manager == null) return false;

                var loaders = GetProperty(manager, "activeLoaders") as System.Collections.IEnumerable;
                if (loaders == null) return false;

                foreach (var loader in loaders)
                    if (loader != null && loader.GetType().FullName == LoaderType) return true;
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
