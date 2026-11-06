using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Eosat.MR
{
    /// <summary>
    /// Lets you pick up the tabletop globe with a hand pinch / controller grip (XRGrabInteractable) and
    /// move it around the desk. It stays upright while held, and on release it is set back down at the
    /// desk height it was placed at.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class TabletopGrab : MonoBehaviour
    {
        XRGrabInteractable grab;
        float restHeight;
        bool hasRestHeight;

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            grab.selectEntered.AddListener(OnGrab);
            grab.selectExited.AddListener(OnRelease);
        }

        void OnDestroy()
        {
            if (!grab) return;
            grab.selectEntered.RemoveListener(OnGrab);
            grab.selectExited.RemoveListener(OnRelease);
        }

        void OnGrab(SelectEnterEventArgs _)
        {
            if (!hasRestHeight) { restHeight = transform.position.y; hasRestHeight = true; }
            Debug.Log("[EOSAT MR] Globe grabbed");
        }

        void OnRelease(SelectExitEventArgs _)
        {
            Vector3 p = transform.position;
            transform.SetPositionAndRotation(new Vector3(p.x, restHeight, p.z),
                Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            Debug.Log($"[EOSAT MR] Globe set down at ({p.x:0.00}, {restHeight:0.00}, {p.z:0.00})");
        }

        /// <summary>Call after the placer moves the globe, so release returns it to the new desk height.</summary>
        public void ResetRestHeight() => hasRestHeight = false;
    }
}
