// The rationale check: does the stated reasoning match the room?
//
// Its own short block, deliberately separate from the detection block. In the main block
// the room is broken and the question is "does anything look wrong". Here the room is
// always correct and only the stated reasoning is sometimes borrowed from a different
// emotion -- so the same question would be answered "no, nothing is wrong", correctly,
// and scored as a miss. Half the trials carry the model's own rationale and half carry
// another emotion's, which makes this its own even-split detection task with its own
// d-prime rather than something that contaminates the other one.
//
// Runs after the oversight block, on the same participant, ~3 minutes.

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
    public class RationaleTrialData
    {
        public string trial_id;
        public string condition;                 // rationale_matched | rationale_mismatched
        public string target_emotion_shown;
        public string rationale_shown;
        public RoomConfig stimulus;
        public OversightGroundTruth ground_truth;
    }

    [Serializable]
    public class RationaleBlockData
    {
        public string participant;
        public string question;
        public List<RationaleTrialData> trials = new List<RationaleTrialData>();

        public static RationaleBlockData FromJson(string json)
        {
            try { return JsonUtility.FromJson<RationaleBlockData>(json); }
            catch (Exception e)
            {
                Debug.LogError("RationaleReview: could not parse the block. " + e.Message);
                return null;
            }
        }
    }

    public class RationaleReview : MonoBehaviour
    {
        [Header("Wiring")]
        public RoomLoader loader;
        public EventLog events;
        public StudyTelemetry telemetry;
        public MessageBoard board;

        [Tooltip("Reused from the oversight block: a two-option panel is a two-option panel.")]
        public QuestionPanel answerPanel;

        [Header("Session")]
        public string participantId = "p01";
        public string blockFileName = "rationale.json";

        [Tooltip("Seconds the room is shown with its stated reasoning before the question.")]
        public float exposureSeconds = 8f;

        public string responsesFileName = "rationale_responses.csv";

        public event Action BlockFinished;
        public bool IsRunning { get; private set; }

        [NonSerialized] public bool pendingMatches;
        bool answered;
        RationaleBlockData block;
        int completed;

        string ResponsePath
        {
            get { return Path.Combine(Application.persistentDataPath, responsesFileName); }
        }

        /// <summary>Called from the input driver when the panel resolves.</summary>
        public void CommitAnswer(bool matches)
        {
            pendingMatches = matches;
            answered = true;
        }

        public void BeginBlock()
        {
            if (IsRunning) return;

            string json = ParticipantPacks.Read(participantId, blockFileName);
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("RationaleReview: no " + blockFileName + " for " +
                                 participantId + "; skipping the rationale block.");
                Finish();
                return;
            }

            block = RationaleBlockData.FromJson(json);
            if (block == null || block.trials == null || block.trials.Count == 0)
            {
                Debug.LogWarning("RationaleReview: block parsed but holds no trials.");
                Finish();
                return;
            }

            if (!File.Exists(ResponsePath))
            {
                File.WriteAllText(ResponsePath,
                    "participant,trial_index,trial_id,condition,target_emotion_shown," +
                    "rationale_is_wrong,said_matches,correct,response_ms,utc\n",
                    Encoding.UTF8);
            }

            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            IsRunning = true;
            completed = 0;

            if (board != null)
                board.Show("Next: a few rooms with the reasoning\nthe system gave for them.");
            yield return new WaitForSeconds(4f);

            for (int i = 0; i < block.trials.Count; i++)
            {
                yield return RunTrial(block.trials[i], i + 1);
            }

            Finish();
        }

        IEnumerator RunTrial(RationaleTrialData trial, int index)
        {
            if (board != null) board.Hide();

            // Same gate as everywhere else: nothing unvalidated becomes a stimulus.
            var errors = trial.stimulus != null
                ? trial.stimulus.Validate()
                : new List<string> { "missing stimulus" };
            if (errors.Count > 0)
            {
                Debug.LogError("RationaleReview: skipping invalid stimulus on " +
                               trial.trial_id + ": " + string.Join("; ", errors.ToArray()));
                yield break;
            }

            if (telemetry != null)
            {
                telemetry.ClearTrialResponse();
                telemetry.SetPhase("B-rationale");
                telemetry.SetTrial(index, trial.trial_id);
                telemetry.SetReviewTrial(trial.condition, null);
                telemetry.Mark("rationale_trial_start");
            }
            if (events != null)
            {
                events.Phase = "B-rationale";
                events.TrialIndex = index;
                events.TrialId = trial.trial_id;
                events.WriteValues("rationale_trial_start", trial.condition,
                    trial.target_emotion_shown, null);
            }

            loader.Load(trial.stimulus);

            // The reasoning is shown WITH the room, for the whole exposure. Showing it
            // afterwards would make this a memory task; the question is whether the
            // words describe what is in front of them.
            if (board != null)
                board.Show("The system said it built this room to feel " +
                           trial.target_emotion_shown + ",\nbecause:\n\n\"" +
                           (trial.rationale_shown ?? "") + "\"");

            yield return new WaitForSeconds(exposureSeconds);

            answered = false;
            float began = Time.time;
            if (answerPanel != null)
                answerPanel.Show("Does that reasoning match this room?", MatchOptions);
            if (events != null) events.Write("rationale_question_shown", null);

            while (!answered) yield return null;
            if (answerPanel != null) answerPanel.Hide();
            if (board != null) board.Hide();
            loader.HideRooms();

            bool isWrong = trial.ground_truth != null && trial.ground_truth.rationale_is_wrong;
            bool correct = pendingMatches != isWrong;
            long ms = (long)((Time.time - began) * 1000f);

            Append(new[]
            {
                participantId,
                index.ToString(CultureInfo.InvariantCulture),
                trial.trial_id, trial.condition, trial.target_emotion_shown,
                isWrong ? "1" : "0",
                pendingMatches ? "1" : "0",
                correct ? "1" : "0",
                ms.ToString(CultureInfo.InvariantCulture),
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            });
            completed++;

            if (events != null)
                events.WriteValues("rationale_answered",
                    pendingMatches ? "matches" : "does_not_match",
                    correct ? "correct" : "incorrect", null);

            yield return new WaitForSeconds(2f);
        }

        static readonly string[] MatchOptions = { "yes", "no" };

        void Append(string[] fields)
        {
            try
            {
                var row = new StringBuilder();
                for (int i = 0; i < fields.Length; i++)
                {
                    if (i > 0) row.Append(',');
                    string value = fields[i] ?? "";
                    row.Append(value.IndexOf(',') >= 0
                        ? "\"" + value.Replace("\"", "\"\"") + "\"" : value);
                }
                File.AppendAllText(ResponsePath, row.ToString() + "\n", Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogError("RationaleReview: could not write a response. " + e.Message);
            }
        }

        void Finish()
        {
            IsRunning = false;
            if (loader != null) loader.HideRooms();
            if (answerPanel != null) answerPanel.Hide();

            Debug.Log("RationaleReview: " + completed + " trials written to " + ResponsePath);

            var handler = BlockFinished;
            if (handler != null) handler();
        }

        public void Abort(string reason)
        {
            if (!IsRunning) return;
            StopAllCoroutines();
            if (events != null) events.WriteValues("rationale_aborted", reason, null, null);
            Finish();
        }
    }
}
