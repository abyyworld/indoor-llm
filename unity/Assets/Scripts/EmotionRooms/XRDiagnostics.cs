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
// Toggle with the B button (or Y on the left controller). Off by default.

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

        bool showing;
        bool lastToggle;

        void Update()
        {
            if (Pressed(CommonUsages.secondaryButton, ref lastToggle))
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
                report.Append("  moving with head: ")
                      .Append(CameraFollowsHead(headPosition) ? "yes" : "NO").Append('\n');
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

            report.Append("\nB hides this.");
            return report.ToString();
        }

        Vector3 lastHead;
        float lastHeadChange;
        bool everMoved;

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
