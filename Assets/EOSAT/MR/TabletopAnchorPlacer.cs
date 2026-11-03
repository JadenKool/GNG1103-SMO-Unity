using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Eosat.MR
{
    /// <summary>
    /// Finds the desk in front of the user (Space Setup planes, over Link with "Spatial data over
    /// Meta Horizon Link" on), places the tabletop content on it and pins it with an ARAnchor.
    /// Falls back to a pose in front of the user if no desk is found.
    /// </summary>
    public class TabletopAnchorPlacer : MonoBehaviour
    {
        [Header("References")]
        public XROrigin xrOrigin;
        public ARPlaneManager planeManager;
        public ARAnchorManager anchorManager;
        [Tooltip("Root of the tabletop content (Globe Pivot). Hidden until placed.")]
        public Transform content;

        [Header("Desk search (metres)")]
        [Tooltip("Desk height above the floor. Requires XR Origin Tracking Origin Mode = Floor.")]
        public Vector2 deskHeightRange = new Vector2(0.55f, 1.2f);
        public float maxDistance = 1.5f;
        public float forwardDistance = 0.5f;
        [Tooltip("Sideways shift (+ = right) so the globe sits beside the laptop, not on top of it.")]
        public float lateralOffset = 0.3f;
        [Tooltip("Globe centre height above the desk surface.")]
        public float hoverHeight = 0.2f;
        public float fallbackAfterSeconds = 6f;

        public bool IsPlaced { get; private set; }

        ARAnchor currentAnchor;
        bool busy;

        void Start()
        {
            if (!xrOrigin) xrOrigin = FindAnyObjectByType<XROrigin>();
            content.gameObject.SetActive(false);
            BeginSearch();
        }

        /// <summary>Re-run desk detection and move the content.</summary>
        public void Replace()
        {
            if (busy) return;
            IsPlaced = false;
            content.SetParent(null, true);
            if (currentAnchor && anchorManager) anchorManager.TryRemoveAnchor(currentAnchor);
            currentAnchor = null;
            content.gameObject.SetActive(false);
            BeginSearch();
        }

        void BeginSearch()
        {
            if (planeManager && UnityEngine.XR.XRSettings.isDeviceActive) planeManager.enabled = true; // no headset = no planes to scan
            StopAllCoroutines();
            StartCoroutine(SearchRoutine());
        }

        IEnumerator SearchRoutine()
        {
            const float step = 0.5f;
            var wait = new WaitForSeconds(step);
            float elapsed = 0f;
            yield return null; // let the XR camera pick up the head pose first
            while (true)
            {
                if (TryFindDesk(out Pose pose)) { PlaceAt(pose, true); yield break; }
                if (elapsed >= fallbackAfterSeconds) { PlaceAt(FallbackPose(), false); yield break; }
                yield return wait;
                elapsed += step;
            }
        }

        bool TryFindDesk(out Pose pose)
        {
            pose = default;
            if (!planeManager || planeManager.subsystem == null || !planeManager.subsystem.running) return false;

            Transform cam = xrOrigin.Camera.transform;
            Vector3 fwd = FlatForward(cam);
            float floorY = xrOrigin.Origin.transform.position.y;

            ARPlane best = null;
            float bestDist = float.MaxValue;
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment != PlaneAlignment.HorizontalUp) continue;

                float height = plane.center.y - floorY;
                if (height < deskHeightRange.x || height > deskHeightRange.y) continue;

                Vector3 toPlane = plane.center - cam.position;
                toPlane.y = 0f;
                float dist = toPlane.magnitude;
                if (dist > maxDistance) continue;
                if (dist > 0.05f && Vector3.Dot(toPlane / dist, fwd) < 0.2f) continue; // must be in front

                if (dist < bestDist) { best = plane; bestDist = dist; }
            }
            if (!best) return false;

            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 target = cam.position + fwd * forwardDistance + right * lateralOffset;
            target.y = best.center.y;
            Vector3 local = best.transform.InverseTransformPoint(target);
            if (Mathf.Abs(local.x) > best.extents.x || Mathf.Abs(local.z) > best.extents.y)
                target = best.center;

            pose = new Pose(target, Quaternion.LookRotation(fwd, Vector3.up));
            return true;
        }

        Pose FallbackPose()
        {
            Transform cam = xrOrigin.Camera.transform;
            Vector3 fwd = FlatForward(cam);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 pos = cam.position + fwd * forwardDistance + right * (lateralOffset * 0.5f) + Vector3.down * 0.45f;
            return new Pose(pos, Quaternion.LookRotation(fwd, Vector3.up));
        }

        async void PlaceAt(Pose deskPose, bool foundDesk)
        {
            busy = true;
            bool anchored = false;
            if (anchorManager && anchorManager.subsystem != null && anchorManager.subsystem.running)
            {
                var result = await anchorManager.TryAddAnchorAsync(deskPose);
                if (result.status.IsSuccess())
                {
                    currentAnchor = result.value;
                    content.SetParent(currentAnchor.transform, false);
                    content.localPosition = Vector3.up * hoverHeight;
                    content.localRotation = Quaternion.identity;
                    anchored = true;
                }
            }
            if (!anchored)
                content.SetPositionAndRotation(deskPose.position + Vector3.up * hoverHeight, deskPose.rotation);

            content.gameObject.SetActive(true);
            if (planeManager) planeManager.enabled = false; // finished scanning
            IsPlaced = true;
            busy = false;
            Debug.Log($"[EOSAT MR] Tabletop placed ({(foundDesk ? "on desk plane" : "fallback pose")}, {(anchored ? "anchored" : "unanchored")}).");
        }

        static Vector3 FlatForward(Transform t)
        {
            Vector3 f = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }
    }
}

