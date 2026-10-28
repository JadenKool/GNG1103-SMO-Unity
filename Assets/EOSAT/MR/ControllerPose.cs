using UnityEngine;
using UnityEngine.InputSystem;

namespace Eosat.MR
{
    /// <summary>
    /// Moves this transform to a Touch controller's pose and reports whether it is tracked.
    /// Used as a fallback for the hand tablet and the poke finger when hands aren't tracked.
    /// </summary>
    public class ControllerPose : MonoBehaviour
    {
        public enum Side { Left, Right }
        public Side side = Side.Left;

        InputAction position, rotation, tracked;

        public bool IsTracked { get; private set; }

        void OnEnable()
        {
            string hand = side == Side.Left ? "{LeftHand}" : "{RightHand}";
            position = new InputAction(binding: $"<XRController>{hand}/devicePosition");
            rotation = new InputAction(binding: $"<XRController>{hand}/deviceRotation");
            tracked = new InputAction(binding: $"<XRController>{hand}/isTracked");
            position.Enable(); rotation.Enable(); tracked.Enable();
        }

        void OnDisable()
        {
            position?.Dispose(); rotation?.Dispose(); tracked?.Dispose();
            IsTracked = false;
        }

        void Update()
        {
            IsTracked = tracked.ReadValue<float>() > 0.5f;
            if (!IsTracked) return;
            transform.localPosition = position.ReadValue<Vector3>();
            transform.localRotation = rotation.ReadValue<Quaternion>();
        }
    }
}
