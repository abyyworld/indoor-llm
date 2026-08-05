// Offscreen renderer: turns configs into still images for the Phase B online study.
//
// Phase B shows people rendered rooms rather than putting them in a headset, because
// reviewing output is how people actually supervise an agent, and because it scales past
// the one-participant-at-a-time bottleneck of VR.
//
// Renders from the participant's standing position with the same field of view they
// would have in the headset, so the stills are not a different stimulus from the VR
// scenes -- they are the same room, seen from the same place.
//
// Unity menu: Emotion Rooms > Render Stills From Session
//
// Deliberately renders through the ordinary RoomLoader path rather than setting
// materials directly. If the renderer applied colour its own way, the images could drift
// from what the headset shows, and the two phases would stop being about the same rooms.

using System.Collections;
using System.IO;
using UnityEngine;

namespace EmotionRooms
{
    public class SceneRenderer : MonoBehaviour
    {
        [Header("Wiring")]
        public RoomLoader loader;

        [Tooltip("Camera used for stills. Left empty, one is created at the standing " +
                 "position defined in RoomDimensions.")]
        public Camera renderCamera;

        [Header("Output")]
        public int width = 1600;
        public int height = 900;

        [Tooltip("Vertical FOV. 60 is a reasonable still-image default; set it to the " +
                 "headset's vertical FOV if you want the framing to match exactly.")]
        public float fieldOfView = 60f;

        [Tooltip("Folder under Application.persistentDataPath.")]
        public string outputFolder = "stills";

        [Tooltip("Frames to wait after loading before capturing, so lighting and any " +
                 "material changes have actually been applied.")]
        public int settleFrames = 3;

        public string OutputPath
        {
            get { return Path.Combine(Application.persistentDataPath, outputFolder); }
        }

        /// <summary>Render every room in a batch, one PNG per room, named by id.</summary>
        public IEnumerator RenderBatch(RoomBatch batch)
        {
            if (batch == null || batch.rooms == null || batch.rooms.Length == 0)
            {
                Debug.LogError("SceneRenderer: nothing to render.");
                yield break;
            }

            Directory.CreateDirectory(OutputPath);
            Camera camera = EnsureCamera();

            foreach (var config in batch.rooms)
            {
                var errors = config.Validate();
                if (errors.Count > 0)
                {
                    // Same rule as the headset: nothing unvalidated becomes a stimulus.
                    Debug.LogError("SceneRenderer: skipping invalid config " + config.Id +
                                   ": " + string.Join("; ", errors.ToArray()));
                    continue;
                }

                loader.Load(config);
                for (int i = 0; i < settleFrames; i++) yield return new WaitForEndOfFrame();

                string file = Path.Combine(OutputPath, SafeName(config) + ".png");
                Capture(camera, file);
                Debug.Log("SceneRenderer: wrote " + file);
            }

            Debug.Log("SceneRenderer: finished, images in " + OutputPath);
        }

        static string SafeName(RoomConfig config)
        {
            string id = string.IsNullOrEmpty(config.Id) ? "room" : config.Id;
            string shape = string.IsNullOrEmpty(config.Shape) ? "" : "_" + config.Shape;
            return id + shape;
        }

        Camera EnsureCamera()
        {
            if (renderCamera != null) return renderCamera;

            var go = new GameObject("Still Camera");
            var camera = go.AddComponent<Camera>();

            // Same viewpoint as the participant: standing position, eye height, facing
            // into the room. Anything else would make the stills a different stimulus.
            go.transform.position = RoomDimensions.StandingPosition + new Vector3(0f, 1.6f, 0f);
            go.transform.rotation = Quaternion.identity;
            camera.fieldOfView = fieldOfView;

            renderCamera = camera;
            return camera;
        }

        void Capture(Camera camera, string path)
        {
            var target = new RenderTexture(width, height, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            camera.targetTexture = target;
            camera.Render();

            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();

            File.WriteAllBytes(path, image.EncodeToPNG());

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Destroy(image);
            target.Release();
            Destroy(target);
        }
    }
}
