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

        [Tooltip("Apply rotation as well as position. Off for the camera when something " +
                 "else already orients it.")]
        public bool applyRotation = true;

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
                transform.rotation = basis != null ? basis.rotation * rotation : rotation;
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

            headDriver = headCamera.gameObject.AddComponent<XRPoseDriver>();
            headDriver.node = XRNode.CenterEye;
            headDriver.origin = anchor;

            var hand = new GameObject("Pointer").transform;
            hand.SetParent(anchor, false);
            pointerDriver = hand.gameObject.AddComponent<XRPoseDriver>();
            pointerDriver.node = rightHanded ? XRNode.RightHand : XRNode.LeftHand;
            pointerDriver.origin = anchor;
            pointer = hand;
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
