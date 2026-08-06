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
using UnityEditor.Build.Reporting;
using UnityEngine;

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
            Build(BuildTarget.Android, "Android", "EmotionRooms.apk");
        }

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
