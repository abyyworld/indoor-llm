// The Affect Grid, in-headset.
//
// A 9x9 grid (Russell, Weiss and Mendelsohn 1989) presented on a world-space quad, with
// SAM-style pictorial anchors instead of the original corner wording. One pointer hit
// gives both dimensions.
//
//     x = valence  1 unpleasant  ->  9 pleasant
//     y = arousal  1 sleepy      ->  9 high arousal
//
// Deliberately built on a collider and a raycast rather than Unity's UI canvas system.
// A world-space canvas in VR needs an event camera, an input module and a graphic
// raycaster wired correctly or it silently stops responding, and that failure mode
// during a participant session is unrecoverable. A quad with a BoxCollider either gets
// hit or does not.
//
// Anchors are assigned in the inspector as sprites or renderers, so whoever prepares the
// study can swap the SAM figures without touching this file.

using System;
using UnityEngine;

namespace EmotionRooms
{
    [Serializable]
    public struct AffectResponse
    {
        /// <summary>1..9, unpleasant to pleasant.</summary>
        public int valence;

        /// <summary>1..9, sleepy to high arousal.</summary>
        public int arousal;

        /// <summary>Milliseconds from the grid appearing to the response landing.</summary>
        public long durationMs;

        public override string ToString()
        {
            return string.Format("valence={0} arousal={1} in {2} ms", valence, arousal, durationMs);
        }
    }

    [RequireComponent(typeof(BoxCollider))]
    public class AffectGrid : MonoBehaviour
    {
        [Header("Grid")]
        [Tooltip("Cells per axis. 9 is the published Affect Grid resolution; changing it " +
                 "makes the data non-comparable with anything else measured on the grid.")]
        public int cells = 9;

        [Header("Feedback")]
        [Tooltip("Follows the pointer across the grid. Optional but strongly advised: " +
                 "without it a participant cannot tell what they are about to select.")]
        public Transform hoverMarker;

        [Tooltip("Snaps to the chosen cell once a response is committed.")]
        public Transform selectionMarker;

        [Header("Logging")]
        [Tooltip("Optional. Logs a row every time the hovered cell CHANGES, not every " +
                 "frame, so hesitation between cells is visible without flooding the file.")]
        public EventLog events;

        [Header("Behaviour")]
        [Tooltip("Ignore hits for this long after the grid appears, so a click meant for " +
                 "the previous screen cannot fall through into a response.")]
        public float inputLockSeconds = 0.4f;

        /// <summary>Fires once per presentation, when a response is committed.</summary>
        public event Action<AffectResponse> Responded;

        public bool IsAwaitingResponse { get; private set; }
        public bool HasResponded { get; private set; }

        BoxCollider area;
        float shownAt;
        int lastHoverValence = -1;
        int lastHoverArousal = -1;

        void Awake()
        {
            area = GetComponent<BoxCollider>();
            area.isTrigger = true;
            Hide();
        }

        /// <summary>Present the grid and start accepting input.</summary>
        public void Show()
        {
            HasResponded = false;
            IsAwaitingResponse = true;
            shownAt = Time.time;
            lastHoverValence = -1;
            lastHoverArousal = -1;

            if (selectionMarker != null) selectionMarker.gameObject.SetActive(false);
            if (hoverMarker != null) hoverMarker.gameObject.SetActive(true);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            IsAwaitingResponse = false;
            if (hoverMarker != null) hoverMarker.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Move the hover marker to wherever the pointer is. Call every frame while the
        /// grid is up; safe to call when it is not.
        /// </summary>
        public void Hover(Ray ray)
        {
            if (!IsAwaitingResponse || hoverMarker == null) return;

            int valence, arousal;
            Vector3 point;
            if (TryResolve(ray, out valence, out arousal, out point))
            {
                hoverMarker.position = CellCentre(valence, arousal);

                // One row per change of cell. Every frame would be noise; only on
                // commit would lose the hesitation, which is the interesting part.
                if (events != null && (valence != lastHoverValence || arousal != lastHoverArousal))
                {
                    events.WriteGrid("grid_hover", valence, arousal, point.x, point.y, null);
                    lastHoverValence = valence;
                    lastHoverArousal = arousal;
                }
            }
        }

        /// <summary>
        /// Commit a response. Returns false if the ray missed, the grid is not up, a
        /// response has already been given, or the input lock is still running.
        /// </summary>
        public bool TrySelect(Ray ray, out AffectResponse response)
        {
            response = default(AffectResponse);

            if (!IsAwaitingResponse || HasResponded) return false;
            if (Time.time - shownAt < inputLockSeconds) return false;

            int valence, arousal;
            Vector3 point;
            if (!TryResolve(ray, out valence, out arousal, out point)) return false;

            response = new AffectResponse
            {
                valence = valence,
                arousal = arousal,
                durationMs = (long)((Time.time - shownAt) * 1000f),
            };

            HasResponded = true;
            IsAwaitingResponse = false;

            if (events != null)
                events.WriteGrid("grid_selected", valence, arousal, point.x, point.y,
                    "dwell_ms=" + response.durationMs.ToString());

            if (selectionMarker != null)
            {
                selectionMarker.gameObject.SetActive(true);
                selectionMarker.position = CellCentre(valence, arousal);
            }
            if (hoverMarker != null) hoverMarker.gameObject.SetActive(false);

            var handler = Responded;
            if (handler != null) handler(response);
            return true;
        }

        /// <summary>Ray to grid cell. Both axes come out 1..cells inclusive.</summary>
        public bool TryResolve(Ray ray, out int valence, out int arousal, out Vector3 point)
        {
            valence = arousal = 0;
            point = Vector3.zero;

            RaycastHit hit;
            if (!area.Raycast(ray, out hit, 100f)) return false;

            point = hit.point;
            Vector3 local = transform.InverseTransformPoint(hit.point);

            // Local space of a default quad runs -0.5..+0.5 on x and y.
            float u = Mathf.Clamp01(local.x + 0.5f);
            float v = Mathf.Clamp01(local.y + 0.5f);

            valence = CellIndex(u);
            arousal = CellIndex(v);
            return true;
        }

        int CellIndex(float normalised)
        {
            // Clamp rather than let a hit exactly on the far edge produce cells+1.
            return Mathf.Clamp(Mathf.FloorToInt(normalised * cells) + 1, 1, cells);
        }

        /// <summary>World position of a cell's centre, for the markers.</summary>
        public Vector3 CellCentre(int valence, int arousal)
        {
            float u = (valence - 0.5f) / cells - 0.5f;
            float v = (arousal - 0.5f) / cells - 0.5f;
            return transform.TransformPoint(new Vector3(u, v, 0f));
        }

        void OnDrawGizmosSelected()
        {
            // Draw the lattice in the editor so the grid can be positioned and scaled
            // without entering play mode.
            Gizmos.color = Color.cyan;
            for (int x = 1; x <= cells; x++)
            {
                for (int y = 1; y <= cells; y++)
                {
                    Gizmos.DrawWireCube(CellCentre(x, y), transform.lossyScale / cells * 0.9f);
                }
            }
        }
    }
}
