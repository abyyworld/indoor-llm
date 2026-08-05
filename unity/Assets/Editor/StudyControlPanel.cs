// The researcher-facing window. Everything needed to run one participant, in the order
// it is needed, with the state of each step visible.
//
// This exists because the study was previously driven from three menu items, a component
// context menu and four separately-typed participant ids. That is fine for the person who
// wrote it and hostile to everyone else, including the same person in six weeks. A study
// that is fiddly to run gets run inconsistently, and inconsistency between participants
// is measurement error you cannot subtract later.

using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EmotionRooms.EditorTools
{
    public class StudyControlPanel : EditorWindow
    {
        const string ConsentUrlKey = "EmotionRooms.ConsentUrl";
        const string QuestionnaireUrlKey = "EmotionRooms.QuestionnaireUrl";
        const string RepoKey = "EmotionRooms.RepoPath";

        // Buttons queue their work instead of doing it inline.
        //
        // An exception thrown from inside OnGUI unwinds between a BeginVertical and its
        // EndVertical, and IMGUI then reports "Invalid GUILayout state" on every repaint
        // afterwards -- so one bad session file turned into a permanently broken panel,
        // and the error you actually needed to read was buried under GUI noise. Running
        // the action after layout has finished keeps a failure to one clear message.
        Action pending;

        string participant = "";
        string consentUrl = "";
        string questionnaireUrl = "";
        string repoPath = "";
        Vector2 scroll;
        string lastCommandOutput = "";

        [MenuItem("Emotion Rooms/Study Control Panel _%#e", priority = -100)]
        public static void Open()
        {
            var window = GetWindow<StudyControlPanel>(false, "Study Control");
            window.minSize = new Vector2(380f, 560f);
            window.Show();
        }

        void OnEnable()
        {
            consentUrl = EditorPrefs.GetString(ConsentUrlKey, "");
            questionnaireUrl = EditorPrefs.GetString(QuestionnaireUrlKey, "");
            repoPath = EditorPrefs.GetString(RepoKey, GuessRepoPath());
            participant = NextParticipantId();
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            Title("Emotion Rooms study");
            EditorGUILayout.LabelField(
                "Run the steps top to bottom. Each one turns green when it is done.",
                EditorStyles.wordWrappedMiniLabel);
            Space();

            var bootstrap = UnityEngine.Object.FindFirstObjectByType<StudyBootstrap>();

            DrawSceneStep(bootstrap);
            DrawModeStep(bootstrap);
            DrawParticipantStep(bootstrap);
            DrawConsentStep(bootstrap);
            DrawRunStep(bootstrap);
            DrawAfterStep();

            if (!string.IsNullOrEmpty(lastCommandOutput))
            {
                Space();
                EditorGUILayout.LabelField("Last command", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(lastCommandOutput,
                    EditorStyles.textArea, GUILayout.Height(110f));
            }

            EditorGUILayout.EndScrollView();

            if (pending != null && Event.current.type == EventType.Repaint)
            {
                var action = pending;
                pending = null;
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    lastCommandOutput = e.Message;
                    Debug.LogError("Study Control: " + e.Message + "\n" + e.StackTrace);
                }
                Repaint();
            }
        }

        // ------------------------------------------------------------------ steps

        void DrawSceneStep(StudyBootstrap bootstrap)
        {
            bool ready = bootstrap != null;
            var models = StudySceneSetup.FindFurnitureSet();
            string furniture = models == null
                ? "Placeholder furniture (no FurnitureSet in the project)."
                : models.MissingCount() == 0
                    ? "Real furniture models."
                    : models.MissingCount() + " of 7 slots still on placeholders.";
            Step(1, "Scene", ready, (ready ? "Built. " : "Not built yet. ") + furniture);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button(ready ? "Rebuild study scene" : "Build study scene",
                                     GUILayout.Height(24f)))
                    pending = () => { StudySceneSetup.SetUp(); participant = NextParticipantId(); };
            }
            if (ready && GUILayout.Button("Check scene")) pending = StudySceneSetup.CheckScene;
            EndStep();
        }

        void DrawModeStep(StudyBootstrap bootstrap)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Mode", EditorStyles.boldLabel);

            if (bootstrap == null)
            {
                EditorGUILayout.LabelField("Build the scene first.", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            int mode = bootstrap.practiceOnly ? 1 : 0;
            int picked = GUILayout.Toolbar(mode, new[] { "Real session", "Practice only" });
            if (picked != mode)
            {
                bootstrap.practiceOnly = picked == 1;
                EditorUtility.SetDirty(bootstrap);
            }

            EditorGUILayout.LabelField(
                bootstrap.practiceOnly
                    ? "Two warm-up rooms, then stop. Nothing scored, no review block. " +
                      "Use this to pilot the kit or train a researcher without burning a " +
                      "participant id."
                    : "Warm-up rooms, eight scored trials, the review block, then the " +
                      "after forms.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUI.BeginChangeCheck();
            bool warmUp = EditorGUILayout.ToggleLeft(
                "Show warm-up rooms before the first scored trial", bootstrap.practiceRooms);
            if (EditorGUI.EndChangeCheck())
            {
                bootstrap.practiceRooms = warmUp;
                EditorUtility.SetDirty(bootstrap);
            }

            EditorGUILayout.EndVertical();
        }

        void DrawParticipantStep(StudyBootstrap bootstrap)
        {
            string dest = Application.persistentDataPath;
            bool haveSession = File.Exists(Path.Combine(dest, "session.json"));
            bool haveBlock = File.Exists(Path.Combine(dest, "oversight.json"));
            bool ready = haveSession && haveBlock;

            Step(2, "Participant and stimuli", ready,
                ready ? "session.json and oversight.json are in place."
                      : "Missing " + (haveSession ? "" : "session.json ") +
                        (haveBlock ? "" : "oversight.json"));

            EditorGUILayout.BeginHorizontal();
            participant = EditorGUILayout.TextField("Participant", participant);
            if (GUILayout.Button("Next", GUILayout.Width(50f)))
            {
                participant = NextParticipantId();
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                " ", "Auto-suggested from the data folder. Never reuse an id.",
                EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Prepare " + participant + "  (build session + review block)",
                                     GUILayout.Height(26f)))
                    pending = () => PrepareParticipant(bootstrap);
            }
            EndStep();
        }

        void DrawConsentStep(StudyBootstrap bootstrap)
        {
            bool ready = bootstrap != null && bootstrap.ConsentConfirmed;
            Step(3, "Consent  (before the headset goes on)", ready,
                ready ? "Recorded for this session."
                      : "Take consent on the web form, then confirm here.");

            EditorGUI.BeginChangeCheck();
            consentUrl = EditorGUILayout.TextField("Consent form URL", consentUrl);
            if (!string.IsNullOrEmpty(consentUrl) && !consentUrl.Contains("PARTICIPANT_ID"))
                EditorGUILayout.HelpBox(
                    "This link has no PARTICIPANT_ID placeholder, so the id will not be " +
                    "prefilled and the participant has to type it. Use the prefill link " +
                    "that build-forms.gs prints, not the form's plain share URL.",
                    MessageType.Warning);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(ConsentUrlKey, consentUrl);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(consentUrl)))
            {
                if (GUILayout.Button("Open consent form for " + participant, GUILayout.Height(24f)))
                    Application.OpenURL(WithParticipant(consentUrl, participant));
            }

            using (new EditorGUI.DisabledScope(bootstrap == null || ready))
            {
                if (GUILayout.Button("Consent was taken — record it", GUILayout.Height(24f)))
                    pending = () =>
                    {
                        bootstrap.participantId = participant;
                        bootstrap.ApplyParticipantId();
                        bootstrap.ConfirmConsentTaken();
                    };
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox(
                    "Recording consent writes consent_log.csv. Do it in play mode so it " +
                    "lands in the same session as the data.", MessageType.None);
            EndStep();
        }

        void DrawRunStep(StudyBootstrap bootstrap)
        {
            bool running = Application.isPlaying &&
                           bootstrap != null && bootstrap.trialRunner != null &&
                           bootstrap.trialRunner.IsRunning;
            Step(4, "Run", running, running
                ? "Trial " + bootstrap.trialRunner.CompletedTrials + " of 8 complete."
                : Application.isPlaying ? "Ready to begin." : "Press Play first.");

            if (!Application.isPlaying)
            {
                if (GUILayout.Button("Enter play mode", GUILayout.Height(26f)))
                    EditorApplication.isPlaying = true;
            }
            else
            {
                using (new EditorGUI.DisabledScope(bootstrap == null || running))
                {
                    if (GUILayout.Button("Begin study", GUILayout.Height(30f)))
                        pending = bootstrap.BeginStudy;
                }
                using (new EditorGUI.DisabledScope(bootstrap == null))
                {
                    var previous = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.75f, 0.75f);
                    if (GUILayout.Button("Participant withdrew — stop now"))
                    {
                        if (EditorUtility.DisplayDialog("Withdraw",
                            "End " + participant + "'s session now?\n\nEverything recorded " +
                            "so far is kept and marked withdrawn.", "Withdraw", "Cancel"))
                            pending = bootstrap.WithdrawParticipant;
                    }
                    GUI.backgroundColor = previous;
                }
                EditorGUILayout.LabelField(" ", "Or hold F12 for 1.5 s in the headset.",
                    EditorStyles.miniLabel);
            }
            EndStep();
        }

        void DrawAfterStep()
        {
            string bundled = Path.Combine(repoPath ?? "", "runs", "bundles",
                                          participant + "_all.csv");
            bool ready = !string.IsNullOrEmpty(repoPath) && File.Exists(bundled);
            Step(5, "After", ready,
                ready ? "Bundled to runs/bundles/" + participant + "_all.csv"
                      : "The after-forms run in the app. Then bundle the logs.");

            if (GUILayout.Button("Bundle " + participant + "'s logs into one file",
                                 GUILayout.Height(24f)))
                pending = BundleLogs;

            if (GUILayout.Button("Reveal data folder"))
                EditorUtility.RevealInFinder(Application.persistentDataPath);
            EndStep();

            Space();
            EditorGUI.BeginChangeCheck();
            repoPath = EditorGUILayout.TextField("Repo path", repoPath);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(RepoKey, repoPath);
        }

        // ------------------------------------------------------------------ actions

        void PrepareParticipant(StudyBootstrap bootstrap)
        {
            if (!Directory.Exists(repoPath))
            {
                EditorUtility.DisplayDialog("Emotion Rooms",
                    "Set the repo path at the bottom of this window first.\n\nIt should be " +
                    "the folder holding pipeline/ and configs/.", "OK");
                return;
            }

            int index = IndexOf(participant);
            string args = string.Format("./test-participant.sh {0} {1} {2}",
                participant, 40 + index, index);
            bool ok = Run("/bin/bash", "-c \"" + args + "\"", repoPath);

            if (ok && bootstrap != null)
            {
                bootstrap.participantId = participant;
                bootstrap.ApplyParticipantId();
                EditorUtility.SetDirty(bootstrap);
            }
            AssetDatabase.Refresh();
        }

        void BundleLogs()
        {
            if (!Directory.Exists(repoPath))
            {
                EditorUtility.DisplayDialog("Emotion Rooms", "Set the repo path first.", "OK");
                return;
            }
            string args = string.Format(
                "python3 -m pipeline.cli bundle-participant --participant {0} --data '{1}'",
                participant, Application.persistentDataPath);
            Run("/bin/bash", "-c \"" + args + "\"", repoPath);
        }

        bool Run(string file, string arguments, string workingDirectory)
        {
            try
            {
                var info = new ProcessStartInfo(file, arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    lastCommandOutput = (output + "\n" + error).Trim();
                    Repaint();

                    if (process.ExitCode != 0)
                    {
                        Debug.LogError("Study Control: command failed\n" + lastCommandOutput);
                        return false;
                    }
                    Debug.Log("Study Control:\n" + lastCommandOutput);
                    return true;
                }
            }
            catch (Exception e)
            {
                lastCommandOutput = e.Message;
                Debug.LogError("Study Control: could not run the command. " + e.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// One past the highest id already in the data folder. Reused ids are the classic
        /// way to lose a participant: the second session appends to the first one's files
        /// and neither is recoverable afterwards.
        /// </summary>
        static string NextParticipantId()
        {
            int highest = 0;
            string dir = Application.persistentDataPath;
            if (Directory.Exists(dir))
            {
                foreach (var path in Directory.GetFiles(dir, "*.csv", SearchOption.AllDirectories))
                {
                    foreach (Match m in Regex.Matches(Path.GetFileName(path), @"p(\d+)"))
                    {
                        int value;
                        if (int.TryParse(m.Groups[1].Value, out value) && value > highest)
                            highest = value;
                    }
                }

                string consent = Path.Combine(dir, "consent_log.csv");
                if (File.Exists(consent))
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(consent), @"\bp(\d+)\b"))
                    {
                        int value;
                        if (int.TryParse(m.Groups[1].Value, out value) && value > highest)
                            highest = value;
                    }
                }
            }
            return "p" + (highest + 1).ToString("00");
        }

        static int IndexOf(string id)
        {
            var m = Regex.Match(id ?? "", @"(\d+)");
            int value;
            if (m.Success && int.TryParse(m.Groups[1].Value, out value)) return Mathf.Max(0, value - 1);
            return 0;
        }

        /// <summary>
        /// Put the participant id into the form link.
        ///
        /// Google Forms prefill links carry the field's own generated entry id, which
        /// build-forms.gs emits with PARTICIPANT_ID as a placeholder. Substituting it is
        /// the only thing that actually prefills; appending ?participant=p01 to a form
        /// URL does nothing, because Google ignores parameters it does not recognise.
        /// The fallback query string is kept for any other form host.
        /// </summary>
        static string WithParticipant(string url, string id)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (url.Contains("PARTICIPANT_ID"))
                return url.Replace("PARTICIPANT_ID", Uri.EscapeDataString(id));
            return url + (url.Contains("?") ? "&" : "?") + "participant=" + Uri.EscapeDataString(id);
        }

        static string GuessRepoPath()
        {
            // Assets/.. is the Unity project; the repo is its parent.
            var project = Directory.GetParent(Application.dataPath);
            return project != null && project.Parent != null ? project.Parent.FullName : "";
        }

        // ------------------------------------------------------------------ chrome

        static void Title(string text)
        {
            var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
            EditorGUILayout.LabelField(text, style);
        }

        static void Space() { EditorGUILayout.Space(6f); }

        void Step(int number, string name, bool done, string detail)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            var tick = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = done ? new Color(0.2f, 0.7f, 0.3f) : Color.gray },
            };
            EditorGUILayout.LabelField(done ? "●" : "○", tick, GUILayout.Width(16f));
            EditorGUILayout.LabelField(number + ". " + name, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedMiniLabel);
        }

        static void EndStep() { EditorGUILayout.EndVertical(); }
    }
}
