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
                "The catch: the questionnaires are served from the app, and reaching them " +
                "from the headset is awkward. Quest Link keeps everything on the laptop " +
                "and is what the runbook recommends.\n\nBuild the APK anyway?",
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
                Debug.Log(string.Format(
                    "Study build: {0} build succeeded, {1:0.0} MB, at\n  {2}",
                    label, summary.totalSize / (1024f * 1024f), options.locationPathName));
                EditorUtility.RevealInFinder(options.locationPathName);
            }
            else
            {
                Debug.LogError("Study build: " + label + " build " + summary.result +
                               " with " + summary.totalErrors + " error(s). See above.");
            }
        }

        static string EditorSceneManagerScenePath()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            return string.IsNullOrEmpty(scene.path) ? null : scene.path;
        }
    }
}
