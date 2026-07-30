"""Generate `unity/PoolConstants.cs` from `pools.py`.

The engine has to validate configs too -- a config hand-edited on the headset never
gets a Python process to protect it -- and two hand-maintained copies of the pools
would silently diverge on the first tuning pass. So the C# copy is generated.
"""

from __future__ import annotations

from . import pools


def _ints(values: tuple[int, ...]) -> str:
    return ", ".join(str(v) for v in values)


def _floats(values: tuple[float, ...]) -> str:
    return ", ".join(f"{v}f" for v in values)


def _strings(values: tuple[str, ...]) -> str:
    return ", ".join(f'"{v}"' for v in values)


def render() -> str:
    return f'''// GENERATED FILE -- DO NOT EDIT BY HAND.
//
// Source of truth: pipeline/pools.py
// Regenerate with:  python3 -m pipeline.cli emit-unity-pools
//
// design-spec.md section 3.

namespace EmotionRooms
{{
    /// <summary>The frozen parameter pools, mirrored from the Python pipeline.</summary>
    public static class PoolConstants
    {{
        /// <summary>HSV hue of the wall colour, in degrees.</summary>
        public static readonly int[] Hues = {{ {_ints(pools.HUES)} }};

        /// <summary>HSV saturation of the wall colour.</summary>
        public static readonly float[] Saturations = {{ {_floats(pools.SATURATIONS)} }};

        /// <summary>Normalised intensity of the room's light source.</summary>
        public static readonly float[] Brightnesses = {{ {_floats(pools.BRIGHTNESSES)} }};

        /// <summary>Greyscale wall materials.</summary>
        public static readonly string[] Textures = {{ {_strings(pools.TEXTURES)} }};

        /// <summary>Researcher-set room geometry. Never an LLM output.</summary>
        public static readonly string[] Shapes = {{ {_strings(pools.SHAPES)} }};

        /// <summary>Legal values of target_emotion, including the control labels.</summary>
        public static readonly string[] TargetLabels = {{ {_strings(pools.TARGET_LABELS)} }};

        /// <summary>How a room's parameters were chosen -- the experimental arm.</summary>
        public static readonly string[] Sources = {{ {_strings(pools.SOURCES)} }};

        /// <summary>Fixed HSV value of wall albedo. Brightness lives on the light.</summary>
        public const float WallValue = {pools.WALL_VALUE}f;

        /// <summary>Tolerance for float pool membership after narrowing to float32.</summary>
        public const float FloatTolerance = {pools.UNITY_FLOAT_TOL}f;

        /// <summary>Total distinct rooms the pools can express, ignoring shape.</summary>
        public const int DesignSpaceSize = {pools.design_space_size()};

        public static bool Contains(int[] pool, int value)
        {{
            for (int i = 0; i < pool.Length; i++)
            {{
                if (pool[i] == value) return true;
            }}
            return false;
        }}

        public static bool Contains(float[] pool, float value)
        {{
            for (int i = 0; i < pool.Length; i++)
            {{
                float delta = pool[i] - value;
                if (delta < 0f) delta = -delta;
                if (delta <= FloatTolerance) return true;
            }}
            return false;
        }}

        public static bool Contains(string[] pool, string value)
        {{
            if (value == null) return false;
            for (int i = 0; i < pool.Length; i++)
            {{
                if (pool[i] == value) return true;
            }}
            return false;
        }}

        public static string Join(int[] pool) {{ return string.Join(", ", System.Array.ConvertAll(pool, v => v.ToString())); }}

        public static string Join(float[] pool) {{ return string.Join(", ", System.Array.ConvertAll(pool, v => v.ToString())); }}

        public static string Join(string[] pool) {{ return string.Join(", ", pool); }}
    }}
}}
'''
