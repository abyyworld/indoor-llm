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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace EmotionRooms.EditorTools
{
    public class StudyControlPanel : EditorWindow
    {
        const string RepoKey = "EmotionRooms.RepoPath";
        const string PracticeKey = "EmotionRooms.PracticeOnly";
        const string PilotKey = "EmotionRooms.PilotSkip";
        const string ModeKey = "EmotionRooms.SessionMode";

        int sessionMode;   // 0 both, 1 Phase A only, 2 Phase B only

        bool practiceOnly;
        bool pilotSkip;

        /// <summary>
        /// Send the participant and the mode to wherever the study is running.
        ///
        /// Both are set here and nowhere else, so neither the headset app nor the browser
        /// page ever asks again. Anything that can be set in two places eventually is,
        /// differently.
        /// </summary>
        string PhaseLetter()
        {
            return sessionMode == 1 ? "A" : sessionMode == 2 ? "B" : "";
        }

        void PushSettings()
        {
            StudyServerLink.SetParticipant(participant);
            if (!string.IsNullOrEmpty(cachedHeadsetIp))
                StudyServerLink.PushToHeadset(cachedHeadsetIp, participant, practiceOnly,
                                              sessionMode, pilotSkip, null);
        }

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
            repoPath = ResolveRepoPath();
            practiceOnly = EditorPrefs.GetBool(PracticeKey, false);
            pilotSkip = EditorPrefs.GetBool(PilotKey, false);
            sessionMode = EditorPrefs.GetInt(ModeKey, 0);
            Probe(true);
        }

        // Cached probes. The panel used to hit the filesystem on every repaint, and
        // OnInspectorUpdate repaints continuously in play mode; nothing here changes
        // faster than a person can act on it.
        double probedAt;
        int cachedPacks;
        string[] cachedDevices = new string[0];
        string cachedHeadsetIp;
        bool showBrowserRoute;
        HeadsetState headsetApp;   // what the app on the headset reports, or null

        void Probe(bool force = false)
        {
            if (!force && EditorApplication.timeSinceStartup - probedAt < 5.0) return;
            probedAt = EditorApplication.timeSinceStartup;

            string packs = Path.Combine(Application.streamingAssetsPath, "participants");
            cachedPacks = Directory.Exists(packs) ? Directory.GetDirectories(packs).Length : 0;

            // Two adb processes. Worth it every few seconds, ruinous every frame.
            cachedDevices = StudyBuild.ConnectedDevices();
            // Cable first, network second. The cable is present whenever the panel can
            // see the headset at all, and it does not depend on the room's WiFi.
            cachedHeadsetIp = cachedDevices.Length > 0
                ? (StudyBuild.ForwardedAddress() ?? StudyBuild.HeadsetAddress())
                : null;
            if (cachedHeadsetIp != null)
                StudyServerLink.QueryHeadset(cachedHeadsetIp,
                    state => { headsetApp = state; Repaint(); });
            else
                headsetApp = null;

            ReviveIfDead();
        }

        double revivedAt;

        /// <summary>
        /// Relaunch the app when it is installed but not running.
        ///
        /// An idle headset sleeps and the OS kills the immersive app, so the sequence
        /// "press Install, hand the headset over, wait for the panel" kept dying in the
        /// gap: by the time the participant had it on, the app the install launched was
        /// gone, and the panel could only report that truthfully and sit there. The
        /// panel is the thing that knows; it should act. Twenty-second cooldown so a
        /// headset that is genuinely asleep is not spammed with launch intents.
        /// </summary>
        void ReviveIfDead()
        {
            if (headsetApp != null) return;                        // reachable, nothing to do
            if (cachedDevices.Length == 0) return;                 // no adb, cannot help
            if (EditorApplication.timeSinceStartup - revivedAt < 20.0) return;
            if (!StudyBuild.IsInstalled()) return;

            revivedAt = EditorApplication.timeSinceStartup;
            StudyBuild.RelaunchIfNotRunning();
        }

        void OnInspectorUpdate()
        {
            // Probing lives here, not in OnGUI. It spawns adb and makes web requests, and
            // anything that can throw has no business running inside a repaint: the
            // exception unwinds through the layout and the window then draws errors
            // forever instead of the session.
            Probe();

            // Only while something is actually moving. Repainting an idle window ten
            // times a second costs the same as repainting a busy one.
            if (Application.isPlaying || (server != null && server.headset == "running"))
                Repaint();
        }

        void OnGUI()
        {
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<StudyBootstrap>();
            var forms = UnityEngine.Object.FindFirstObjectByType<QuestionnaireRunner>();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            try
            {
                DrawHeader(bootstrap);
                DrawSetup(bootstrap);
                DrawBeforeArrival(bootstrap);
                DrawSession(bootstrap, forms);
                DrawScript();
                DrawTroubleshooting();
            }
            catch (Exception e)
            {
                // A throw part-way through leaves IMGUI's layout groups unbalanced, and
                // every repaint after it reports that instead of the real problem. Caught
                // here, the panel keeps drawing and the actual message stays readable.
                if (Event.current.type == EventType.Repaint)
                    Debug.LogError("Study Control: " + e.Message + "\n" + e.StackTrace);
            }
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
            EditorGUI.BeginChangeCheck();
            participant = EditorGUILayout.TextField(participant, GUILayout.Width(90f));
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(participant))
                PushSettings();
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

            // No phase picker. One session, one protocol, everyone does all of it
            // (decision of 9 Aug): partial sessions produced participants who differed
            // in what they had seen, and analysis can separate the parts perfectly well
            // from the phase column that every logged row already carries. Splitting is
            // now an analysis choice, not a session-time one. The sessionMode plumbing
            // stays for the pilot path and for anyone who later needs a B-only arm.
            sessionMode = 0;

            int mode = practiceOnly ? 1 : 0;
            int picked = GUILayout.Toolbar(mode, new[] { "Real session", "Practice only" });
            if (picked != mode)
            {
                practiceOnly = picked == 1;
                EditorPrefs.SetBool(PracticeKey, practiceOnly);
                PushSettings();
            }

            bool pilotNow = EditorGUILayout.ToggleLeft(
                new GUIContent("Pilot: show a SKIP THIS PART button overhead in the headset",
                    "For piloting only. Adds an overhead button that abandons the running " +
                    "part and jumps to the next. Leave OFF for real participants; the " +
                    "button cannot appear unless this is on."),
                pilotSkip);
            if (pilotNow != pilotSkip)
            {
                pilotSkip = pilotNow;
                EditorPrefs.SetBool(PilotKey, pilotSkip);
                PushSettings();
            }

            EditorGUILayout.LabelField(
                "One session, about 35-40 minutes. Every trial: room, affect grid, " +
                "then on half of them the system's stated reasoning, then was-it-" +
                "altered and the follow-ups. 32 trials, 2x2 over reasoning-shown and " +
                "altered-or-not. The affect ratings are taken before any reasoning " +
                "appears, so the thesis data is clean; the reasoning is what the " +
                "oversight study manipulates.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.LabelField(practiceOnly
                ? "Two warm-up rooms then stop. Nothing scored, no review block, no " +
                  "participant id used up. This is how to try the kit."
                : "Warm-up rooms, 8 scored rooms, then the review block. About 45 minutes " +
                  "including the questionnaires.",
                EditorStyles.wordWrappedMiniLabel);
        }

        // -------------------------------------------------------------------- setup

        void DrawSetup(StudyBootstrap bootstrap)
        {
            Section("Setup", "Once per machine, and again after any code change.");

            var stamp = UnityEngine.Object.FindFirstObjectByType<StudySceneStamp>();
            bool sceneBuilt = bootstrap != null;
            bool current = sceneBuilt && stamp != null && stamp.IsCurrent;

            var openScene = EditorSceneManager.GetActiveScene();
            bool unsaved = openScene.isDirty;

            Row(current && !unsaved,
                !sceneBuilt ? "Scene not built yet"
                : unsaved ? "Scene has UNSAVED changes — a build would not include them"
                : current ? "Scene built, saved and up to date"
                          : "Scene is OUT OF DATE");

            if (unsaved)
            {
                EditorGUILayout.HelpBox(
                    "A build takes the scene from disk, not from the editor. Anything " +
                    "unsaved here would silently be left out of the headset build — which " +
                    "is exactly how a fix can look like it did not work.",
                    MessageType.Error);
                if (GUILayout.Button("Save the scene now", GUILayout.Height(24f)))
                    Later(() => EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()));
            }

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

        void DrawBeforeArrival(StudyBootstrap bootstrap)
        {
            Section("Before they arrive", "Builds this participant's rooms. Takes a second.");

            string dir = Application.persistentDataPath;
            bool session = File.Exists(Path.Combine(dir, "session.json"));
            bool block = File.Exists(Path.Combine(dir, "oversight.json"));
            bool practice = File.Exists(Path.Combine(dir, "practice.json"));

            Row(session && block && practice,
                session && block && practice
                    ? "Rooms ready: 8 trials, 32 review trials, 6 rationale, 2 warm-up"
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

        ServerState server;
        double serverPolledAt;

        void PollServer()
        {
            if (EditorApplication.timeSinceStartup - serverPolledAt < 2.0) return;
            serverPolledAt = EditorApplication.timeSinceStartup;
            StudyServerLink.FetchState(state => { server = state; Repaint(); });
        }

        /// <summary>
        /// The session, as five steps with exactly one live at a time.
        ///
        /// Which step is live comes from what the server says has actually happened --
        /// whether the headset has checked in, whether it is running, whether it has
        /// finished -- rather than from a counter this window keeps. A panel reopened
        /// mid-session then shows where the session is, not where it started.
        /// </summary>
        void DrawSession(StudyBootstrap bootstrap, QuestionnaireRunner forms)
        {
            PollServer();

            Section("Running a session", "One step at a time. Do the highlighted one.");

            if (cachedDevices.Length > 0) DrawApkSession();
            else DrawBrowserSession();

            EndSection();
        }

        /// <summary>
        /// The cable route: the app runs on the headset and this laptop drives it.
        /// Everything here rides on the app's own /set endpoint, so the runbook follows
        /// what the headset is actually doing rather than a counter kept here.
        /// </summary>
        void DrawApkSession()
        {
            bool app = headsetApp != null;
            bool running = app && headsetApp.running;
            bool finished = app && !headsetApp.running && headsetApp.trial >= 8;
            bool haveId = !string.IsNullOrEmpty(participant);

            int live = !app ? 0 : running ? 2 : finished ? 3 : 1;

            Step(0, live, "Study app on the headset",
                app ? "Running and reachable at " + cachedHeadsetIp + "."
                    : "Installed but not running, or the headset is asleep.");
            if (live == 0)
            {
                // One button, because every one of the others was a step this button
                // should have taken itself. Rebuilding, allowing local HTTP and launching
                // are not decisions a researcher should be making between participants;
                // they are things that have to be true before a session can start.
                if (GUILayout.Button("Install on the headset", GUILayout.Height(34f)))
                    Later(() => { StudyBuild.InstallAndRun(); Probe(true); });

                EditorGUILayout.LabelField(
                    "If the headset is asleep, put it on for a moment to wake it.",
                    EditorStyles.wordWrappedMiniLabel);

                // Offered here because a session should not be run on a tether: this
                // study has people walking around the room.
                if (GUILayout.Button(new GUIContent("Untether (adb over WiFi)",
                        "Switches the headset's connection to WiFi so the cable can " +
                        "come off. Everything in the panel keeps working. Needs both " +
                        "on the same network; the cable works regardless.")))
                    Later(() => { StudyBuild.GoWireless(); Probe(true); });
            }

            Step(1, live, "First questionnaires, then start",
                "One page, on this laptop, headset off. Then fit the headset and start.");
            if (live == 1)
            {
                using (new EditorGUI.DisabledScope(!haveId))
                {
                    if (GUILayout.Button("Open the first questionnaires", GUILayout.Height(26f)))
                        Later(() => Application.OpenURL(
                            StudyServerLink.HeadsetPage(cachedHeadsetIp,
                            "group?when=before&phase=" + PhaseLetter())));

                    EditorGUILayout.Space(4f);
                    if (GUILayout.Button(practiceOnly
                            ? "Fit the headset, then START THE PRACTICE"
                            : "Fit the headset, then START THE ROOMS",
                            GUILayout.Height(32f)))
                        Later(() =>
                        {
                            PushSettings();
                            StudyServerLink.StartOnHeadset(cachedHeadsetIp, participant, null);
                        });
                }
                if (!haveId)
                    EditorGUILayout.HelpBox("Type a participant id at the top first.",
                        MessageType.Warning);
            }

            Step(2, live, "The rooms are running",
                running
                    ? (headsetApp.reviewing
                        ? "Review block. Nothing to do."
                        : "Trial " + headsetApp.trial + " of " + headsetApp.of + ". Nothing to do.")
                    : "About 40 minutes once started.");
            if (live == 2)
                EditorGUILayout.LabelField(
                    "If they want to stop, they take the headset off. Everything recorded " +
                    "so far is kept.", EditorStyles.wordWrappedMiniLabel);

            Step(3, live, "Last questionnaires",
                "Headset off, back on this laptop. The debrief is in here.");
            if (live == 3)
            {
                if (GUILayout.Button("Open the last questionnaires", GUILayout.Height(26f)))
                    Later(() => Application.OpenURL(
                        StudyServerLink.HeadsetPage(cachedHeadsetIp,
                            "group?when=after&phase=" + PhaseLetter())));
                if (GUILayout.Button("Pull the data to this Mac", GUILayout.Height(26f)))
                    Later(StudyBuild.PullData);
                EditorGUILayout.LabelField(
                    "Brings every response, log and the combined file into " +
                    "runs/headset-data/.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        void DrawBrowserSession()
        {
            bool up = WebServer.IsRunning && server != null;
            bool headsetIn = up && server.connected &&
                             (server.headset == "in_vr" || server.headset == "running" ||
                              server.headset == "finished");
            bool running = up && server.headset == "running";
            bool finished = up && server.headset == "finished";
            bool haveId = !string.IsNullOrEmpty(participant);

            int live = !up ? 0 : !headsetIn ? 1 : running ? 3 : finished ? 4 : 2;

            // The cable IS the workflow. This branch used to pivot the whole panel to
            // the browser route the moment adb blinked, presenting an IP to hand-type
            // into the headset as if that were the normal next step. It is the fallback
            // for a headset without Developer Mode, nothing more, so it lives behind a
            // foldout and the panel's first word is the actual fix: plug it in.
            // Say which of the three situations this is, not just "no headset".
            EditorGUILayout.HelpBox(
                StudyBuild.Diagnosis() ?? "No headset over USB.", MessageType.Warning);

            showBrowserRoute = EditorGUILayout.Foldout(showBrowserRoute,
                "Browser fallback (only for a headset without Developer Mode)", true);
            if (!showBrowserRoute) return;

            Step(0, live, "Start the study server", up
                ? "Running."
                : "Not running. Everything else needs it.");
            if (live == 0)
            {
                if (GUILayout.Button("Start server", GUILayout.Height(28f)))
                    Later(() =>
                    {
                        string path = Directory.Exists(repoPath) ? repoPath : GuessRepoPath();
                        if (!Directory.Exists(repoPath))
                        {
                            repoPath = path;
                            EditorPrefs.SetString(RepoKey, path);
                        }
                        WebServer.Start(path);
                    });
            }

            Step(1, live, "Point the headset at the study",
                headsetIn ? "Headset is connected."
                          : "In the headset's Browser, type the address below. Nothing " +
                            "else is ever typed in the headset.");
            if (live == 1)
            {
                EditorGUILayout.SelectableLabel(WebServer.ShortUrl,
                    EditorStyles.textField, GUILayout.Height(20f));
                if (GUILayout.Button("Copy address"))
                    EditorGUIUtility.systemCopyBuffer = WebServer.ShortUrl;
            }

            Step(2, live, "First questionnaires, then start",
                "Consent and how they feel, on this laptop with the headset off.");
            if (live == 2)
            {
                using (new EditorGUI.DisabledScope(!haveId))
                {
                    if (GUILayout.Button("Open the first questionnaires", GUILayout.Height(26f)))
                        Later(() => Application.OpenURL(
                            StudyServerLink.FormUrl("before", participant, sessionMode)));

                    EditorGUILayout.Space(4f);
                    if (GUILayout.Button("Fit the headset, then START THE ROOMS",
                                         GUILayout.Height(32f)))
                        Later(() =>
                        {
                            PushSettings();
                            StudyServerLink.StartRooms(participant, null);
                        });
                }
            }

            Step(3, live, "The rooms are running",
                running ? "Trial " + server.trial + " of " + server.of + ". Nothing to do."
                        : "About 40 minutes once started.");

            Step(4, live, "Last questionnaires", "Headset off. The debrief is in here.");
            if (live == 4)
            {
                if (GUILayout.Button("Open the last questionnaires", GUILayout.Height(26f)))
                    Later(() => Application.OpenURL(
                        StudyServerLink.FormUrl("after", participant, sessionMode)));
            }
        }

        /// <summary>One row of the runbook: current, done, or still to come.</summary>
        void Step(int index, int live, string title, string detail)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();

            var mark = new GUIStyle(EditorStyles.boldLabel)
            {
                normal =
                {
                    textColor = index < live ? new Color(0.25f, 0.7f, 0.35f)
                              : index == live ? new Color(0.35f, 0.65f, 1f)
                              : new Color(0.45f, 0.45f, 0.5f),
                },
            };
            EditorGUILayout.LabelField(index < live ? "OK" : index == live ? "->" : "  ",
                mark, GUILayout.Width(22f));

            var label = new GUIStyle(EditorStyles.boldLabel);
            if (index != live) label.normal.textColor = new Color(0.55f, 0.55f, 0.6f);
            EditorGUILayout.LabelField(title, label);
            EditorGUILayout.EndHorizontal();

            if (index == live)
                EditorGUILayout.LabelField("      " + detail,
                    EditorStyles.wordWrappedMiniLabel);
        }

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
            if (!LooksLikeRepo(repoPath)) repoPath = ResolveRepoPath();
            if (!LooksLikeRepo(repoPath))
            {
                EditorUtility.DisplayDialog("Emotion Rooms",
                    "Cannot find the repo folder: the one holding pipeline/ and configs/." +
                    "\n\nLooked at:\n  " + (string.IsNullOrEmpty(repoPath) ? "(nothing saved)" : repoPath) +
                    "\n  " + GuessRepoPath() +
                    "\n\nSet it under \"If something goes wrong\".", "OK");
                return;
            }

            int index = IndexOf(participant);
            if (string.IsNullOrEmpty(participant))
            {
                EditorUtility.DisplayDialog("Emotion Rooms",
                    "Type a participant id first.", "OK");
                return;
            }

            // The four pipeline calls test-participant.sh made, made here instead.
            //
            // The script is still the documented command-line route and still works on
            // macOS; the panel no longer goes through it, because a bash script cannot
            // run on the Windows laptop that will be running most of the sessions.
            int seed = 40 + index;
            string batch = "configs/study_8cell.json";
            string runs = Path.Combine(repoPath, "runs");
            Directory.CreateDirectory(runs);

            string session = "runs/session_" + participant + ".json";
            string unity = "runs/unity_" + participant + ".json";
            string oversight = "runs/oversight_" + participant + ".json";
            string rationale = "runs/oversight_" + participant + "_rationale.json";

            if (!RunPipeline(string.Format(
                    "build-session --batch {0} --participant {1} --seed {2} " +
                    "--participant-index {3} --out {4}",
                    Quote(batch), Quote(participant), seed, index, Quote(session)))) return;

            if (!RunPipeline(string.Format("export-unity {0} --out {1}",
                    Quote(session), Quote(unity)))) return;

            if (!RunPipeline(string.Format(
                    "oversight-block --batch {0} --participant {1} --seed {2} --out {3}",
                    Quote(batch), Quote(participant), seed, Quote(oversight)))) return;

            if (!RunPipeline("build-practice --out runs/practice.json")) return;

            // persistentDataPath is named after the product, so it moves if the product
            // is renamed. Copying here means the stimuli always land where the app will
            // look, rather than in a folder that used to be right.
            string dest = Application.persistentDataPath;
            Directory.CreateDirectory(dest);
            if (!Stage(Path.Combine(repoPath, unity), Path.Combine(dest, "session.json"))) return;
            if (!Stage(Path.Combine(repoPath, oversight), Path.Combine(dest, "oversight.json"))) return;
            if (!Stage(Path.Combine(repoPath, rationale), Path.Combine(dest, "rationale.json"))) return;
            if (!Stage(Path.Combine(repoPath, "runs/practice.json"),
                       Path.Combine(dest, "practice.json"))) return;

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
            if (RunPipeline(command)) AssetDatabase.Refresh();
        }

        /// <summary>
        /// The python interpreter to invoke, by platform.
        ///
        /// Windows installs it as "python" (and "py" via the launcher); macOS and Linux
        /// as "python3", where bare "python" is often absent or still means 2.x.
        /// </summary>
        /// <summary>Copy a generated file into the app's data folder, loudly on failure.</summary>
        bool Stage(string from, string to)
        {
            try
            {
                if (!File.Exists(from))
                {
                    lastOutput = "expected " + from + " but the pipeline did not write it";
                    Debug.LogError("Study Control: " + lastOutput);
                    showTrouble = true;
                    return false;
                }
                File.Copy(from, to, true);
                return true;
            }
            catch (Exception e)
            {
                lastOutput = e.Message;
                Debug.LogError("Study Control: could not stage " + Path.GetFileName(to) +
                               ": " + e.Message);
                showTrouble = true;
                return false;
            }
        }

        /// <summary>Quote an argument so a path with spaces survives the process call.</summary>
        static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// Run one pipeline command. Interpreter discovery lives in PythonTool, which
        /// the study server uses too: the same /bin/bash assumption broke both, and a
        /// single implementation is what stops it breaking a third thing.
        /// </summary>
        bool RunPipeline(string arguments)
        {
            string output;
            bool ok = PythonTool.Run("-m pipeline.cli " + arguments, repoPath, out output);
            lastOutput = output;

            if (ok)
            {
                Debug.Log("Study Control:\n" + output);
                return true;
            }

            Debug.LogError("Study Control: " + output);
            showTrouble = true;
            return false;
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

        /// <summary>
        /// The repo folder, verified rather than remembered.
        ///
        /// This was read straight out of EditorPrefs, so moving or renaming the project
        /// folder left the panel pointing at a directory that no longer existed - and
        /// every pipeline command failed with an error about the command not being
        /// found, which reads as a broken Python install rather than a stale setting.
        /// A stored path is now only used if it still holds pipeline/; otherwise the
        /// folder above the Unity project is used and saved, which is where the repo is
        /// on both of our machines.
        /// </summary>
        static string ResolveRepoPath()
        {
            string stored = EditorPrefs.GetString(RepoKey, "");
            if (LooksLikeRepo(stored)) return stored;

            string guess = GuessRepoPath();
            if (LooksLikeRepo(guess))
            {
                EditorPrefs.SetString(RepoKey, guess);
                if (!string.IsNullOrEmpty(stored))
                    Debug.Log("Study Control: the saved repo path no longer exists, so it " +
                              "now points at " + guess + ".");
                return guess;
            }
            return stored;
        }

        static bool LooksLikeRepo(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   Directory.Exists(Path.Combine(path, "pipeline"));
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
