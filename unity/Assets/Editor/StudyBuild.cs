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
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmotionRooms.EditorTools
{
    public static class StudyBuild
    {
        [MenuItem("Emotion Rooms/Advanced/Build for Windows (for Mengkai)", priority = 121)]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Windows", "EmotionRooms.exe");
        }

        [MenuItem("Emotion Rooms/Advanced/Build for this Mac", priority = 122)]
        public static void BuildMac()
        {
            Build(BuildTarget.StandaloneOSX, "Mac", "EmotionRooms.app");
        }

        [MenuItem("Emotion Rooms/Advanced/Build the Quest APK only", priority = 124)]
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

            // Same reason as the runtime flag, set at build time so it holds from the
            // first frame rather than from whenever Awake happens to run.
            PlayerSettings.runInBackground = true;

            // Engine-code stripping OFF, deliberately, 9 Aug 2026. It was never set
            // here before - the project default (on) applied silently. Every scene-load
            // SIGTRAP this project has seen came off a release build with stripping on,
            // including one APK that loaded at 15:14 and crashed at 15:21; the
            // development player, which never strips engine code, loaded six from six
            // in the same session. Engine preserves in link.xml also changed the crash,
            // pointing the same direction. The cost is APK size, which this study does
            // not care about; the benefit is a loader that has not failed once in this
            // configuration.
            PlayerSettings.stripEngineCode = false;

            // IL2CPP compiled Debug in an otherwise-release player, 9 Aug 2026. With
            // identical code and scene, the optimized IL2CPP runtime failed scene load
            // eight from eight while the Debug-compiled runtime loaded six from six -
            // an optimizer-triggered fault in the deserialization path, not anything
            // this project's data did. Debug config costs some CPU headroom this
            // eight-room scene never uses; a loader that works costs nothing.
            PlayerSettings.SetIl2CppCompilerConfiguration(android,
                Il2CppCompilerConfiguration.Debug);

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

        [MenuItem("Emotion Rooms/Advanced/Build and put it on the headset", priority = 123)]
        public static void BuildAndDeploy()
        {
            BuildAndDeploy(false);
        }

        /// <summary>
        /// The whole install pipeline, runnable headless:
        ///   Unity -batchmode -quit -executeMethod EmotionRooms.EditorTools.StudyBuild.BatchInstall
        /// Exists so the build loop does not need a person at the editor: the load-crash
        /// hunt burns one full build per experiment, and a person pressing the button
        /// for each one was the bottleneck and the friction.
        /// </summary>
        public static void BatchInstall()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/indoor room.unity");
            XRSetup.AllowLocalHttp();
            StudySceneSetup.SetUp(true);
            ClearBuildCache();
            BuildAndDeploy(false);
        }

        /// <summary>
        /// Same pipeline, Development player. The load crash is a Unity fatal assert
        /// that a release player converts into a silent SIGTRAP; a development player
        /// prints the assert text - which names the object being deserialized - to
        /// logcat first. Diagnosis only, never for participants.
        /// </summary>
        public static void BatchInstallDev()
        {
            developmentBuild = true;
            try { BatchInstall(); }
            finally { developmentBuild = false; }
        }

        static bool developmentBuild;

        /// <summary>
        /// The whole route from source to a running app, as one action.
        ///
        /// This used to be five buttons -- install, clean rebuild, launch, allow local
        /// HTTP, check again -- and four of them were steps this one should have taken
        /// itself. None of them is a decision anybody wants to make between participants.
        /// Allowing local HTTP is a one-off permission, launching is what installing is
        /// for, and whether the build needed to be clean is something the tool can work
        /// out better than a person can.
        ///
        /// It also checks the app is still alive afterwards. An APK that installs and
        /// then dies during scene load reports "Success" from adb, so without this the
        /// panel would say it had worked while the headset showed nothing.
        /// </summary>
        public static void InstallAndRun()
        {
            // The permission the panel needs to talk to the app. Idempotent.
            XRSetup.AllowLocalHttp();

            // Regenerate the scene from the code that is about to be built, so the two
            // can never disagree. Stale scenes produced dead script references, missing
            // wiring and days of load crashes; a scene rebuilt every install cannot.
            StudySceneSetup.SetUp(true);

            // ALWAYS clean. This line was once a gate -- rebuild clean only when the
            // scene stamp had moved -- and that gate was itself a bug: a link.xml edit
            // rode through as an incremental build, regenerated libunity with different
            // stripping while reusing the serialized player data the previous engine
            // wrote, and the app died at scene load. Every load crash this project has
            // seen came off an incremental build; no clean build has ever produced one.
            // Minutes per install is the price, and it is cheap against another day of
            // chasing a corrupt build.
            ClearBuildCache();
            BuildAndDeploy(false);

            ReportWhetherItActuallyRuns();
        }

        /// <summary>
        /// Say plainly whether the app survived starting.
        ///
        /// adb reports success once the bytes are on the device, which is not the same
        /// as the app running -- and a player that dies deserializing the scene leaves no
        /// managed exception and nothing in the Unity log, so silence here would read as
        /// everything being fine.
        /// </summary>
        static void ReportWhetherItActuallyRuns()
        {
            // Polled off EditorApplication.update rather than slept: a Thread.Sleep here
            // runs inside a delayCall and freezes the whole editor for the wait,
            // corrupting whatever IMGUI layout was mid-frame.
            double checkAt = EditorApplication.timeSinceStartup + 6.0;
            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (EditorApplication.timeSinceStartup < checkAt) return;
                EditorApplication.update -= poll;
                CheckStillRunning();
            };
            EditorApplication.update += poll;
        }

        static void CheckStillRunning()
        {
            string pid = Run("shell pidof " + Package).Trim();
            if (!string.IsNullOrEmpty(pid))
            {
                Debug.Log("Study build: installed, launched, and still running on the " +
                          "headset (pid " + pid + ").");
                return;
            }

            string crash = Run("logcat -d -t 400");
            bool died = crash.Contains("Fatal signal") || crash.Contains("SIGTRAP") ||
                        crash.Contains("SIGSEGV");

            Debug.LogError("Study build: the app installed and launched, then stopped." +
                (died ? " It crashed while loading -- the headset log has a native stack." +
                        " This is not something to fix by launching it again."
                      : " Put the headset on and try Install again; an idle headset " +
                        "suspends the app before it finishes starting.") +
                "\n\nTo capture the stack:  adb logcat -d | grep -A 30 'Fatal signal'");
        }

        static string Run(string arguments)
        {
            var info = new System.Diagnostics.ProcessStartInfo(AdbPath(), arguments)
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            };
            try
            {
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd() +
                                    process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    return output;
                }
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("Study build: adb " + arguments + " failed: " + error.Message);
                return "";
            }
        }

        /// <summary>
        /// Throw away every cached Android build artefact, then build.
        ///
        /// Unity builds the player incrementally, and the scene it writes carries no type
        /// tree: the engine reads each MonoBehaviour's fields positionally, in the order
        /// the shipped assembly declares them. When a cached artefact survives a change to
        /// a serialized field, the data and the layout disagree by a few bytes, the first
        /// string read afterwards takes a nonsense length, and the player dies in
        /// CachedReader::OutOfBoundsError on the Loading.Preload thread. There is no
        /// managed exception and nothing in the log -- the app just disappears while the
        /// loading screen is up.
        ///
        /// A clean build costs several minutes and removes the whole class of failure, so
        /// it is the right thing to reach for the moment a build stops starting.
        /// </summary>
        public static void CleanBuildAndDeploy()
        {
            BuildAndDeploy(true);
        }

        static void ClearBuildCache()
        {
            // Bee holds the compiled player data, including the serialized scene and the
            // IL2CPP metadata that has to agree with it. Both are regenerated.
            var caches = new[]
            {
                "Library/Bee",
                "Library/PlayerDataCache",
                "Library/il2cpp_cache",
                "Library/Il2cppBuildCache",
                // Native-compile temp. Its cmake caches bake in the project's ABSOLUTE
                // path, so a moved project folder leaves fingerprints pointing at a
                // directory that no longer exists. Cleared like the rest: a cache that
                // can survive a rename is a cache that can lie.
                ".utmp",
            };

            foreach (var relative in caches)
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), relative);
                if (!Directory.Exists(path)) continue;
                try
                {
                    Directory.Delete(path, true);
                    Debug.Log("Study build: cleared " + relative);
                }
                catch (IOException error)
                {
                    Debug.LogWarning("Study build: could not clear " + relative + ": " +
                                     error.Message);
                }
            }
        }

        static void BuildAndDeploy(bool clean)
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

            // Enable the OpenXR loader as part of building rather than as a step to
            // remember. Without it the APK builds, installs and launches perfectly, and
            // then shows a flat window with no tracking -- which looks like a broken
            // study rather than a missing checkbox.
            XRSetup.Run();

            if (clean) ClearBuildCache();

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
                options = developmentBuild ? BuildOptions.Development : BuildOptions.None,
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
        /// The headset's own Wi-Fi address, so the laptop can open the control page the
        /// app serves. Asked of the device rather than guessed: it is on the same network
        /// as the laptop but not at a predictable address.
        /// </summary>
        /// <summary>Whether the study package exists on the connected headset.</summary>
        public static bool IsInstalled()
        {
            return Run("shell pm list packages " + Package).Contains(Package);
        }

        /// <summary>
        /// Start the app unless it is already running. Quiet by design: the panel calls
        /// this on its probe cadence, and a launch that is already satisfied should not
        /// say anything.
        /// </summary>
        public static void RelaunchIfNotRunning()
        {
            if (!string.IsNullOrEmpty(Run("shell pidof " + Package).Trim())) return;

            string activity = Resolve();
            if (activity == null) return;
            Run("shell am start -n " + Package + "/" + activity);
            Debug.Log("Study: app was not running; relaunched it on the headset.");
        }

        public static string HeadsetAddress()
        {
            string adb = AdbPath();
            if (adb == null) return null;

            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(adb, "shell ip route")
                {
                    UseShellExecute = false, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true,
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    int at = output.IndexOf("src ");
                    if (at < 0) return null;
                    string rest = output.Substring(at + 4).Trim();
                    int end = rest.IndexOfAny(new[] { ' ', '\r', '\n' });
                    return end < 0 ? rest : rest.Substring(0, end);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Start the study on the headset from here.
        ///
        /// This is what makes the headset a display rather than a thing to operate: with
        /// the app installed, adb can launch it, so the participant puts the headset on
        /// already running and never opens a browser, types an address or presses a menu.
        /// Everything else in the session is already on the laptop.
        /// </summary>
        [MenuItem("Emotion Rooms/Advanced/Launch on the headset", priority = 125)]
        public static void LaunchOnHeadset()
        {
            if (ConnectedDevices().Length == 0)
            {
                EditorUtility.DisplayDialog("No headset",
                    "adb cannot see the headset, so it cannot be launched from here.",
                    "OK");
                return;
            }
            // Ask the headset which activity to start rather than naming one.
            //
            // Unity 6 defaults to GameActivity, so the class is UnityPlayerGameActivity,
            // not the UnityPlayerActivity every guide still names. Hardcoding either one
            // breaks on the other, and the failure -- "Activity class does not exist" --
            // reads like a failed install when the app is sitting there installed
            // perfectly well.
            string activity = Resolve();
            if (activity == null)
            {
                Debug.LogError("Study: " + Package + " is not installed on the headset. " +
                               "Use Install on the headset first.");
                return;
            }
            Adb("shell am start -n " + activity, "launched on the headset");
        }

        /// <summary>
        /// Copy every file the study wrote on the headset to this machine.
        ///
        /// On the APK route the data lives in the headset's own storage, and a headset
        /// is the worst place to archive anything: it gets factory-reset, borrowed, and
        /// updated. One button brings the whole folder back -- responses, telemetry,
        /// events, questionnaires and the combined bundles.
        /// </summary>
        [MenuItem("Emotion Rooms/Advanced/Pull the data from the headset", priority = 116)]
        public static void PullData()
        {
            if (ConnectedDevices().Length == 0)
            {
                EditorUtility.DisplayDialog("No headset",
                    "adb cannot see the headset. Plug it in and wake it first.", "OK");
                return;
            }

            string dest = Path.Combine(
                Directory.GetParent(Application.dataPath).Parent.FullName,
                "runs", "headset-data");
            Directory.CreateDirectory(dest);

            if (Adb("pull /sdcard/Android/data/" + Package + "/files \"" + dest + "\"",
                    "data pulled to " + dest))
                EditorUtility.RevealInFinder(dest);
        }

        [MenuItem("Emotion Rooms/Advanced/Stop the app on the headset", priority = 115)]
        public static void StopOnHeadset()
        {
            Adb("shell am force-stop " + Package, "stopped on the headset");
        }

        /// <summary>The installed app's launcher activity, or null if it is not there.</summary>
        static string Resolve()
        {
            string adb = AdbPath();
            if (adb == null) return null;

            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(adb,
                    "shell cmd package resolve-activity --brief " + Package)
                {
                    UseShellExecute = false, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true,
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    foreach (var line in output.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith(Package + "/")) return trimmed;
                    }
                }
            }
            catch (Exception) { }
            return null;
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

            // Belt and braces on top of the save in scene setup: a build takes what is on
            // disk, and an unsaved edit made between rebuilding and building would be left
            // behind without a word.
            SaveOpenScene();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(folder, executable),
                target = target,
                options = developmentBuild ? BuildOptions.Development : BuildOptions.None,
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

        /// <summary>Flush the open scene to disk, because the build reads from disk.</summary>
        static void SaveOpenScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("Study build: saved the open scene before building, so the build " +
                          "contains the current one.");
            }
        }

        static string EditorSceneManagerScenePath()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            return string.IsNullOrEmpty(scene.path) ? null : scene.path;
        }
    }
}
