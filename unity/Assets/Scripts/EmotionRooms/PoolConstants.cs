// GENERATED FILE -- DO NOT EDIT BY HAND.
//
// Source of truth: pipeline/pools.py
// Regenerate with:  python3 -m pipeline.cli emit-unity-pools
//
// design-spec.md section 3.

namespace EmotionRooms
{
    /// <summary>The frozen parameter pools, mirrored from the Python pipeline.</summary>
    public static class PoolConstants
    {
        /// <summary>HSV hue of the wall colour, in degrees.</summary>
        public static readonly int[] Hues = { 0, 30, 60, 90, 120, 180, 240, 270, 300, 330 };

        /// <summary>HSV saturation of the wall colour.</summary>
        public static readonly float[] Saturations = { 0.2f, 0.4f };

        /// <summary>Normalised intensity of the room's light source.</summary>
        public static readonly float[] Brightnesses = { 150f, 300f, 500f, 750f };

        /// <summary>Greyscale wall materials.</summary>
        public static readonly string[] Textures = { "plaster", "concrete", "textile" };

        /// <summary>Surface roughness. Empty until Mengkai confirms the levels.</summary>
        public static readonly string[] Roughnesses = { "rough", "smooth" };

        /// <summary>Researcher-set room geometry. Never an LLM output.</summary>
        public static readonly string[] Shapes = { "linear", "curved" };

        /// <summary>Legal values of target_emotion, including the control labels.</summary>
        public static readonly string[] TargetLabels = { "calm", "excited", "depressed", "tense", "neutral", "unassigned", "practice" };

        /// <summary>How a room's parameters were chosen -- the experimental arm.</summary>
        public static readonly string[] Sources = { "llm", "random", "handwritten", "practice" };

        /// <summary>Fixed HSV value of wall albedo. Brightness lives on the light.</summary>
        public const float WallValue = 1.0f;

        /// <summary>Tolerance for float pool membership after narrowing to float32.</summary>
        public const float FloatTolerance = 0.0001f;

        /// <summary>Total distinct rooms the pools can express, ignoring shape.</summary>
        public const int DesignSpaceSize = 480;

        public static bool Contains(int[] pool, int value)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == value) return true;
            }
            return false;
        }

        public static bool Contains(float[] pool, float value)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                float delta = pool[i] - value;
                if (delta < 0f) delta = -delta;
                if (delta <= FloatTolerance) return true;
            }
            return false;
        }

        public static bool Contains(string[] pool, string value)
        {
            if (value == null) return false;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == value) return true;
            }
            return false;
        }

        /// <summary>
        /// Every legal value of one attributable field, as the strings the correction
        /// panel shows and RoomConfig.With parses back. Null for an unknown field.
        /// "material" is accepted as an alias of "texture" so a config in Mengkai's
        /// vocabulary attributes to the same pool.
        /// </summary>
        public static string[] ValuesFor(string field)
        {
            switch (field)
            {
                case "hue": return System.Array.ConvertAll(Hues, v => v.ToString(System.Globalization.CultureInfo.InvariantCulture));
                case "saturation": return System.Array.ConvertAll(Saturations, v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                case "brightness": return System.Array.ConvertAll(Brightnesses, v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                case "texture":
                case "material": return (string[])Textures.Clone();
                case "roughness": return (string[])Roughnesses.Clone();
                default: return null;
            }
        }

        /// <summary>Fields a participant can attribute a problem to. Mirrors
        /// pipeline/oversight.py ATTRIBUTABLE, minus the vocabulary alias.</summary>
        public static readonly string[] Attributable =
            { "hue", "saturation", "texture", "roughness", "brightness" };

        public static string Join(int[] pool) { return string.Join(", ", System.Array.ConvertAll(pool, v => v.ToString())); }

        public static string Join(float[] pool) { return string.Join(", ", System.Array.ConvertAll(pool, v => v.ToString())); }

        public static string Join(string[] pool) { return string.Join(", ", pool); }
    }
}
