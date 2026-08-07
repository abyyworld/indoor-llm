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
            text.characterSize = 0.05f;
            text.fontSize = 96;
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
