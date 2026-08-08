// The oversight block, run at the end of the session, in the same app.
//
// Sits AFTER all eight VR trials are finished. That ordering is the whole reason this
// works: asking "which variable is not consistent with the target emotion?" between trials would tell the participant
// the study is about whether rooms are consistent, and every affect rating after that would
// stop being a naive affective response and become an evaluation. By the time this
// screen appears the primary data is already collected and safe.
//
// Sequence per review trial:
//
//     show room (live, not a still) -> consistent with the target? -> which variable -> what would fit better
//
// Rooms are shown live through RoomLoader rather than as pre-rendered images, because
// the participant has just stood in these rooms and a still would be a different
// stimulus. It also means no rendering step is needed before a session can run.
//
// Ground truth comes from the block file built by:
//     python3 -m pipeline.cli oversight-block

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
    public class OversightTrialData
    {
        /// <summary>"own" or "yoked" -- whose correction actually gets applied.</summary>
        public string correction_source;

        /// <summary>Fixes the substituted value, so a yoked trial is reproducible.</summary>
        public int sham_seed;

        public string trial_id;
        public string condition;
        public string target_emotion_shown;
        public string rationale_shown;
        public RoomConfig stimulus;
        public OversightGroundTruth ground_truth;
    }

    [Serializable]
    public class OversightGroundTruth
    {
        public string swapped_field;

        /// <summary>The value before the swap -- the one that would repair the room.
        /// Read so a yoked substitution can avoid accidentally being correct.</summary>
        public string original_value;
        public string swapped_in_value;

        public string donor_emotion;
        public bool rationale_is_wrong;
    }

    [Serializable]
    public class OversightBlockData
    {
        public string participant;
        public List<OversightTrialData> trials = new List<OversightTrialData>();

        public static OversightBlockData FromJson(string json)
        {
            return JsonUtility.FromJson<OversightBlockData>(json);
        }
    }

    [Serializable]
    public class OversightRecord
    {
        public string participant;
        public int trialIndex;
        public string trialId;
        public string condition;
        public string targetEmotionShown;

        public bool detected;
        public float detectionConfidence;
        public string attributedField;
        public float attributionConfidence;
        public string correctedValue;

        /// <summary>What was actually applied, which is not always what they chose.</summary>
        public string appliedValue;
        public string correctionSource;

        public long durationMs;
        public string swappedField;      // ground truth, written alongside for convenience
        public string startedUtc;

        // The correction loop. -1 means not collected, which is different from a rating
        // of 0 and must not be averaged in as one.
        public int valenceBefore = -1;
        public int arousalBefore = -1;
        public int valenceAfter = -1;
        public int arousalAfter = -1;
        public bool correctionApplied;

        public string ToCsvRow()
        {
            var fields = new[]
            {
                participant, trialIndex.ToString(CultureInfo.InvariantCulture), trialId,
                condition, targetEmotionShown,
                detected ? "1" : "0",
                detectionConfidence.ToString("0.###", CultureInfo.InvariantCulture),
                attributedField ?? "",
                attributionConfidence.ToString("0.###", CultureInfo.InvariantCulture),
                correctedValue ?? "",
                appliedValue ?? "",
                correctionSource ?? "",
                durationMs.ToString(CultureInfo.InvariantCulture),
                swappedField ?? "",
                startedUtc,
                valenceBefore.ToString(CultureInfo.InvariantCulture),
                arousalBefore.ToString(CultureInfo.InvariantCulture),
                valenceAfter.ToString(CultureInfo.InvariantCulture),
                arousalAfter.ToString(CultureInfo.InvariantCulture),
                correctionApplied ? "1" : "0",
            };
            var row = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) row.Append(',');
                string value = fields[i] ?? "";
                row.Append(value.IndexOf(',') >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value);
            }
            return row.ToString();
        }

        public static string CsvHeader()
        {
            return "participant,trial_index,trial_id,condition,target_emotion_shown," +
                   "detected,detection_confidence,attributed_field,attribution_confidence," +
                   "corrected_value,applied_value,correction_source," +
                   "duration_ms,swapped_field,started_utc," +
                   "valence_before,arousal_before,valence_after,arousal_after," +
                   "correction_applied";
        }
    }

    public class OversightReview : MonoBehaviour
    {
        [Header("Wiring")]
        public RoomLoader loader;
        public EventLog events;
        public StudyTelemetry telemetry;

        [Tooltip("The same affect grid used in Phase A. Needed for the re-rating step: " +
                 "after a participant corrects a room they see their corrected version " +
                 "and rate it, which is what closes the delegation loop.")]
        public AffectGrid grid;

        [Tooltip("Screen asking whether the room looks consistent with the stated target emotion.")]
        public QuestionPanel detectionPanel;

        [Tooltip("Screen asking which variable is not consistent with the target emotion. One option per attributable " +
                 "variable, plus an explicit 'nothing looks swapped'.")]
        public QuestionPanel attributionPanel;

        [Tooltip("Screen asking what value would fit the target emotion better. Carries " +
                 "every pool value; the ones offered are narrowed to the attributed variable.")]
        public QuestionPanel correctionPanel;

        [Header("Session")]
        public string participantId = "p00";

        [Tooltip("Block file from: python3 -m pipeline.cli oversight-block")]
        public TextAsset blockAsset;

        public string blockFileName = "";

        [Tooltip("Seconds the room is shown before the questions appear. Shorter than " +
                 "the main trial exposure: they have already stood in these rooms and " +
                 "this is a review, not a fresh affective response.")]
        public float reviewExposureSeconds = 8f;

        [Header("Correction loop")]
        [Tooltip("After a correction, rebuild the room with it and let the participant " +
                 "experience and re-rate it.\n\n" +
                 "This is what makes the correction question answerable without needing " +
                 "an external reference. Rather than asking whether a correction moved " +
                 "toward some value we would have to define first, it asks whether the " +
                 "participant's own correction improved their own affective response. " +
                 "The reference is their first rating of the same room, so no distance " +
                 "metric across the pool is required.\n\n" +
                 "It is also what makes the participant a principal rather than a rater: " +
                 "they act, and then live with the result.")]
        public bool reRateCorrections = true;

        [Header("Output")]
        public string responsesFileName = "oversight_responses.csv";

        public event Action<OversightRecord> TrialCompleted;
        public event Action BlockFinished;

        public bool IsRunning { get; private set; }

        // Set by the UI before Commit* is called.
        [NonSerialized] public bool pendingDetected;
        [NonSerialized] public float pendingDetectionConfidence = 0.5f;
        [NonSerialized] public string pendingAttributedField;
        [NonSerialized] public float pendingAttributionConfidence = 0.5f;
        [NonSerialized] public string pendingCorrectedValue;

        bool detectionAnswered, attributionAnswered, correctionAnswered;
        string appliedValue = "", correctionSource = "";

        /// <summary>
        /// A legal value for this field that the participant did not choose.
        ///
        /// Matched in kind rather than random noise: it comes from the same pool, so a
        /// yoked correction is as plausible a repair as the participant's own and the
        /// comparison isolates whose choice it was, not whether it was sensible.
        /// </summary>
        string OtherValueFor(string field, string chosen, string original, int seed)
        {
            var values = PoolConstants.ValuesFor(field);
            if (values == null || values.Length < 2) return chosen;

            var options = new List<string>();
            foreach (var value in values)
            {
                if (value == chosen) continue;
                // Never the value that would actually repair the room. A substitution
                // that happened to be correct would read as a successful yoked
                // correction and blunt the very contrast it exists to draw.
                if (!string.IsNullOrEmpty(original) && value == original) continue;
                options.Add(value);
            }
            if (options.Count == 0) return chosen;

            // Seeded from the trial file, so the same participant on the same block
            // gets the same substitution on both platforms and it can be reconstructed
            // from the data afterwards.
            var rng = new System.Random(seed);
            return options[rng.Next(options.Count)];
        }
        AffectResponse lastRating;
        bool hasRating;
        OversightBlockData block;
        readonly List<OversightRecord> completed = new List<OversightRecord>();

        string ResponsePath
        {
            get { return Path.Combine(Application.persistentDataPath, responsesFileName); }
        }

        void Awake()
        {
            if (grid != null) grid.Responded += OnRated;
        }

        void OnDestroy()
        {
            if (grid != null) grid.Responded -= OnRated;
        }

        void OnRated(AffectResponse response)
        {
            lastRating = response;
            hasRating = true;
        }

        /// <summary>Call these from the UI buttons.</summary>
        public void CommitDetection(bool noticedSwap)
        {
            pendingDetected = noticedSwap;
            detectionAnswered = true;
        }

        public void CommitAttribution(string field)
        {
            pendingAttributedField = field;
            attributionAnswered = true;
        }

        public void CommitCorrection(string value)
        {
            pendingCorrectedValue = value;
            correctionAnswered = true;
        }

        /// <summary>Stop the review block, keeping everything already written.</summary>
        public void Abort(string reason)
        {
            if (!IsRunning) return;

            StopAllCoroutines();
            IsRunning = false;

            if (loader != null) loader.HideRooms();
            if (grid != null) grid.Hide();
            HideAll();

            if (telemetry != null) { telemetry.SetPhase("aborted"); telemetry.Mark("review_aborted"); }
            if (events != null)
            {
                events.WriteValues("review_aborted", reason,
                    completed.Count.ToString(CultureInfo.InvariantCulture), null);
                events.Flush();
            }

            Debug.LogWarning("OversightReview: block aborted (" + reason + ") after " +
                             completed.Count + " trials. Data kept.");
        }

        [Tooltip("How long the task briefing stays up before the first review trial.")]
        public float briefingSeconds = 15f;

        [Tooltip("Where the briefing is drawn. Wired by scene setup.")]
        public MessageBoard board;

        public void BeginBlock()
        {
            if (IsRunning) return;

            string json = ReadBlockJson();
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("OversightReview: no block file. Run 'oversight-block' and " +
                               "assign blockAsset or blockFileName.");
                return;
            }

            block = OversightBlockData.FromJson(json);
            if (block == null || block.trials == null || block.trials.Count == 0)
            {
                Debug.LogError("OversightReview: block parsed but holds no trials.");
                return;
            }

            if (!File.Exists(ResponsePath))
                File.WriteAllText(ResponsePath, OversightRecord.CsvHeader() + "\n", Encoding.UTF8);

            StartCoroutine(RunBlock());
        }

        IEnumerator RunBlock()
        {
            IsRunning = true;

            // Name the game before the first trial. Without this, participants answered
            // a different question than the one being scored: "do I agree this looks
            // depressed?" -- a taste judgment -- instead of "does this room match its
            // design?" -- an audit. One pilot answered "wrong" nearly every time on
            // exactly that reading. Stating the base rate is standard for a detection
            // task and is what separates disagreement with the design, which is not
            // measured here, from detection of tampering, which is.
            if (board != null)
                board.Show("Checking the system's work\n\n" +
                           "Each room was designed by a system to convey the feeling " +
                           "named with it.\nIn about half of the rooms, ONE setting was " +
                           "changed after design.\n\nSay whether each room is as " +
                           "designed or has been changed.\nYou are not judging whether " +
                           "the design is good -- only whether\nthe room matches it.");
            if (events != null) events.Write("review_briefing_shown", null);
            yield return new WaitForSeconds(briefingSeconds);
            if (board != null) board.Hide();
            completed.Clear();

            for (int i = 0; i < block.trials.Count; i++)
            {
                yield return RunTrial(block.trials[i], i + 1);
            }

            IsRunning = false;
            loader.HideRooms();
            HideAll();

            Debug.Log(string.Format("OversightReview: {0} trials written to {1}",
                completed.Count, ResponsePath));

            var handler = BlockFinished;
            if (handler != null) handler();
        }

        IEnumerator RunTrial(OversightTrialData trial, int index)
        {
            HideAll();
            string startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            float began = Time.time;

            // Same gate as everywhere else: nothing unvalidated becomes a stimulus.
            var errors = trial.stimulus != null ? trial.stimulus.Validate() : new List<string> { "missing stimulus" };
            if (errors.Count > 0)
            {
                Debug.LogError("OversightReview: skipping invalid stimulus on " +
                               trial.trial_id + ": " + string.Join("; ", errors.ToArray()));
                yield break;
            }

            if (telemetry != null)
            {
                telemetry.ClearTrialResponse();
                telemetry.SetPhase("B");
                telemetry.SetTrial(index, trial.trial_id);
                telemetry.SetReviewTrial(trial.condition,
                    trial.ground_truth != null ? trial.ground_truth.swapped_field : null);
                telemetry.SetSegment(false, false, false, true);
                telemetry.Mark("review_trial_start");
            }
            if (events != null)
            {
                events.Phase = "B";
                events.TrialIndex = index;
                events.TrialId = trial.trial_id;
                events.WriteValues("review_trial_start", trial.condition,
                    trial.ground_truth != null ? trial.ground_truth.swapped_field : null,
                    "shown_as=" + trial.target_emotion_shown);
            }

            loader.Load(trial.stimulus);
            if (events != null) events.WriteRoom("review_room_shown", trial.stimulus, trial.condition);
            yield return new WaitForSeconds(reviewExposureSeconds);

            // Rate the room as presented, BEFORE any of the diagnostic questions. This is
            // the baseline the corrected version is compared against, and it has to be
            // collected first: once someone has been asked which variable is off, they
            // are evaluating rather than reporting.
            int valenceBefore = -1, arousalBefore = -1;
            if (reRateCorrections && grid != null)
            {
                loader.HideRooms();
                hasRating = false;
                grid.Show();
                if (events != null) events.Write("review_rating_before_shown", null);
                while (!hasRating) yield return null;
                grid.Hide();
                valenceBefore = lastRating.valence;
                arousalBefore = lastRating.arousal;
                if (events != null)
                    events.WriteGrid("review_rating_before", valenceBefore, arousalBefore, 0f, 0f, null);
                loader.Load(trial.stimulus);
            }

            detectionAnswered = false;
            if (detectionPanel != null)
                // Wording and the yes/no mapping in StudyBootstrap.OnDetectionAnswered
                // are a pair: yes must always mean "changed", i.e. detected.
                detectionPanel.Show("Designed to convey: " + trial.target_emotion_shown +
                                    ".\nHas this room been changed from its design?");
            if (telemetry != null) { telemetry.SetReviewSegment(true, false, false); telemetry.Mark("detection_shown"); }
            if (events != null) events.Write("detection_shown", null);
            while (!detectionAnswered) yield return null;
            if (detectionPanel != null) detectionPanel.Hide();
            if (telemetry != null) telemetry.SetDetection(pendingDetected, pendingDetectionConfidence);
            if (events != null)
                events.WriteValues("detection_answered", pendingDetected ? "noticed_swap" : "looks_consistent",
                    pendingDetectionConfidence.ToString("0.##"), null);

            // Attribution and correction are only asked when they said something is
            // wrong. Forcing an attribution out of someone who noticed nothing would
            // manufacture data and wreck the false-alarm measure.
            pendingAttributedField = null;
            pendingCorrectedValue = null;
            appliedValue = "";
            correctionSource = "";

            if (pendingDetected)
            {
                attributionAnswered = false;
                if (attributionPanel != null)
                    attributionPanel.Show("Which one is wrong for " + trial.target_emotion_shown + "?");
                if (telemetry != null) { telemetry.SetReviewSegment(false, true, false); telemetry.Mark("attribution_shown"); }
                if (events != null) events.Write("attribution_shown", null);
                while (!attributionAnswered) yield return null;
                if (attributionPanel != null) attributionPanel.Hide();
                if (telemetry != null) telemetry.SetAttribution(pendingAttributedField, pendingAttributionConfidence);
                if (events != null)
                    events.WriteValues("attribution_answered", pendingAttributedField,
                        pendingAttributionConfidence.ToString("0.##"), null);

                // They said something was off, then could not point at anything. That is a
                // real response, not a missing one, so it is recorded and the correction
                // step is skipped rather than being forced.
                if (pendingAttributedField == "nothing_wrong")
                    pendingAttributedField = null;

                correctionAnswered = false;
                if (pendingAttributedField == null) correctionAnswered = true;
                else if (correctionPanel != null)
                    correctionPanel.Show("What should " + (pendingAttributedField ?? "it") +
                                         " be instead?", PoolConstants.ValuesFor(pendingAttributedField));
                if (telemetry != null) { telemetry.SetReviewSegment(false, false, true); telemetry.Mark("correction_shown"); }
                if (events != null) events.Write("correction_shown", null);
                while (!correctionAnswered) yield return null;
                if (correctionPanel != null) correctionPanel.Hide();
                if (telemetry != null) telemetry.SetCorrection(pendingCorrectedValue);
                if (events != null)
                    events.WriteValues("correction_answered", pendingCorrectedValue, null, null);
            }

            // The correction loop. Apply what they chose, show it, and let them rate the
            // room they themselves produced.
            int valenceAfter = -1, arousalAfter = -1;
            bool applied = false;

            if (reRateCorrections && grid != null && pendingDetected &&
                !string.IsNullOrEmpty(pendingAttributedField) &&
                !string.IsNullOrEmpty(pendingCorrectedValue))
            {
                // The yoked control.
                //
                // Half the corrected trials apply a value the participant did not
                // choose, unannounced. Without this comparison the correction effect has
                // no defence: someone who diagnoses a fault, picks a fix, watches it
                // applied and then rates the result rates it higher because it was
                // theirs. That is self-consistency, not correction quality, and it is
                // the alternative explanation a reviewer reaches for first.
                //
                // Both values are logged. Analysis compares own against yoked; if they
                // do not differ, that is a finding -- people believed their oversight
                // helped when it did not.
                bool yoked = trial.correction_source == "yoked";
                string original = trial.ground_truth != null
                    ? trial.ground_truth.original_value : null;
                string applyValue = yoked
                    ? OtherValueFor(pendingAttributedField, pendingCorrectedValue,
                                    original, trial.sham_seed)
                    : pendingCorrectedValue;

                var corrected = trial.stimulus.With(pendingAttributedField, applyValue);
                var problems = corrected != null ? corrected.Validate() : new List<string> { "unparseable correction" };

                if (corrected != null && problems.Count == 0)
                {
                    applied = true;
                    appliedValue = applyValue;
                    correctionSource = yoked ? "yoked" : "own";

                    loader.HideRooms();
                    loader.Load(corrected);
                    if (events != null)
                        events.WriteRoom("correction_room_shown", corrected,
                            pendingAttributedField + "=" + applyValue +
                            " source=" + correctionSource +
                            " chose=" + pendingCorrectedValue);
                    yield return new WaitForSeconds(reviewExposureSeconds);

                    loader.HideRooms();
                    hasRating = false;
                    grid.Show();
                    if (events != null) events.Write("review_rating_after_shown", null);
                    while (!hasRating) yield return null;
                    grid.Hide();
                    valenceAfter = lastRating.valence;
                    arousalAfter = lastRating.arousal;
                    if (events != null)
                        events.WriteGrid("review_rating_after", valenceAfter, arousalAfter, 0f, 0f, null);
                }
                else
                {
                    // Never show an out-of-pool room. A correction that cannot be applied
                    // is logged as not applied rather than silently skipped, so the
                    // analysis can tell "did not help" apart from "never happened".
                    Debug.LogWarning("OversightReview: correction not applicable on " +
                                     trial.trial_id + ": " +
                                     string.Join("; ", problems.ToArray()));
                    if (events != null)
                        events.WriteValues("correction_not_applied", pendingAttributedField,
                            pendingCorrectedValue, string.Join("; ", problems.ToArray()));
                }
            }

            loader.HideRooms();

            var record = new OversightRecord
            {
                participant = participantId,
                trialIndex = index,
                trialId = trial.trial_id,
                condition = trial.condition,
                targetEmotionShown = trial.target_emotion_shown,
                detected = pendingDetected,
                detectionConfidence = pendingDetectionConfidence,
                attributedField = pendingAttributedField,
                attributionConfidence = pendingAttributionConfidence,
                correctedValue = pendingCorrectedValue,
                appliedValue = appliedValue,
                correctionSource = correctionSource,
                durationMs = (long)((Time.time - began) * 1000f),
                swappedField = trial.ground_truth != null ? trial.ground_truth.swapped_field : null,
                startedUtc = startedUtc,
                valenceBefore = valenceBefore,
                arousalBefore = arousalBefore,
                valenceAfter = valenceAfter,
                arousalAfter = arousalAfter,
                correctionApplied = applied,
            };

            completed.Add(record);
            AppendRecord(record);
            if (events != null) events.Write("review_trial_end", null);

            var handler = TrialCompleted;
            if (handler != null) handler(record);
        }

        void HideAll()
        {
            if (detectionPanel != null) detectionPanel.Hide();
            if (attributionPanel != null) attributionPanel.Hide();
            if (correctionPanel != null) correctionPanel.Hide();
        }

        string ReadBlockJson()
        {
            string name = string.IsNullOrEmpty(blockFileName) ? "oversight.json" : blockFileName;

            string json = ParticipantPacks.Read(participantId, name);
            if (json != null) return json;

            Debug.LogWarning("OversightReview: no " + name + " for '" + participantId + "'.");
            return blockAsset != null ? blockAsset.text : null;
        }

        void AppendRecord(OversightRecord record)
        {
            try
            {
                File.AppendAllText(ResponsePath, record.ToCsvRow() + "\n", Encoding.UTF8);
            }
            catch (Exception error)
            {
                Debug.LogError("OversightReview: could not append: " + error.Message);
            }
        }
    }
}
