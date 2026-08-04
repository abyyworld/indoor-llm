// Runs one participant's session: load room, expose, hide, ask, log, repeat.
//
// Hangs off RoomLoader's RoomLoaded event, which was left as the hook for exactly this
// and is why the loader itself needed no changes.
//
// Sequence per trial, per design-spec section 6 with Mengkai's 1 Aug durations:
//
//     load config -> 20 s exposure -> room hidden -> affect grid -> response -> 15 s gap
//
// The room is hidden before the grid appears, deliberately. If the room is still visible
// while someone rates it, they rate what they are looking at rather than how the room
// made them feel, and the measure stops being an affect report.
//
// Responses are appended to disk as each one arrives rather than held in memory and
// written at the end. A session that crashes at trial 7 should not cost the first six.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace EmotionRooms
{
    [Serializable]
    public class TrialRecord
    {
        public string participant;
        public int trialIndex;
        public string trialId;
        public string targetEmotion;
        public string shape;
        public int valence;
        public int arousal;
        public long responseMs;
        public long exposureMs;
        public string startedUtc;

        public string ToCsvRow()
        {
            return string.Join(",", new[]
            {
                Escape(participant),
                trialIndex.ToString(CultureInfo.InvariantCulture),
                Escape(trialId),
                Escape(targetEmotion),
                Escape(shape),
                valence.ToString(CultureInfo.InvariantCulture),
                arousal.ToString(CultureInfo.InvariantCulture),
                responseMs.ToString(CultureInfo.InvariantCulture),
                exposureMs.ToString(CultureInfo.InvariantCulture),
                Escape(startedUtc),
            });
        }

        public static string CsvHeader()
        {
            return "participant,trial_index,trial_id,target_emotion,shape," +
                   "valence,arousal,response_ms,exposure_ms,started_utc";
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }

    public class TrialRunner : MonoBehaviour
    {
        [Header("Wiring")]
        public RoomLoader loader;
        public AffectGrid grid;

        [Tooltip("Event log. Optional but strongly advised: the summary CSV records the " +
                 "answer, this records what happened on the way to it.")]
        public EventLog events;

        [Tooltip("Root holding everything the participant sees between trials. Shown " +
                 "during the gap so they are not left in an empty void.")]
        public GameObject restScreen;

        [Header("Timing, seconds")]
        [Tooltip("Mengkai, 1 Aug 2026: 20 s. Was 30 s in the original spec.")]
        public float exposureSeconds = 20f;

        [Tooltip("Gap between committing a response and the next room appearing.")]
        public float transitionSeconds = 15f;

        [Header("Session")]
        public string participantId = "p00";

        [Tooltip("Session file produced by: python3 -m pipeline.cli export-unity")]
        public TextAsset sessionAsset;

        [Tooltip("Read the session from persistentDataPath instead of the asset above. " +
                 "This is the sideloading path, so a mis-generated session is a file " +
                 "swap rather than a rebuild.")]
        public string sessionFileName = "";

        [Header("Output")]
        [Tooltip("CSV written to Application.persistentDataPath. Appended per trial.")]
        public string responsesFileName = "responses.csv";

        public event Action<TrialRecord> TrialCompleted;
        public event Action SessionFinished;

        public bool IsRunning { get; private set; }
        public int CompletedTrials { get { return completed.Count; } }

        readonly List<TrialRecord> completed = new List<TrialRecord>();
        RoomBatch session;
        AffectResponse latest;
        bool hasLatest;

        string ResponsePath
        {
            get { return Path.Combine(Application.persistentDataPath, responsesFileName); }
        }

        void Awake()
        {
            if (grid != null) grid.Responded += OnResponded;
        }

        void OnDestroy()
        {
            if (grid != null) grid.Responded -= OnResponded;
        }

        void OnResponded(AffectResponse response)
        {
            latest = response;
            hasLatest = true;
        }

        /// <summary>Load the session file and run it start to finish.</summary>
        public void BeginSession()
        {
            if (IsRunning)
            {
                Debug.LogWarning("TrialRunner: a session is already running; ignoring.");
                return;
            }

            string json = ReadSessionJson();
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("TrialRunner: no session file. Assign sessionAsset or " +
                               "sessionFileName, and check persistentDataPath.");
                return;
            }

            session = RoomBatch.FromJson(json);
            if (session == null || session.rooms == null || session.rooms.Length == 0)
            {
                Debug.LogError("TrialRunner: session file parsed but holds no trials.");
                return;
            }

            // Validate every trial before showing the participant anything, rather than
            // discovering a bad config partway through and having to abandon the session.
            var problems = new List<string>();
            foreach (var room in session.rooms)
            {
                var errors = room.Validate();
                if (errors.Count > 0)
                    problems.Add(room.Id + ": " + string.Join("; ", errors.ToArray()));
            }
            if (problems.Count > 0)
            {
                Debug.LogError("TrialRunner: session contains invalid configs, refusing " +
                               "to run:\n  " + string.Join("\n  ", problems.ToArray()));
                return;
            }

            WriteHeaderIfNeeded();
            if (events != null)
            {
                events.Phase = "A";
                events.WriteValues("session_begin", participantId,
                    session.rooms.Length.ToString(), "trial runner starting");
            }
            StartCoroutine(RunSession());
        }

        IEnumerator RunSession()
        {
            IsRunning = true;
            completed.Clear();

            for (int i = 0; i < session.rooms.Length; i++)
            {
                yield return RunTrial(session.rooms[i], i + 1);
            }

            IsRunning = false;
            if (restScreen != null) restScreen.SetActive(true);
            if (grid != null) grid.Hide();

            Debug.Log(string.Format(
                "TrialRunner: session complete, {0} trials written to {1}",
                completed.Count, ResponsePath));

            var handler = SessionFinished;
            if (handler != null) handler();
        }

        IEnumerator RunTrial(RoomConfig config, int index)
        {
            if (restScreen != null) restScreen.SetActive(false);
            if (grid != null) grid.Hide();

            string startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            if (events != null)
            {
                events.TrialIndex = index;
                events.TrialId = config.Id;
                events.WriteRoom("trial_start", config, null);
            }

            loader.Load(config);
            if (events != null) events.WriteRoom("room_shown", config, "exposure begins");

            float exposureStart = Time.time;
            yield return new WaitForSeconds(exposureSeconds);
            long exposureMs = (long)((Time.time - exposureStart) * 1000f);

            // Hide the room BEFORE asking. Rating a room you can still see measures the
            // room, not how it made you feel.
            loader.HideRooms();
            if (events != null)
                events.WriteValues("room_hidden", exposureMs.ToString(), null,
                    "hidden before the grid, so the room is rated from memory not sight");

            hasLatest = false;
            grid.Show();
            if (events != null) events.Write("grid_shown", null);
            while (!hasLatest) yield return null;
            grid.Hide();
            if (events != null)
                events.WriteGrid("grid_hidden", latest.valence, latest.arousal, 0f, 0f, "response committed");

            var record = new TrialRecord
            {
                participant = participantId,
                trialIndex = index,
                trialId = string.IsNullOrEmpty(config.Id) ? "trial_" + index : config.Id,
                targetEmotion = config.TargetEmotion,
                shape = config.Shape,
                valence = latest.valence,
                arousal = latest.arousal,
                responseMs = latest.durationMs,
                exposureMs = exposureMs,
                startedUtc = startedUtc,
            };

            completed.Add(record);
            AppendRecord(record);

            var handler = TrialCompleted;
            if (handler != null) handler(record);

            if (restScreen != null) restScreen.SetActive(true);
            if (events != null) events.Write("trial_end", null);
            if (index < session.rooms.Length)
            {
                if (events != null) events.Write("transition_start", null);
                yield return new WaitForSeconds(transitionSeconds);
                if (events != null) events.Write("transition_end", null);
            }
        }

        string ReadSessionJson()
        {
            if (!string.IsNullOrEmpty(sessionFileName))
            {
                string path = Path.Combine(Application.persistentDataPath, sessionFileName);
                if (File.Exists(path)) return File.ReadAllText(path);
                Debug.LogWarning("TrialRunner: no session at " + path);
            }
            return sessionAsset != null ? sessionAsset.text : null;
        }

        void WriteHeaderIfNeeded()
        {
            if (File.Exists(ResponsePath)) return;
            File.WriteAllText(ResponsePath, TrialRecord.CsvHeader() + "\n", Encoding.UTF8);
        }

        void AppendRecord(TrialRecord record)
        {
            try
            {
                File.AppendAllText(ResponsePath, record.ToCsvRow() + "\n", Encoding.UTF8);
            }
            catch (Exception error)
            {
                // Never let a write failure end the session. The record is still in
                // memory and in the log, so the trial is recoverable.
                Debug.LogError("TrialRunner: could not append response: " + error.Message);
            }
        }

        [ContextMenu("Begin Session")]
        void BeginFromContextMenu()
        {
            BeginSession();
        }
    }
}
