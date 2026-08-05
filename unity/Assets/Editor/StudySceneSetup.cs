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

        [MenuItem("Emotion Rooms/Set Up Study Scene", priority = 0)]
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

            var events = root.AddComponent<EventLog>();
            events.headTransform = camera.transform;

            var runner = root.AddComponent<TrialRunner>();
            runner.loader = loader;
            runner.grid = grid;
            runner.events = events;
            runner.sessionFileName = "session.json";

            var review = root.AddComponent<OversightReview>();
            review.loader = loader;
            review.grid = grid;
            review.events = events;
            review.blockFileName = "oversight.json";

            var bootstrap = root.AddComponent<StudyBootstrap>();
            bootstrap.trialRunner = runner;
            bootstrap.oversightReview = review;
            bootstrap.grid = grid;
            bootstrap.fallbackCamera = camera;
            bootstrap.autoStart = false;
            bootstrap.chainOversightBlock = true;

            grid.events = events;

            Undo.RegisterCreatedObjectUndo(root, "Set Up Study Scene");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log(
                "Study scene ready.\n" +
                "  " + wallRenderers.Length + " tintable surfaces wired\n" +
                "  grid at eye height, 1.2 m ahead of the standing position\n" +
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
            grid.cells = 9;
            grid.hoverMarker = Marker(quad, "Hover Marker", new Color(1f, 1f, 1f, 0.9f), 0.05f);
            grid.selectionMarker = Marker(quad, "Selection Marker", new Color(0.2f, 0.9f, 0.3f), 0.07f);

            quad.SetActive(false);   // shown only when a response is wanted
            return grid;
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
            // One entry per pool value, names matching exactly. The maps are left empty:
            // a null map tints cleanly, so the study runs on flat colour until real
            // greyscale textures are dropped in. Any map added later MUST be greyscale,
            // or it fights the hue and the manipulation stops being clean.
            var table = new WallTexture[PoolConstants.Textures.Length];
            for (int i = 0; i < table.Length; i++)
            {
                string name = PoolConstants.Textures[i];
                table[i] = new WallTexture
                {
                    name = name,
                    greyscaleMap = null,
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

        [MenuItem("Emotion Rooms/Check Scene", priority = 20)]
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

        [MenuItem("Emotion Rooms/Reveal Data Folder", priority = 21)]
        public static void RevealDataFolder()
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            EditorUtility.RevealInFinder(Application.persistentDataPath);
            Debug.Log("Session files go here: " + Application.persistentDataPath);
        }
    }
}
