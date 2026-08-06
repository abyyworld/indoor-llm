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

        [Tooltip("Continuous telemetry: one complete row per tick, every column filled. " +
                 "EventLog is the sparse per-change companion; both are wanted.")]
        public StudyTelemetry telemetry;

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
        public string sessionFileName = "session.json";

        [Tooltip("Run only the warm-up rooms and stop. Piloting mode: nothing is scored " +
                 "and no review block runs, so the kit can be exercised without burning " +
                 "a participant id. Set from the control panel.")]
        public bool practiceOnly = false;

        [Tooltip("Practice rooms shown before the real trials, from this file in the " +
                 "data folder. Leave empty to skip practice.\n\n" +
                 "The first rating anyone gives is not a rating of the room, it is them " +
                 "working out what the grid is, where the pointer is, and how hard to " +
                 "squeeze the trigger. Without practice that noise lands on whichever " +
                 "emotion happened to be first, and counterbalancing spreads it across " +
                 "conditions rather than removing it. Practice rooms use parameter " +
                 "combinations that are not in the eight cells, so nobody rates a study " +
                 "stimulus twice.")]
        public string practiceFileName = "practice.json";

        [Header("Output")]
        [Tooltip("CSV written to Application.persistentDataPath. Appended per trial.")]
        public string responsesFileName = "responses.csv";

        public event Action<TrialRecord> TrialCompleted;
        public event Action SessionFinished;

        public bool IsRunning { get; private set; }

        /// <summary>True while the practice rooms are running.</summary>
        public bool IsPractice { get { return isPractice; } }

        bool isPractice;
        RoomBatch practice;
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
        /// <summary>
        /// Stop the session where it stands and leave the data written so far intact.
        ///
        /// A withdrawal is not an error: the participant is exercising a right they were
        /// told they had. The trials they completed stay on disk with a marker in the
        /// event log saying where the session ended, so analysis can decide whether to
        /// keep a partial session rather than having that decision made for it by a
        /// deleted file.
        /// </summary>
        public void Abort(string reason)
        {
            if (!IsRunning) return;

            StopAllCoroutines();
            IsRunning = false;

            if (loader != null) loader.HideRooms();
            if (grid != null) grid.Hide();
            if (restScreen != null) restScreen.SetActive(false);

            if (telemetry != null)
            {
                telemetry.SetPhase("aborted");
                telemetry.Mark("session_aborted");
            }
            if (events != null)
            {
                events.WriteValues("session_aborted", reason,
                    completed.Count.ToString(CultureInfo.InvariantCulture), null);
                events.Flush();
            }

            Debug.LogWarning("TrialRunner: session aborted (" + reason + ") after " +
                             completed.Count + " trials. Data kept.");
        }

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

            // Practice is optional: a missing file is not an error, it just means no
            // practice. A malformed one is, since an invalid config must never be shown.
            practice = null;
            if (!string.IsNullOrEmpty(practiceFileName))
            {
                string practiceJson = ParticipantPacks.Read(participantId, practiceFileName);
                if (practiceJson != null)
                {
                    practice = RoomBatch.FromJson(practiceJson);
                    if (practice != null && practice.rooms != null)
                    {
                        foreach (var room in practice.rooms)
                        {
                            var practiceErrors = room.Validate();
                            if (practiceErrors.Count > 0)
                            {
                                Debug.LogError("TrialRunner: practice config " + room.Id +
                                    " is invalid, refusing to run: " +
                                    string.Join("; ", practiceErrors.ToArray()));
                                return;
                            }
                        }
                    }
                }
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
            if (telemetry != null)
            {
                telemetry.SetPhase(isPractice ? "practice" : "A");
                telemetry.latinSeed = 0;
            }
            if (events != null)
            {
                events.Phase = isPractice ? "practice" : "A";
                events.WriteValues("session_begin", participantId,
                    session.rooms.Length.ToString(), "trial runner starting");
            }
            StartCoroutine(RunSession());
        }

        IEnumerator RunSession()
        {
            IsRunning = true;
            completed.Clear();

            // Practice first. These rooms are rated exactly like real ones so the
            // rehearsal is genuine, but they are written with phase "practice" and never
            // enter `completed`, so they cannot reach the analysis by accident.
            if (practice != null && practice.rooms != null && practice.rooms.Length > 0)
            {
                isPractice = true;
                if (events != null) events.Write("practice_begin", null);
                for (int i = 0; i < practice.rooms.Length; i++)
                {
                    yield return RunTrial(practice.rooms[i], -(i + 1));
                }
                isPractice = false;
                if (events != null) events.Write("practice_end", null);
            }

            // Piloting stops here: warm-up rooms only, nothing scored, no review block.
            if (practiceOnly)
            {
                IsRunning = false;
                if (grid != null) grid.Hide();
                if (restScreen != null) restScreen.SetActive(true);
                if (events != null) events.Write("practice_only_end", null);
                Debug.Log("TrialRunner: practice-only run finished. No trials were scored.");

                var practiceHandler = SessionFinished;
                if (practiceHandler != null) practiceHandler();
                yield break;
            }

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
            if (telemetry != null)
            {
                telemetry.ClearTrialResponse();
                telemetry.SetTrial(index, config.Id);
                telemetry.SetSegment(true, false, false, false);
                telemetry.Mark("trial_start");
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
            if (telemetry != null)
            {
                telemetry.SetSegment(false, true, false, false);
                telemetry.Mark("grid_shown");
            }
            grid.Show();
            if (events != null) events.Write("grid_shown", null);
            while (!hasLatest) yield return null;
            if (telemetry != null)
                telemetry.SetResponse(latest.valence, latest.arousal, latest.durationMs);
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

            // Practice rooms are rated for real but never counted. Keeping them out of
            // `completed` is what stops them reaching responses.csv and the trial count.
            if (!isPractice)
            {
                completed.Add(record);
                AppendRecord(record);
            }

            var handler = TrialCompleted;
            if (handler != null) handler(record);

            if (restScreen != null) restScreen.SetActive(true);
            if (events != null) events.Write("trial_end", null);
            if (isPractice || index < session.rooms.Length)
            {
                if (telemetry != null)
                {
                    telemetry.SetSegment(false, false, true, false);
                    telemetry.Mark("transition_start");
                }
                if (events != null) events.Write("transition_start", null);
                yield return new WaitForSeconds(transitionSeconds);
                if (events != null) events.Write("transition_end", null);
            }
        }

        string ReadSessionJson()
        {
            if (!string.IsNullOrEmpty(sessionFileName))
            {
                string json = ParticipantPacks.Read(participantId, sessionFileName);
                if (json != null) return json;
                Debug.LogWarning("TrialRunner: no " + sessionFileName + " for " +
                                 participantId + " in the data folder or the shipped packs.");
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
