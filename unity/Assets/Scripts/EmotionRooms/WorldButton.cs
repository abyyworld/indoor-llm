// A button in the room, pressed by pointing at it and squeezing the trigger.
//
// The study needed two things it did not have: a way to say "this is my answer" separately
// from choosing it, and a way to say "I am ready for the next room". Both were implicit
// before -- a rating committed the instant the trigger went down, and rooms advanced on a
// timer -- which gave a participant no way to change their mind and no control over pace.

using System;
using UnityEngine;

namespace EmotionRooms
{
    public class WorldButton : MonoBehaviour
    {
        public event Action Pressed;

        [Tooltip("Ignore hits for this long after appearing, so the trigger press that " +
                 "answered the previous screen cannot fall straight through this one.")]
        public float inputLockSeconds = 0.4f;

        [Tooltip("The caption. Held here rather than parented to the slab because the " +
                 "slab is scaled to a wide flat box and text inside it inherits that stretch.")]
        public GameObject label;

        public bool IsShowing { get; private set; }

        /// <summary>
        /// Build a button in code rather than in the scene.
        ///
        /// Everything here used to be created by scene setup and serialized into the
        /// scene file. The built player reads that file positionally -- it carries no
        /// type tree -- so every new component in it is another chance for the data and
        /// the shipped layout to disagree, and when they do the player dies during scene
        /// load with no managed exception and nothing in the log. XRRig already builds
        /// its rig and pointer this way for the same reason. Nothing that can be built at
        /// runtime should be sitting in the scene file.
        /// </summary>
        public static WorldButton Create(Transform parent, string name, string caption,
                                         Vector3 localPosition, float width, float height)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(parent, false);
            slab.transform.localPosition = localPosition;
            slab.transform.localScale = new Vector3(width, height, 0.02f);

            var box = slab.GetComponent<BoxCollider>();
            if (box != null) box.isTrigger = true;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var renderer = slab.GetComponent<Renderer>();
            if (renderer != null && shader != null)
                renderer.material = new Material(shader) { name = name, color = Idle };

            // Beside the slab, not inside it: the slab is scaled to a wide flat box and
            // text parented to it would inherit that stretch.
            var text = new GameObject(name + " Label").transform;
            text.SetParent(parent, false);
            text.localPosition = localPosition + new Vector3(0f, 0f, -0.02f);
            text.localRotation = Quaternion.identity;
            WorldLabel.Attach(text, caption, height * 0.45f, Vector3.zero, TextAnchor.MiddleCenter);

            var button = slab.AddComponent<WorldButton>();
            button.label = text.gameObject;
            return button;
        }

        Collider area;
        Renderer face;
        float shownAt;

        static readonly Color Idle = new Color(0.22f, 0.42f, 0.65f);
        static readonly Color Hot = new Color(0.35f, 0.75f, 1f);
        static readonly Color Off = new Color(0.25f, 0.25f, 0.28f);

        void Awake()
        {
            area = GetComponent<Collider>();
            face = GetComponent<Renderer>();
            Hide();
        }

        public void Show()
        {
            IsShowing = true;
            shownAt = Time.time;
            gameObject.SetActive(true);
            if (label != null) label.SetActive(true);
            Tint(Idle);
        }

        public void Hide()
        {
            IsShowing = false;
            gameObject.SetActive(false);
            if (label != null) label.SetActive(false);
        }

        /// <summary>Enabled but not pressable -- shown greyed while it is not yet valid.</summary>
        public void ShowDisabled()
        {
            IsShowing = false;
            gameObject.SetActive(true);
            if (label != null) label.SetActive(true);
            Tint(Off);
        }

        public void Hover(Ray ray)
        {
            if (!IsShowing) return;
            Tint(Hit(ray) ? Hot : Idle);
        }

        public bool TryPress(Ray ray)
        {
            if (!IsShowing) return false;
            if (Time.time - shownAt < inputLockSeconds) return false;
            if (!Hit(ray)) return false;

            var handler = Pressed;
            if (handler != null) handler();
            return true;
        }

        bool Hit(Ray ray)
        {
            if (area == null) return false;
            RaycastHit hit;
            // The button's own collider, not the scene's: room geometry nearer than the
            // button would otherwise swallow the press.
            return area.Raycast(ray, out hit, 100f);
        }

        void Tint(Color colour)
        {
            if (face != null && face.material != null) face.material.color = colour;
        }
    }
}
