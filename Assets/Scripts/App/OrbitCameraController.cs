using UnityEngine;

namespace RobotArmStudio
{
    public sealed class OrbitCameraController : MonoBehaviour
    {
        private Transform _focus;
        private float _yaw = 24f;
        private float _pitch = 24f;
        private float _distance = 1.75f;
        private bool _overheadView;

        public void SetFocus(Transform focus)
        {
            _focus = focus;
            UpdateCamera();
        }

        public bool SetView(string view, out string reason)
        {
            if (string.Equals(view, "oblique", System.StringComparison.OrdinalIgnoreCase))
            {
                _overheadView = false;
                _yaw = 24f;
                _pitch = 24f;
                _distance = 1.75f;
            }
            else if (string.Equals(view, "overhead", System.StringComparison.OrdinalIgnoreCase))
            {
                _overheadView = true;
                _yaw = 24f;
                _pitch = 58f;
                _distance = 2.05f;
            }
            else
            {
                reason = "view 必须是 oblique 或 overhead。";
                return false;
            }

            UpdateCamera();
            reason = null;
            return true;
        }

        private void LateUpdate()
        {
            if (_focus == null) return;
            if (Input.GetKeyDown(KeyCode.V))
            {
                _overheadView = !_overheadView;
                _pitch = _overheadView ? 58f : 24f;
                _distance = _overheadView ? 2.05f : 1.75f;
            }
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * 3.5f;
                _pitch -= Input.GetAxis("Mouse Y") * 2.5f;
                _pitch = Mathf.Clamp(_pitch, 8f, 72f);
            }
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.001f)
                _distance = Mathf.Clamp(_distance - wheel * 1.2f, 0.55f, 4.5f);
            UpdateCamera();
        }

        private void UpdateCamera()
        {
            if (_focus == null) return;
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.position = _focus.position + rotation * new Vector3(0f, 0f, -_distance);
            transform.rotation = rotation;
        }
    }
}
