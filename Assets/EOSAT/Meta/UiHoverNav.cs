// UiHoverNav.cs - controller navigation of uGUI panels while an ISDK ray hovers them.
//   Thumbstick flick  : move between buttons (Selectable.FindSelectableOnUp / Down / Left / Right)
//   On an IP field    : the octet under the pointer is highlighted; up / down (or A / X = +1) change it (wraps 0-255),
//                       left / right move between octets
//   Pointer           : the highlighted control follows whatever the ray points at
//   Stick Y (held)    : scrolls a ScrollRect on the hovered panel (Replay list)
//   A / X             : click the highlighted control
// While Hovering is true the VR orbit / zoom sticks and the A / X recenter are paused (OrbitalViewerLocomotion,
// EyeLevelSpawner read UiHoverNav.Hovering). The trigger keeps working as the normal ISDK ray click.

using Oculus.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Eosat.Meta
{
    [DefaultExecutionOrder(-50)]          // before the locomotion scripts read Hovering
    public class UiHoverNav : MonoBehaviour
    {
        /// <summary>True while any controller ray hovers a UI canvas.</summary>
        public static bool Hovering { get; private set; }

        [SerializeField, Range(0.3f, 0.95f)] float flickThreshold = 0.6f;
        [SerializeField] float repeatSeconds = 0.3f;
        [SerializeField] float scrollSpeed = 1.2f;
        [SerializeField] OVRInput.Button clickA = OVRInput.Button.One;
        [SerializeField] OVRInput.Button clickX = OVRInput.Button.Three;

        RayInteractor[] rays = System.Array.Empty<RayInteractor>();
        float nextFind, nextRepeat;
        Canvas canvas;
        Selectable current;
        int octet;

        void OnDisable() => Hovering = false;

        void Update()
        {
            if (Time.unscaledTime >= nextFind) { rays = FindObjectsByType<RayInteractor>(FindObjectsSortMode.None); nextFind = Time.unscaledTime + 2f; }

            Canvas hit = null; Vector3 hitPoint = default;
            foreach (var r in rays)
            {
                if (!r || !r.isActiveAndEnabled || r.State == InteractorState.Disabled || !r.HasInteractable) continue;
                var c = r.Interactable ? r.Interactable.GetComponentInParent<Canvas>() : null;
                if (c) { hit = c.rootCanvas; hitPoint = r.End; break; }
            }
            Hovering = hit;
            if (!hit) { canvas = null; current = null; ShowOctet(null); return; }
            if (hit != canvas) { canvas = hit; current = Nearest(hit, hitPoint); octet = 0; Highlight(); }

            // The highlight follows the pointer; on the IP field the octet under the pointer is picked.
            var under = Under(canvas, hitPoint);
            if (under && under != current) { current = under; octet = 0; Highlight(); }
            if (under is InputField uf && TrySplitIp(uf.text, out _, out _)) octet = OctetAt(uf, hitPoint, octet);
            ShowOctet(current as InputField);

            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.Touch)
                          + OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.Touch);
            Scroll(stick.y);

            if (stick.magnitude < flickThreshold) nextRepeat = 0f;
            else if (Time.unscaledTime >= nextRepeat)
            {
                nextRepeat = Time.unscaledTime + repeatSeconds;
                Move(Mathf.Abs(stick.x) > Mathf.Abs(stick.y) ? (stick.x > 0 ? MoveDirection.Right : MoveDirection.Left)
                                                              : (stick.y > 0 ? MoveDirection.Up : MoveDirection.Down));
            }

            if (OVRInput.GetDown(clickA, OVRInput.Controller.Touch) || OVRInput.GetDown(clickX, OVRInput.Controller.Touch)) Click();
        }

        void Move(MoveDirection dir)
        {
            if (!current || !current.IsInteractable()) { current = Nearest(canvas, canvas.transform.position); Highlight(); return; }
            if (current is InputField f && StepOctet(f, dir)) return;
            Selectable next = dir switch
            {
                MoveDirection.Up => current.FindSelectableOnUp(),
                MoveDirection.Down => current.FindSelectableOnDown(),
                MoveDirection.Left => current.FindSelectableOnLeft(),
                _ => current.FindSelectableOnRight()
            };
            // Stay on the hovered panel: automatic navigation can jump to another canvas.
            if (next && next.GetComponentInParent<Canvas>()?.rootCanvas == canvas) { current = next; octet = 0; Highlight(); }
        }

        /// <summary>IPv4 in the host field: up / down change an octet, left / right pick one. False = leave the field.</summary>
        bool StepOctet(InputField f, MoveDirection dir)
        {
            if (!TrySplitIp(f.text, out var parts, out string rest)) return false;
            switch (dir)
            {
                case MoveDirection.Left: if (octet == 0) return false; octet--; break;
                case MoveDirection.Right: if (octet == 3) return false; octet++; break;
                default:
                    parts[octet] = (parts[octet] + (dir == MoveDirection.Up ? 1 : 255)) % 256;
                    f.text = string.Join(".", parts) + rest;
                    break;
            }
            return true;
        }

        /// <summary>"8.232.20.196/VR" -> [8,232,20,196] + "/VR". Also accepts a ":port" suffix.</summary>
        public static bool TrySplitIp(string text, out int[] parts, out string rest)
        {
            parts = new int[4]; rest = "";
            if (string.IsNullOrEmpty(text)) return false;
            int cut = text.IndexOfAny(new[] { ':', '/' });
            string ip = cut < 0 ? text : text.Substring(0, cut);
            if (cut >= 0) rest = text.Substring(cut);
            var s = ip.Split('.');
            if (s.Length != 4) return false;
            for (int i = 0; i < 4; i++)
                if (!int.TryParse(s[i], out parts[i]) || parts[i] < 0 || parts[i] > 255) return false;
            return true;
        }

        void Click()
        {
            if (current is InputField f) { StepOctet(f, MoveDirection.Up); return; }        // A / X: +1 on the highlighted octet
            if (!current || !current.IsInteractable()) return;
            ExecuteEvents.Execute(current.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        }

        void Scroll(float y)
        {
            if (Mathf.Abs(y) < 0.2f) return;
            var sr = canvas.GetComponentInChildren<ScrollRect>();
            if (sr && sr.vertical) sr.verticalNormalizedPosition = Mathf.Clamp01(sr.verticalNormalizedPosition + y * scrollSpeed * Time.unscaledDeltaTime);
        }

        void Highlight()
        {
            var es = EventSystem.current;
            if (es) es.SetSelectedGameObject(current ? current.gameObject : null);
        }

        static Selectable Under(Canvas c, Vector3 p)
        {
            foreach (var s in c.GetComponentsInChildren<Selectable>())
            {
                if (!s.IsInteractable() || !s.isActiveAndEnabled) continue;
                var rt = (RectTransform)s.transform;
                if (rt.rect.Contains(rt.InverseTransformPoint(p))) return s;
            }
            return null;
        }

        /// <summary>Which IPv4 octet (0-3) of the field's text is under world point p. Keeps fallback past the IP part.</summary>
        static int OctetAt(InputField f, Vector3 p, int fallback)
        {
            var t = f.textComponent;
            if (!t) return fallback;
            var chars = t.cachedTextGenerator.characters;
            int ipLen = f.text.IndexOfAny(new[] { ':', '/' }); if (ipLen < 0) ipLen = f.text.Length;
            float x = t.rectTransform.InverseTransformPoint(p).x * t.pixelsPerUnit;
            int dots = 0;
            for (int i = 0; i < ipLen && i < chars.Count - 1; i++)
            {
                if (x < (chars[i].cursorPos.x + chars[i + 1].cursorPos.x) * 0.5f) return dots;
                if (f.text[i] == '.') dots++;
            }
            return x < (chars.Count > ipLen ? chars[ipLen].cursorPos.x : float.MaxValue) ? Mathf.Min(dots, 3) : fallback;
        }

        Image octetBox;

        // Translucent box behind the highlighted octet of an IP field (no focus needed, so no system keyboard).
        void ShowOctet(InputField f)
        {
            bool show = f && f.textComponent && TrySplitIp(f.text, out _, out _);
            if (!show) { if (octetBox) octetBox.enabled = false; return; }
            var t = f.textComponent;
            if (!octetBox || octetBox.transform.parent != t.transform.parent)
            {
                if (!octetBox) { octetBox = new GameObject("IP Octet Highlight", typeof(RectTransform)).AddComponent<Image>(); octetBox.raycastTarget = false; }
                octetBox.transform.SetParent(t.transform.parent, false);
                octetBox.transform.SetSiblingIndex(t.transform.GetSiblingIndex());   // behind the digits
                octetBox.color = new Color(0.2f, 0.75f, 1f, 0.45f);
            }
            var chars = t.cachedTextGenerator.characters;
            int start = 0;
            for (int i = 0, dots = 0; i < f.text.Length && dots < octet; i++) if (f.text[i] == '.') { dots++; start = i + 1; }
            int end = start; while (end < f.text.Length && char.IsDigit(f.text[end])) end++;
            if (end >= chars.Count) { octetBox.enabled = false; return; }
            float ppu = t.pixelsPerUnit, pad = 4f;
            var rt = octetBox.rectTransform;
            Vector3 a = t.rectTransform.TransformPoint(new Vector3(chars[start].cursorPos.x / ppu - pad, 0f, 0f));
            Vector3 b = t.rectTransform.TransformPoint(new Vector3(chars[end].cursorPos.x / ppu + pad, 0f, 0f));
            var parent = (RectTransform)rt.parent;
            Vector2 la = parent.InverseTransformPoint(a), lb = parent.InverseTransformPoint(b);
            Vector2 tc = parent.InverseTransformPoint(t.rectTransform.TransformPoint(t.rectTransform.rect.center));
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localPosition = new Vector3((la.x + lb.x) * 0.5f, tc.y, 0f);
            rt.sizeDelta = new Vector2(Mathf.Abs(lb.x - la.x), t.rectTransform.rect.height * 0.9f);
            octetBox.enabled = true;
        }

        static Selectable Nearest(Canvas c, Vector3 p)
        {
            Selectable best = null; float bestD = float.MaxValue;
            foreach (var s in c.GetComponentsInChildren<Selectable>())
            {
                if (!s.IsInteractable() || !s.isActiveAndEnabled) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }
    }
}
