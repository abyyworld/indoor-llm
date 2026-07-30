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
        /// Wall albedo. Hue and saturation come from the config; HSV value is the fixed
        /// researcher constant, because how bright the room looks is the light's job.
        /// </summary>
        public Color WallColor()
        {
            return Color.HSVToRGB(hue / 360f, saturation, PoolConstants.WallValue);
        }

        /// <summary>Maps normalised brightness onto a real light intensity.</summary>
        public float LightIntensity(float minIntensity, float maxIntensity)
        {
            return Mathf.Lerp(minIntensity, maxIntensity, brightness);
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
