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
            // NO Hide() here, and none may ever return. The grid is saved inactive in
            // the scene, and Unity defers Awake on an inactive object until the first
            // SetActive(true) -- which is the one inside Show(). A Hide() here therefore
            // runs in the middle of the first Show(), deactivates the object again and
            // clears IsAwaitingResponse: the grid un-shows itself, input goes dead, and
            // the trial waits forever on an answer that can never come. That single line
            // was every "I see the four words but no grid and nothing selects" report
            // from the headset. Hidden-at-start is the scene's job, not Awake's.
        }


        /// <summary>Present the grid and start accepting input.</summary>
        [Tooltip("Camera the grid places itself in front of when shown. Empty uses " +
                 "Camera.main.")]
        public Camera viewer;

        [Tooltip("Axis names and the question. Shown and hidden with the grid, and " +
                 "kept outside it so the quad's scale does not stretch the text.")]
        public GameObject labels;

        [Tooltip("Metres in front of the viewer.")]
        public float distance = 1.2f;

        [Tooltip("Move the grid in front of the viewer each time it is shown.\n\n" +
                 "On by default. The grid was positioned once at scene-build time, so it " +
                 "sat wherever the camera happened to be then -- and if the camera moved, " +
                 "or the participant turned, the rating screen appeared somewhere behind " +
                 "them and the session looked like it had frozen on an empty scene.")]
        public bool followViewer = true;

        public void Show()
        {
            EnsureCells();
            PlaceInFrontOfViewer();
            EnsureVisible();

            HasResponded = false;
            IsAwaitingResponse = true;
            PaintCells();
            shownAt = Time.time;
            lastHoverValence = -1;
            lastHoverArousal = -1;

            if (selectionMarker != null) selectionMarker.gameObject.SetActive(false);
            if (hoverMarker != null) hoverMarker.gameObject.SetActive(true);
            gameObject.SetActive(true);
            if (labels != null) labels.SetActive(true);
        }

        /// <summary>
        /// Draw the 9x9 lattice as real geometry, one tile per cell.
        ///
        /// It was a texture baked onto a single quad with Unlit/Transparent. Twice now
        /// that has come back as "I see no grid": in a build the lattice depends on the
        /// texture asset surviving import and on a built-in transparent shader surviving
        /// shader stripping, and when either fails there is nothing to say so -- the quad
        /// renders blank or not at all, and the participant is left looking at four words
        /// floating in space with nothing to aim at.
        ///
        /// Tiles use the same lit shader the rooms and markers already use, so if
        /// anything in the scene renders, these do. They also give the pointer something
        /// that visibly responds, which a painted texture never did.
        ///
        /// Deliberately neutral greys. The grid is the instrument for measuring how a
        /// room made someone feel, so it must not carry colour of its own.
        /// </summary>
        void EnsureCells()
        {
            if (tiles != null && tiles.Length == cells * cells) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            tiles = new Renderer[cells * cells];

            float span = 1f / cells;
            for (int v = 1; v <= cells; v++)
            {
                for (int a = 1; a <= cells; a++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = "Cell " + v.ToString() + "," + a.ToString();
                    tile.transform.SetParent(transform, false);
                    // A little smaller than the cell, so the gaps draw the lattice.
                    tile.transform.localScale = new Vector3(span * 0.88f, span * 0.88f, 0.04f);
                    tile.transform.localPosition = new Vector3(
                        (v - 0.5f) * span - 0.5f, (a - 0.5f) * span - 0.5f, -0.03f);

                    // The grid's own BoxCollider resolves the ray. A collider per tile
                    // would just be 81 more things for the pointer to catch on.
                    var collider = tile.GetComponent<Collider>();
                    if (collider != null) Destroy(collider);

                    var renderer = tile.GetComponent<Renderer>();
                    if (renderer != null && shader != null)
                        renderer.material = new Material(shader) { color = Idle };
                    tiles[Index(v, a)] = renderer;
                }
            }

            // A dark backing panel, assigned at runtime rather than trusted to a saved
            // material asset, so the tiles read against something.
            var own = GetComponent<Renderer>();
            if (own != null && shader != null)
                own.material = new Material(shader) { color = new Color(0.08f, 0.08f, 0.11f) };
        }

        int Index(int valence, int arousal)
        {
            return (arousal - 1) * cells + (valence - 1);
        }

        void PaintCells()
        {
            if (tiles == null) return;
            for (int i = 0; i < tiles.Length; i++)
                if (tiles[i] != null && tiles[i].material != null) tiles[i].material.color = Idle;
        }

        void PaintCell(int valence, int arousal, Color colour)
        {
            if (tiles == null) return;
            if (valence < 1 || valence > cells || arousal < 1 || arousal > cells) return;
            var tile = tiles[Index(valence, arousal)];
            if (tile != null && tile.material != null) tile.material.color = colour;
        }

        static readonly Color Idle = new Color(0.62f, 0.62f, 0.66f);
        static readonly Color Hot = new Color(0.95f, 0.95f, 1f);
        static readonly Color Chosen = new Color(0.30f, 0.85f, 0.42f);

        Renderer[] tiles;

        void PlaceInFrontOfViewer()
        {
            if (!followViewer) return;

            var camera = viewer != null ? viewer : Camera.main;
            if (camera == null) return;

            var eye = camera.transform;
            // Level with the eye but not tilted with it: a grid pitched to match a
            // downward glance is harder to read and harder to point at.
            var forward = eye.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            transform.position = eye.position + forward * distance;
            transform.rotation = Quaternion.LookRotation(forward);

            if (labels != null)
            {
                labels.transform.position = transform.position;
                labels.transform.rotation = transform.rotation;
            }
        }

        /// <summary>
        /// Give every renderer under the grid a material if it has lost one.
        ///
        /// Editor-created materials are not saved unless something writes them out as
        /// assets, so a recompile could leave the grid present, correctly positioned and
        /// completely invisible. Indistinguishable from a frozen session, and it happened.
        /// </summary>
        void EnsureVisible()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterial != null) continue;

                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                renderer.sharedMaterial = new Material(shader) { name = "Grid (recovered)" };
                Debug.LogWarning("[AffectGrid] " + renderer.name + " had no material, so " +
                                 "one was created. Rebuild the scene from the Study " +
                                 "Control Panel to stop this recurring.");
            }
        }

        public void Hide()
        {
            IsAwaitingResponse = false;
            if (hoverMarker != null) hoverMarker.gameObject.SetActive(false);
            if (labels != null) labels.SetActive(false);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Move the hover marker to wherever the pointer is. Call every frame while the
        /// grid is up; safe to call when it is not.
        /// </summary>
        public void Hover(Ray ray)
        {
            if (!IsAwaitingResponse) return;

            int valence, arousal;
            Vector3 point;
            if (TryResolve(ray, out valence, out arousal, out point))
            {
                if (hoverMarker != null) hoverMarker.position = CellCentre(valence, arousal);
                PaintCells();
                PaintCell(valence, arousal, Hot);

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
        /// Commit the cell under the pointer, there and then.
        ///
        /// This was briefly a two-step act -- mark, then press a confirm button -- so a
        /// rating could be changed before it counted. In the headset that turned into a
        /// dead end: the confirm button was the only way forward and it was not always
        /// there, so the session simply stopped with the grid up and nothing that could
        /// be done about it. A single press cannot strand anybody, and it is how the
        /// published Affect Grid is administered anyway.
        ///
        /// The cost is real and worth stating in the write-up: a misplaced press is
        /// recorded, with no correction. `marks` is gone as a measure, so the response
        /// time is what carries how considered an answer was.
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

            PaintCells();
            PaintCell(valence, arousal, Chosen);
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

        /// <summary>
        /// Commit a cell directly, no ray involved.
        ///
        /// This exists so a session can be driven end to end from the researcher side --
        /// the trial loop, the acknowledgements, the review block -- without a person in
        /// the headset. Every use is written to the event log as remote, so a driven
        /// session can never be mistaken for participant data.
        /// </summary>
        public bool CommitCell(int valence, int arousal)
        {
            if (!IsAwaitingResponse || HasResponded) return false;
            if (Time.time - shownAt < inputLockSeconds) return false;
            if (valence < 1 || valence > cells || arousal < 1 || arousal > cells) return false;

            var response = new AffectResponse
            {
                valence = valence,
                arousal = arousal,
                durationMs = (long)((Time.time - shownAt) * 1000f),
            };

            HasResponded = true;
            IsAwaitingResponse = false;

            if (events != null)
                events.WriteGrid("grid_selected", valence, arousal, 0f, 0f,
                    "REMOTE dwell_ms=" + response.durationMs.ToString());

            PaintCells();
            PaintCell(valence, arousal, Chosen);
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
