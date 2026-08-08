// Head and controller tracking, built on the XR module that ships with Unity.
//
// Deliberately not built on XR Interaction Toolkit or the Input System. Those are the
// right choice for a game with grabbing, teleporting and UI panels; this study needs
// exactly two things -- where the head is, and where the pointer is aiming plus whether
// the trigger went down -- and UnityEngine.XR.InputDevices provides both with no extra
// package, no input-action assets to keep in sync, and nothing that breaks the legacy
// mouse path the editor is driven with. Fewer moving parts between a participant and a
// rating.
//
// Harmless when no headset is present: every method checks whether an XR device is
// actually tracking, so the same scene runs on a laptop with a mouse.
//
// The one thing this cannot do from script is enable the OpenXR loader. That lives in
// Project Settings and has to be ticked once per machine -- the control panel says so.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace EmotionRooms
{
    /// <summary>Drives a transform from a tracked XR node.</summary>
    public class XRPoseDriver : MonoBehaviour
    {
        public XRNode node = XRNode.CenterEye;

        [Tooltip("Apply rotation as well as position.")]
        public bool applyRotation = true;

        [Tooltip("Degrees of pitch applied after the tracked rotation.\n\n" +
                 "CommonUsages.deviceRotation reports the GRIP pose -- how the controller " +
                 "sits in a closed hand -- not the aim pose a ray should follow. On Touch " +
                 "controllers the grip is tilted back from where the participant thinks " +
                 "they are pointing, which is why a ray follows the hand correctly and " +
                 "still points somewhere wrong. About -40 degrees brings it back to the " +
                 "line people expect.")]
        public float pitchOffset;

        [Tooltip("Where the tracked pose is measured from. Left empty, the parent is used, " +
                 "which is what puts the rig at the researcher-set standing position.")]
        public Transform origin;

        void Update()
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;

            Vector3 position;
            Quaternion rotation;
            bool hasPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
            bool hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
            if (!hasPosition && !hasRotation) return;

            var basis = origin != null ? origin : transform.parent;

            if (hasPosition)
            {
                transform.position = basis != null
                    ? basis.TransformPoint(position)
                    : position;
            }
            if (hasRotation && applyRotation)
            {
                var aim = rotation * Quaternion.Euler(pitchOffset, 0f, 0f);
                transform.rotation = basis != null ? basis.rotation * aim : aim;
            }
        }
    }

    /// <summary>
    /// Sets up head and controller tracking at runtime, and reports whether a headset is
    /// actually there.
    /// </summary>
    public class XRRig : MonoBehaviour
    {
        [Tooltip("The study camera. Gains head tracking when a headset is present.")]
        public Camera headCamera;

        [Tooltip("Created at runtime: the transform whose forward axis is the pointer ray.")]
        public Transform pointer;

        [Tooltip("Pitch correction from the controller's grip pose to where it appears " +
                 "to point. Negative tilts the ray up from the grip.")]
        public float gripToAimDegrees = -40f;

        [Tooltip("Preferred hand. Falls back to the other one if it is not tracking, so a " +
                 "left-handed participant is not stuck holding the wrong controller.")]
        public bool rightHanded = true;

        public bool HeadsetPresent { get; private set; }

        XRPoseDriver headDriver;
        XRPoseDriver pointerDriver;
        static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        void Awake()
        {
            if (headCamera == null) headCamera = Camera.main;
            if (headCamera == null) return;

            // The camera sits at the researcher-set standing position and must keep it:
            // tracking is applied relative to a parent anchor rather than by moving the
            // camera in world space, so a participant's height and pose never change
            // where the study says they are standing.
            // The anchor goes on the FLOOR, not at the camera.
            //
            // The camera sits at eye height so the scene is usable without a headset. A
            // floor-referenced XR pose already carries the participant's own eye height,
            // so anchoring at the camera added it twice: 1.6 m of scene plus 1.6 m of
            // person put the viewpoint at 3.2 m, above a 2.4 m ceiling. The room was
            // rendering correctly the whole time and being viewed from the roof, which
            // is why it read as "white ground and nothing else".
            var anchor = new GameObject("XR Origin").transform;
            anchor.SetParent(headCamera.transform.parent, false);
            anchor.position = new Vector3(headCamera.transform.position.x,
                                          RoomDimensions.StandingPosition.y,
                                          headCamera.transform.position.z);
            anchor.rotation = headCamera.transform.rotation;
            headCamera.transform.SetParent(anchor, true);

            // The camera IS driven here, deliberately.
            //
            // With the plain OpenXR plugin and no pose driver, nothing moves the camera:
            // the engine renders stereo from a static pose, so the world turns with the
            // head and looking around shows the same wall forever. Removing this driver
            // (on the theory that Unity applied the head pose itself) is exactly what
            // produced that. Unity only auto-drives the camera through a TrackedPoseDriver
            // component, which comes from packages this project deliberately does not use.
            headDriver = headCamera.gameObject.AddComponent<XRPoseDriver>();
            headDriver.node = XRNode.CenterEye;
            headDriver.origin = anchor;

            var hand = new GameObject("Pointer").transform;
            hand.SetParent(anchor, false);
            pointerDriver = hand.gameObject.AddComponent<XRPoseDriver>();
            pointerDriver.node = rightHanded ? XRNode.RightHand : XRNode.LeftHand;
            pointerDriver.origin = anchor;
            pointerDriver.pitchOffset = gripToAimDegrees;
            pointer = hand;

            BuildRay(hand);
        }

        /// <summary>
        /// Ask for floor-referenced tracking, so a pose is measured from the floor the
        /// participant is standing on rather than from wherever the headset happened to
        /// be when it woke. Without it a participant's height is whatever the runtime
        /// last calibrated, and the standing position stops meaning anything.
        /// </summary>
        void UseFloorOrigin()
        {
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var subsystem in subsystems)
            {
                if (subsystem.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor) continue;
                if (!subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
                    Debug.LogWarning("[XRRig] The runtime refused floor-referenced " +
                                     "tracking; heights will be measured from the " +
                                     "headset's own origin instead.");
            }
        }

        bool askedForFloor;

        void Update()
        {
            HeadsetPresent = IsHeadsetRunning();

            if (HeadsetPresent && !askedForFloor)
            {
                askedForFloor = true;
                UseFloorOrigin();
            }

            if (!HeadsetPresent || pointerDriver == null) return;

            // Swap hands if the preferred controller is not tracking. A participant given
            // the other controller should not silently have no pointer at all.
            var preferred = InputDevices.GetDeviceAtXRNode(pointerDriver.node);
            if (preferred.isValid) return;

            var other = pointerDriver.node == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand;
            if (InputDevices.GetDeviceAtXRNode(other).isValid) pointerDriver.node = other;
        }

        /// <summary>
        /// A visible beam from the controller.
        ///
        /// Without one there is no way to tell a controller that is not tracked from one
        /// that is tracked and aimed somewhere unexpected -- both look like nothing
        /// happening. A participant also needs to see where they are pointing before they
        /// can point at anything, which is the whole interaction.
        /// </summary>
        void BuildRay(Transform hand)
        {
            var line = hand.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, new Vector3(0f, 0f, 3f));
            line.startWidth = 0.006f;
            line.endWidth = 0.002f;

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var material = new Material(shader) { name = "Pointer Ray" };
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", new Color(0.3f, 0.65f, 1f));
                line.sharedMaterial = material;
            }
            ray = line;
        }

        LineRenderer ray;

        void LateUpdate()
        {
            // Shown only while a headset is actually tracking, so it does not hang in the
            // air during the mouse-driven editor path.
            if (ray != null) ray.enabled = HeadsetPresent && ControllerTracked();
        }

        /// <summary>Whether the pointer's controller is reporting a pose.</summary>
        public bool ControllerTracked()
        {
            if (pointerDriver == null) return false;
            return InputDevices.GetDeviceAtXRNode(pointerDriver.node).isValid;
        }

        public static bool IsHeadsetRunning()
        {
            SubsystemManager.GetSubsystems(displays);
            for (int i = 0; i < displays.Count; i++)
                if (displays[i].running) return true;
            return false;
        }

        /// <summary>True on the frame the trigger is pressed, on either controller.</summary>
        public static bool TriggerPressed()
        {
            return Pressed(XRNode.RightHand) || Pressed(XRNode.LeftHand);
        }

        static readonly Dictionary<XRNode, bool> wasDown = new Dictionary<XRNode, bool>();

        static bool Pressed(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return false;

            bool down;
            if (!device.TryGetFeatureValue(CommonUsages.triggerButton, out down))
            {
                // Some runtimes only expose the analogue axis.
                float value;
                if (!device.TryGetFeatureValue(CommonUsages.trigger, out value)) return false;
                down = value > 0.7f;
            }

            bool previous;
            wasDown.TryGetValue(node, out previous);
            wasDown[node] = down;
            return down && !previous;
        }
    }
}
