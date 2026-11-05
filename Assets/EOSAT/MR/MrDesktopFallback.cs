using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Eosat.MR
{
    /// <summary>
    /// When no headset is active at Play, switches the MR scene to keyboard + mouse:
    /// right-drag look, WASD/QE move (Shift = fast), scroll = move forward/back,
    /// 1/2/3 = Data / Antenna / Solar Panels, R = re-place the globe.
    /// The wrist tablet is pinned to the lower-left of the view and clickable with the mouse.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class MrDesktopFallback : MonoBehaviour
    {
        public Camera cam;
        public HandTablet handTablet;
        public Canvas tabletCanvas;
        public GameObject desktopHint;
        public TabletUIController tablet;
        public TabletopAnchorPlacer placer;
        [Tooltip("AR Session / camera / plane / anchor managers. Saved disabled in the scene and switched on only when a headset is running, so desktop Play has no 'No active XR...Subsystem' warnings.")]
        public Behaviour[] arComponents;

        [Header("Desktop preview (no headset)")]
        [Tooltip("Stand-in desk/room shown only on desktop, where there is no passthrough of the real room.")]
        public GameObject previewRoom;
        [Tooltip("Height of the globe centre above the desk surface (same as TabletopAnchorPlacer.hoverHeight).")]
        public float deskOffset = 0.2f;
        public Color previewBackground = new Color(0.36f, 0.34f, 0.32f, 1f);
        bool framed;

        [Header("Desktop controls")]
        public float moveSpeed = 0.6f;
        public float lookSpeed = 0.15f;
        public float scrollStep = 0.1f;
        [Tooltip("Tablet position relative to the camera (m): left, down, forward.")]
        public Vector3 tabletOffset = new Vector3(-0.17f, -0.07f, 0.45f);

        public bool Active { get; private set; }
        float yaw, pitch;

        IEnumerator Start()
        {
            yield return null; // XR finishes starting before the first frame
            if (XRSettings.isDeviceActive)
            {
                if (arComponents != null)
                    foreach (var b in arComponents) if (b) b.enabled = true;
                yield break;
            }
            EnableDesktopMode();
        }

        void EnableDesktopMode()
        {
            Active = true;
            var tpd = cam.GetComponent<TrackedPoseDriver>();
            if (tpd) tpd.enabled = false;

            Transform root = handTablet ? handTablet.transform : tabletCanvas.transform;
            if (handTablet) handTablet.enabled = false;
            root.SetParent(cam.transform, false);
            root.localPosition = tabletOffset;
            root.localRotation = Quaternion.identity;

            tabletCanvas.gameObject.SetActive(true);
            tabletCanvas.worldCamera = cam;
            if (!tabletCanvas.GetComponent<GraphicRaycaster>()) tabletCanvas.gameObject.AddComponent<GraphicRaycaster>();
            if (desktopHint) desktopHint.SetActive(true);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = previewBackground;   // no passthrough on desktop: neutral room colour instead of black

            Vector3 e = cam.transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
            Debug.Log("[EOSAT MR] No headset detected - desktop controls on (right-drag look, WASD/QE move, 1/2/3 toggles, R re-place).");
        }

        void Update()
        {
            if (!Active) return;
            if (!framed && placer && placer.IsPlaced) FrameDesk();
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.deltaTime;
            Transform t = cam.transform;

            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * lookSpeed;
                    pitch = Mathf.Clamp(pitch - d.y * lookSpeed, -80f, 80f);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) t.position += t.forward * Mathf.Sign(scroll) * scrollStep;
            }
            t.rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (kb == null) return;
            Vector3 m = Vector3.zero;
            if (kb.wKey.isPressed) m.z += 1f;
            if (kb.sKey.isPressed) m.z -= 1f;
            if (kb.dKey.isPressed) m.x += 1f;
            if (kb.aKey.isPressed) m.x -= 1f;
            if (kb.eKey.isPressed) m.y += 1f;
            if (kb.qKey.isPressed) m.y -= 1f;
            if (m != Vector3.zero)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                float speed = moveSpeed * (kb.leftShiftKey.isPressed ? 3f : 1f);
                t.position += (right * m.x + Vector3.up * m.y + fwd * m.z) * speed * dt;
            }

            if (tablet)
            {
                if (kb.digit1Key.wasPressedThisFrame) Flip(tablet.dataToggle);
                if (kb.digit2Key.wasPressedThisFrame) Flip(tablet.antennaToggle);
                if (kb.digit3Key.wasPressedThisFrame) Flip(tablet.panelsToggle);
            }
            if (kb.rKey.wasPressedThisFrame && placer) { placer.Replace(); framed = false; }
        }

        /// <summary>Puts the stand-in desk under the globe and seats the camera at it, like the reference shot.</summary>
        void FrameDesk()
        {
            framed = true;
            Transform globe = placer.content;
            Vector3 toGlobe = globe.position - cam.transform.position;
            toGlobe.y = 0f;
            Vector3 fwd = toGlobe.sqrMagnitude > 1e-4f ? toGlobe.normalized : Vector3.forward;
            float deskY = globe.position.y - deskOffset;

            if (previewRoom)
            {
                previewRoom.SetActive(true);
                previewRoom.transform.SetPositionAndRotation(new Vector3(globe.position.x, deskY, globe.position.z) - fwd * 0.05f,
                                                             Quaternion.LookRotation(fwd, Vector3.up));
            }

            // Seated viewer ~0.7 m back, eyes ~0.42 m above the desk, looking at the globe.
            cam.transform.position = globe.position - fwd * 0.7f + Vector3.up * 0.22f;
            cam.transform.LookAt(globe.position + Vector3.up * 0.04f);
            Vector3 e = cam.transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
            Debug.Log($"[EOSAT MR] Desktop preview: desk top at y={deskY:0.00} m, globe centre {deskOffset * 100f:0} cm above it, camera {Vector3.Distance(cam.transform.position, globe.position):0.00} m away.");
        }

        static void Flip(Toggle toggle)
        {
            if (toggle) toggle.isOn = !toggle.isOn; // fires the same handler as a click/poke
        }
    }
}
