using System.Collections.Generic;
using UnityEngine;

namespace RobotArmStudio
{
    public sealed class ManipulablePayload : MonoBehaviour
    {
        private static readonly List<ManipulablePayload> ActiveItems = new List<ManipulablePayload>();

        [SerializeField] private string payloadName = "Payload";
        private Rigidbody _body;
        private Transform _originalParent;
        private Transform _heldGrip;
        private Vector3 _heldLocalPosition;
        private Quaternion _heldLocalRotation;
        private RigidbodyInterpolation _previousInterpolation;

        public string PayloadName => payloadName;
        public bool IsHeld { get; private set; }
        public Vector3 WorldPosition => transform.position;
        public static IReadOnlyList<ManipulablePayload> Active => ActiveItems;

        public void Configure(string displayName, Color color, Vector3 size)
        {
            payloadName = displayName;
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = color;
            transform.localScale = size;
            _body = GetComponent<Rigidbody>();
            if (_body == null) _body = gameObject.AddComponent<Rigidbody>();
            _body.mass = 0.25f;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _originalParent = transform.parent;
        }

        private void OnEnable()
        {
            if (!ActiveItems.Contains(this)) ActiveItems.Add(this);
        }

        private void OnDisable()
        {
            ActiveItems.Remove(this);
        }

        private void LateUpdate()
        {
            if (!IsHeld || _heldGrip == null) return;

            Vector3 position = _heldGrip.TransformPoint(_heldLocalPosition);
            Quaternion rotation = _heldGrip.rotation * _heldLocalRotation;
            transform.SetPositionAndRotation(position, rotation);
            if (_body != null)
            {
                _body.position = position;
                _body.rotation = rotation;
            }
        }

        public void AttachTo(Transform grip)
        {
            if (IsHeld || grip == null) return;
            _originalParent = transform.parent;
            if (_body == null) _body = GetComponent<Rigidbody>();
            if (_body == null) _body = gameObject.AddComponent<Rigidbody>();
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _previousInterpolation = _body.interpolation;
            _body.interpolation = RigidbodyInterpolation.None;
            _body.isKinematic = true;
            _heldGrip = grip;
            _heldLocalPosition = new Vector3(0f, 0f, 0.07f);
            _heldLocalRotation = Quaternion.identity;
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(
                _heldGrip.TransformPoint(_heldLocalPosition),
                _heldGrip.rotation * _heldLocalRotation);
            _body.position = transform.position;
            _body.rotation = transform.rotation;
            IsHeld = true;
        }

        public void Release()
        {
            if (!IsHeld) return;
            Vector3 position = _heldGrip != null
                ? _heldGrip.TransformPoint(_heldLocalPosition)
                : transform.position;
            Quaternion rotation = _heldGrip != null
                ? _heldGrip.rotation * _heldLocalRotation
                : transform.rotation;
            transform.SetParent(_originalParent, true);
            if (_body == null) _body = GetComponent<Rigidbody>();
            transform.SetPositionAndRotation(position, rotation);
            if (_body != null)
            {
                _body.position = position;
                _body.rotation = rotation;
                _body.isKinematic = false;
                _body.interpolation = _previousInterpolation;
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            _heldGrip = null;
            IsHeld = false;
        }
    }
}
