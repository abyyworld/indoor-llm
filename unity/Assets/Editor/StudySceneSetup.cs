// One menu command that builds a runnable study scene and wires everything.
//
//   Emotion Rooms > Set Up Study Scene
//
// Doing this by hand is about forty inspector fields across six components, and a
// mis-wired reference does not fail loudly: it fails as a grid that never responds, or
// a light that never changes, in the middle of a session. Generating it means the
// wiring is the same every time and can be re-run after any change.
//
// Safe to run repeatedly. It replaces what it created and leaves anything else alone.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EmotionRooms.EditorTools
{
    public static class StudySceneSetup
    {
        const string RootName = "Study";

        [MenuItem("Emotion Rooms/Advanced/Set Up Study Scene", priority = 100)]
        public static void SetUp()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog(
                        "Replace study scene?",
                        "A 'Study' object already exists. Rebuild it?\n\n" +
                        "Anything you parented under it will be destroyed. The rooms are " +
                        "rebuilt too.",
                        "Rebuild", "Cancel"))
                {
                    return;
                }
                Object.DestroyImmediate(existing);
            }

            var rooms = GameObject.Find("EmotionRooms");
            if (rooms != null) Object.DestroyImmediate(rooms);

            // Real models if a FurnitureSet exists in the project, procedural otherwise.
            // Found by search rather than by an inspector field: the rooms are rebuilt
            // from a static method, so there is no component alive to hold the reference.
            RoomBuilder.Models = FindFurnitureSet();
            rooms = RoomBuilder.BuildAll();

            var root = new GameObject(RootName);

            var camera = SetUpCamera(root);
            var light = SetUpLight(root);
            var grid = SetUpGrid(root, camera);
            var wallRenderers = CollectTintables(rooms);

            var loader = root.AddComponent<RoomLoader>();
            loader.wallRenderers = wallRenderers;
            loader.roomLight = light;
            loader.linearRoomRoot = FindChild(rooms, "Linear Room Root");
            loader.curvedRoomRoot = FindChild(rooms, "Curved Room Root");
            loader.loadOnStart = false;
            loader.wallTextures = BuildTextureTable();
            // Placeholders. Tune in the headset and record what you settle on: the lux to
            // intensity mapping is a study parameter, not a rendering detail.
            loader.minIntensity = 0.2f;
            loader.maxIntensity = 2.5f;

            // First, so its coroutine is running before anything asks for a file.
            root.AddComponent<ShippedAssets>();

            var events = root.AddComponent<EventLog>();
            events.headTransform = camera.transform;

            var telemetry = root.AddComponent<StudyTelemetry>();
            telemetry.loader = loader;
            telemetry.grid = grid;
            telemetry.headTransform = camera.transform;

            var runner = root.AddComponent<TrialRunner>();
            runner.loader = loader;
            runner.grid = grid;
            runner.events = events;
            runner.sessionFileName = "session.json";
            runner.telemetry = telemetry;

            var review = root.AddComponent<OversightReview>();
            review.loader = loader;
            review.grid = grid;
            review.events = events;
            review.blockFileName = "oversight.json";
            review.telemetry = telemetry;

            // The three review panels. Without these OversightReview waits forever on
            // detectionAnswered and the session hangs after the eighth room.
            var detection = BuildPanel(root, camera, "Detection Panel",
                new[] { "yes", "no" }, true);
            var attribution = BuildPanel(root, camera, "Attribution Panel",
                AttributionLabels(), true);
            var correction = BuildPanel(root, camera, "Correction Panel",
                PoolValueLabels(), false);

            review.detectionPanel = detection;
            review.attributionPanel = attribution;
            review.correctionPanel = correction;

            var forms = root.AddComponent<QuestionnaireRunner>();
            forms.events = events;
            forms.telemetry = telemetry;

            var server = root.AddComponent<FormServer>();
            server.questionnaires = forms;
            server.trialRunner = runner;
            server.review = review;

            var rationale = root.AddComponent<RationaleReview>();
            rationale.loader = loader;
            rationale.events = events;
            rationale.telemetry = telemetry;
            rationale.answerPanel = detection;

            var messageBoard = root.AddComponent<MessageBoard>();
            messageBoard.viewer = camera;

            var bootstrap = root.AddComponent<StudyBootstrap>();
            bootstrap.board = messageBoard;
            bootstrap.rationaleReview = rationale;
            rationale.board = messageBoard;
            bootstrap.questionnaires = forms;
            server.bootstrap = bootstrap;
            bootstrap.detectionPanel = detection;
            bootstrap.attributionPanel = attribution;
            bootstrap.correctionPanel = correction;
            bootstrap.trialRunner = runner;
            bootstrap.oversightReview = review;
            bootstrap.grid = grid;
            bootstrap.fallbackCamera = camera;
            bootstrap.autoStart = false;
            bootstrap.chainOversightBlock = true;

            grid.events = events;
            telemetry.trialRunner = runner;
            telemetry.review = review;

            // After everything exists. The grid and the three question panels build
            // their own materials too, and persisting only the rooms left those invisible
            // in play mode -- the room would hide for the rating and the participant
            // would be looking at an empty scene with the grid right in front of them.
            PersistMaterials(rooms);
            PersistMaterials(root);

            var xr = root.AddComponent<XRRig>();
            xr.headCamera = camera;
            bootstrap.xrRig = xr;

            var runtimePanel = root.AddComponent<RuntimeControlPanel>();
            runtimePanel.bootstrap = bootstrap;
            runtimePanel.trialRunner = runner;
            runtimePanel.review = review;
            runtimePanel.questionnaires = forms;
            runtimePanel.server = server;
            // Off in the editor, where the docked panel is better; on in a build, which
            // is the only interface a second researcher has.
            runtimePanel.visibleOnStart = !Application.isEditor;

            var stamp = root.AddComponent<StudySceneStamp>();
            stamp.version = StudySceneStamp.Current;
            stamp.note = "form server, self-positioning grid, saved materials";

            Undo.RegisterCreatedObjectUndo(root, "Set Up Study Scene");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log(
                "Study scene ready.\n" +
                "  " + wallRenderers.Length + " tintable surfaces wired\n" +
                "  grid at eye height, 1.2 m ahead of the standing position\n" +
                "  telemetry at " + telemetry.sampleHz + " Hz, every column every row\n" +
                "  session.json and oversight.json expected in:\n    " +
                Application.persistentDataPath + "\n\n" +
                "Press play, then use the Study object's context menu (three dots) and " +
                "choose 'Begin Study'. Click cells with the mouse to answer in the editor.");
        }

        // ------------------------------------------------------------------ pieces

        static Camera SetUpCamera(GameObject root)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                // No AudioListener: the study has no audio, and adding one pulls in a
                // module dependency for nothing.
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = go.AddComponent<Camera>();
            }

            // The participant's viewpoint: standing position, eye height, facing in.
            camera.transform.SetParent(root.transform, false);
            camera.transform.position = RoomDimensions.StandingPosition + new Vector3(0f, 1.6f, 0f);
            camera.transform.rotation = Quaternion.identity;
            camera.nearClipPlane = 0.05f;
            return camera;
        }

        static Light SetUpLight(GameObject root)
        {
            var go = new GameObject("Room Light");
            go.transform.SetParent(root.transform, false);
            // Centred, just below the ceiling. Position is fixed for the whole study:
            // only intensity ever varies.
            go.transform.position = new Vector3(
                0f, RoomDimensions.CeilingHeight - 0.25f, RoomDimensions.Depth / 2f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 12f;
            light.shadows = LightShadows.Soft;
            // 4500K neutral white (Mengkai, 31 Jul). The loader forces white anyway,
            // because hue lives on the walls and must not also live on the light.
            light.color = Mathf.CorrelatedColorTemperatureToRGB(4500f);
            return light;
        }

        static AffectGrid SetUpGrid(GameObject root, Camera camera)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Affect Grid";
            quad.transform.SetParent(root.transform, false);
            quad.transform.position = camera.transform.position + camera.transform.forward * 1.2f;
            quad.transform.rotation = Quaternion.LookRotation(camera.transform.forward);
            quad.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

            // A quad ships with a MeshCollider; AffectGrid raycasts a BoxCollider.
            Object.DestroyImmediate(quad.GetComponent<MeshCollider>());
            var box = quad.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1f, 1f, 0.01f);

            var grid = quad.AddComponent<AffectGrid>();
            grid.viewer = camera;
            grid.cells = 9;
            grid.hoverMarker = Marker(quad, "Hover Marker", new Color(1f, 1f, 1f, 0.9f), 0.05f);
            grid.selectionMarker = Marker(quad, "Selection Marker", new Color(0.2f, 0.9f, 0.3f), 0.07f);

            quad.SetActive(false);   // shown only when a response is wanted
            return grid;
        }

        const string MaterialFolder = "Assets/EmotionRooms/Materials";

        /// <summary>
        /// Replace every runtime-created material with a saved .mat asset.
        ///
        /// RoomBuilder builds materials with `new Material(...)`, which is correct for a
        /// runtime path but does not survive the editor. Nothing references those objects
        /// from an asset, so entering play mode -- or any script recompile -- destroys
        /// them and leaves every renderer with a null sharedMaterial. The symptom is
        /// RoomLoader throwing "No wall renderer with a material to derive the room
        /// material from" on the first trial, which reads like a wiring problem and is
        /// not one.
        ///
        /// Deduplicated by material name, so the eight furniture shades and the wall
        /// surface become nine assets rather than one per renderer.
        /// </summary>
        static void PersistMaterials(GameObject subject)
        {
            Directory.CreateDirectory(MaterialFolder);

            var byName = new System.Collections.Generic.Dictionary<string, Material>();
            foreach (var renderer in subject.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    var material = slots[i];
                    if (material == null) continue;
                    if (AssetDatabase.Contains(material)) continue;

                    Material asset;
                    if (!byName.TryGetValue(material.name, out asset))
                    {
                        string path = AssetDatabase.GenerateUniqueAssetPath(
                            MaterialFolder + "/" + Sanitise(material.name) + ".mat");
                        asset = new Material(material);
                        AssetDatabase.CreateAsset(asset, path);
                        byName[material.name] = asset;
                    }
                    slots[i] = asset;
                }
                renderer.sharedMaterials = slots;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (byName.Count > 0)
                Debug.Log("Emotion Rooms: saved " + byName.Count + " materials from " +
                          subject.name + " to " + MaterialFolder +
                          " so they survive play mode.");
        }

        static string Sanitise(string name)
        {
            foreach (var bad in Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');
            return name;
        }

        /// <summary>The project's FurnitureSet, or null to use the placeholders.</summary>
        public static FurnitureSet FindFurnitureSet()
        {
            var guids = AssetDatabase.FindAssets("t:FurnitureSet");
            if (guids.Length == 0) return null;
            if (guids.Length > 1)
                Debug.LogWarning("Emotion Rooms: " + guids.Length + " FurnitureSet assets " +
                                 "found; using the first. Keep exactly one so every " +
                                 "participant sees the same furnishing.");
            return AssetDatabase.LoadAssetAtPath<FurnitureSet>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        static string[] AttributionLabels()
        {
            // Field names exactly as RoomConfig.With and the block file's swapped_field
            // spell them, so a chosen attribution can be scored and applied without a
            // translation step. Plus a way to say the detection was a false alarm.
            var labels = new System.Collections.Generic.List<string>(PoolConstants.Attributable);
            labels.Add("nothing_wrong");
            return labels.ToArray();
        }

        static string[] PoolValueLabels()
        {
            // Every value a participant could propose, across all five variables. Built
            // from the same ValuesFor the correction panel narrows by, so the strings
            // match exactly -- a near-miss here would show an empty correction screen.
            var labels = new System.Collections.Generic.List<string>();
            foreach (var field in PoolConstants.Attributable)
            {
                var values = PoolConstants.ValuesFor(field);
                if (values == null) continue;
                foreach (var v in values)
                {
                    if (!labels.Contains(v)) labels.Add(v);
                }
            }
            return labels.ToArray();
        }

        static QuestionPanel BuildPanel(GameObject root, Camera camera, string name,
                                        string[] values, bool withConfidence)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.position = camera.transform.position + camera.transform.forward * 1.2f;
            go.transform.rotation = Quaternion.LookRotation(camera.transform.forward);

            var panel = go.AddComponent<QuestionPanel>();

            // Laid out in a grid so a long option list stays reachable without leaning.
            int perRow = values.Length > 6 ? 5 : Mathf.Max(values.Length, 1);
            float w = 0.22f, h = 0.11f, gapX = 0.02f, gapY = 0.03f;

            for (int i = 0; i < values.Length; i++)
            {
                int col = i % perRow;
                int rowIndex = i / perRow;
                int inThisRow = Mathf.Min(perRow, values.Length - rowIndex * perRow);

                var cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cell.name = values[i];
                cell.transform.SetParent(go.transform, false);
                cell.transform.localScale = new Vector3(w, h, 0.01f);
                cell.transform.localPosition = new Vector3(
                    (col - (inThisRow - 1) / 2f) * (w + gapX),
                    0.2f - rowIndex * (h + gapY),
                    0f);

                var collider = cell.GetComponent<BoxCollider>();
                collider.isTrigger = true;   // must not push the rig around

                var material = new Material(DefaultShader()) { name = values[i] };
                material.color = new Color(0.85f, 0.85f, 0.85f);
                cell.GetComponent<Renderer>().sharedMaterial = material;

                panel.options.Add(new QuestionPanel.Option
                {
                    value = values[i],
                    target = cell.transform,
                });
            }

            if (withConfidence)
            {
                var strip = new GameObject("Confidence").transform;
                strip.SetParent(go.transform, false);
                strip.localPosition = new Vector3(0f, -0.28f, 0f);
                panel.confidenceStrip = strip;
                panel.confidenceSteps = 5;

                for (int i = 0; i < panel.confidenceSteps; i++)
                {
                    var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    step.name = "conf_" + i;
                    step.transform.SetParent(strip, false);
                    step.transform.localScale = new Vector3(0.12f, 0.06f, 0.01f);
                    step.transform.localPosition =
                        new Vector3((i - (panel.confidenceSteps - 1) / 2f) * 0.14f, 0f, 0f);
                    step.GetComponent<BoxCollider>().isTrigger = true;

                    var material = new Material(DefaultShader()) { name = step.name };
                    material.color = new Color(0.85f, 0.85f, 0.85f);
                    step.GetComponent<Renderer>().sharedMaterial = material;
                }
            }

            go.SetActive(false);
            return panel;
        }

        static Transform Marker(GameObject parent, string name, Color colour, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localScale = Vector3.one * size;
            Object.DestroyImmediate(go.GetComponent<Collider>());  // must not block the grid

            var material = new Material(DefaultShader()) { name = name + " Material" };
            material.color = colour;
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.SetActive(false);
            return go.transform;
        }

        static Renderer[] CollectTintables(GameObject rooms)
        {
            var marked = rooms.GetComponentsInChildren<TintableSurface>(true);
            var renderers = new Renderer[marked.Length];
            for (int i = 0; i < marked.Length; i++)
            {
                renderers[i] = marked[i].GetComponent<Renderer>();
            }
            return renderers;
        }

        static WallTexture[] BuildTextureTable()
        {
            // One entry per pool value, names matching exactly, each with a baked
            // greyscale map. The maps used to be left null, which RoomLoader rejects
            // outright -- and simply allowing null would have been worse: three
            // identical flat walls, with `texture` manipulated in the data and invisible
            // in the headset. The maps are greyscale so the config's hue tints them
            // rather than fighting them.
            TextureBaker.BakeAll();
            var table = new WallTexture[PoolConstants.Textures.Length];
            for (int i = 0; i < table.Length; i++)
            {
                string name = PoolConstants.Textures[i];
                table[i] = new WallTexture
                {
                    name = name,
                    greyscaleMap = TextureBaker.Load(name),
                    smoothness = name == "plaster" ? 0.35f : name == "textile" ? 0.1f : 0.2f,
                    tiling = 2f,
                };
            }
            return table;
        }

        static GameObject FindChild(GameObject parent, string name)
        {
            var child = parent.transform.Find(name);
            return child != null ? child.gameObject : null;
        }

        static Shader DefaultShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        }

        // ------------------------------------------------------------------ checks

        [MenuItem("Emotion Rooms/Advanced/Check Scene", priority = 101)]
        public static void CheckScene()
        {
            var problems = new System.Collections.Generic.List<string>();

            var root = GameObject.Find(RootName);
            if (root == null)
            {
                Debug.LogError("No 'Study' object. Run Emotion Rooms > Set Up Study Scene.");
                return;
            }

            var loader = root.GetComponent<RoomLoader>();
            if (loader == null) problems.Add("no RoomLoader");
            else
            {
                if (loader.wallRenderers == null || loader.wallRenderers.Length == 0)
                    problems.Add("RoomLoader has no wall renderers, so nothing will be tinted");
                if (loader.roomLight == null)
                    problems.Add("RoomLoader has no light, so illuminance will not vary");
                if (loader.linearRoomRoot == null || loader.curvedRoomRoot == null)
                    problems.Add("RoomLoader is missing a shape root, so shape will not switch");
                if (loader.wallTextures == null || loader.wallTextures.Length != PoolConstants.Textures.Length)
                    problems.Add("wall texture table does not cover every material in the pool");
            }

            var runner = root.GetComponent<TrialRunner>();
            if (runner == null) problems.Add("no TrialRunner");
            else if (runner.grid == null) problems.Add("TrialRunner has no affect grid, so no response can be collected");

            var bootstrap = root.GetComponent<StudyBootstrap>();
            if (bootstrap == null) problems.Add("no StudyBootstrap, so nothing drives grid input");
            else if (bootstrap.grid == null) problems.Add("StudyBootstrap has no grid; it will be unresponsive");

            var review = root.GetComponent<OversightReview>();
            if (review == null) problems.Add("no OversightReview");
            else
            {
                // The failure this catches is the nastiest one in the study: a missing
                // panel does not error, it makes the review block wait forever on an
                // answer nobody can give, half an hour into a session with a participant
                // in the headset.
                if (review.detectionPanel == null)
                    problems.Add("OversightReview has no detection panel; the review block will hang");
                if (review.attributionPanel == null)
                    problems.Add("OversightReview has no attribution panel; it will hang after a detection");
                if (review.correctionPanel == null)
                    problems.Add("OversightReview has no correction panel; it will hang after an attribution");
            }

            if (root.GetComponent<QuestionnaireRunner>() == null)
                problems.Add("no QuestionnaireRunner, so no consent, TLX, SSQ or debrief " +
                             "forms will appear");
            else if (!File.Exists(Path.Combine(Application.streamingAssetsPath,
                                               "questionnaires.json")))
                problems.Add("no StreamingAssets/questionnaires.json (build it with: " +
                             "python3 -m pipeline.cli emit-questionnaires). The session " +
                             "will run, but with no forms at all.");

            if (bootstrap != null && bootstrap.detectionPanel == null)
                problems.Add("StudyBootstrap has no panels wired, so nothing forwards an " +
                             "answer to OversightReview and the review block will hang");

            var events = root.GetComponent<EventLog>();
            if (events != null && events.headTransform == null)
                problems.Add("EventLog has no head transform, so head pose will not be logged");

            string session = Path.Combine(Application.persistentDataPath, "session.json");
            if (!File.Exists(session))
                problems.Add("no session.json at " + Application.persistentDataPath +
                             " (build one with: python3 -m pipeline.cli export-unity)");

            if (problems.Count == 0)
                Debug.Log("Scene check passed. Session file found, everything wired.");
            else
                Debug.LogWarning("Scene check found " + problems.Count + " issue(s):\n  - " +
                                 string.Join("\n  - ", problems.ToArray()));
        }

        [MenuItem("Emotion Rooms/Advanced/Reveal Data Folder", priority = 102)]
        public static void RevealDataFolder()
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            EditorUtility.RevealInFinder(Application.persistentDataPath);
            Debug.Log("Session files go here: " + Application.persistentDataPath);
        }
    }
}
