// EyeLevelSpawner.cs  -  Meta XR Core SDK (OVRCameraRig / OVRInput)
// Places an object relative to the user's head once the headset is really tracking (the first frames report
// a zero pose): at eye height, straight ahead of the horizontal gaze, at a fixed distance. After that the
// object is left alone, so it stays fixed in the room and you can walk around it.
// Used for the Earth (eye level, in front), the screen layout (centred on the user) and the white platform
// (Place On Floor). No floor / desk / spatial anchors are used: only the head pose.
// Press A or X (or call Recenter()) to place again, if Recenter Input is on.

using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Eosat.Meta
{
    public class EyeLevelSpawner : MonoBehaviour
    {
        [Tooltip("Object to place. Defaults to this object.")]
        [SerializeField] Transform target;
        [Tooltip("OVRCameraRig > TrackingSpace > CenterEyeAnchor. Auto-found if empty.")]
        [SerializeField] Transform head;

        [Header("Where")]
        [Tooltip("Metres in front of the eyes (horizontal). 0 = at the user's own position.")]
        [SerializeField] float forwardDistance = 0.6f;
        [Tooltip("Metres above (+) or below (-) eye height. 0 = exactly eye level.")]
        [SerializeField] float heightOffset = 0f;
        [Tooltip("Metres to the right (+) or left (-). 0 = dead centre.")]
        [SerializeField] float rightOffset = 0f;
        [Tooltip("Ignore eye height and put the object on the floor (tracking-space origin height).")]
        [SerializeField] bool placeOnFloor;
        [Tooltip("Turn the object so its forward axis points along the user's gaze when placed.")]
        [SerializeField] bool alignToGaze = true;

        [Header("When")]
        [SerializeField] bool spawnOnStart = true;
        [Tooltip("Give up waiting for head tracking after this many seconds and place anyway.")]
        [SerializeField] float maxWaitForTracking = 5f;

        [Header("Recenter input")]
        [Tooltip("Off = placed once at start only (e.g. the screen layout, so screens you moved are not reset).")]
        [SerializeField] bool recenterInput = true;
        [Tooltip("A on the right controller.")]
        [SerializeField] OVRInput.Button recenterButton = OVRInput.Button.One;
        [Tooltip("X on the left controller.")]
        [SerializeField] OVRInput.Button alternateButton = OVRInput.Button.Three;
        [SerializeField] OVRInput.Controller controllers = OVRInput.Controller.Touch;
        [SerializeField] Key desktopKey = Key.C;

        public UnityEvent onPlaced;

        /// <summary>True once the object has been placed at least once.</summary>
        public bool IsPlaced { get; private set; }
        public Vector3 LastPlacedPosition { get; private set; }

        bool HeadsetActive => UnityEngine.XR.XRSettings.isDeviceActive;
        // CenterEyeAnchor is local to TrackingSpace: a non-zero local pose means tracking has started.
        bool HeadTracked => HeadsetActive && head && head.localPosition.sqrMagnitude > 1e-4f;

        void Awake()
        {
            if (!target) target = transform;
            if (!head)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig) head = rig.centerEyeAnchor ? rig.centerEyeAnchor : rig.transform.Find("TrackingSpace/CenterEyeAnchor");
                else if (Camera.main) head = Camera.main.transform;
            }
        }

        void OnEnable()
        {
            if (spawnOnStart && !IsPlaced) StartCoroutine(SpawnWhenTracked());
        }

        IEnumerator SpawnWhenTracked()
        {
            yield return null;                                   // XR finishes starting
            if (!HeadsetActive) yield break;                     // desktop preview: keep the pose saved in the scene
            float deadline = Time.unscaledTime + maxWaitForTracking;
            while (!HeadTracked && Time.unscaledTime < deadline) yield return null;
            yield return null;                                   // one more frame so the pose is this frame's
            Place();
        }

        void Update()
        {
            if (!recenterInput) return;
            bool pressed = OVRInput.GetDown(recenterButton, controllers) || OVRInput.GetDown(alternateButton, controllers);
            var kb = Keyboard.current;
            if (kb != null && kb[desktopKey].wasPressedThisFrame) pressed = true;
            if (pressed) Place();
        }

        /// <summary>Place again relative to where the user stands and looks now (for a UI button).</summary>
        public void Recenter() => Place();

        /// <summary>Place now. Works in the player's own frame (TrackingSpace), so it is correct even if the rig is moved or tilted.</summary>
        public void Place()
        {
            if (!head || !target) return;
            Transform space = head.parent;                                   // TrackingSpace
            Vector3 up = space ? space.up : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, up);
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : (space ? space.forward : Vector3.forward);   // looking straight up/down
            Vector3 right = Vector3.Cross(up, fwd);

            Vector3 pos = head.position + fwd * forwardDistance + right * rightOffset;
            if (placeOnFloor && space) pos -= up * Vector3.Dot(pos - space.position, up);   // drop to the floor plane
            pos += up * heightOffset;
            Quaternion rot = alignToGaze ? Quaternion.LookRotation(fwd, up) : target.rotation;

            var rb = target.GetComponent<Rigidbody>();
            if (rb) { rb.position = pos; rb.rotation = rot; if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } }
            target.SetPositionAndRotation(pos, rot);

            IsPlaced = true;
            LastPlacedPosition = pos;
            Debug.Log($"[EyeLevelSpawner] {target.name} placed at {pos:F2} (eye height {(space ? Vector3.Dot(head.position - space.position, up) : head.position.y):F2} m)");
            onPlaced?.Invoke();
        }
    }
}
