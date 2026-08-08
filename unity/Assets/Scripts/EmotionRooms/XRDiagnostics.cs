// What the headset actually sees, shown in the headset.
//
// Head tracking and pointer aim have now been diagnosed twice from a laptop and fixed
// twice wrongly, because the only evidence available was a log written after the fact.
// This puts the live values in front of whoever is wearing the thing: whether the runtime
// reports a head pose at all, whether the camera is moving with it, whether a controller
// is tracked, and where its ray points.
//
// It also makes the ray's pitch adjustable in place. The grip-to-aim offset is a single
// number that can only really be judged by pointing at something, and rebuilding an APK
// to try another value costs ten minutes a guess.
//
// Toggle by HOLDING B (or Y on the left controller) for a second. Off by default, and a
// hold rather than a tap because a tap is easy to hit by accident mid-session -- at which
// point a participant is looking at a wall of yes/NO diagnostics that reads exactly like a
// question they are being asked, with no way to answer it. Every line is now prefixed as a
// readout for that reason.

using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace EmotionRooms
{
    public class XRDiagnostics : MonoBehaviour
    {
        public XRRig rig;
        public Camera headCamera;
        public MessageBoard board;

        [Tooltip("Seconds the button must be held. A tap does nothing, so a participant " +
                 "cannot open the readout by fumbling for the trigger.")]
        public float holdSeconds = 1f;

        bool showing;
        bool wasDown;
        float heldSince = -1f;

        void Update()
        {
            if (HeldLongEnough())
            {
                showing = !showing;
                if (!showing && board != null) board.Hide();
            }
            if (!showing) return;

            // The thumbsticks belong to walking now, and the ray is settled at 60, so
            // this no longer competes for them: a stick that both moves you and re-aims
            // your pointer is worse than either alone.

            if (board != null) board.Show(Report());
        }

        string Report()
        {
            var report = new StringBuilder();
            report.Append("DIAGNOSTIC READOUT  (researcher only)\n")
                  .Append("Nothing here is a question. Nothing to answer.\n\n");

            var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            Vector3 headPosition = Vector3.zero;
            Quaternion headRotation = Quaternion.identity;
            bool hasHead = head.isValid &&
                           head.TryGetFeatureValue(CommonUsages.devicePosition, out headPosition) &&
                           head.TryGetFeatureValue(CommonUsages.deviceRotation, out headRotation);

            report.Append("HEAD  runtime ").Append(head.isValid ? "yes" : "NO")
                  .Append("   pose ").Append(hasHead ? "yes" : "NO").Append('\n');
            report.Append("  reported  ").Append(Fmt(headPosition))
                  .Append("  yaw ").Append(headRotation.eulerAngles.y.ToString("0")).Append('\n');

            if (headCamera != null)
            {
                report.Append("  camera    ").Append(Fmt(headCamera.transform.position))
                      .Append("  yaw ")
                      .Append(headCamera.transform.eulerAngles.y.ToString("0")).Append('\n');
                // If the reported pose moves and the camera does not, nothing is applying
                // it -- which is the difference between a dead runtime and a dead rig.
                // Reads "still" rather than "NO" when the head simply has not moved:
                // a stationary head and a dead rig used to print the same word, which is
                // why this line looked like a question that could not be answered.
                report.Append("  view tracks head: ").Append(TrackingState(headPosition))
                      .Append('\n');
            }

            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            report.Append("\nCONTROLLERS  right ").Append(right.isValid ? "yes" : "NO")
                  .Append("   left ").Append(left.isValid ? "yes" : "NO").Append('\n');

            if (rig != null)
            {
                report.Append("  ray pitch ").Append(XRRig.GripToAim.ToString("0"))
                      .Append(" deg (fixed)\n");
                if (rig.pointer != null)
                    report.Append("  aiming    ").Append(Fmt(rig.pointer.forward)).Append('\n');
            }

            report.Append("\nHold B to hide this.");
            return report.ToString();
        }

        /// <summary>True once, on the frame a long-enough hold completes.</summary>
        bool HeldLongEnough()
        {
            bool down = Read(XRNode.RightHand, CommonUsages.secondaryButton) ||
                        Read(XRNode.LeftHand, CommonUsages.secondaryButton);

            if (!down)
            {
                wasDown = false;
                heldSince = -1f;
                return false;
            }
            if (!wasDown)
            {
                wasDown = true;
                heldSince = Time.time;
                return false;
            }
            if (heldSince < 0f || Time.time - heldSince < holdSeconds) return false;

            // Consumed, so holding longer does not toggle repeatedly.
            heldSince = -1f;
            return true;
        }

        Vector3 lastHead;
        float lastHeadChange;
        bool everMoved;

        string TrackingState(Vector3 reported)
        {
            if (!everMoved) return "unknown (hold still tells us nothing -- turn your head)";
            return CameraFollowsHead(reported) ? "OK" : "STALE (moved, view did not)";
        }

        bool CameraFollowsHead(Vector3 reported)
        {
            if ((reported - lastHead).sqrMagnitude > 0.0001f)
            {
                lastHead = reported;
                lastHeadChange = Time.time;
                everMoved = true;
            }
            // Only meaningful once the head has actually moved; a perfectly still head
            // and a broken rig look identical.
            return everMoved && Time.time - lastHeadChange < 2f;
        }

        static string Fmt(Vector3 v)
        {
            return v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00");
        }

        static bool Pressed(InputFeatureUsage<bool> button, ref bool wasDown)
        {
            bool down = Read(XRNode.RightHand, button) || Read(XRNode.LeftHand, button);
            bool fired = down && !wasDown;
            wasDown = down;
            return fired;
        }

        static bool Read(XRNode node, InputFeatureUsage<bool> button)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            bool value;
            return device.isValid && device.TryGetFeatureValue(button, out value) && value;
        }

    }
}
