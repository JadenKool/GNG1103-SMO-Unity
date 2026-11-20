using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Global show / hide of the quaternion (body) axes. Visible by default. Put one on each axes object: it
    /// enables / disables the renderers under it (the GameObject stays active so runtime-built lines still exist).
    /// AttitudeGuides also hides its quaternion axis / arc / label when Visible is false. UI: AxesToggle.Toggle().
    /// </summary>
    public class AxesToggle : MonoBehaviour
    {
        public static bool Visible { get; private set; } = true;
        public static event System.Action<bool> Changed;

        public static void Set(bool visible) { if (visible == Visible) return; Visible = visible; Changed?.Invoke(visible); }
        public static void ToggleAll() => Set(!Visible);

        // Instance wrappers for persistent UnityEvent listeners (static methods cannot be wired in the Inspector).
        public void Toggle() => ToggleAll();
        public void SetVisible(bool visible) => Set(visible);

        bool applied = true, first = true;

        void LateUpdate()
        {
            if (!first && applied == Visible) return;
            first = false; applied = Visible;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = Visible;
        }
    }
}
