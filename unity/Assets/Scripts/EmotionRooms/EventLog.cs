// Event log. One row per thing that happens, from before the participant does anything.
//
// The trial CSVs written by TrialRunner and OversightReview are summaries: one row per
// trial, the answer only. This is the opposite and they are both wanted. Summaries are
// what the analysis joins on; this is what you go back to when a number looks wrong and
// you need to know what actually happened in the session.
//
// Rules, deliberately blunt:
//
//   * The first row is written at session start, before the participant does anything,
//     so every session has a t=0 anchor and a record of the configuration it ran under.
//   * Every row carries wall-clock UTC and milliseconds since session start. The second
//     is what you analyse on; the first is what you use to line up against anything
//     external, like a note the experimenter made.
//   * One row per discrete change. Continuous things (head pose) are sampled at a fixed
//     rate AND written on significant change, so a still head costs little and a fast
//     turn is not smoothed away.
//   * Wide and sparse. Most columns are empty on most rows. That is fine: a wide CSV
//     that never loses a field beats a tidy one that cannot represent an event.
//   * Append and flush as it goes. A session that crashes keeps everything up to the
//     crash, which is usually the interesting part.
//
// Writes to Application.persistentDataPath/logs/<participant>_<timestamp>_events.csv

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace EmotionRooms
{
    public class EventLog : MonoBehaviour
    {
        [Header("Identity")]
        public string participantId = "p00";

        [Tooltip("Subfolder under persistentDataPath. Kept separate from the summary " +
                 "CSVs so an event log is never mistaken for analysable trial data.")]
        public string folder = "logs";

        [Header("Head pose")]
        [Tooltip("Transform to sample, normally the XR camera. Leave empty to skip pose " +
                 "logging entirely.")]
        public Transform headTransform;

        [Tooltip("Samples per second. 10 is plenty for 'where were they looking' and " +
                 "keeps the file readable; raise it if you want motion detail.")]
        public float poseSampleHz = 10f;

        [Tooltip("Also write a row when the head moves more than this many degrees " +
                 "between samples, so a fast turn is not lost between ticks.")]
        public float poseChangeDegrees = 5f;

        [Header("Robustness")]
        [Tooltip("Flush to disk every this many rows. Lower is safer, higher is faster.")]
        public int flushEveryRows = 10;

        public string Path { get; private set; }
        public bool IsOpen { get { return writer != null; } }

        StreamWriter writer;
        float sessionStart;
        int sequence;
        int sinceFlush;
        float nextPoseSample;
        Quaternion lastPoseRotation;
        bool havePose;

        static readonly string[] Columns =
        {
            "seq", "t_utc", "t_ms", "participant", "phase",
            "trial_index", "trial_id", "event", "detail",
            "target_emotion", "shape", "room_id",
            "hue", "saturation", "brightness", "texture",
            "head_x", "head_y", "head_z", "head_yaw", "head_pitch", "head_roll",
            "pointer_x", "pointer_y", "valence", "arousal",
            "value_a", "value_b", "note",
        };

        // Start, not Awake. Unity runs every Awake before any Start, and StudyBootstrap
        // assigns the participant id in its Awake. Opening here was a guaranteed
        // mismatch rather than a race: StudyBootstrap is added to the Study object last,
        // so its Awake runs last, and this file had already been created under the
        // previous participant's id. The responses would say p07 and the log filename
        // p06, which is only noticeable long after the session.
        string openedFor;

        void Start()
        {
            Open();
            openedFor = participantId;
        }



        void OnDestroy()
        {
            Write("session_end", "component destroyed");
            Close();
        }

        void OnApplicationPause(bool paused)
        {
            // Headset removed or app backgrounded. Worth knowing: a 4 minute gap in the
            // middle of a trial explains an outlier that would otherwise look like data.
            Write(paused ? "app_paused" : "app_resumed", null);
            Flush();
        }

        void OnApplicationFocus(bool focused)
        {
            Write(focused ? "app_focus_gained" : "app_focus_lost", null);
        }

        public void Open()
        {
            if (writer != null) return;

            string dir = System.IO.Path.Combine(Application.persistentDataPath, folder);
            Directory.CreateDirectory(dir);

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            Path = System.IO.Path.Combine(dir, participantId + "_" + stamp + "_events.csv");

            writer = new StreamWriter(Path, false, Encoding.UTF8);
            writer.WriteLine(string.Join(",", Columns));

            sessionStart = Time.realtimeSinceStartup;
            sequence = 0;

            // The first row, before the participant has done anything. It records the
            // configuration the session ran under, which is the thing you most want and
            // least often have when a result looks strange months later.
            var first = NewRow("session_start", "log opened");
            first["note"] = string.Format(
                "unity={0} platform={1} device={2} pools_hue={3} pools_sat={4} " +
                "pools_lux={5} pools_tex={6} wall_value={7} albedo_ceiling={8}",
                Application.unityVersion, Application.platform, SystemInfo.deviceModel,
                Join(PoolConstants.Hues), Join(PoolConstants.Saturations),
                Join(PoolConstants.Brightnesses), string.Join("|", PoolConstants.Textures),
                PoolConstants.WallValue, RoomConfig.AlbedoCeiling);
            Commit(first);
        }

        static string Join(int[] values)
        {
            var parts = new List<string>();
            foreach (var v in values) parts.Add(v.ToString(CultureInfo.InvariantCulture));
            return string.Join("|", parts.ToArray());
        }

        static string Join(float[] values)
        {
            var parts = new List<string>();
            foreach (var v in values) parts.Add(v.ToString("0.###", CultureInfo.InvariantCulture));
            return string.Join("|", parts.ToArray());
        }

        void Update()
        {
            // The id can arrive after Start: the researcher sets it on the laptop and it
            // reaches the app over the network, by which point this file is already open
            // under whatever the scene shipped with. That produced logs named p01 for a
            // participant recorded everywhere else as 09 -- two names for one person, and
            // nothing to say which was right.
            if (openedFor != participantId && !string.IsNullOrEmpty(participantId))
            {
                Close();
                Open();
                openedFor = participantId;
            }

            if (writer == null || headTransform == null || poseSampleHz <= 0f) return;

            bool due = Time.realtimeSinceStartup >= nextPoseSample;
            bool moved = havePose &&
                         Quaternion.Angle(lastPoseRotation, headTransform.rotation) >= poseChangeDegrees;

            if (!due && !moved) return;

            nextPoseSample = Time.realtimeSinceStartup + (1f / poseSampleHz);
            lastPoseRotation = headTransform.rotation;
            havePose = true;

            var row = NewRow("head_pose", moved && !due ? "change" : "sample");
            Vector3 p = headTransform.position;
            Vector3 e = headTransform.rotation.eulerAngles;
            row["head_x"] = F(p.x); row["head_y"] = F(p.y); row["head_z"] = F(p.z);
            row["head_yaw"] = F(e.y); row["head_pitch"] = F(e.x); row["head_roll"] = F(e.z);
            Commit(row);
        }

        // ------------------------------------------------------------------ context

        /// <summary>Set once per phase so every later row is attributable without repeating it.</summary>
        public string Phase { get; set; }
        public int TrialIndex { get; set; }
        public string TrialId { get; set; }

        // ------------------------------------------------------------------ writing

        public void Write(string eventName, string detail)
        {
            Commit(NewRow(eventName, detail));
        }

        public void WriteRoom(string eventName, RoomConfig config, string detail)
        {
            var row = NewRow(eventName, detail);
            if (config != null)
            {
                row["room_id"] = config.Id;
                row["target_emotion"] = config.TargetEmotion;
                row["shape"] = config.Shape;
                row["hue"] = config.Hue.ToString(CultureInfo.InvariantCulture);
                row["saturation"] = F(config.Saturation);
                row["brightness"] = F(config.Brightness);
                row["texture"] = config.Texture;
            }
            Commit(row);
        }

        public void WriteGrid(string eventName, int valence, int arousal, float px, float py, string detail)
        {
            var row = NewRow(eventName, detail);
            row["valence"] = valence.ToString(CultureInfo.InvariantCulture);
            row["arousal"] = arousal.ToString(CultureInfo.InvariantCulture);
            row["pointer_x"] = F(px);
            row["pointer_y"] = F(py);
            Commit(row);
        }

        public void WriteValues(string eventName, string a, string b, string detail)
        {
            var row = NewRow(eventName, detail);
            row["value_a"] = a;
            row["value_b"] = b;
            Commit(row);
        }

        Dictionary<string, string> NewRow(string eventName, string detail)
        {
            var row = new Dictionary<string, string>();
            row["seq"] = (++sequence).ToString(CultureInfo.InvariantCulture);
            row["t_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            row["t_ms"] = ((long)((Time.realtimeSinceStartup - sessionStart) * 1000f))
                .ToString(CultureInfo.InvariantCulture);
            row["participant"] = participantId;
            row["phase"] = Phase;
            row["trial_index"] = TrialIndex > 0 ? TrialIndex.ToString(CultureInfo.InvariantCulture) : "";
            row["trial_id"] = TrialId;
            row["event"] = eventName;
            row["detail"] = detail;
            return row;
        }

        void Commit(Dictionary<string, string> row)
        {
            if (writer == null) return;

            var line = new StringBuilder();
            for (int i = 0; i < Columns.Length; i++)
            {
                if (i > 0) line.Append(',');
                string value;
                if (!row.TryGetValue(Columns[i], out value) || value == null) continue;
                line.Append(Escape(value));
            }

            try
            {
                writer.WriteLine(line.ToString());
                if (++sinceFlush >= flushEveryRows) Flush();
            }
            catch (Exception error)
            {
                Debug.LogError("EventLog: write failed: " + error.Message);
            }
        }

        public void Flush()
        {
            if (writer == null) return;
            writer.Flush();
            sinceFlush = 0;
        }

        public void Close()
        {
            if (writer == null) return;
            writer.Flush();
            writer.Dispose();
            writer = null;
        }

        static string F(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        static string Escape(string value)
        {
            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
