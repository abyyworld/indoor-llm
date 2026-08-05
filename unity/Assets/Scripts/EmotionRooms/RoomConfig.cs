// The engine-side half of the JSON contract in design-spec.md section 4.
//
// Field names are snake_case because they have to match the JSON keys exactly for
// UnityEngine.JsonUtility. Use the properties further down in your own code.
//
// This file repeats the pool checks the Python validator already performs. That is
// deliberate: a config hand-edited on the headset, or an older run file dropped into
// the project, never gets a Python process to protect it, and the spec is explicit
// that a malformed config must not reach a participant.

using System;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;

namespace EmotionRooms
{
    [Serializable]
    public class RoomConfig
    {
        // Sentinels, not defaults. JsonUtility silently leaves absent fields at their
        // initial value, so a missing "brightness" would otherwise arrive as a
        // perfectly plausible 0. These values are outside every pool, so Validate()
        // catches the omission instead of building a black room.
        public string id = null;
        public string target_emotion = null;
        public string source = null;
        public int hue = -1;
        public float saturation = -1f;
        public float brightness = -1f;
        public string texture = null;

        /// <summary>
        /// Surface roughness, independent of material type. Mengkai confirmed the split
        /// on 1 Aug; the levels are pending, so this is OPTIONAL: null means absent and
        /// validates, a value off the pool does not. Accepting an unknown roughness
        /// would let a surface the material system cannot render reach a participant.
        /// </summary>
        public string roughness = null;

        public string rationale = null;

        /// <summary>Researcher-set experimental factor. Optional; never an LLM output.</summary>
        public string shape = null;

        public string Id { get { return id; } }
        public string TargetEmotion { get { return target_emotion; } }
        public string Source { get { return source; } }
        public int Hue { get { return hue; } }
        public float Saturation { get { return saturation; } }
        public float Brightness { get { return brightness; } }
        public string Texture { get { return texture; } }
        public string Roughness { get { return roughness; } }
        public bool HasRoughness { get { return !string.IsNullOrEmpty(roughness); } }
        public string Rationale { get { return rationale; } }
        public string Shape { get { return shape; } }
        public bool HasShape { get { return !string.IsNullOrEmpty(shape); } }

        public static RoomConfig FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new RoomConfigException("Config JSON was empty.");
            }

            RoomConfig config;
            try
            {
                config = JsonUtility.FromJson<RoomConfig>(json);
            }
            catch (Exception exception)
            {
                throw new RoomConfigException("Config JSON could not be parsed: " + exception.Message);
            }

            if (config == null)
            {
                throw new RoomConfigException("Config JSON parsed to null.");
            }

            config.AssertValid();
            return config;
        }

        /// <summary>Every reason this config must not be shown to a participant.</summary>
        public List<string> Validate()
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrEmpty(id))
            {
                errors.Add("id is missing");
            }
            if (!PoolConstants.Contains(PoolConstants.TargetLabels, target_emotion))
            {
                errors.Add("target_emotion=" + Describe(target_emotion) + " is not one of: " +
                           PoolConstants.Join(PoolConstants.TargetLabels));
            }
            if (!PoolConstants.Contains(PoolConstants.Sources, source))
            {
                errors.Add("source=" + Describe(source) + " is not one of: " +
                           PoolConstants.Join(PoolConstants.Sources));
            }
            if (!PoolConstants.Contains(PoolConstants.Hues, hue))
            {
                errors.Add("hue=" + hue + " is not in pool: " + PoolConstants.Join(PoolConstants.Hues));
            }
            if (!PoolConstants.Contains(PoolConstants.Saturations, saturation))
            {
                errors.Add("saturation=" + saturation + " is not in pool: " +
                           PoolConstants.Join(PoolConstants.Saturations));
            }
            if (!PoolConstants.Contains(PoolConstants.Brightnesses, brightness))
            {
                errors.Add("brightness=" + brightness + " is not in pool: " +
                           PoolConstants.Join(PoolConstants.Brightnesses));
            }
            if (!PoolConstants.Contains(PoolConstants.Textures, texture))
            {
                errors.Add("texture=" + Describe(texture) + " is not in pool: " +
                           PoolConstants.Join(PoolConstants.Textures));
            }
            if (HasRoughness && !PoolConstants.Contains(PoolConstants.Roughnesses, roughness))
            {
                errors.Add("roughness=" + Describe(roughness) + " is not in pool: " +
                           PoolConstants.Join(PoolConstants.Roughnesses));
            }
            if (HasShape && !PoolConstants.Contains(PoolConstants.Shapes, shape))
            {
                errors.Add("shape=" + Describe(shape) + " is not in pool: " +
                           PoolConstants.Join(PoolConstants.Shapes));
            }

            return errors;
        }

        public void AssertValid()
        {
            List<string> errors = Validate();
            if (errors.Count == 0)
            {
                return;
            }

            string label = string.IsNullOrEmpty(id) ? "<no id>" : id;
            throw new RoomConfigException(
                "Room config '" + label + "' is invalid:\n  - " + string.Join("\n  - ", errors.ToArray()));
        }

        /// <summary>
        /// True when the scene is achromatic. Mengkai, 1 Aug 2026: "whenever saturation
        /// = 0%, treat the scene as achromatic (black or white, based on V) regardless of
        /// whatever hue value is stored, don't read hue as meaningful when saturation is 0."
        /// </summary>
        public bool IsAchromatic { get { return Mathf.Approximately(saturation, 0f); } }

        /// <summary>
        /// Wall albedo.
        ///
        /// Two rules from her, both deliberate:
        ///
        /// 1. Achromatic. At saturation 0 the stored hue is meaningless and must not be
        ///    read. The scene is black or white, chosen by HSV value.
        /// 2. Albedo. V=100% stays the documented colour specification, but a reflectance
        ///    of 1.0 is not physically reachable and would clip toward white at the top of
        ///    the illuminance range, weakening the hue manipulation in exactly the rooms
        ///    that depend on it. So the renderer applies V scaled by AlbedoCeiling. She
        ///    agreed to this on 1 Aug: "that's fine to do at the rendering layer, just
        ///    preserve the ratios". Uniform scaling is what preserves them -- her 1.21x
        ///    and 1.54x hue-luminance ratios are scale-invariant.
        /// </summary>
        public Color WallColor()
        {
            float value = PoolConstants.WallValue * AlbedoCeiling;

            if (IsAchromatic)
            {
                // Black or white by V, hue ignored entirely.
                float level = PoolConstants.WallValue >= AchromaticSplit ? value : 0f;
                return new Color(level, level, level);
            }

            return Color.HSVToRGB(hue / 360f, saturation, value);
        }

        /// <summary>
        /// Physical ceiling on diffuse reflectance. A white painted wall is about 0.85;
        /// nothing real reaches 1.0. Rendering-layer only -- it does not change the
        /// documented colour spec, and scaling uniformly leaves every ratio intact.
        /// </summary>
        public const float AlbedoCeiling = 0.85f;

        /// <summary>V at or above this reads as white, below it as black.</summary>
        public const float AchromaticSplit = 0.5f;

        /// <summary>
        /// Maps the config's illuminance onto a Unity light intensity.
        ///
        /// `brightness` is now a value in LUX, not the old normalised 0..1. A Unity light
        /// intensity is not a photometric quantity, so this is a NOMINAL mapping: the
        /// light is configured so the rendered scene corresponds to that band, not so an
        /// instrument would read that many lux off the display. See build-decisions.md
        /// section 4 -- the mapping actually used has to be recorded per study, and the
        /// write-up can claim the intended band was applied, not that a physical
        /// illuminance was achieved.
        ///
        /// Interpolation is in LOG space, because perceived brightness is roughly
        /// logarithmic in illuminance. Linear interpolation across 30..900 lx would put
        /// almost the entire perceptual range into the bottom tenth of the scale, making
        /// the dim conditions nearly indistinguishable from each other and the bright
        /// ones nearly identical.
        /// </summary>
        public float LightIntensity(float minIntensity, float maxIntensity)
        {
            float lux = Mathf.Max(brightness, MinimumLux);
            float lowLux = Mathf.Max(PoolConstants.Brightnesses[0], MinimumLux);
            float highLux = Mathf.Max(
                PoolConstants.Brightnesses[PoolConstants.Brightnesses.Length - 1], lowLux + 1f);

            float t = Mathf.InverseLerp(Mathf.Log(lowLux), Mathf.Log(highLux), Mathf.Log(lux));
            return Mathf.Lerp(minIntensity, maxIntensity, Mathf.Clamp01(t));
        }

        /// <summary>Floor so the log mapping cannot be handed a zero or negative lux.</summary>
        public const float MinimumLux = 1f;

        /// <summary>
        /// A copy with one variable replaced, used to apply a participant's correction.
        ///
        /// Returns null if the field is unknown or the value will not parse, rather than
        /// silently applying nothing. A correction that quietly failed to take effect
        /// would produce a "corrected" room identical to the original, and the re-rating
        /// would then look like the correction did not help when in fact it never
        /// happened.
        /// </summary>
        public RoomConfig With(string field, string value)
        {
            if (string.IsNullOrEmpty(field) || value == null) return null;

            var copy = new RoomConfig
            {
                id = id, target_emotion = target_emotion, source = source,
                hue = hue, saturation = saturation, brightness = brightness,
                texture = texture, roughness = roughness, rationale = rationale,
                shape = shape,
            };

            switch (field)
            {
                case "hue":
                    int h;
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out h))
                        return null;
                    copy.hue = h;
                    break;
                case "saturation":
                    float sat;
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out sat))
                        return null;
                    copy.saturation = sat;
                    break;
                case "brightness":
                    float lux;
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out lux))
                        return null;
                    copy.brightness = lux;
                    break;
                case "texture":
                case "material":
                    copy.texture = value;
                    break;
                case "roughness":
                    copy.roughness = value;
                    break;
                default:
                    return null;
            }

            return copy;
        }

        public override string ToString()
        {
            return string.Format(
                "{0} [{1}/{2}] hue={3} sat={4} bright={5} tex={6}{7}",
                id, target_emotion, source, hue, saturation, brightness, texture,
                HasShape ? " shape=" + shape : "");
        }

        static string Describe(string value)
        {
            return value == null ? "<missing>" : "'" + value + "'";
        }
    }

    /// <summary>A run or session file: <c>{"meta": {...}, "rooms": [...]}</c>.</summary>
    [Serializable]
    public class RoomBatch
    {
        public RoomConfig[] rooms = null;

        public static RoomBatch FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new RoomConfigException("Batch JSON was empty.");
            }

            RoomBatch batch;
            try
            {
                batch = JsonUtility.FromJson<RoomBatch>(json);
            }
            catch (Exception exception)
            {
                throw new RoomConfigException("Batch JSON could not be parsed: " + exception.Message);
            }

            if (batch == null || batch.rooms == null || batch.rooms.Length == 0)
            {
                throw new RoomConfigException(
                    "Batch JSON has no 'rooms' array. Export it with: " +
                    "python3 -m pipeline.cli export-unity <file> --out <file>");
            }

            // All or nothing. A batch with one bad room is a broken batch: finding out
            // mid-session which room fails is not an option.
            List<string> errors = new List<string>();
            for (int i = 0; i < batch.rooms.Length; i++)
            {
                List<string> roomErrors = batch.rooms[i].Validate();
                for (int j = 0; j < roomErrors.Count; j++)
                {
                    errors.Add("rooms[" + i + "]: " + roomErrors[j]);
                }
            }
            if (errors.Count > 0)
            {
                throw new RoomConfigException(
                    "Batch is invalid:\n  - " + string.Join("\n  - ", errors.ToArray()));
            }

            return batch;
        }

        public RoomConfig Find(string roomId)
        {
            for (int i = 0; i < rooms.Length; i++)
            {
                if (rooms[i].id == roomId)
                {
                    return rooms[i];
                }
            }
            throw new RoomConfigException("No room with id '" + roomId + "' in this batch.");
        }
    }

    public class RoomConfigException : Exception
    {
        public RoomConfigException(string message) : base(message) { }
    }
}
