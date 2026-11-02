using UnityEngine;

namespace RobotArmStudio
{
    /// <summary>SO-100 follower kinematic model with five arm joints and a parallel gripper.</summary>
    public sealed class SimulatedRobotArm : MonoBehaviour, IRobotArmDriver
    {
        private const float UpperArmLength = 0.135f;
        private const float ForearmLength = 0.140f;
        private const float ToolLength = 0.200f;
        private const float ShoulderHeight = 0.080f;
        private const float TcpOffsetY = -0.010f;
        private const float TargetTolerance = 0.006f;
        private const float JointSpeedDegPerSecond = 65f;

        // Joint order follows the SO-100 follower motor names used by LeRobot.
        private static readonly string[] JointNames =
        {
            "shoulder_pan", "shoulder_lift", "elbow_flex", "wrist_flex", "wrist_roll"
        };
        private static readonly float[] JointMin = { -115f, -95f, 0f, -130f, -180f };
        private static readonly float[] JointMax = { 115f, 95f, 150f, 130f, 180f };

        private int _index;
        private float[] _angles = new float[5];
        private float[] _targets = new float[5];
        private Transform _yaw;
        private Transform _shoulder;
        private Transform _elbow;
        private Transform _wristFlex;
        private Transform _wristRoll;
        private Transform _gripper;
        private Transform _leftFinger;
        private Transform _rightFinger;
        private Transform _gripPoint;
        private Transform _targetMarker;
        private Material _printed;
        private Material _motor;
        private Material _accent;
        private float _gripperTarget = 1f;
        private ManipulablePayload _heldPayload;
        private string _status = "待机";
        private bool _paused;
        private bool _hasTarget;
        private Vector3 _commandedTarget;

        public int Index => _index;
        public string Name => $"SO-100 Arm {_index + 1}";
        public Vector3 Tcp => _gripPoint != null ? _gripPoint.position : transform.position;
        public float[] JointAnglesDeg => (float[])_angles.Clone();
        public float GripperOpen => _gripperTarget;
        public bool IsMoving
        {
            get
            {
                if (_hasTarget && Vector3.Distance(Tcp, _commandedTarget) > TargetTolerance) return true;
                for (int i = 0; i < _angles.Length; i++)
                    if (Mathf.Abs(Mathf.DeltaAngle(_angles[i], _targets[i])) > 0.8f) return true;
                return false;
            }
        }
        public string Holding { get; private set; } = "";
        public string Status => _status;

        public static string[] GetJointNames() => (string[])JointNames.Clone();
        public static float[] GetJointMins() => (float[])JointMin.Clone();
        public static float[] GetJointMaxes() => (float[])JointMax.Clone();

        public void Configure(int index, Vector3 basePosition, Material printed, Material motor, Material accent)
        {
            _index = index;
            _printed = printed;
            _motor = motor;
            _accent = accent;
            transform.position = basePosition;
            transform.name = Name;
            BuildKinematicChain();

            // A compact, slightly folded neutral pose. Angles are relative simulation coordinates;
            // a physical driver must apply the user's SO-100 motor calibration before transmission.
            _angles = new[] { 0f, -25f, 50f, -25f, 0f };
            _targets = (float[])_angles.Clone();
            ApplyJointRotations();
            UpdateGripperVisual(1f);
            if (_targetMarker != null) _targetMarker.position = Tcp;
            _status = "待机 · SO-100 仿真";
        }

        private void BuildKinematicChain()
        {
            CreateBox("底座打印件", transform, new Vector3(0f, 0.018f, 0f), new Vector3(0.14f, 0.036f, 0.14f), _printed);
            CreateBox("底座舵机", transform, new Vector3(0f, 0.060f, 0f), new Vector3(0.074f, 0.070f, 0.090f), _motor);

            var yawObject = new GameObject("J1 shoulder_pan");
            yawObject.transform.SetParent(transform, false);
            yawObject.transform.localPosition = new Vector3(0f, ShoulderHeight, 0f);
            _yaw = yawObject.transform;
            CreateCylinder("旋转轴", _yaw, Vector3.zero, 0.055f, 0.030f, _accent, Vector3.up);

            var shoulderObject = new GameObject("J2 shoulder_lift");
            shoulderObject.transform.SetParent(_yaw, false);
            shoulderObject.transform.localPosition = Vector3.zero;
            _shoulder = shoulderObject.transform;
            CreateBox("肩部支架", _shoulder, Vector3.zero, new Vector3(0.070f, 0.060f, 0.070f), _printed);
            CreateCylinder("肩部舵机", _shoulder, Vector3.zero, 0.060f, 0.050f, _motor, Vector3.right);
            CreateSegment("上臂打印件", _shoulder, UpperArmLength, 0.052f, 0.034f, _printed);

            var elbowObject = new GameObject("J3 elbow_flex");
            elbowObject.transform.SetParent(_shoulder, false);
            elbowObject.transform.localPosition = new Vector3(0f, 0f, UpperArmLength);
            _elbow = elbowObject.transform;
            CreateCylinder("肘部舵机", _elbow, Vector3.zero, 0.054f, 0.046f, _motor, Vector3.right);
            CreateBox("肘部连接件", _elbow, new Vector3(0f, 0f, 0.025f), new Vector3(0.064f, 0.042f, 0.052f), _printed);
            CreateSegment("前臂打印件", _elbow, ForearmLength, 0.045f, 0.030f, _printed);

            var wristFlexObject = new GameObject("J4 wrist_flex");
            wristFlexObject.transform.SetParent(_elbow, false);
            wristFlexObject.transform.localPosition = new Vector3(0f, 0f, ForearmLength);
            _wristFlex = wristFlexObject.transform;
            CreateCylinder("腕部俯仰舵机", _wristFlex, Vector3.zero, 0.048f, 0.040f, _motor, Vector3.right);
            CreateBox("腕部支架", _wristFlex, new Vector3(0f, 0f, 0.023f), new Vector3(0.055f, 0.040f, 0.050f), _printed);

            var wristRollObject = new GameObject("J5 wrist_roll");
            wristRollObject.transform.SetParent(_wristFlex, false);
            wristRollObject.transform.localPosition = new Vector3(0f, 0f, 0.045f);
            _wristRoll = wristRollObject.transform;
            CreateCylinder("腕部旋转舵机", _wristRoll, Vector3.zero, 0.043f, 0.046f, _motor, Vector3.forward);

            var gripperObject = new GameObject("夹爪 gripper");
            gripperObject.transform.SetParent(_wristRoll, false);
            gripperObject.transform.localPosition = new Vector3(0f, 0f, 0.035f);
            _gripper = gripperObject.transform;
            CreateBox("夹爪舵机座", _gripper, new Vector3(0f, 0f, 0.018f), new Vector3(0.076f, 0.050f, 0.064f), _printed);
            CreateBox("夹爪舵机", _gripper, new Vector3(0f, 0f, 0.012f), new Vector3(0.046f, 0.043f, 0.046f), _motor);
            _leftFinger = CreateBox("左夹指", _gripper, Vector3.zero, new Vector3(0.018f, 0.022f, 0.105f), _printed).transform;
            _rightFinger = CreateBox("右夹指", _gripper, Vector3.zero, new Vector3(0.018f, 0.022f, 0.105f), _printed).transform;
            var grip = new GameObject("TCP");
            grip.transform.SetParent(_gripper, false);
            grip.transform.localPosition = new Vector3(0f, -0.010f, 0.120f);
            _gripPoint = grip.transform;
            UpdateGripperVisual(1f);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"{Name} · 目标位置";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.localScale = Vector3.one * 0.035f;
            marker.GetComponent<Renderer>().material = _accent;
            marker.GetComponent<Renderer>().material.color = new Color(0.2f, 0.95f, 0.83f, 0.62f);
            _targetMarker = marker.transform;
            _targetMarker.position = Tcp;
        }

        private static GameObject CreateBox(string objectName, Transform parent, Vector3 localPosition, Vector3 size, Material material)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = objectName;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPosition;
            obj.transform.localScale = size;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            var collider = obj.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return obj;
        }

        private static void CreateSegment(string objectName, Transform parent, float length, float width, float height, Material material)
        {
            CreateBox(objectName, parent, new Vector3(0f, 0f, length * 0.5f), new Vector3(width, height, length), material);
        }

        private static void CreateCylinder(string objectName, Transform parent, Vector3 localPosition, float length, float diameter, Material material, Vector3 axis)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            obj.name = objectName;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPosition;
            obj.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            obj.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            obj.GetComponent<Renderer>().sharedMaterial = material;
            var collider = obj.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private void Update()
        {
            if (_paused) return;
            float step = JointSpeedDegPerSecond * Time.deltaTime;
            for (int i = 0; i < _angles.Length; i++)
                _angles[i] = Mathf.MoveTowardsAngle(_angles[i], _targets[i], step);
            ApplyJointRotations();
            UpdateGripperVisual(_gripperTarget);

            bool jointsMoving = false;
            for (int i = 0; i < _angles.Length; i++)
                jointsMoving |= Mathf.Abs(Mathf.DeltaAngle(_angles[i], _targets[i])) > 0.8f;
            if (_hasTarget && !jointsMoving && Vector3.Distance(Tcp, _commandedTarget) <= TargetTolerance)
            {
                _hasTarget = false;
                _status = "目标已到达";
            }
            else if (_hasTarget || jointsMoving) _status = "运动中";
            else if (_status == "运动中") _status = "待机";
            if (_targetMarker != null && _hasTarget) _targetMarker.position = _commandedTarget;
        }

        private void ApplyJointRotations()
        {
            _yaw.localRotation = Quaternion.AngleAxis(_angles[0], Vector3.up);
            _shoulder.localRotation = Quaternion.AngleAxis(_angles[1], Vector3.right);
            _elbow.localRotation = Quaternion.AngleAxis(_angles[2], Vector3.right);
            _wristFlex.localRotation = Quaternion.AngleAxis(_angles[3], Vector3.right);
            _wristRoll.localRotation = Quaternion.AngleAxis(_angles[4], Vector3.forward);
        }

        private void UpdateGripperVisual(float openness)
        {
            if (_leftFinger == null || _rightFinger == null) return;
            float spread = Mathf.Lerp(0.012f, 0.032f, Mathf.Clamp01(openness));
            _leftFinger.localPosition = new Vector3(-spread, -0.010f, 0.080f);
            _rightFinger.localPosition = new Vector3(spread, -0.010f, 0.080f);
        }

        public bool MoveTo(Vector3 target, out string reason)
        {
            if (!CanMoveTo(target, out reason)) return false;
            if (!TrySolve(target, out float[] solution, out reason)) return false;
            _targets = solution;
            _commandedTarget = target;
            _hasTarget = true;
            _status = "收到末端目标";
            return true;
        }

        public bool CanMoveTo(Vector3 target, out string reason)
        {
            if (!RobotSafety.TryValidateTarget(target, out reason)) return false;
            return TrySolve(target, out _, out reason);
        }

        public bool CanSetJointTargets(float[] targetsDeg, out string reason)
        {
            if (targetsDeg == null || targetsDeg.Length != JointNames.Length)
            {
                reason = $"SO-100 关节目标必须按 {string.Join(", ", JointNames)} 顺序提供 5 个角度。";
                return false;
            }
            for (int i = 0; i < targetsDeg.Length; i++)
            {
                if (!RobotSafety.IsFinite(targetsDeg[i]))
                {
                    reason = $"{JointNames[i]} 目标角度不是有限数值。";
                    return false;
                }
                if (targetsDeg[i] < JointMin[i] || targetsDeg[i] > JointMax[i])
                {
                    reason = $"{JointNames[i]} 目标超出限位 [{JointMin[i]:F1},{JointMax[i]:F1}] 度。";
                    return false;
                }
            }
            reason = null;
            return true;
        }

        public bool SetJointTargets(float[] targetsDeg, out string reason)
        {
            if (!CanSetJointTargets(targetsDeg, out reason)) return false;
            _targets = (float[])targetsDeg.Clone();
            _hasTarget = false;
            if (_targetMarker != null) _targetMarker.position = Tcp;
            _status = "收到关节目标";
            return true;
        }

        private bool TrySolve(Vector3 target, out float[] solution, out string reason)
        {
            solution = new float[JointNames.Length];
            Vector3 local = target - transform.position;
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float radialToTcp = Mathf.Sqrt(local.x * local.x + local.z * local.z);
            float radial = radialToTcp - ToolLength;
            // TCP is offset 10 mm below the wrist axis between the gripper fingers.
            float vertical = local.y - ShoulderHeight - TcpOffsetY;
            float distance = Mathf.Sqrt(radial * radial + vertical * vertical);
            float minReach = Mathf.Abs(UpperArmLength - ForearmLength) + 0.012f;
            float maxReach = UpperArmLength + ForearmLength - 0.012f;
            if (radial < 0.012f || distance < minReach || distance > maxReach)
            {
                reason = $"目标在 {Name} 的 SO-100 可达范围之外（平面距离 {distance:F2} 米）。";
                return false;
            }

            float phi = Mathf.Atan2(vertical, radial);
            float cosShoulder = Mathf.Clamp((UpperArmLength * UpperArmLength + distance * distance - ForearmLength * ForearmLength) /
                                            (2f * UpperArmLength * distance), -1f, 1f);
            float cosElbow = Mathf.Clamp((UpperArmLength * UpperArmLength + ForearmLength * ForearmLength - distance * distance) /
                                         (2f * UpperArmLength * ForearmLength), -1f, 1f);
            float shoulderMath = phi + Mathf.Acos(cosShoulder);
            float elbowInternal = Mathf.Acos(cosElbow);
            float elbowRelative = -(Mathf.PI - elbowInternal);

            solution[0] = yaw;
            solution[1] = -shoulderMath * Mathf.Rad2Deg;
            solution[2] = -elbowRelative * Mathf.Rad2Deg;
            solution[3] = -(solution[1] + solution[2]);
            solution[4] = _targets[4]; // Position-only IK preserves the current wrist roll.
            for (int i = 0; i < solution.Length; i++)
            {
                if (solution[i] < JointMin[i] || solution[i] > JointMax[i])
                {
                    reason = $"目标要求 {JointNames[i]} 超出关节限位。";
                    return false;
                }
            }
            reason = null;
            return true;
        }

        public void SetGripper(bool open)
        {
            _gripperTarget = open ? 1f : 0f;
            UpdateGripperVisual(_gripperTarget);
            if (open)
            {
                ReleaseHeldPayload();
                _status = "夹爪已打开";
            }
            else
            {
                TryGraspPayload();
                _status = string.IsNullOrEmpty(Holding) ? "夹爪已闭合" : $"已抓取 {Holding}";
            }
        }

        private void TryGraspPayload()
        {
            if (!string.IsNullOrEmpty(Holding)) return;
            ManipulablePayload best = null;
            float bestDistance = 0.10f;
            foreach (var item in ManipulablePayload.Active)
            {
                if (item == null || item.IsHeld) continue;
                float distance = Vector3.Distance(item.WorldPosition, _gripPoint.position);
                if (distance < bestDistance)
                {
                    best = item;
                    bestDistance = distance;
                }
            }
            if (best == null) return;
            best.AttachTo(_gripPoint);
            _heldPayload = best;
            Holding = best.PayloadName;
        }

        private void ReleaseHeldPayload()
        {
            if (_heldPayload != null) _heldPayload.Release();
            _heldPayload = null;
            Holding = "";
        }

        public void Home()
        {
            _targets = new float[JointNames.Length];
            _hasTarget = false;
            SetGripper(true);
            _status = "正在回零";
        }

        public void Stop()
        {
            _targets = (float[])_angles.Clone();
            _hasTarget = false;
            if (_targetMarker != null) _targetMarker.position = Tcp;
            _status = "已停止";
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            _status = paused ? "已暂停" : "待机";
        }

        public void Resume()
        {
            _paused = false;
            _status = "待机";
        }

        public static CalibrationSnapshot CreateCalibration()
        {
            var min = GetJointMins();
            var max = GetJointMaxes();
            var joints = new JointCalibration[JointNames.Length];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new JointCalibration { name = JointNames[i], minDeg = min[i], maxDeg = max[i] };
            return new CalibrationSnapshot { joints = joints };
        }
    }
}
