// Editor entry points. Put this file under an "Editor" folder in the Unity project,
// otherwise it will not compile into a player build.
//
//   Emotion Rooms > Build Both Shells      generate the greybox into the open scene
//   Emotion Rooms > Report Dimensions      print the brief's arithmetic and check it

using UnityEditor;
using UnityEngine;

namespace EmotionRooms.EditorTools
{
    public static class RoomBuilderMenu
    {
        [MenuItem("Emotion Rooms/Advanced/Build Both Shells", priority = 110)]
        public static void BuildBothShells()
        {
            var existing = GameObject.Find("EmotionRooms");
            if (existing != null &&
                !EditorUtility.DisplayDialog(
                    "Replace existing rooms?",
                    "An 'EmotionRooms' object already exists in this scene. Replace it?\n\n" +
                    "Anything you have parented under it will be destroyed.",
                    "Replace", "Cancel"))
            {
                return;
            }

            if (existing != null) Object.DestroyImmediate(existing);

            var root = RoomBuilder.BuildAll();
            Undo.RegisterCreatedObjectUndo(root, "Build Emotion Rooms");
            Selection.activeGameObject = root;

            Debug.Log(
                "Built both shells. Linear is active, curved is inactive: shape is " +
                "between-subjects, so exactly one is ever live. Wire both roots into " +
                "RoomLoader's linearRoomRoot and curvedRoomRoot fields.");
        }

        [MenuItem("Emotion Rooms/Advanced/Report Dimensions", priority = 111)]
        public static void ReportDimensions()
        {
            var errors = RoomDimensions.Validate();

            var report =
                "Scene brief section 2\n" +
                string.Format("  entrance width     {0:0.00} m\n", RoomDimensions.EntranceWidth) +
                string.Format("  depth              {0:0.00} m\n", RoomDimensions.Depth) +
                string.Format("  ceiling height     {0:0.00} m\n", RoomDimensions.CeilingHeight) +
                string.Format("  standing position  {0:0.00} m from entrance, centred\n", RoomDimensions.StandingFromEntrance) +
                string.Format("  -> side wall       {0:0.00} m\n", RoomDimensions.ToSideWall) +
                string.Format("  -> facing wall     {0:0.00} m\n", RoomDimensions.ToFacingWall) +
                string.Format("  linear floor area  {0:0.0} m^2  (brief says ~18.1)\n", RoomDimensions.LinearArea) +
                string.Format("  curved floor area  {0:0.0} m^2  (brief says ~16.2)\n", RoomDimensions.CurvedArea) +
                string.Format("  area difference    {0:0.0} m^2  (intended, do not 'fix')\n",
                    RoomDimensions.LinearArea - RoomDimensions.CurvedArea);

            if (errors.Count > 0)
                Debug.LogError(report + "\nINCONSISTENT:\n  - " + string.Join("\n  - ", errors.ToArray()));
            else
                Debug.Log(report + "\nAll matched-dimension constraints hold.");
        }
    }
}
