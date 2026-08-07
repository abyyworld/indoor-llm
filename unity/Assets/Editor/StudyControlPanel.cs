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
using UnityEngine.Networking;
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
        }

        // Cached probes. The panel used to hit the filesystem on every repaint, and
        // OnInspectorUpdate repaints continuously in play mode; nothing here changes
        // faster than a person can act on it.
        double probedAt;
        int cachedPacks;

        void Probe(bool force = false)
        {
            if (!force && EditorApplication.timeSinceStartup - probedAt < 3.0) return;
            probedAt = EditorApplication.timeSinceStartup;

            string packs = Path.Combine(Application.streamingAssetsPath, "participants");
            cachedPacks = Directory.Exists(packs) ? Directory.GetDirectories(packs).Length : 0;
        }

        void OnInspectorUpdate()
        {
            // Once a second, not ten times. The only thing that needs to be live is
            // whether a session is running, and a second is fast enough to act on.
            if (Application.isPlaying) Repaint();
        }

        void OnGUI()
        {
            Probe();

            var bootstrap = UnityEngine.Object.FindFirstObjectByType<StudyBootstrap>();
            var forms = UnityEngine.Object.FindFirstObjectByType<QuestionnaireRunner>();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawHeader(bootstrap);
            DrawSetup(bootstrap);
            DrawBeforeArrival(bootstrap);
            DrawSession(bootstrap, forms);
            DrawAfter();
            DrawWebStudy();
            DrawScript();
            DrawTroubleshooting();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Run a button's work outside the GUI callback entirely.
        ///
        /// The previous version ran it during Repaint, still inside OnGUI. That is fine
        /// for a one-line action and quietly fatal for anything real: Set Up Study Scene
        /// opens a modal dialog, destroys and recreates GameObjects and writes assets, and
        /// doing that from inside a repaint aborts partway with nothing in the console.
        /// The symptom was a Rebuild button that did nothing while the identical menu item
        /// worked. delayCall runs on the next editor tick, with no GUI in progress.
        /// </summary>
        void Later(Action action)
        {
            EditorApplication.delayCall += () =>
            {
                try { action(); }
                catch (Exception e)
                {
                    lastOutput = e.Message;
                    Debug.LogError("Study Control: " + e.Message + "\n" + e.StackTrace);
                }
                Repaint();
            };
        }

        // ------------------------------------------------------------------- header

        void DrawHeader(StudyBootstrap bootstrap)
        {
            EditorGUILayout.Space(4f);
            var title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            EditorGUILayout.LabelField("Emotion Rooms", title);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Participant", GUILayout.Width(70f));
            participant = EditorGUILayout.TextField(participant, GUILayout.Width(90f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(participant))
                EditorGUILayout.HelpBox(
                    "Type the participant id before anything else. Everything written " +
                    "this session is filed under it, and reusing one appends a second " +
                    "session onto the first with neither recoverable.", MessageType.Warning);
            else if (HasDataAlready(participant))
                EditorGUILayout.HelpBox(
                    "There is already data filed under " + participant + ". Use a " +
                    "different id unless you mean to add to it.", MessageType.Warning);

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

            var stamp = UnityEngine.Object.FindFirstObjectByType<StudySceneStamp>();
            bool sceneBuilt = bootstrap != null;
            bool current = sceneBuilt && stamp != null && stamp.IsCurrent;

            Row(current, !sceneBuilt ? "Scene not built yet"
                       : current ? "Scene built and up to date"
                                 : "Scene is OUT OF DATE");

            if (sceneBuilt && !current)
                EditorGUILayout.HelpBox(
                    "This scene was built by an older version of the setup code, so it is " +
                    "missing pieces the study now needs — that is why forms and buttons " +
                    "are not appearing.\n\nRebuild it. Nothing is lost: the rooms, grid " +
                    "and wiring are all generated.", MessageType.Error);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(sceneBuilt ? "Rebuild scene" : "Build scene",
                                     GUILayout.Height(24f)))
                    Later(StudySceneSetup.SetUp);
                if (GUILayout.Button("Check", GUILayout.Height(24f), GUILayout.Width(70f)))
                    Later(StudySceneSetup.CheckScene);
                EditorGUILayout.EndHorizontal();
            }

            bool haveForms = File.Exists(Path.Combine(Application.streamingAssetsPath,
                                                      "questionnaires.json"));
            Row(haveForms, haveForms
                ? "Questionnaires loaded (consent, demographics, SSQ, NASA-TLX, trust, presence, debrief)"
                : "questionnaires.json missing — no forms will appear");
            if (!haveForms && GUILayout.Button("Build questionnaires"))
                Later(() => RunPython("emit-questionnaires"));

            var models = StudySceneSetup.FindFurnitureSet();
            int missing = models == null ? 7 : models.MissingCount();
            Row(missing < 7, models == null
                ? "Furniture: placeholders only"
                : (7 - missing) + " of 7 furniture models loaded" +
                  (missing > 0 ? " (teacup and wall art stay procedural)" : ""));

            EndSection();
        }

        // ------------------------------------------------------------ before arrival

        void DrawWebStudy()
        {
            Section("Run on the headset through its browser",
                    "No Developer Mode, no cable, no install. This is the route that works " +
                    "on a headset you do not own.");

            bool up = WebServer.IsRunning;
            Row(up, up ? "Study server running at " + WebServer.HeadsetUrl
                       : "Study server stopped");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(up ? "Stop server" : "Start server", GUILayout.Height(26f)))
                Later(() =>
                {
                    if (up) { WebServer.Stop(); return; }
                    // Fall back to the guessed repo rather than refusing over a field
                    // nobody was told to fill in.
                    string path = Directory.Exists(repoPath) ? repoPath : GuessRepoPath();
                    if (!Directory.Exists(repoPath)) { repoPath = path; EditorPrefs.SetString(RepoKey, path); }
                    WebServer.Start(path);
                });

            using (new EditorGUI.DisabledScope(!up))
            {
                if (GUILayout.Button("Open researcher panel", GUILayout.Height(26f)))
                    Later(() => Application.OpenURL(
                        WebServer.PanelUrl + "?participant=" +
                        UnityWebRequest.EscapeURL(participant)));
            }
            EditorGUILayout.EndHorizontal();

            if (up)
            {
                EditorGUILayout.LabelField(
                    "In the headset's Browser, once, before the participant arrives:",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.SelectableLabel(WebServer.HeadsetUrl,
                    EditorStyles.textField, GUILayout.Height(18f));
                EditorGUILayout.LabelField(
                    "Accept the certificate warning, press Enter VR, and put it down. " +
                    "Everything after that is on this laptop.",
                    EditorStyles.wordWrappedMiniLabel);
            }

            EndSection();
        }

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
                    Later(() => PrepareParticipant(bootstrap));
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

            EditorGUILayout.HelpBox(
                "Watch the GAME tab, not Scene. Everything the participant sees — the " +
                "rooms, the rating grid, and every questionnaire — is drawn to the Game " +
                "view. The Scene view shows the room geometry only, so from there a " +
                "session looks like rooms appearing and vanishing with nothing in " +
                "between.", MessageType.Info);

            Numbered(1, "Sit them at the laptop with the headset OFF. Press Play.");
            Numbered(2, "Open the BEFORE forms below and hand them the keyboard. " +
                        "They open in a browser, not in the headset. When they submit, " +
                        "each one turns green here.");

            EditorGUILayout.BeginHorizontal();
            if (!Application.isPlaying)
            {
                if (GUILayout.Button("Press Play", GUILayout.Height(30f)))
                    EditorApplication.isPlaying = true;
            }
            else
            {
                var stamp = UnityEngine.Object.FindFirstObjectByType<StudySceneStamp>();
                bool stale = stamp == null || !stamp.IsCurrent;
                bool noId = string.IsNullOrEmpty(participant);

                if (stale)
                    EditorGUILayout.HelpBox(
                        "Rebuild the scene first — it is out of date and the session will " +
                        "not behave.", MessageType.Error);

                using (new EditorGUI.DisabledScope(bootstrap == null || running || stale || noId))
                {
                    if (GUILayout.Button(running ? "Running…" :
                                         noId ? "Type a participant id first" :
                                         stale ? "Scene out of date" : "Begin " + participant,
                                         GUILayout.Height(30f)))
                        Later(() =>
                        {
                            bootstrap.participantId = participant;
                            bootstrap.ApplyParticipantId();
                            bootstrap.BeginStudy();
                        });
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawForms("before", forms, UnityEngine.Object.FindFirstObjectByType<FormServer>());

            if (Application.isPlaying && bootstrap != null && !bootstrap.ConsentConfirmed)
                EditorGUILayout.HelpBox(
                    "Consent not yet affirmed. Nothing is blocked, but this participant's " +
                    "data is not usable until it is.", MessageType.Warning);

            Numbered(4, "Fit the headset. Check they can see clearly and are standing " +
                        "comfortably with room to turn around.");
            Numbered(5, "2 warm-up rooms, then 8 real ones. ~15 min. Each room appears " +
                        "for 20 seconds, then vanishes and a 9x9 grid takes its place. " +
                        "They click one square: left-right is pleasant-unpleasant, " +
                        "up-down is calm-excited. Then the next room. You do nothing.");
            Numbered(6, "The review block: 12 rooms, asking whether anything looks wrong. " +
                        "~12 min. Still nothing for you to do.");
            Numbered(7, "Headset off. Open the AFTER forms and hand back the keyboard.");

            DrawForms("after", forms, UnityEngine.Object.FindFirstObjectByType<FormServer>());

            Numbered(8, "Check nothing above is still amber, then stop Play. The combined " +
                        "file is written on the way out.");

            if (Application.isPlaying)
                EditorGUILayout.HelpBox(
                    "To stop early, just press Play again. Everything recorded so far is " +
                    "kept and the combined file is written on the way out, so stopping " +
                    "midway costs nothing.", MessageType.None);

            EndSection();
        }

        // -------------------------------------------------------------------- after

        void DrawForms(string when, QuestionnaireRunner forms, FormServer server)
        {
            if (forms == null || server == null)
            {
                EditorGUILayout.LabelField(
                    "      (press Play to open forms)", EditorStyles.miniLabel);
                return;
            }
            if (!server.IsRunning)
            {
                EditorGUILayout.HelpBox("The form server is not running. See the console.",
                    MessageType.Warning);
                return;
            }

            foreach (var form in forms.Due(when))
            {
                var state = forms.StateOf(form.id);
                EditorGUILayout.BeginHorizontal();

                var tick = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal =
                    {
                        textColor = state == FormState.Completed
                            ? new Color(0.25f, 0.7f, 0.35f)
                            : new Color(0.75f, 0.55f, 0.2f),
                    },
                };
                EditorGUILayout.LabelField(state == FormState.Completed ? "●" : "○", tick,
                    GUILayout.Width(14f));

                if (GUILayout.Button(form.title, GUILayout.Height(22f)))
                {
                    string url = server.Root + "form?id=" + form.id;
                    Later(() => Application.OpenURL(url));
                }
                EditorGUILayout.LabelField(
                    state == FormState.Completed ? "done" :
                    state == FormState.PartlyAnswered ? "partly" : "",
                    EditorStyles.miniLabel, GUILayout.Width(46f));
                EditorGUILayout.EndHorizontal();
            }
        }

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
            if (string.IsNullOrEmpty(participant))
            {
                EditorUtility.DisplayDialog("Emotion Rooms",
                    "Type a participant id first.", "OK");
                return;
            }

            if (!RunShell(string.Format("./test-participant.sh {0} {1} {2}",
                                        participant, 40 + index, index)))
                return;

            Debug.Log("Study Control: " + participant + " is ready. Rooms, review block " +
                      "and warm-up written to the data folder.");

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

        static bool HasDataAlready(string id)
        {
            string dir = Application.persistentDataPath;
            if (!Directory.Exists(dir)) return false;
            foreach (var path in Directory.GetFiles(dir, "*.csv", SearchOption.AllDirectories))
                if (Path.GetFileName(path).Contains(id)) return true;

            string consent = Path.Combine(dir, "consent_log.csv");
            return File.Exists(consent) &&
                   File.ReadAllText(consent).Contains("\n" + id + ",");
        }

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
