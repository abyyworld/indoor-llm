// Turning, on the thumbstick. Walking is built and switched off.
//
// The two shells were dimensioned so linear and curved present identical sightlines
// from one fixed point: same entry position, same distance to the side wall and to the
// facing wall or vault apex. Walking discards that match, and the cost lands exactly on
// the thesis question - if people wander, a difference between shapes can be a
// difference in where they chose to stand rather than in the geometry. Seated and
// still, every participant sees each shape from the viewpoint the rooms were designed
// around, and the contrast is between the rooms rather than between their occupants.
//
// Three smaller things follow the same way. A seated participant cannot trip over a
// cable or walk into a wall, which is what let the headset go wireless. Sickness risk
// drops, because smooth thumbstick translation is the largest contributor to it and it
// is now absent. And a 35 minute session is more comfortable sitting than standing.
//
// Snap turning stays: looking behind you is part of judging a room, and snapping is the
// biggest sickness reduction available here.
//
// enableMovement flips it back for anyone who wants free walking. If it is turned on,
// the 20 Hz head telemetry is what makes the viewpoint spread checkable afterwards
// rather than merely assumed.
//
// Bounded to the room. Walking through a wall would end the illusion the study depends
// on, and a participant outside the room is rating nothing.

using UnityEngine;
using UnityEngine.XR;

namespace EmotionRooms
{
    public class Locomotion : MonoBehaviour
    {
        [Tooltip("The transform that gets moved -- the XR origin, not the camera. Moving " +
                 "the camera would fight the head pose that XR writes to it every frame.")]
        public Transform rig;

        [Tooltip("Where the rig comes from. The anchor is created in XRRig.Awake, so it " +
                 "cannot be wired in the editor.")]
        public XRRig xrRig;

        public Camera headCamera;
        public RoomLoader loader;
        public EventLog events;

        [Tooltip("Metres per second. Walking pace; faster provokes sickness in VR.")]
        public float speed = 1.4f;

        [Tooltip("Degrees per snap turn. Snap rather than smooth, which is the single " +
                 "biggest reduction in simulator sickness available here.")]
        public float snapDegrees = 30f;

        [Tooltip("How close a participant may get to a wall.")]
        public float wallMargin = 0.45f;

        [Tooltip("Walking on the thumbstick. OFF by default: see the note at the top " +
                 "of this file. Turn it on only with a reason that outweighs losing the " +
                 "matched viewpoint.")]
        // On, at Akbar's call, 16 Aug 2026, overruling fdfeb50.
        //
        // The cost is real and belongs in the limitations rather than being argued
        // about again: the two shells are matched on sightlines from one point, so a
        // participant who wanders sees a geometry nobody specified, and a difference
        // between curved and linear can then be a difference in where they chose to
        // stand. What makes it acceptable is that everyone still MEETS each room from
        // the matched viewpoint -- OversightReview recentres at the start of every
        // trial -- and that head position is logged at 20 Hz, so how far anyone
        // actually moved is measurable rather than assumed. An uncontrolled factor that
        // is recorded can be checked, and if movement turns out not to predict the
        // ratings, that is a sentence in the paper rather than a hole in it.
        public bool enableMovement = true;

        [Tooltip("Snap turning. Stays on: a seated participant has to be able to look " +
                 "behind them, and snapping is the single biggest reduction in " +
                 "simulator sickness available here.")]
        public bool enableSnapTurn = true;

        bool turnArmed = true;
        bool announced;

        void Update()
        {
            if (rig == null && xrRig != null) rig = xrRig.Origin;
            if (rig == null) return;

            var camera = headCamera != null ? headCamera : Camera.main;
            if (camera == null) return;

            if (enableMovement) Move(camera);
            if (enableSnapTurn) SnapTurn(camera);
        }

        /// <summary>
        /// Keep the HEAD inside the room, whatever moved it.
        ///
        /// Clamping the rig on stick input missed the other way people move: physically
        /// walking. The headset tracks real steps and applies them to the camera, not
        /// the rig, so a participant could lean or walk straight through a wall while
        /// the rig stayed politely inside. Run after XR writes the head pose, this
        /// shifts the rig by however far the head has strayed past the bound, which
        /// works identically for both kinds of movement.
        /// </summary>
        void LateUpdate()
        {
            if (rig == null) return;
            var camera = headCamera != null ? headCamera : Camera.main;
            if (camera == null) return;

            var head = camera.transform.position;
            var held = Clamp(head);
            if (held.x != head.x || held.z != head.z)
            {
                rig.position += new Vector3(held.x - head.x, 0f, held.z - head.z);
                // One row per contact, not per frame: wallHeld collapses a continuous
                // lean into enter/leave so the file records episodes, not frame spam.
                if (!wallHeld && events != null)
                    events.WriteValues("wall_hold", F3(head), F3(held), null);
                wallHeld = true;
            }
            else wallHeld = false;
        }

        bool wallHeld;

        static string F3(Vector3 v)
        {
            return v.x.ToString("0.00") + "|" + v.y.ToString("0.00") + "|" + v.z.ToString("0.00");
        }

        void Move(Camera camera)
        {
            Vector2 stick;
            if (!Stick(XRNode.LeftHand, out stick) || stick.sqrMagnitude < 0.04f) return;

            // Relative to where they are looking, not to the room, which is what people
            // expect from a thumbstick and what makes it usable without instruction.
            var forward = camera.transform.forward;
            var right = camera.transform.right;
            forward.y = 0f; right.y = 0f;
            forward.Normalize(); right.Normalize();

            var step = (forward * stick.y + right * stick.x) * speed * Time.deltaTime;
            var wanted = rig.position + step;

            rig.position = Clamp(wanted);

            if (!announced && events != null)
            {
                announced = true;
                // Recorded once per session: whether people could walk is a property of
                // the data that a reader of the file should not have to infer.
                events.WriteValues("locomotion_used", "thumbstick",
                    speed.ToString("0.0"), null);
            }
        }

        void SnapTurn(Camera camera)
        {
            Vector2 stick;
            if (!Stick(XRNode.RightHand, out stick)) { turnArmed = true; return; }

            if (Mathf.Abs(stick.x) < 0.6f) { turnArmed = true; return; }
            if (!turnArmed) return;
            turnArmed = false;

            // Rotate about the head, not the rig's origin, or the room appears to swing
            // around the participant rather than the participant turning within it.
            rig.RotateAround(camera.transform.position, Vector3.up,
                             snapDegrees * Mathf.Sign(stick.x));
            if (events != null)
                events.WriteValues("snap_turn", (snapDegrees * Mathf.Sign(stick.x)).ToString("0"),
                    camera.transform.eulerAngles.y.ToString("0.0"), null);
        }

        /// <summary>Keep the participant inside whichever shell is loaded.</summary>
        Vector3 Clamp(Vector3 position)
        {
            float halfWidth = RoomDimensions.EntranceWidth / 2f - wallMargin;
            position.x = Mathf.Clamp(position.x, -halfWidth, halfWidth);
            position.z = Mathf.Clamp(position.z, wallMargin,
                                     RoomDimensions.Depth - wallMargin);

            bool curved = loader != null && loader.Current != null &&
                          loader.Current.Shape == "curved";
            if (curved && position.z > RoomDimensions.FoyerDepth)
            {
                // Past the springline the wall is a half-cylinder, so a box would let
                // someone walk out through the curve.
                var springline = new Vector2(0f, RoomDimensions.FoyerDepth);
                var offset = new Vector2(position.x, position.z) - springline;
                float limit = RoomDimensions.VaultRadius - wallMargin;
                if (offset.magnitude > limit)
                {
                    offset = offset.normalized * limit;
                    position.x = springline.x + offset.x;
                    position.z = springline.y + offset.y;
                }
            }
            return position;
        }

        static bool Stick(XRNode node, out Vector2 value)
        {
            value = Vector2.zero;
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid &&
                   device.TryGetFeatureValue(CommonUsages.primary2DAxis, out value);
        }
    }
}
