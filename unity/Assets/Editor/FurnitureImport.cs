// One-click setup for the furniture models.
//
//   Emotion Rooms > Import Furniture Models
//
// Finds the imported FBX models by name, builds the FurnitureSet asset, and rebuilds the
// rooms. Doing it by hand is seven drag-and-drops into a ScriptableObject that does not
// exist yet, which is exactly the kind of step that gets done differently on the day.
//
// Slots with no matching model stay on the procedural placeholder, so a partial import
// still runs.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EmotionRooms.EditorTools
{
    public static class FurnitureImport
    {
        const string AssetPath = "Assets/Furniture/FurnitureSet.asset";

        // Slot to model name. Chosen for the brief's list: a three-seat sofa, a single
        // armchair, a coffee table, a rectangular rug and an open bookcase. Kenney's kit
        // has no teacup and no wall art, so those two stay procedural.
        static readonly Dictionary<string, string> Wanted = new Dictionary<string, string>
        {
            { "sofa",        "loungeSofaLong" },
            { "armchair",    "loungeChair" },
            { "coffeeTable", "tableCoffee" },
            { "rug",         "rugRectangle" },
            { "bookshelf",   "bookcaseOpen" },
        };

        [MenuItem("Emotion Rooms/Advanced/Import Furniture Models", priority = 120)]
        public static void Import()
        {
            var set = AssetDatabase.LoadAssetAtPath<FurnitureSet>(AssetPath);
            if (set == null)
            {
                Directory.CreateDirectory("Assets/Furniture");
                set = ScriptableObject.CreateInstance<FurnitureSet>();
                AssetDatabase.CreateAsset(set, AssetPath);
            }

            var found = new List<string>();
            var missing = new List<string>();

            foreach (var pair in Wanted)
            {
                var model = Find(pair.Value);
                if (model == null) { missing.Add(pair.Value); continue; }

                switch (pair.Key)
                {
                    case "sofa": set.sofa = model; break;
                    case "armchair": set.armchair = model; break;
                    case "coffeeTable": set.coffeeTable = model; break;
                    case "rug": set.rug = model; break;
                    case "bookshelf": set.bookshelf = model; break;
                }
                found.Add(pair.Key + " = " + pair.Value);
            }

            set.forceNeutralMaterials = true;
            set.normaliseToFootprint = true;

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string report =
                "Furniture set written to " + AssetPath + "\n" +
                "  using models: " + (found.Count == 0 ? "none" : string.Join(", ", found.ToArray())) + "\n" +
                "  still placeholder: teacup, wall art" +
                (missing.Count > 0 ? ", " + string.Join(", ", missing.ToArray()) : "") + "\n" +
                "  materials forced neutral so furniture colour cannot compete with the " +
                "hue manipulation";

            if (missing.Count > 0)
                Debug.LogWarning(report + "\n\nMissing models were not found under Assets/. " +
                                 "Check they imported.");
            else
                Debug.Log(report);

            StudySceneSetup.SetUp();
        }

        static GameObject Find(string modelName)
        {
            foreach (var guid in AssetDatabase.FindAssets(modelName + " t:GameObject"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != modelName) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) return go;
            }
            return null;
        }
    }
}
