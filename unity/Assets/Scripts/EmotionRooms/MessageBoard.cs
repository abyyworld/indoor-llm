// Words the participant can actually read, in VR.
//
// Every status message in this project was drawn with IMGUI, which renders to the screen
// and not into an immersive view -- so on the headset there was no "finished", no "please
// wait", nothing. A session ended by simply going quiet, which is indistinguishable from
// a session that has crashed.
//
// A TextMesh in world space is the smallest thing that fixes that: no TextMeshPro import,
// no canvas, no event camera, and it is visible from anywhere in the room.

using UnityEngine;

namespace EmotionRooms
{
    public class MessageBoard : MonoBehaviour
    {
        public Camera viewer;

        [Tooltip("Metres in front of the participant.")]
        public float distance = 1.6f;

        TextMesh text;
        Transform board;

        void Awake()
        {
            board = new GameObject("Message").transform;
            board.SetParent(transform, false);

            text = board.gameObject.AddComponent<TextMesh>();
            // Same sizing rule as WorldLabel: characterSize is not metres. 0.045 m lines.
            text.fontSize = 96;
            text.characterSize = 0.03f * 10f / text.fontSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.96f, 0.96f, 0.98f);

            // Drawn over the room rather than through it, so a message never ends up
            // inside a wall where it cannot be read.
            var renderer = board.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sortingOrder = 100;

            Hide();
        }

        public void Show(string message)
        {
            text.text = message;
            PlaceInFront();
            board.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (board != null) board.gameObject.SetActive(false);
        }

        void Update()
        {
            // Follow the viewer while showing. Placed once, the idle message stayed
            // wherever the head was at app start -- someone who walked away with the
            // stick then turned around saw an empty grey stage and read it as broken,
            // because the one sentence explaining the state was behind them somewhere.
            if (board == null || !board.gameObject.activeSelf) return;

            var camera = Camera.main;
            if (camera == null) return;

            var forward = camera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            var wanted = camera.transform.position + forward * distance;
            // Eased, not snapped: text glued rigidly to the head is unreadable and
            // nauseating; text that drifts after it reads as a sign hanging in space.
            board.position = Vector3.Lerp(board.position, wanted, Time.deltaTime * 3f);
            var look = board.position - camera.transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f)
                board.rotation = Quaternion.Slerp(board.rotation,
                    Quaternion.LookRotation(look), Time.deltaTime * 3f);
        }

        void PlaceInFront()
        {
            var camera = viewer != null ? viewer : Camera.main;
            if (camera == null) return;

            var forward = camera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            board.position = camera.transform.position + forward * distance;
            board.rotation = Quaternion.LookRotation(forward);
        }
    }
}
