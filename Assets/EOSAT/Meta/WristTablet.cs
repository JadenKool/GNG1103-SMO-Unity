// WristTablet.cs  -  Meta XR Core SDK (OVRCameraRig / OVRInput)
// Keeps the UI tablet attached to the left wrist at all times, like a bracer.
// Hierarchy: OVRCameraRig > TrackingSpace > LeftHandAnchor > Wrist Tablet (this) > Tablet Canvas
// LeftHandAnchor follows the left hand when hands are tracked and the left controller otherwise,
// so the tablet works with both.
//
// Face User ON (default): the tablet floats just above the wrist and always turns toward your eyes,
//   so it is readable however you hold your arm.
// Face User OFF: rigid mount - it uses Rigid Local Position / Rotation relative to LeftHandAnchor
//   (a watch face on the back of the hand); tune those two values in the Inspector while wearing the headset.

using UnityEngine;

namespace Eosat.Meta
{
    [DisallowMultipleComponent]
    public class WristTablet : MonoBehaviour
    {
        [Tooltip("LeftHandAnchor. Defaults to this object's parent.")]
        [SerializeField] Transform wristAnchor;
        [Tooltip("CenterEyeAnchor. Auto-found if empty.")]
        [SerializeField] Transform head;

        [Header("Face User (billboard above the wrist)")]
        [SerializeField] bool faceUser = true;
        [Tooltip("Metres above the wrist / controller grip (along your own 'up').")]
        [SerializeField] float liftAboveWrist = 0.12f;
        [Tooltip("Metres pulled toward your eyes, so the hand never covers it.")]
        [SerializeField] float towardEyes = 0.02f;

        [Header("Rigid mount (Face User off)")]
        [SerializeField] Vector3 rigidLocalPosition = new Vector3(-0.07f, 0f, -0.06f);
        [Tooltip("Screen normal out of the back of the left hand, text running along the forearm.")]
        [SerializeField] Vector3 rigidLocalEuler = new Vector3(0f, 90f, 180f);

        [Header("Visibility")]
        [Tooltip("Hidden while the left hand / controller is not tracked (otherwise it would sit at the floor origin).")]
        [SerializeField] GameObject[] content = System.Array.Empty<GameObject>();

        bool shown = true;

        void Awake()
        {
            if (!wristAnchor) wristAnchor = transform.parent;
            if (!head)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig) head = rig.centerEyeAnchor ? rig.centerEyeAnchor : rig.transform.Find("TrackingSpace/CenterEyeAnchor");
            }
        }

        void LateUpdate()
        {
            if (!UnityEngine.XR.XRSettings.isDeviceActive) return;   // desktop preview pins the canvas to the view instead

            bool tracked = OVRInput.GetControllerPositionValid(OVRInput.Controller.LHand)
                        || OVRInput.GetControllerPositionValid(OVRInput.Controller.LTouch);
            if (tracked != shown)
            {
                shown = tracked;
                foreach (var go in content) if (go) go.SetActive(tracked);
            }
            if (!tracked || !wristAnchor) return;

            if (faceUser && head)
            {
                // "Up" is the player's own up (the rig is tilted while orbiting in VR).
                Vector3 up = wristAnchor.parent ? wristAnchor.parent.up : Vector3.up;
                Vector3 pos = wristAnchor.position + up * liftAboveWrist;
                Vector3 toEyes = head.position - pos;
                if (toEyes.sqrMagnitude > 1e-6f) pos += toEyes.normalized * towardEyes;
                // A canvas is read from its -Z side: point +Z away from the eyes.
                transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, up));
            }
            else
            {
                transform.localPosition = rigidLocalPosition;
                transform.localRotation = Quaternion.Euler(rigidLocalEuler);
            }
        }
    }
}
