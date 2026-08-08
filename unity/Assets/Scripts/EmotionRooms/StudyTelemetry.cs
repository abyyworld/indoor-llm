// Continuous telemetry. One complete row per tick, every column populated every row.
//
// This is the wide-and-dense model: sample at a fixed rate, repeat every value on every
// row, and write an extra row immediately whenever anything discrete happens. It is
// verbose on purpose. A reviewer asking "what was the participant looking at when they
// gave that rating" should be able to answer it from the file rather than from trust.
//
// EventLog is the other half and both are kept. EventLog is sparse and event-typed: one
// row per discrete change, easy to read by eye. This is dense and uniform: every row has
// the same shape, so it loads straight into pandas or R with no reshaping and no missing
// columns to reason about.
//
//   telemetry_<participant>_<timestamp>.csv     this file, every column every row
//   <participant>_<timestamp>_events.csv        EventLog, one row per discrete change
//   responses.csv                               one row per trial, the answer only
//   oversight_responses.csv                     one row per review trial
//
// Writes to Application.persistentDataPath/logs/.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace EmotionRooms
{
    public class StudyTelemetry : MonoBehaviour
    {
        [Header("Identity")]
        public string participantId = "p00";

        [Tooltip("Position in the recruitment order, which is also the counterbalancing " +
                 "row. Logged so the trial order can be reconstructed from the data alone.")]
        public int sessionOrder = 0;

        [Tooltip("The seed the session was built with. Logged for the same reason.")]
        public int latinSeed = 0;

        [Header("Sources")]
        public RoomLoader loader;
        public AffectGrid grid;
        public TrialRunner trialRunner;
        public OversightReview review;
        public Transform headTransform;
        public Transform pointerOrigin;

        [Header("Sampling")]
        [Tooltip("Rows per second. 20 is dense enough to reconstruct a head turn and " +
                 "still leaves a 45-minute session around 8 MB.")]
        public float sampleHz = 20f;

        [Tooltip("Also write a row the instant anything discrete happens, on top of the " +
                 "fixed-rate samples, so no event can fall between two ticks.")]
        public bool rowOnEvent = true;

        [Header("Output")]
        public string folder = "logs";
        [Tooltip("Flush every N rows. Lower is safer if the app dies, higher is faster.")]
        public int flushEveryRows = 20;

        public string Path { get; private set; }
        public long RowsWritten { get; private set; }

        StreamWriter writer;
        float sessionStart;
        float nextSample;
        int sinceFlush;

        // Current state, updated by whoever knows and repeated on every row until it
        // changes. This is what makes a row self-contained.
        string phase = "";
        string marker = "";
        int trialIndex;
        string trialId = "";
        string condition = "";
        string swappedField = "";
        bool isExposure, isRating, isTransition, isReview;
        bool isDetection, isAttribution, isCorrection;
        int selectedValence = -1, selectedArousal = -1;
        long responseMs = -1;
        bool detected;
        float detectionConfidence = -1f, attributionConfidence = -1f;
        string attributedField = "", correctedValue = "";

        static readonly string[] Columns =
        {
            // identity and clock
            "ParticipantID", "SessionOrder", "LatinSeed", "Time_s", "UnixMs", "Frame",
            // where we are
            "Phase", "Marker", "TrialIndex", "TrialID", "TargetEmotion", "Shape", "RoomID",
            // phase flags, one column each so filtering never needs string parsing
            "IsExposure", "IsRating", "IsTransition", "IsReview",
            "IsDetectionPhase", "IsAttributionPhase", "IsCorrectionPhase",
            "IsRoomVisible", "IsGridVisible", "IsAwaitingResponse",
            // the manipulated variables, as loaded
            "Hue", "Saturation", "BrightnessLux", "Texture", "Roughness",
            "WallColor_R", "WallColor_G", "WallColor_B", "LightIntensity", "LightColorTempK",
            // head
            "Head_PosX", "Head_PosY", "Head_PosZ",
            "Head_RotX", "Head_RotY", "Head_RotZ",
            "Head_FwdX", "Head_FwdY", "Head_FwdZ",
            // pointer
            "Pointer_PosX", "Pointer_PosY", "Pointer_PosZ",
            "Pointer_FwdX", "Pointer_FwdY", "Pointer_FwdZ",
            // grid interaction
            "GridHit", "HoverValence", "HoverArousal", "GridHit_LocalX", "GridHit_LocalY",
            "SelectedValence", "SelectedArousal", "ResponseMs",
            // review block
            "Condition", "SwappedField", "Detected", "DetectionConfidence",
            "AttributedField", "AttributionConfidence", "CorrectedValue",
            // system
            "DeltaMs", "FPS",
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
            Mark("session_end");
            Close();
        }

        void OnApplicationPause(bool paused)
        {
            Mark(paused ? "app_paused" : "app_resumed");
            Flush();
        }

        public void Open()
        {
            if (writer != null) return;

            string dir = System.IO.Path.Combine(Application.persistentDataPath, folder);
            Directory.CreateDirectory(dir);
            string stamp = System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            Path = System.IO.Path.Combine(dir, "telemetry_" + participantId + "_" + stamp + ".csv");

            writer = new StreamWriter(Path, false, Encoding.UTF8);
            writer.WriteLine(string.Join(",", Columns));

            sessionStart = Time.realtimeSinceStartup;
            Mark("session_start");
        }

        /// <summary>Write a row now and tag it. Use for anything discrete.</summary>
        public void Mark(string what)
        {
            marker = what;
            if (rowOnEvent) WriteRow();
            marker = "";
        }

        // ---------------------------------------------------------------- state setters

        public void SetPhase(string value) { phase = value; Mark("phase=" + value); }

        public void SetTrial(int index, string id)
        {
            trialIndex = index;
            trialId = id ?? "";
        }

        public void SetSegment(bool exposure, bool rating, bool transition, bool reviewing)
        {
            isExposure = exposure; isRating = rating;
            isTransition = transition; isReview = reviewing;
        }

        public void SetReviewSegment(bool detection, bool attribution, bool correction)
        {
            isDetection = detection; isAttribution = attribution; isCorrection = correction;
        }

        public void SetReviewTrial(string cond, string swapped)
        {
            condition = cond ?? "";
            swappedField = swapped ?? "";
        }

        public void SetResponse(int valence, int arousal, long ms)
        {
            selectedValence = valence; selectedArousal = arousal; responseMs = ms;
            Mark("response");
        }

        public void SetDetection(bool value, float confidence)
        {
            detected = value; detectionConfidence = confidence;
            Mark("detection");
        }

        public void SetAttribution(string field, float confidence)
        {
            attributedField = field ?? ""; attributionConfidence = confidence;
            Mark("attribution");
        }

        public void SetCorrection(string value)
        {
            correctedValue = value ?? "";
            Mark("correction");
        }

        /// <summary>Clear the per-trial answers so one trial's response cannot bleed
        /// into the next trial's rows and look like an answer that was never given.</summary>
        public void ClearTrialResponse()
        {
            selectedValence = selectedArousal = -1;
            responseMs = -1;
            detected = false;
            detectionConfidence = attributionConfidence = -1f;
            attributedField = correctedValue = "";
            condition = swappedField = "";
        }

        // ---------------------------------------------------------------- the row

        void Update()
        {
            // The id can arrive after Start: the researcher sets it on the laptop and it
            // reaches the app over the network, by which point this file is already open
            // under whatever the scene shipped with. That produced telemetry named p01 for
            // a participant recorded everywhere else as 09 -- two names for one person,
            // and nothing to say which was right.
            if (openedFor != participantId && !string.IsNullOrEmpty(participantId))
            {
                Close();
                Open();
                openedFor = participantId;
            }

            if (writer == null || sampleHz <= 0f) return;
            if (Time.realtimeSinceStartup < nextSample) return;

            nextSample = Time.realtimeSinceStartup + (1f / sampleHz);
            WriteRow();
        }

        void WriteRow()
        {
            if (writer == null) return;

            var row = new List<string>(Columns.Length);

            row.Add(participantId);
            row.Add(I(sessionOrder));
            row.Add(I(latinSeed));
            row.Add(F((Time.realtimeSinceStartup - sessionStart), 4));
            row.Add(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        .ToString(CultureInfo.InvariantCulture));
            row.Add(I(Time.frameCount));

            row.Add(phase);
            row.Add(marker);
            row.Add(I(trialIndex));
            row.Add(trialId);

            var config = loader != null ? loader.Current : null;
            row.Add(config != null ? Safe(config.TargetEmotion) : "");
            row.Add(config != null ? Safe(config.Shape) : "");
            row.Add(config != null ? Safe(config.Id) : "");

            row.Add(B(isExposure)); row.Add(B(isRating));
            row.Add(B(isTransition)); row.Add(B(isReview));
            row.Add(B(isDetection)); row.Add(B(isAttribution)); row.Add(B(isCorrection));

            bool roomVisible = loader != null &&
                ((loader.linearRoomRoot != null && loader.linearRoomRoot.activeInHierarchy) ||
                 (loader.curvedRoomRoot != null && loader.curvedRoomRoot.activeInHierarchy));
            row.Add(B(roomVisible));
            row.Add(B(grid != null && grid.gameObject.activeInHierarchy));
            row.Add(B(grid != null && grid.IsAwaitingResponse));

            if (config != null)
            {
                row.Add(I(config.Hue));
                row.Add(F(config.Saturation, 4));
                row.Add(F(config.Brightness, 2));
                row.Add(Safe(config.Texture));
                row.Add(Safe(config.Roughness));
                var c = config.WallColor();
                row.Add(F(c.r, 4)); row.Add(F(c.g, 4)); row.Add(F(c.b, 4));
                row.Add(loader.roomLight != null ? F(loader.roomLight.intensity, 4) : "");
            }
            else
            {
                for (int i = 0; i < 9; i++) row.Add("");
            }
            row.Add("4500");   // fixed for the whole study; logged so it is in the record

            AddPose(row, headTransform, true);
            AddPose(row, pointerOrigin, false);

            // Grid interaction, resolved live so hover is captured even between events.
            int hv = -1, ha = -1; float lx = 0f, ly = 0f; bool hit = false;
            if (grid != null && grid.IsAwaitingResponse)
            {
                Ray ray;
                if (TryRay(out ray))
                {
                    Vector3 point;
                    if (grid.TryResolve(ray, out hv, out ha, out point))
                    {
                        hit = true;
                        var local = grid.transform.InverseTransformPoint(point);
                        lx = local.x; ly = local.y;
                    }
                }
            }
            row.Add(B(hit));
            row.Add(I(hv)); row.Add(I(ha));
            row.Add(F(lx, 4)); row.Add(F(ly, 4));
            row.Add(I(selectedValence)); row.Add(I(selectedArousal));
            row.Add(responseMs.ToString(CultureInfo.InvariantCulture));

            row.Add(condition); row.Add(swappedField);
            row.Add(B(detected)); row.Add(F(detectionConfidence, 3));
            row.Add(attributedField); row.Add(F(attributionConfidence, 3));
            row.Add(correctedValue);

            row.Add(F(Time.unscaledDeltaTime * 1000f, 3));
            row.Add(F(Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f, 2));

            try
            {
                writer.WriteLine(string.Join(",", row));
                RowsWritten++;
                if (++sinceFlush >= flushEveryRows) Flush();
            }
            catch (System.Exception error)
            {
                Debug.LogError("StudyTelemetry: write failed: " + error.Message);
            }
        }

        void AddPose(List<string> row, Transform t, bool includeForward)
        {
            if (t == null)
            {
                row.Add(""); row.Add(""); row.Add("");
                row.Add(""); row.Add(""); row.Add("");
                if (includeForward) { row.Add(""); row.Add(""); row.Add(""); }
                return;
            }

            var p = t.position;
            var e = t.rotation.eulerAngles;
            row.Add(F(p.x, 5)); row.Add(F(p.y, 5)); row.Add(F(p.z, 5));
            row.Add(F(e.x, 4)); row.Add(F(e.y, 4)); row.Add(F(e.z, 4));
            if (includeForward)
            {
                var f = t.forward;
                row.Add(F(f.x, 5)); row.Add(F(f.y, 5)); row.Add(F(f.z, 5));
            }
        }

        bool TryRay(out Ray ray)
        {
            if (pointerOrigin != null)
            {
                ray = new Ray(pointerOrigin.position, pointerOrigin.forward);
                return true;
            }
            var camera = Camera.main;
            if (camera == null) { ray = default(Ray); return false; }
            ray = camera.ScreenPointToRay(Input.mousePosition);
            return true;
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
            writer.Flush(); writer.Dispose(); writer = null;
        }

        static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        static string B(bool v) { return v ? "1" : "0"; }
        static string F(float v, int dp)
        {
            return v.ToString("F" + dp.ToString(CultureInfo.InvariantCulture),
                              CultureInfo.InvariantCulture);
        }
        static string Safe(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v.IndexOf(',') >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
    }
}
