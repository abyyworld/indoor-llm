// The whole researcher interface: Emotion Rooms > Study Control Panel (Cmd-Shift-E).
//
// Written as a runbook rather than a settings window. It says what to do next, who does
// it -- you or the participant -- and roughly how long it takes, because the person
// running a session is usually not the person who wrote the software, and quite often is
// the same person eight weeks later. A study that is fiddly to run gets run
// inconsistently, and inconsistency between participants is measurement error nobody can
// subtract afterwards.
//
// Nothing here needs a terminal, and nothing needs the inspector.

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
        const string RepoKey = "EmotionRooms.RepoPath";

        string participant = "";
        string repoPath = "";
        Vector2 scroll;
        string lastOutput = "";
        bool showScript;
        bool showTrouble;
        Action pending;

        [MenuItem("Emotion Rooms/Study Control Panel _%#e", priority = -100)]
        public static void Open()
        {
            var window = GetWindow<StudyControlPanel>(false, "Study Control");
            window.minSize = new Vector2(430f, 620f);
            window.Show();
        }

        void OnEnable()
        {
            repoPath = EditorPrefs.GetString(RepoKey, GuessRepoPath());
            participant = NextParticipantId();
        }

        void OnInspectorUpdate()
        {
            // The panel reports live session state, which changes without a mouse event.
            if (Application.isPlaying) Repaint();
        }

        void OnGUI()
        {
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<StudyBootstrap>();
            var forms = UnityEngine.Object.FindFirstObjectByType<QuestionnaireRunner>();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawHeader(bootstrap);
            DrawSetup(bootstrap);
            DrawBeforeArrival(bootstrap);
            DrawSession(bootstrap, forms);
            DrawAfter();
            DrawScript();
            DrawTroubleshooting();

            EditorGUILayout.EndScrollView();

            if (pending != null && Event.current.type == EventType.Repaint)
            {
                var action = pending;
                pending = null;
                try { action(); }
                catch (Exception e)
                {
                    lastOutput = e.Message;
                    Debug.LogError("Study Control: " + e.Message + "\n" + e.StackTrace);
                }
                Repaint();
            }
        }

        // ------------------------------------------------------------------- header

        void DrawHeader(StudyBootstrap bootstrap)
        {
            EditorGUILayout.Space(4f);
            var title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            EditorGUILayout.LabelField("Emotion Rooms", title);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Participant", GUILayout.Width(70f));
            participant = EditorGUILayout.TextField(participant, GUILayout.Width(70f));
            if (GUILayout.Button("Next", GUILayout.Width(46f)))
            {
                participant = NextParticipantId();
                GUI.FocusControl(null);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (bootstrap != null)
            {
                int mode = bootstrap.practiceOnly ? 1 : 0;
                int picked = GUILayout.Toolbar(mode, new[] { "Real session", "Practice only" });
                if (picked != mode)
                {
                    bootstrap.practiceOnly = picked == 1;
                    EditorUtility.SetDirty(bootstrap);
                }
                EditorGUILayout.LabelField(
                    bootstrap.practiceOnly
                        ? "Two warm-up rooms then stop. Nothing scored, no review block, " +
                          "no participant id burned. Use this to try the kit."
                        : "Warm-up, 8 scored rooms, the review block, then the after-forms. " +
                          "About 45 minutes.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        // -------------------------------------------------------------------- setup

        void DrawSetup(StudyBootstrap bootstrap)
        {
            Section("Setup", "Once per machine, and again after any code change.");

            bool sceneBuilt = bootstrap != null;
            Row(sceneBuilt, sceneBuilt ? "Scene built" : "Scene not built yet");

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(sceneBuilt ? "Rebuild scene" : "Build scene",
                                     GUILayout.Height(24f)))
                    pending = () => { StudySceneSetup.SetUp(); participant = NextParticipantId(); };
                if (GUILayout.Button("Check", GUILayout.Height(24f), GUILayout.Width(70f)))
                    pending = StudySceneSetup.CheckScene;
                EditorGUILayout.EndHorizontal();
            }

            bool haveForms = File.Exists(Path.Combine(Application.streamingAssetsPath,
                                                      "questionnaires.json"));
            Row(haveForms, haveForms
                ? "Questionnaires loaded (consent, demographics, SSQ, NASA-TLX, trust, presence, debrief)"
                : "questionnaires.json missing — no forms will appear");
            if (!haveForms && GUILayout.Button("Build questionnaires"))
                pending = () => RunPython("emit-questionnaires");

            var models = StudySceneSetup.FindFurnitureSet();
            int missing = models == null ? 7 : models.MissingCount();
            Row(missing < 7, models == null
                ? "Furniture: placeholders only"
                : (7 - missing) + " of 7 furniture models loaded" +
                  (missing > 0 ? " (teacup and wall art stay procedural)" : ""));

            EndSection();
        }

        // ------------------------------------------------------------ before arrival

        void DrawBeforeArrival(StudyBootstrap bootstrap)
        {
            Section("Before they arrive", "Builds this participant's rooms. Takes a second.");

            string dir = Application.persistentDataPath;
            bool session = File.Exists(Path.Combine(dir, "session.json"));
            bool block = File.Exists(Path.Combine(dir, "oversight.json"));
            bool practice = File.Exists(Path.Combine(dir, "practice.json"));

            Row(session && block && practice,
                session && block && practice
                    ? "Rooms ready: 8 trials, 12 review trials, 2 warm-up"
                    : "Rooms not built for this participant yet");

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Prepare " + participant, GUILayout.Height(26f)))
                    pending = () => PrepareParticipant(bootstrap);
            }
            EditorGUILayout.LabelField(
                "Each participant gets a different room order. Never reuse an id: a second " +
                "session under the same id appends to the first and neither is recoverable.",
                EditorStyles.wordWrappedMiniLabel);

            EndSection();
        }

        // ------------------------------------------------------------------ session

        void DrawSession(StudyBootstrap bootstrap, QuestionnaireRunner forms)
        {
            Section("Running the session", "Follow these in order.");

            bool running = Application.isPlaying && bootstrap != null &&
                           bootstrap.trialRunner != null && bootstrap.trialRunner.IsRunning;

            Numbered(1, "Sit them at the laptop with the headset OFF.");
            Numbered(2, "Press Play, then Begin. You do not touch anything after that " +
                        "until they take the headset off.");

            EditorGUILayout.BeginHorizontal();
            if (!Application.isPlaying)
            {
                if (GUILayout.Button("Press Play", GUILayout.Height(30f)))
                    EditorApplication.isPlaying = true;
            }
            else
            {
                using (new EditorGUI.DisabledScope(bootstrap == null || running))
                {
                    if (GUILayout.Button(running ? "Running…" : "Begin " + participant,
                                         GUILayout.Height(30f)))
                        pending = () =>
                        {
                            bootstrap.participantId = participant;
                            bootstrap.ApplyParticipantId();
                            bootstrap.BeginStudy();
                        };
                }
            }
            EditorGUILayout.EndHorizontal();

            Numbered(3, "THEY fill in consent, demographics and how they feel, on screen. " +
                        "Leave them to it. Every form can be skipped.");

            if (Application.isPlaying && forms != null)
            {
                var outstanding = forms.Outstanding();
                EditorGUILayout.LabelField(
                    outstanding.Count == 0
                        ? "      All forms completed."
                        : "      Outstanding: " + outstanding.Count,
                    EditorStyles.miniLabel);
                foreach (var line in outstanding)
                    EditorGUILayout.LabelField("        " + line, EditorStyles.miniLabel);

                if (bootstrap != null && !bootstrap.ConsentConfirmed)
                    EditorGUILayout.HelpBox(
                        "Consent not yet affirmed. The session still runs and the gap is " +
                        "recorded, but this participant's data is not usable until it is.",
                        MessageType.Warning);
            }

            Numbered(4, "Fit the headset. Check they can see clearly and are standing " +
                        "comfortably with room to turn around.");
            Numbered(5, "2 warm-up rooms, then 8 real ones. ~15 min. They rate each room " +
                        "on the grid by pointing and clicking. You do nothing.");
            Numbered(6, "The review block: 12 rooms, asking whether anything looks wrong. " +
                        "~12 min. Still nothing for you to do.");
            Numbered(7, "Headset off. They fill in the after-forms on screen: sickness, " +
                        "workload, trust, presence, then the debrief.");
            Numbered(8, "The end screen names anything they skipped. Note it on paper.");

            if (Application.isPlaying && bootstrap != null)
            {
                EditorGUILayout.Space(6f);
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
                if (GUILayout.Button("Participant wants to stop — end now", GUILayout.Height(26f)))
                {
                    if (EditorUtility.DisplayDialog("Stop the session",
                        "End " + participant + " now?\n\nEverything recorded so far is kept " +
                        "and marked as a withdrawal.", "Stop", "Cancel"))
                        pending = bootstrap.WithdrawParticipant;
                }
                GUI.backgroundColor = previous;
                EditorGUILayout.LabelField("Or they hold F12 for 1.5 s themselves.",
                    EditorStyles.miniLabel);
            }

            EndSection();
        }

        // -------------------------------------------------------------------- after

        void DrawAfter()
        {
            Section("After", "Nothing to run. The combined file is already written.");

            string bundle = Path.Combine(Application.persistentDataPath, "bundles",
                                         participant + "_all.csv");
            bool done = File.Exists(bundle);
            Row(done, done
                ? "bundles/" + participant + "_all.csv — every response, event and 20 Hz sample in one file"
                : "Written automatically when the session ends or a participant withdraws");

            if (GUILayout.Button("Show me the data folder"))
                EditorUtility.RevealInFinder(
                    done ? bundle : Application.persistentDataPath);

            EndSection();
        }

        // ------------------------------------------------------------------- script

        void DrawScript()
        {
            showScript = EditorGUILayout.Foldout(showScript, "What to say", true);
            if (!showScript) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Say("On arrival",
                "\"Thanks for coming. You'll wear a VR headset and stand in some virtual " +
                "rooms. After each one I'll ask how it made you feel. It takes about " +
                "45 minutes. You can stop at any point, for any reason or none, and " +
                "nothing happens if you do. First, some questions on this laptop.\"");
            Say("Before the headset",
                "\"Any questions before we start? If you feel dizzy or unwell at any " +
                "point, say so and we stop straight away — that's not a problem, it " +
                "happens.\"");
            Say("Fitting the headset",
                "\"Let me know when it's comfortable and you can read text clearly. " +
                "You'll be standing and you can turn around. Point with the controller " +
                "and press the trigger to answer.\"");
            Say("Before the first room",
                "\"The first two rooms are practice, just to get used to the grid. " +
                "After that they count, but there are no right answers — just how the " +
                "room makes you feel.\"");
            Say("Before the review block",
                "\"Now you'll see some rooms again. Each was built to feel a certain " +
                "way. I'll ask whether anything looks wrong for that feeling. Sometimes " +
                "nothing is wrong, so 'no' is a real answer.\"");
            Say("At the end",
                "\"That's everything. A few last questions on the laptop, including an " +
                "explanation of what we were actually testing.\"");
            EditorGUILayout.EndVertical();
        }

        static void Say(string when, string what)
        {
            EditorGUILayout.LabelField(when, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(what, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
        }

        void DrawTroubleshooting()
        {
            showTrouble = EditorGUILayout.Foldout(showTrouble, "If something goes wrong", true);
            if (!showTrouble) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Say("\"No wall renderer with a material\"",
                "The scene's materials were lost on a recompile. Rebuild the scene in " +
                "Setup. Materials are saved as assets now, so it should not recur.");
            Say("The room is empty or furniture is missing",
                "Rebuild the scene. If furniture is still placeholder boxes, run " +
                "Emotion Rooms > Import Furniture Models.");
            Say("Nothing happens when the participant clicks",
                "Check the console. If the review block is waiting on an answer, the " +
                "panel it wants is missing — rebuild the scene and run Check.");
            Say("The bundle says there is no data",
                "That participant never wrote anything, usually because the session was " +
                "never begun under that id. Check the id in the header matches the one " +
                "you ran.");
            Say("Repo path",
                "Only needed for Prepare. It should hold pipeline/ and configs/.");
            EditorGUILayout.Space(2f);
            EditorGUI.BeginChangeCheck();
            repoPath = EditorGUILayout.TextField("Repo path", repoPath);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(RepoKey, repoPath);

            if (!string.IsNullOrEmpty(lastOutput))
            {
                EditorGUILayout.LabelField("Last command", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(lastOutput, EditorStyles.textArea,
                    GUILayout.Height(90f));
            }
            EditorGUILayout.EndVertical();
        }

        // ------------------------------------------------------------------ actions

        void PrepareParticipant(StudyBootstrap bootstrap)
        {
            if (!Directory.Exists(repoPath))
            {
                EditorUtility.DisplayDialog("Emotion Rooms",
                    "Set the repo path under \"If something goes wrong\" first.\n\n" +
                    "It is the folder holding pipeline/ and configs/.", "OK");
                return;
            }

            int index = IndexOf(participant);
            if (!RunShell(string.Format("./test-participant.sh {0} {1} {2}",
                                        participant, 40 + index, index)))
                return;

            if (bootstrap != null)
            {
                bootstrap.participantId = participant;
                bootstrap.ApplyParticipantId();
                EditorUtility.SetDirty(bootstrap);
            }
            AssetDatabase.Refresh();
        }

        void RunPython(string command)
        {
            if (!Directory.Exists(repoPath))
            {
                EditorUtility.DisplayDialog("Emotion Rooms", "Set the repo path first.", "OK");
                return;
            }
            if (RunShell("python3 -m pipeline.cli " + command)) AssetDatabase.Refresh();
        }

        bool RunShell(string command)
        {
            try
            {
                var info = new ProcessStartInfo("/bin/bash", "-c \"" + command + "\"")
                {
                    WorkingDirectory = repoPath,
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
                    lastOutput = (output + "\n" + error).Trim();

                    if (process.ExitCode != 0)
                    {
                        Debug.LogError("Study Control: command failed\n" + lastOutput);
                        showTrouble = true;
                        return false;
                    }
                    Debug.Log("Study Control:\n" + lastOutput);
                    return true;
                }
            }
            catch (Exception e)
            {
                lastOutput = e.Message;
                Debug.LogError("Study Control: " + e.Message);
                showTrouble = true;
                return false;
            }
        }

        // ------------------------------------------------------------------ helpers

        static string NextParticipantId()
        {
            int highest = 0;
            string dir = Application.persistentDataPath;
            if (Directory.Exists(dir))
            {
                foreach (var path in Directory.GetFiles(dir, "*.csv", SearchOption.AllDirectories))
                    foreach (Match m in Regex.Matches(Path.GetFileName(path), @"p(\d+)"))
                        highest = Math.Max(highest, Parse(m.Groups[1].Value));

                string consent = Path.Combine(dir, "consent_log.csv");
                if (File.Exists(consent))
                    foreach (Match m in Regex.Matches(File.ReadAllText(consent), @"\bp(\d+)\b"))
                        highest = Math.Max(highest, Parse(m.Groups[1].Value));
            }
            return "p" + (highest + 1).ToString("00");
        }

        static int Parse(string text)
        {
            int value;
            return int.TryParse(text, out value) ? value : 0;
        }

        static int IndexOf(string id)
        {
            var m = Regex.Match(id ?? "", @"(\d+)");
            return m.Success ? Mathf.Max(0, Parse(m.Groups[1].Value) - 1) : 0;
        }

        static string GuessRepoPath()
        {
            var project = Directory.GetParent(Application.dataPath);
            return project != null && project.Parent != null ? project.Parent.FullName : "";
        }

        // ------------------------------------------------------------------- chrome

        static void Section(string name, string detail)
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(name, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(detail))
                EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(2f);
        }

        static void EndSection() { EditorGUILayout.EndVertical(); }

        static void Row(bool done, string text)
        {
            EditorGUILayout.BeginHorizontal();
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = done ? new Color(0.25f, 0.7f, 0.35f) : new Color(0.75f, 0.55f, 0.2f) },
            };
            EditorGUILayout.LabelField(done ? "●" : "○", style, GUILayout.Width(14f));
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();
        }

        static void Numbered(int n, string text)
        {
            EditorGUILayout.Space(3f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(n + ".", EditorStyles.boldLabel, GUILayout.Width(18f));
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
        }
    }
}
