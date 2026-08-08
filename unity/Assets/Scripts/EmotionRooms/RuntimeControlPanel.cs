// The researcher panel inside the built app.
//
// The editor Study Control Panel is an EditorWindow, so it does not exist in a build,
// and a build is what a second researcher on a different machine actually runs. This is
// the same procedure with the same steps, drawn by the app itself.
//
// It deliberately does not offer "prepare participant": that needs the Python pipeline,
// which the person running a session should not have to install. The stimuli for the
// whole sample are pre-built into StreamingAssets by `build-participants`, so running a
// session is choosing a number.
//
// Press F9 to show or hide it. Hidden by default so a participant never sees it.

using System.Collections.Generic;
using UnityEngine;

namespace EmotionRooms
{
    public class RuntimeControlPanel : MonoBehaviour
    {
        public StudyBootstrap bootstrap;
        public TrialRunner trialRunner;
        public OversightReview review;
        public QuestionnaireRunner questionnaires;
        public FormServer server;

        [Tooltip("Shows and hides the panel. The participant should never see it.")]
        public KeyCode toggleKey = KeyCode.F9;

        [Tooltip("Open on launch, so a researcher is not hunting for a key they were " +
                 "never told about.")]
        public bool visibleOnStart = true;

        bool visible;
        Vector2 scroll;
        int selected;
        List<string> participants = new List<string>();

        void Start()
        {
            visible = visibleOnStart;
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;

            // Refreshed rather than read once: the packs arrive asynchronously so the
            // list is empty for the first few frames.
            if (ShippedAssets.Ready && participants.Count == 0)
            {
                participants = ParticipantPacks.Available();
                if (participants.Count == 0)
                    Debug.LogWarning("RuntimeControlPanel: no participant packs in this " +
                                     "build. Build them with: python3 -m pipeline.cli " +
                                     "build-participants, then rebuild the app.");
            }
        }

        void OnGUI()
        {
            if (!visible) return;

            float w = Mathf.Min(430f, Screen.width * 0.45f);
            var area = new Rect(Screen.width - w - 12f, 12f, w, Screen.height - 24f);

            GUI.DrawTexture(area, Panel());
            GUI.Box(area, GUIContent.none);

            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 12f, area.width - 28f,
                                         area.height - 24f));
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("Study control", Head());
            GUILayout.Label("F9 hides this. The participant should not see it.", Fine());
            GUILayout.Space(8f);

            DrawPreflight();
            DrawParticipant();
            DrawForms("Before the headset goes on", "before");
            DrawRun();
            DrawForms("After the headset comes off", "after");
            DrawFinish();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Everything that has to be true before a participant sits down, checked here
        /// rather than discovered during a session.
        ///
        /// This is the only screen the person running the study can see, and they may not
        /// be the person who built the app. A missing headset or an empty pack folder has
        /// to be visible in the ten seconds before someone arrives, not inferred from a
        /// room that never appears.
        /// </summary>
        void DrawPreflight()
        {
            GUILayout.Label("0. Before anyone sits down", Sub());

            bool packs = participants.Count > 0;
            bool forms = questionnaires != null && questionnaires.FormCount > 0;
            bool serving = server != null && server.IsRunning;
            bool writable = CanWriteData();

            Check(packs, packs ? participants.Count + " participants loaded"
                               : "NO participant rooms in this build");
            Check(forms, forms ? "questionnaires loaded" : "NO questionnaires in this build");
            Check(serving, serving ? "forms reachable at " + server.Root
                                   : "form server not running");
            Check(writable, writable ? "data folder writable" : "CANNOT WRITE DATA");
            GUILayout.Space(10f);
        }

        static bool CanWriteData()
        {
            try
            {
                string probe = System.IO.Path.Combine(Application.persistentDataPath, ".probe");
                System.IO.File.WriteAllText(probe, "ok");
                System.IO.File.Delete(probe);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        void Check(bool good, string text)
        {
            GUILayout.BeginHorizontal();
            var style = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = good ? new Color(0.35f, 0.8f, 0.45f)
                                            : new Color(0.95f, 0.65f, 0.25f) },
            };
            GUILayout.Label(good ? "OK" : "!!", style, GUILayout.Width(24f));
            GUILayout.Label(text, Fine());
            GUILayout.EndHorizontal();
        }

        void DrawParticipant()
        {
            GUILayout.Label("1. Participant", Sub());

            if (participants.Count == 0)
            {
                GUILayout.Label(ShippedAssets.Ready
                    ? "No participant packs found in this build."
                    : "Reading the participant index…", Fine());
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(32f)))
                selected = Mathf.Max(0, selected - 1);
            GUILayout.Label(participants[selected], Head(), GUILayout.Width(70f));
            if (GUILayout.Button(">", GUILayout.Width(32f)))
                selected = Mathf.Min(participants.Count - 1, selected + 1);
            GUILayout.FlexibleSpace();
            GUILayout.Label((selected + 1) + " of " + participants.Count, Fine());
            GUILayout.EndHorizontal();

            bool running = trialRunner != null && trialRunner.IsRunning;
            using (new Scope(!running))
            {
                if (GUILayout.Button("Use " + participants[selected], GUILayout.Height(26f)))
                {
                    bootstrap.participantId = participants[selected];
                    bootstrap.ApplyParticipantId();
                    Debug.Log("RuntimeControlPanel: participant set to " + bootstrap.participantId);
                }
            }
            GUILayout.Label("Now: " + (bootstrap != null ? bootstrap.participantId : "?") +
                            ".  Never reuse one.", Fine());
            GUILayout.Space(10f);
        }

        void DrawForms(string title, string when)
        {
            GUILayout.Label(title, Sub());

            if (questionnaires == null || server == null || !server.IsRunning)
            {
                GUILayout.Label("Form server not running — see the log.", Fine());
                GUILayout.Space(10f);
                return;
            }

            bool onHeadset = Application.platform == RuntimePlatform.Android;
            GUILayout.Label(onHeadset
                ? "Open these on the RESEARCHER'S LAPTOP, at the address below. Do not " +
                  "try to answer them in the headset."
                : "Open in a browser on this machine:", Fine());
            foreach (var form in questionnaires.Due(when))
            {
                var state = questionnaires.StateOf(form.id);
                GUILayout.BeginHorizontal();
                GUILayout.Label(state == FormState.Completed ? "done" : "  ·  ",
                    Fine(), GUILayout.Width(38f));
                using (new Scope(!onHeadset))
                {
                    if (GUILayout.Button(form.title, GUILayout.Height(22f)))
                        Application.OpenURL(server.Root + "form?id=" + form.id);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(onHeadset ? server.NetworkRoot : server.Root, Fine());
            GUILayout.Space(10f);
        }

        void DrawRun()
        {
            GUILayout.Label("2. The rooms", Sub());

            bool running = trialRunner != null && trialRunner.IsRunning;
            bool reviewing = review != null && review.IsRunning;

            if (running)
                GUILayout.Label("Running — trial " + trialRunner.CompletedTrials + " of 8.", Fine());
            else if (reviewing)
                GUILayout.Label("Review block running.", Fine());

            using (new Scope(!running && !reviewing && bootstrap != null))
            {
                if (GUILayout.Button(running || reviewing ? "Running…" : "Begin",
                                     GUILayout.Height(30f)))
                    bootstrap.BeginStudy();
            }
            GUILayout.Label("Fit the headset first. ~27 min of rooms and review.", Fine());
            GUILayout.Space(10f);
        }

        void DrawFinish()
        {
            GUILayout.Label("3. Finish", Sub());

            if (questionnaires != null)
            {
                var outstanding = questionnaires.Outstanding();
                GUILayout.Label(outstanding.Count == 0
                    ? "All forms completed."
                    : outstanding.Count + " form(s) not completed:", Fine());
                foreach (var line in outstanding) GUILayout.Label("   " + line, Fine());
            }

            if (GUILayout.Button("Save and finish this participant", GUILayout.Height(26f)))
            {
                string path = SessionBundle.Write(
                    bootstrap != null ? bootstrap.participantId : "unknown");
                Debug.Log(path == null
                    ? "RuntimeControlPanel: nothing to save yet."
                    : "RuntimeControlPanel: saved to " + path);
                if (questionnaires != null) questionnaires.ShowSummary();
            }
            GUILayout.Label("Also written automatically when the app closes.", Fine());
            GUILayout.Space(6f);

            if (GUILayout.Button("Where is the data?", GUILayout.Height(22f)))
                Debug.Log("Data folder: " + Application.persistentDataPath);
            GUILayout.Label(Application.persistentDataPath, Fine());
        }

        // ------------------------------------------------------------------- chrome

        class Scope : System.IDisposable
        {
            public Scope(bool enabled) { GUI.enabled = enabled; }
            public void Dispose() { GUI.enabled = true; }
        }

        static Texture2D panel;
        static GUIStyle head, sub, fine;

        static Texture2D Panel()
        {
            if (panel != null) return panel;
            panel = new Texture2D(1, 1);
            panel.SetPixel(0, 0, new Color(0.09f, 0.09f, 0.11f, 0.96f));
            panel.Apply();
            panel.hideFlags = HideFlags.HideAndDontSave;
            return panel;
        }

        static GUIStyle Head()
        {
            if (head == null)
                head = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 17, fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.94f, 0.94f, 0.96f) },
                };
            return head;
        }

        static GUIStyle Sub()
        {
            if (sub == null)
                sub = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14, fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.55f, 0.8f, 1f) },
                };
            return sub;
        }

        static GUIStyle Fine()
        {
            if (fine == null)
                fine = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11, wordWrap = true,
                    normal = { textColor = new Color(0.72f, 0.72f, 0.76f) },
                };
            return fine;
        }
    }
}
