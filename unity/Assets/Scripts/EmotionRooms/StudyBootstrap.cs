// Starts the session and drives pointer input into the affect grid.
//
// AffectGrid exposes Hover() and TrySelect() but deliberately does not read input
// itself: what counts as "pointing" differs between a controller ray, a gaze cursor and
// a mouse in the editor, and burying one of those inside the instrument would make the
// other two awkward. This is the piece that decides.
//
// Attach to one GameObject in the scene and wire the three references. Nothing else in
// the study needs an input dependency.
//
// Editor testing works out of the box with the mouse. For the headset, point
// `pointerOrigin` at your controller's ray transform and call Select() from whatever
// input system you use, or leave `useLegacyInput` on if your rig maps the trigger to a
// legacy button.

using System.Collections;
using UnityEngine;

namespace EmotionRooms
{
    public class StudyBootstrap : MonoBehaviour
    {
        [Header("Wiring")]
        public TrialRunner trialRunner;
        public OversightReview oversightReview;
        public AffectGrid grid;

        [Header("Pointer")]
        [Tooltip("Transform whose forward axis is the pointing ray. A controller's ray " +
                 "origin in the headset. Leave empty in the editor and the mouse is used.")]
        public Transform pointerOrigin;

        [Tooltip("Camera used to build the ray when pointerOrigin is empty. Defaults to " +
                 "Camera.main.")]
        public Camera fallbackCamera;

        [Header("Input")]
        [Tooltip("Poll a legacy button or mouse click to commit a response. Turn this off " +
                 "if you drive selection from an XR input action and call Select() yourself.")]
        public bool useLegacyInput = true;

        [Tooltip("Legacy button name for the trigger. Left empty, mouse button 0 is used.")]
        public string selectButton = "";

        [Header("Session")]
        [Tooltip("Start the trial runner automatically. Turn off if a researcher-facing " +
                 "screen starts it, which is usually what you want with a participant present.")]
        public bool autoStart = false;

        [Tooltip("Seconds to wait before auto-starting, so the participant is settled and " +
                 "the headset has finished tracking-init before the first room appears.")]
        public float startDelaySeconds = 3f;

        [Tooltip("Run the oversight review automatically once the eight trials finish. " +
                 "The review must never begin before the main session is complete.")]
        public bool chainOversightBlock = true;

        void Start()
        {
            if (trialRunner != null && chainOversightBlock && oversightReview != null)
            {
                trialRunner.SessionFinished += OnSessionFinished;
            }

            if (autoStart) StartCoroutine(AutoStart());
        }

        void OnDestroy()
        {
            if (trialRunner != null) trialRunner.SessionFinished -= OnSessionFinished;
        }

        IEnumerator AutoStart()
        {
            yield return new WaitForSeconds(startDelaySeconds);
            BeginStudy();
        }

        /// <summary>Start the main session. Safe to call from a UI button.</summary>
        [ContextMenu("Begin Study")]
        public void BeginStudy()
        {
            if (trialRunner == null)
            {
                Debug.LogError("StudyBootstrap: no TrialRunner assigned.");
                return;
            }
            trialRunner.BeginSession();
        }

        void OnSessionFinished()
        {
            // Phase A is complete and its data is written before anything asks the
            // participant to evaluate a room. That ordering is the whole reason the
            // review block does not contaminate the affect ratings.
            Debug.Log("StudyBootstrap: main session finished, starting the review block.");
            oversightReview.BeginBlock();
        }

        void Update()
        {
            if (grid == null || !grid.IsAwaitingResponse) return;

            Ray ray;
            if (!TryBuildRay(out ray)) return;

            grid.Hover(ray);

            if (useLegacyInput && Pressed()) Select();
        }

        /// <summary>Commit whatever the pointer is currently over. Call from XR input.</summary>
        public void Select()
        {
            if (grid == null || !grid.IsAwaitingResponse) return;

            Ray ray;
            if (!TryBuildRay(out ray)) return;

            AffectResponse response;
            if (grid.TrySelect(ray, out response))
            {
                Debug.Log("StudyBootstrap: response " + response);
            }
        }

        bool TryBuildRay(out Ray ray)
        {
            if (pointerOrigin != null)
            {
                ray = new Ray(pointerOrigin.position, pointerOrigin.forward);
                return true;
            }

            var camera = fallbackCamera != null ? fallbackCamera : Camera.main;
            if (camera == null)
            {
                ray = default(Ray);
                return false;
            }

            ray = camera.ScreenPointToRay(Input.mousePosition);
            return true;
        }

        bool Pressed()
        {
            if (!string.IsNullOrEmpty(selectButton))
            {
                // Wrapped: an unmapped axis name throws rather than returning false, and
                // a study should not die mid-session because of an inspector typo.
                try { return Input.GetButtonDown(selectButton); }
                catch (System.ArgumentException)
                {
                    Debug.LogWarning("StudyBootstrap: no input button named '" +
                                     selectButton + "'; falling back to mouse.");
                    selectButton = "";
                }
            }
            return Input.GetMouseButtonDown(0);
        }
    }
}
