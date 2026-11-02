using System;
using UnityEngine;

namespace RobotArmStudio
{
    [Serializable]
    public sealed class RobotActionPlan
    {
        public string summary;
        public RobotAction[] actions;
    }

    [Serializable]
    public sealed class RobotAction
    {
        public string type;
        public int arm;
        public float[] target;
        public bool open;
        public float waitSeconds;
    }

    [Serializable]
    public sealed class ApiCommand
    {
        public string action;
        public int arm;
        public float[] target;
        public float open;
    }

    [Serializable]
    public sealed class ApiControlRequest
    {
        public string action;
    }

    [Serializable]
    public sealed class RobotStateSnapshot
    {
        public string app = "Robot Arm Studio";
        public string timestamp;
        public bool paused;
        public bool emergencyStop;
        public int armCount;
        public string llmStatus;
        public ArmState[] arms;
        public PayloadState[] payloads;
    }

    [Serializable]
    public sealed class ArmState
    {
        public int index;
        public string name;
        public string driver;
        public int dof;
        public string status;
        public bool moving;
        public float[] tcp;
        public float[] jointsDeg;
        public float gripperOpen;
        public string holding;
    }

    [Serializable]
    public sealed class PayloadState
    {
        public string name;
        public float[] position;
        public bool held;
    }

    [Serializable]
    public sealed class CalibrationSnapshot
    {
        public string units = "meters/degrees";
        public float[] workspaceMin = { RobotSafety.MinX, RobotSafety.MinY, RobotSafety.MinZ };
        public float[] workspaceMax = { RobotSafety.MaxX, RobotSafety.MaxY, RobotSafety.MaxZ };
        public JointCalibration[] joints;
    }

    [Serializable]
    public sealed class JointCalibration
    {
        public string name;
        public float minDeg;
        public float maxDeg;
    }

    [Serializable]
    public sealed class ApiReply
    {
        public bool ok;
        public string message;
        public string error;
        public float staleSeconds;
    }

    public static class RobotSafety
    {
        public const float MinX = -0.48f;
        public const float MaxX = 0.48f;
        public const float MinY = 0.43f;
        public const float MaxY = 0.78f;
        public const float MinZ = -0.35f;
        public const float MaxZ = 0.55f;
        public const int MaxActionsPerPlan = 8;

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool TryValidateTarget(Vector3 target, out string reason)
        {
            if (!IsFinite(target.x) || !IsFinite(target.y) || !IsFinite(target.z))
            {
                reason = "目标包含无效数值。";
                return false;
            }

            if (target.x < MinX || target.x > MaxX || target.y < MinY || target.y > MaxY ||
                target.z < MinZ || target.z > MaxZ)
            {
                reason = $"目标超出模拟工作空间：X[{MinX:F2},{MaxX:F2}]、Y[{MinY:F2},{MaxY:F2}]、Z[{MinZ:F2},{MaxZ:F2}] 米。";
                return false;
            }

            reason = null;
            return true;
        }

        public static bool TryReadTarget(float[] values, out Vector3 target)
        {
            target = Vector3.zero;
            if (values == null || values.Length != 3) return false;
            if (!IsFinite(values[0]) || !IsFinite(values[1]) || !IsFinite(values[2])) return false;
            target = new Vector3(values[0], values[1], values[2]);
            return true;
        }

        public static float[] ToArray(Vector3 value)
        {
            return new[] { value.x, value.y, value.z };
        }
    }
}
