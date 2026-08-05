// Generates the three wall texture maps.
//
//   Emotion Rooms > Bake Wall Textures     (also run automatically by scene setup)
//
// `texture` is one of the five variables the model controls, so plaster, concrete and
// textile have to be visibly different or that variable is manipulated in the data and
// invisible in the headset. The scene setup previously registered all three with a null
// map, which meant either a hard error at load (what happened) or, if the error were
// simply removed, three identical flat walls and one fifth of the manipulation silently
// doing nothing. The second failure is much worse, because nobody sees it.
//
// Greyscale on purpose. The wall's hue and saturation come from the config and are
// applied as a tint, so any colour in the map itself would fight the manipulation.
//
// Deterministic: fixed seeds, so every participant sees the same three surfaces and a
// re-bake does not silently change the stimulus midway through data collection.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace EmotionRooms.EditorTools
{
    public static class TextureBaker
    {
        public const string Folder = "Assets/Textures";
        const int Size = 512;

        [MenuItem("Emotion Rooms/Bake Wall Textures", priority = 2)]
        public static void BakeMenu()
        {
            BakeAll();
            Debug.Log("Baked wall textures into " + Folder);
        }

        /// <summary>Bakes any map that is missing. Returns the folder.</summary>
        public static void BakeAll()
        {
            Directory.CreateDirectory(Folder);
            foreach (var name in PoolConstants.Textures)
            {
                if (Load(name) == null) Bake(name);
            }
            AssetDatabase.Refresh();
        }

        public static Texture2D Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Path(name));
        }

        static string Path(string name) { return Folder + "/wall_" + name + ".png"; }

        static void Bake(string name)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            var pixels = new Color32[Size * Size];

            switch (name)
            {
                case "plaster": Plaster(pixels); break;
                case "concrete": Concrete(pixels); break;
                case "textile": Textile(pixels); break;
                default: Flat(pixels); break;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path(name), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(Path(name));
            var importer = AssetImporter.GetAtPath(Path(name)) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        // Fine, near-uniform mottling with very slight relief. Reads as a painted wall.
        static void Plaster(Color32[] pixels)
        {
            var rng = new System.Random(11);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float n = Fbm(x * 0.035f, y * 0.035f, 3, 101);
                    float speck = (float)rng.NextDouble() * 0.03f;
                    Set(pixels, x, y, 0.86f + (n - 0.5f) * 0.10f + speck);
                }
        }

        // Coarse blotching plus aggregate speckle, and a faint pour line. Clearly harder
        // and more uneven than plaster at a glance, which is the point.
        static void Concrete(Color32[] pixels)
        {
            var rng = new System.Random(23);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float broad = Fbm(x * 0.008f, y * 0.008f, 4, 202);
                    float grain = Fbm(x * 0.09f, y * 0.09f, 2, 303);
                    float v = 0.72f + (broad - 0.5f) * 0.26f + (grain - 0.5f) * 0.10f;
                    if (rng.NextDouble() < 0.012) v -= (float)rng.NextDouble() * 0.22f;
                    if (y % 171 == 0) v -= 0.05f;
                    Set(pixels, x, y, v);
                }
        }

        // A visible weave: alternating warp and weft blocks with slubs in the yarn.
        static void Textile(Color32[] pixels)
        {
            const int Thread = 8;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    bool warp = ((x / Thread) + (y / Thread)) % 2 == 0;
                    float across = warp ? (x % Thread) / (float)Thread : (y % Thread) / (float)Thread;
                    float round = Mathf.Sin(across * Mathf.PI);          // yarn is round
                    float slub = Fbm(x * 0.05f, y * 0.05f, 2, 404);
                    float v = 0.66f + round * 0.16f + (slub - 0.5f) * 0.09f;
                    if (!warp) v -= 0.04f;                                // weft sits lower
                    Set(pixels, x, y, v);
                }
        }

        static void Flat(Color32[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 200, 200, 255);
        }

        static void Set(Color32[] pixels, int x, int y, float value)
        {
            byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
            pixels[y * Size + x] = new Color32(v, v, v, 255);
        }

        // Tiling value noise. Tiles because the wall repeats the map and a visible seam
        // would be a landmark participants could orient by, differing between shapes.
        static float Fbm(float x, float y, int octaves, int seed)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Mathf.PerlinNoise(x + seed, y + seed) * amplitude;
                total += amplitude;
                x *= 2f; y *= 2f; amplitude *= 0.5f;
            }
            return total > 0f ? sum / total : 0.5f;
        }
    }
}
