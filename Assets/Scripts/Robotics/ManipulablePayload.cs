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

        public void AttachTo(Transform grip)
        {
            if (IsHeld || grip == null) return;
            _originalParent = transform.parent;
            if (_body == null) _body = GetComponent<Rigidbody>();
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.isKinematic = true;
            transform.SetParent(grip, false);
            transform.localPosition = new Vector3(0f, 0f, 0.07f);
            transform.localRotation = Quaternion.identity;
            IsHeld = true;
        }

        public void Release()
        {
            if (!IsHeld) return;
            transform.SetParent(_originalParent, true);
            if (_body == null) _body = GetComponent<Rigidbody>();
            _body.isKinematic = false;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            IsHeld = false;
        }
    }
}
