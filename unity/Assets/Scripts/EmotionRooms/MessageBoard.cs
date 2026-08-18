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
        GameObject plate;

        void Awake()
        {
            board = new GameObject("Message").transform;
            board.SetParent(transform, false);

            // Through WorldLabel so the board inherits the black outline: these
            // messages appear over rooms that range from 150 to 750 lux, and plain
            // white text is unreadable at the bright end.
            // A dark plate behind the words, so the room cannot compete with them.
            //
            // Mengkai raised this: the ink is amber, and orange is one of the ten hues
            // in the pool, so on a bright orange wall the text is amber on orange. She
            // is right, and picking a different ink only moves the problem to whichever
            // hue the new colour is near. The pools contain ten hues and no single ink
            // is far from all of them.
            //
            // A plate takes the room out of the comparison entirely. The text is then
            // read against a fixed dark surface whatever the wall is doing, at 150 lux
            // or at 750, in orange or in blue. Built before the text so it sorts behind
            // it, and unlit so a dim room does not dim the instrument.
            plate = WorldLabel.Solid("Quad.fbx", "Message Plate", board);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            plate.transform.localScale = new Vector3(1.15f, 0.42f, 1f);
            var plateRenderer = plate.GetComponent<MeshRenderer>();
            if (plateRenderer != null)
            {
                var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                if (shader != null)
                    plateRenderer.sharedMaterial = new Material(shader)
                    { name = "Message Plate", color = new Color(0.06f, 0.06f, 0.08f) };
                plateRenderer.sortingOrder = 99;
            }
            // A plate the pointer can hit would eat clicks meant for the panels behind
            // it, and the review block blocks on an answer, so a swallowed click hangs.
            var stray = plate.GetComponent<Collider>();
            if (stray != null) DestroyImmediate(stray);

            text = WorldLabel.Attach(board, "", 0.03f);

            // Drawn over the room rather than through it, so a message never ends up
            // inside a wall where it cannot be read.
            var renderer = board.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sortingOrder = 100;

            Hide();
        }

        /// <summary>
        /// What is on the board right now, empty when nothing is. Read by the researcher
        /// panel so the person running the session can see the words the participant is
        /// reading, and repeat them exactly if they were missed. Paraphrasing an
        /// instruction is not neutral when the wording is part of the manipulation.
        /// </summary>
        public string Current { get; private set; }

        [Tooltip("Characters per line. Long single lines run off the plate and off the " +
                 "readable part of a headset's view.")]
        public int charactersPerLine = 30;

        [Tooltip("Follow the head. Turned off while a question is on screen, so the " +
                 "reasoning stays where it was put instead of chasing the eyes.")]
        public bool follow = true;

        [Tooltip("Metres above eye line. Lifted while a question panel is up so the two " +
                 "do not sit on top of each other.")]
        public float verticalOffset;

        [Tooltip("Metres to the side. Negative is left. Used to park the reasoning beside " +
                 "the buttons rather than above them.")]
        public float horizontalOffset;

        public void Show(string message)
        {
            // Wrapped, and the plate resized to fit what wrapping produced.
            //
            // Messages used to be set raw. A rationale is two long sentences, so it went
            // out as one enormous horizontal line: wider than the plate behind it, wider
            // than the comfortable field of view, and unreadable without turning the
            // head to track along it. Shorter lines and more of them is the right shape
            // for text at 1.6 m.
            string wrapped = WorldLabel.Wrap(message ?? "", charactersPerLine);
            WorldLabel.SetText(text, wrapped);
            FitPlate(wrapped);
            PlaceInFront();
            board.gameObject.SetActive(true);
            Current = message ?? "";
        }

        /// <summary>Size the backing to the wrapped text, so it always covers it.</summary>
        void FitPlate(string wrapped)
        {
            if (plate == null) return;

            int lines = 1, widest = 0, run = 0;
            foreach (char c in wrapped)
            {
                if (c == '\n') { lines++; if (run > widest) widest = run; run = 0; }
                else run++;
            }
            if (run > widest) widest = run;

            // 0.03 m glyph height, roughly half that per character of width, plus a
            // margin so the outline is not clipped at the edges.
            float width = Mathf.Max(0.45f, widest * 0.0165f + 0.12f);
            float height = Mathf.Max(0.18f, lines * 0.042f + 0.10f);
            plate.transform.localScale = new Vector3(width, height, 1f);
        }

        public void Hide()
        {
            if (board != null) board.gameObject.SetActive(false);
            Current = "";
        }

        void Update()
        {
            // Follow the viewer while showing. Placed once, the idle message stayed
            // wherever the head was at app start -- someone who walked away with the
            // stick then turned around saw an empty grey stage and read it as broken,
            // because the one sentence explaining the state was behind them somewhere.
            if (board == null || !board.gameObject.activeSelf) return;
            if (!follow) return;

            var camera = Camera.main;
            if (camera == null) return;

            var forward = camera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            var right = Vector3.Cross(Vector3.up, forward).normalized * -1f;
            var wanted = camera.transform.position + forward * distance
                         + Vector3.up * verticalOffset
                         + right * horizontalOffset;
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

            var side = Vector3.Cross(Vector3.up, forward).normalized * -1f;
            board.position = camera.transform.position + forward * distance
                           + Vector3.up * verticalOffset
                           + side * horizontalOffset;
            board.rotation = Quaternion.LookRotation(forward);
        }
    }
}
