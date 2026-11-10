// LayoutSlotHints.cs - shows the outlines of the layout slots only while a screen is being moved
// (like Meta Home showing where a window can go). Keeps the view clear the rest of the time.
// "Being moved" = its position inside the layout changed in the last moment. (A snapped screen is held by its
// SnapInteractor all the time, so the grab count can't be used for this.)

using UnityEngine;

namespace Eosat.Meta
{
    public class LayoutSlotHints : MonoBehaviour
    {
        [Tooltip("The root of every screen (children of this layout).")]
        public Transform[] screens = System.Array.Empty<Transform>();
        [Tooltip("The outline renderer of every slot.")]
        public Renderer[] outlines = System.Array.Empty<Renderer>();
        [Tooltip("Outlines stay on this long after the last movement (s).")]
        public float holdSeconds = 1.5f;
        [Tooltip("Ignore movement during the first seconds (screens settle into their start slots).")]
        public float startDelay = 2f;

        Vector3[] last;
        float lastMove = -100f, enabledAt;
        bool shown = true;

        void OnEnable()
        {
            enabledAt = Time.unscaledTime;
            last = new Vector3[screens.Length];
            for (int i = 0; i < screens.Length; i++) if (screens[i]) last[i] = transform.InverseTransformPoint(screens[i].position);
            Show(false);
        }

        void LateUpdate()
        {
            for (int i = 0; i < screens.Length; i++)
            {
                if (!screens[i]) continue;
                Vector3 p = transform.InverseTransformPoint(screens[i].position);     // layout space: unaffected by the VR orbit
                if ((p - last[i]).sqrMagnitude > 0.0005f * 0.0005f && Time.unscaledTime - enabledAt > startDelay) lastMove = Time.unscaledTime;
                last[i] = p;
            }
            bool want = Time.unscaledTime - lastMove < holdSeconds;
            if (want != shown) Show(want);
        }

        void Show(bool on)
        {
            shown = on;
            foreach (var r in outlines) if (r) r.enabled = on;
        }
    }
}
