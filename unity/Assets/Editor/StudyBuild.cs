// Builds the app the second researcher runs.
//
// Emotion Rooms > Build for Mengkai (Windows)  and  > Build for this Mac
//
// A build is how somebody without this repo, without Python and without Unity collects
// data. Everything it needs travels inside it: the participant packs in StreamingAssets,
// the questionnaires, and the runtime control panel.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmotionRooms.EditorTools
{
    public static class StudyBuild
    {
        [MenuItem("Emotion Rooms/Build for Windows (for Mengkai)", priority = 2)]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Windows", "EmotionRooms.exe");
        }

        [MenuItem("Emotion Rooms/Build for this Mac", priority = 3)]
        public static void BuildMac()
        {
            Build(BuildTarget.StandaloneOSX, "Mac", "EmotionRooms.app");
        }

        [MenuItem("Emotion Rooms/Build for the Quest (standalone APK)", priority = 5)]
        public static void BuildQuest()
        {
            // A standalone APK runs on the headset with no PC, which is the better
            // arrangement for a study run in a room without a desk. It costs the
            // researcher panel, though: the forms are served over localhost and a Quest
            // has no browser the researcher can reach easily, so the questionnaires have
            // to be filled on a laptop pointed at the headset's address, or on paper.
            //
            // Quest Link is the simpler path and the one the runbook recommends.
            if (!EditorUtility.DisplayDialog("Standalone Quest build",
                "This runs on the headset with no PC attached.\n\n" +
                "Installing it needs DEVELOPER MODE on the headset, which is tied to the " +
                "Meta account that owns it. On a borrowed headset you cannot enable that " +
                "yourself -- the owner has to.\n\n" +
                "The questionnaires are also served from the app, so they have to be " +
                "answered from a laptop pointed at the headset's address on the same " +
                "Wi-Fi.\n\nQuest Link on a Windows PC avoids both problems.\n\n" +
                "Build the APK anyway?",
                "Build APK", "Cancel"))
                return;

            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android);
            ConfigureForQuest();
            Build(BuildTarget.Android, "Android", "EmotionRooms.apk");
        }

        /// <summary>
        /// The player settings a Quest build needs, none of which are Unity's defaults.
        ///
        /// Left unset, the build fails on OpenXR's own validation with messages that name
        /// the symptom rather than the setting -- "Gamma Color Space is not supported when
        /// using OpenGLES" is really "you are on the wrong graphics API and the wrong
        /// colour space", and neither is discoverable from the text. Set here so the build
        /// button produces a build rather than a puzzle.
        /// </summary>
        static void ConfigureForQuest()
        {
            var android = NamedBuildTarget.Android;

            // Vulkan only. The Quest runtime prefers it, and OpenGLES cannot do the
            // linear colour space that the lighting in this study depends on -- a gamma
            // pipeline would change how every room looks, which is the manipulation.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // The headset is 64-bit and IL2CPP is the only supported backend for it.
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            // Landscape and no auto-rotation: the headset composites its own view, and a
            // rotating player window fights it.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            PlayerSettings.SetApplicationIdentifier(android, "com.emotionrooms.study");
            PlayerSettings.productName = "Emotion Rooms";

            // Multi-threaded rendering off: it interacts badly with some OpenXR runtimes
            // and this scene is nowhere near needing it.
            PlayerSettings.SetMobileMTRendering(android, false);

            AssetDatabase.SaveAssets();
            Debug.Log("Study build: Quest player settings applied (Vulkan, linear, " +
                      "IL2CPP, ARM64, API 32, ASTC).");
        }

        /// <summary>Path to adb inside the installed Android build support.</summary>
        public static string AdbPath()
        {
            string root = Path.GetDirectoryName(EditorApplication.applicationPath);
            string[] candidates =
            {
                Path.Combine(root ?? "", "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"),
                Path.Combine(EditorPrefs.GetString("AndroidSdkRoot", ""), "platform-tools/adb"),
            };
            foreach (var path in candidates)
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            return null;
        }

        /// <summary>Serial numbers of headsets adb can see. Empty means none.</summary>
        public static string[] ConnectedDevices()
        {
            string adb = AdbPath();
            if (adb == null) return new string[0];

            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(adb, "devices")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    var devices = new System.Collections.Generic.List<string>();
                    foreach (var line in output.Split('\n'))
                    {
                        var parts = line.Trim().Split('\t');
                        // "unauthorized" means the headset is plugged in but the Allow USB
                        // Debugging prompt has not been accepted inside it -- a different
                        // problem from not being plugged in, and worth saying so.
                        if (parts.Length == 2 && parts[1].Trim() == "device") devices.Add(parts[0]);
                        else if (parts.Length == 2 && parts[1].Trim() == "unauthorized")
                            devices.Add(parts[0] + " (not authorised — put the headset on " +
                                        "and accept Allow USB Debugging)");
                    }
                    return devices.ToArray();
                }
            }
            catch (Exception)
            {
                return new string[0];
            }
        }

        [MenuItem("Emotion Rooms/Build and put it on the headset", priority = 4)]
        public static void BuildAndDeploy()
        {
            var devices = ConnectedDevices();
            if (devices.Length == 0)
            {
                EditorUtility.DisplayDialog("No headset",
                    "adb cannot see a headset, so there is nowhere to install to.\n\n" +
                    "Check, in order:\n" +
                    "  1. The cable carries data, not just power. A charging cable will\n" +
                    "     not work and looks identical.\n" +
                    "  2. Developer Mode is on for the headset. This is set by whoever\n" +
                    "     owns the Meta account, in the Meta Horizon phone app.\n" +
                    "  3. Put the headset on. Accept 'Allow USB Debugging'.\n\n" +
                    "Without Developer Mode there is no way to install a build. The " +
                    "browser version needs none of this.", "OK");
                return;
            }

            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android);
            ConfigureForQuest();

            string apk = Path.Combine(Path.GetTempPath(), "EmotionRooms.apk");
            string scene = EditorSceneManagerScenePath();
            if (scene == null)
            {
                EditorUtility.DisplayDialog("Save the scene first",
                    "The open scene has never been saved.", "OK");
                return;
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = apk,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("Study build: APK build failed, nothing installed.");
                return;
            }

            Install(apk);
        }

        const string Package = "com.emotionrooms.study";

        /// <summary>
        /// Start the study on the headset from here.
        ///
        /// This is what makes the headset a display rather than a thing to operate: with
        /// the app installed, adb can launch it, so the participant puts the headset on
        /// already running and never opens a browser, types an address or presses a menu.
        /// Everything else in the session is already on the laptop.
        /// </summary>
        [MenuItem("Emotion Rooms/Launch on the headset", priority = 5)]
        public static void LaunchOnHeadset()
        {
            if (ConnectedDevices().Length == 0)
            {
                EditorUtility.DisplayDialog("No headset",
                    "adb cannot see the headset, so it cannot be launched from here.",
                    "OK");
                return;
            }
            Adb("shell am start -n " + Package + "/com.unity3d.player.UnityPlayerActivity",
                "launched on the headset");
        }

        [MenuItem("Emotion Rooms/Advanced/Stop the app on the headset", priority = 115)]
        public static void StopOnHeadset()
        {
            Adb("shell am force-stop " + Package, "stopped on the headset");
        }

        /// <summary>Run an adb command and report what it said.</summary>
        static bool Adb(string arguments, string success)
        {
            string adb = AdbPath();
            if (adb == null)
            {
                Debug.LogError("Study build: adb not found. Install Android Build Support.");
                return false;
            }

            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(adb, arguments)
                {
                    UseShellExecute = false, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true,
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    string output = (process.StandardOutput.ReadToEnd() + "\n" +
                                     process.StandardError.ReadToEnd()).Trim();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && !output.Contains("Error"))
                    {
                        Debug.Log("Study: " + success + (output.Length > 0 ? "\n" + output : ""));
                        return true;
                    }
                    Debug.LogError("Study: adb failed.\n" + output);
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Study: adb failed. " + e.Message);
                return false;
            }
        }

        static void Install(string apk)
        {
            string adb = AdbPath();
            var info = new System.Diagnostics.ProcessStartInfo(adb, "install -r \"" + apk + "\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            };
            using (var process = System.Diagnostics.Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd() +
                                process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (output.Contains("Success"))
                {
                    Debug.Log("Study build: installed on the headset. Launching it now — " +
                              "the participant should find it already running.");
                    LaunchOnHeadset();
                }
                else
                    Debug.LogError("Study build: adb install failed.\n" + output);
            }
        }

        [MenuItem("Emotion Rooms/Advanced/Apply Quest Player Settings", priority = 113)]
        static void ApplyQuestSettingsOnly() { ConfigureForQuest(); }

        static void Build(BuildTarget target, string label, string executable)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            {
                EditorUtility.DisplayDialog("Missing build support",
                    label + " Build Support is not installed for this editor.\n\n" +
                    "Unity Hub > Installs > this version > the gear icon > Add modules > " +
                    label + " Build Support, then try again.", "OK");
                return;
            }

            if (!File.Exists(Path.Combine(Application.streamingAssetsPath, "questionnaires.json")))
            {
                EditorUtility.DisplayDialog("Questionnaires missing",
                    "StreamingAssets/questionnaires.json is not there, so the build would " +
                    "ship with no forms.\n\nRun: python3 -m pipeline.cli emit-questionnaires",
                    "OK");
                return;
            }

            string packs = Path.Combine(Application.streamingAssetsPath, "participants");
            if (!Directory.Exists(packs) || Directory.GetDirectories(packs).Length == 0)
            {
                EditorUtility.DisplayDialog("Participant packs missing",
                    "StreamingAssets/participants is empty, so the build would have no " +
                    "rooms to show and no way to make any.\n\n" +
                    "Run: python3 -m pipeline.cli build-participants", "OK");
                return;
            }

            string folder = EditorUtility.SaveFolderPanel(
                "Where should the " + label + " build go?", "", "");
            if (string.IsNullOrEmpty(folder)) return;

            string scene = EditorSceneManagerScenePath();
            if (scene == null)
            {
                EditorUtility.DisplayDialog("Save the scene first",
                    "The open scene has never been saved, so there is nothing to build.\n\n" +
                    "Save it, then build again.", "OK");
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(folder, executable),
                target = target,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                WriteReadme(folder, executable);
                Debug.Log(string.Format(
                    "Study build: {0} build succeeded, {1:0.0} MB, at\n  {2}\n" +
                    "Send the whole folder. READ ME FIRST.txt is inside it.",
                    label, summary.totalSize / (1024f * 1024f), options.locationPathName));
                EditorUtility.RevealInFinder(options.locationPathName);
            }
            else
            {
                Debug.LogError("Study build: " + label + " build " + summary.result +
                               " with " + summary.totalErrors + " error(s). See above.");
            }
        }

        /// <summary>
        /// Instructions in the folder, for the person who did not build it.
        ///
        /// A build handed over with the procedure living in a chat message is a build
        /// that gets run wrong once and then trusted. This travels with it.
        /// </summary>
        static void WriteReadme(string folder, string executable)
        {
            string text =
"EMOTION ROOMS -- running a session\n" +
"==================================\n\n" +
"You need: this folder, a Meta Quest, and a Windows PC. No Unity, no Python,\n" +
"nothing to install except Quest Link.\n\n" +
"ONE-TIME SETUP\n" +
"--------------\n" +
"1. Install Meta Quest Link from meta.com/quest/setup and sign in.\n" +
"2. Plug the Quest in with its cable. Put it on, and accept 'Enable Quest Link'.\n" +
"   The headset should show a flat desktop-style room, not the normal Quest home.\n\n" +
"RUNNING ONE PARTICIPANT\n" +
"-----------------------\n" +
"1. Start " + executable + ". If Windows Firewall asks, allow it on PRIVATE networks.\n" +
"   That is the questionnaire page; nothing leaves this computer.\n" +
"2. Press F9 to show the control panel. Check section 0 is all green before\n" +
"   anyone sits down.\n" +
"3. Pick the participant number you have been allocated. Never reuse one --\n" +
"   a repeat writes two people into one file and neither can be separated later.\n" +
"4. Headset OFF. Open the four 'before' forms from the panel; they open in your\n" +
"   browser. Let the participant fill them in themselves.\n" +
"5. Fit the headset. Press Begin. About 27 minutes: two practice rooms, eight\n" +
"   real ones, then a review block. You do nothing during this.\n" +
"6. Headset off. Open the 'after' forms. The debrief is the last one and\n" +
"   explains the study -- do not skip it.\n" +
"7. Press 'Save and finish'. Note on paper anything the panel still lists as\n" +
"   outstanding.\n\n" +
"IF THE PARTICIPANT WANTS TO STOP\n" +
"--------------------------------\n" +
"Close the app, or they hold F12 for 1.5 seconds. Everything recorded so far is\n" +
"kept and marked as a withdrawal. Stopping midway costs nothing and is always\n" +
"the right call if someone feels unwell.\n\n" +
"THE DATA\n" +
"--------\n" +
"One file per participant, written automatically:\n\n" +
"  %USERPROFILE%\\AppData\\LocalLow\\DefaultCompany\\unity\\bundles\\pNN_all.csv\n\n" +
"Paste that path into Explorer. Send that file back. It contains every rating,\n" +
"every questionnaire answer, every event and 20 Hz head tracking.\n\n" +
"IF SOMETHING IS WRONG\n" +
"---------------------\n" +
"Section 0 of the panel names it. 'No headset' means Quest Link is not running:\n" +
"put the headset on and accept the Link prompt. Everything else there means the\n" +
"build is incomplete -- do not run a participant, ask for a new build.\n";

            try
            {
                File.WriteAllText(Path.Combine(folder, "READ ME FIRST.txt"), text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Study build: could not write the readme. " + e.Message);
            }
        }

        static string EditorSceneManagerScenePath()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            return string.IsNullOrEmpty(scene.path) ? null : scene.path;
        }
    }
}
