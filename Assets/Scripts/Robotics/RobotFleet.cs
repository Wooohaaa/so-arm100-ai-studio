using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotArmStudio
{
    /// <summary>Routes validated application commands to one or more arm drivers.</summary>
    public sealed class RobotFleet : MonoBehaviour
    {
        private readonly List<IRobotArmDriver> _arms = new List<IRobotArmDriver>();
        private bool _paused;
        private bool _emergencyStop;
        private float _timeScaleBeforePause = 1f;

        public int ArmCount => _arms.Count;
        public bool IsPaused => _paused;
        public bool EmergencyStopped => _emergencyStop;
        public IReadOnlyList<IRobotArmDriver> Arms => _arms;

        public void Configure(int requestedArmCount, Material metal, Material jointMaterial, Material accent)
        {
            int armCount = Mathf.Clamp(requestedArmCount, 1, 4);
            _arms.Clear();
            for (int i = 0; i < armCount; i++)
            {
                float x = (i - (armCount - 1) * 0.5f) * 0.52f;
                var root = new GameObject($"Arm_{i + 1}");
                root.transform.SetParent(transform, false);
                var arm = root.AddComponent<SimulatedRobotArm>();
                arm.Configure(i, new Vector3(x, 0.42f, 0.05f), metal, jointMaterial, accent);
                _arms.Add(arm);
            }
        }

        public bool TryGetArm(int index, out IRobotArmDriver arm)
        {
            if (index < 0 || index >= _arms.Count)
            {
                arm = null;
                return false;
            }
            arm = _arms[index];
            return arm != null;
        }

        public bool Dispatch(ApiCommand command, out string reason)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.action))
            {
                reason = "命令缺少 action。";
                return false;
            }

            string action = command.action.Trim();
            if (string.Equals(action, "Stop", StringComparison.OrdinalIgnoreCase))
            {
                StopAll();
                reason = "急停已触发。";
                return true;
            }
            if (string.Equals(action, "Resume", StringComparison.OrdinalIgnoreCase))
            {
                ResumeAll();
                reason = "仿真已恢复。";
                return true;
            }

            if (_paused || _emergencyStop)
            {
                reason = _emergencyStop ? "急停状态中，请先恢复。" : "仿真已暂停。";
                return false;
            }

            if (!TryGetArm(command.arm, out IRobotArmDriver arm))
            {
                reason = $"机械臂编号 {command.arm} 不存在。";
                return false;
            }

            if (string.Equals(action, "MoveTcp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "MoveTo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "MoveTip", StringComparison.OrdinalIgnoreCase))
            {
                if (!RobotSafety.TryReadTarget(command.target, out Vector3 target))
                {
                    reason = "target 必须是三个有限数值组成的数组。";
                    return false;
                }
                if (!arm.MoveTo(target, out reason)) return false;
                reason = $"{arm.Name} 移动目标已接受。";
                return true;
            }

            if (string.Equals(action, "SetJoints", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "MoveJoints", StringComparison.OrdinalIgnoreCase))
            {
                if (!arm.SetJointTargets(command.target, out reason)) return false;
                reason = $"{arm.Name} 关节目标已接受。";
                return true;
            }

            if (string.Equals(action, "Gripper", StringComparison.OrdinalIgnoreCase))
            {
                if (!RobotSafety.IsFinite(command.open) || command.open < 0f || command.open > 1f)
                {
                    reason = "open 必须是 0 到 1 之间的有限数值。";
                    return false;
                }
                arm.SetGripper(command.open >= 0.5f);
                reason = command.open >= 0.5f ? "夹爪已打开。" : "夹爪已闭合。";
                return true;
            }

            if (string.Equals(action, "Home", StringComparison.OrdinalIgnoreCase))
            {
                arm.Home();
                reason = "已发送回零动作。";
                return true;
            }

            reason = $"不支持的 action：{action}。";
            return false;
        }

        public bool ExecuteAiAction(RobotAction action, out string reason)
        {
            if (action == null)
            {
                reason = "动作为空。";
                return false;
            }

            if (string.Equals(action.type, "move", StringComparison.OrdinalIgnoreCase))
            {
                return Dispatch(new ApiCommand { action = "MoveTcp", arm = action.arm, target = action.target }, out reason);
            }
            if (string.Equals(action.type, "joints", StringComparison.OrdinalIgnoreCase))
            {
                return Dispatch(new ApiCommand { action = "SetJoints", arm = action.arm, target = action.target }, out reason);
            }
            if (string.Equals(action.type, "gripper", StringComparison.OrdinalIgnoreCase))
            {
                return Dispatch(new ApiCommand { action = "Gripper", arm = action.arm, open = action.open ? 1f : 0f }, out reason);
            }
            if (string.Equals(action.type, "home", StringComparison.OrdinalIgnoreCase))
            {
                return Dispatch(new ApiCommand { action = "Home", arm = action.arm }, out reason);
            }
            if (string.Equals(action.type, "stop", StringComparison.OrdinalIgnoreCase))
            {
                StopAll();
                reason = "急停已触发。";
                return true;
            }

            reason = $"AI 动作类型不受支持：{action.type}。";
            return false;
        }

        public bool ValidateAiPlan(RobotActionPlan plan, out string reason)
        {
            reason = null;
            if (plan == null || plan.actions == null || plan.actions.Length > RobotSafety.MaxActionsPerPlan)
            {
                reason = "AI 计划为空或动作数量超过限制。";
                return false;
            }

            for (int i = 0; i < plan.actions.Length; i++)
            {
                RobotAction action = plan.actions[i];
                if (action == null || string.IsNullOrWhiteSpace(action.type))
                {
                    reason = $"第 {i + 1} 个动作缺少类型。";
                    return false;
                }
                if (action.type == "stop") continue;
                if (!TryGetArm(action.arm, out IRobotArmDriver arm))
                {
                    reason = $"第 {i + 1} 个动作引用了不存在的机械臂。";
                    return false;
                }

                if (action.type == "move")
                {
                    if (!RobotSafety.TryReadTarget(action.target, out Vector3 target) || !arm.CanMoveTo(target, out reason))
                    {
                        if (string.IsNullOrEmpty(reason)) reason = $"第 {i + 1} 个动作的目标格式无效。";
                        return false;
                    }
                }
                else if (action.type == "joints")
                {
                    if (!arm.CanSetJointTargets(action.target, out reason))
                    {
                        if (string.IsNullOrEmpty(reason)) reason = $"第 {i + 1} 个动作的关节目标格式无效。";
                        return false;
                    }
                }
                else if (action.type == "wait")
                {
                    if (!RobotSafety.IsFinite(action.waitSeconds) || action.waitSeconds < 0f || action.waitSeconds > 10f)
                    {
                        reason = $"第 {i + 1} 个等待时间必须在 0 到 10 秒之间。";
                        return false;
                    }
                }
                else if (action.type != "gripper" && action.type != "home")
                {
                    reason = $"第 {i + 1} 个动作类型不受支持：{action.type}。";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public void Pause()
        {
            if (!_paused)
            {
                _timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            _paused = true;
            foreach (var arm in _arms) arm?.SetPaused(true);
        }

        public void ResumeAll()
        {
            if (_paused) Time.timeScale = _timeScaleBeforePause;
            _paused = false;
            _emergencyStop = false;
            foreach (var arm in _arms) arm?.Resume();
        }

        public void StopAll()
        {
            _emergencyStop = true;
            foreach (var arm in _arms) arm?.Stop();
        }

        public RobotStateSnapshot CreateSnapshot(string llmStatus)
        {
            var armStates = new ArmState[_arms.Count];
            for (int i = 0; i < _arms.Count; i++)
            {
                var arm = _arms[i];
                float[] joints = arm.JointAnglesDeg;
                armStates[i] = new ArmState
                {
                    index = arm.Index,
                    name = arm.Name,
                    driver = "Unity SO-100 kinematic simulation",
                    dof = joints.Length,
                    status = arm.Status,
                    moving = arm.IsMoving,
                    tcp = RobotSafety.ToArray(arm.Tcp),
                    jointsDeg = joints,
                    gripperOpen = arm.GripperOpen,
                    holding = arm.Holding
                };
            }

            return new RobotStateSnapshot
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                paused = _paused,
                emergencyStop = _emergencyStop,
                armCount = armStates.Length,
                llmStatus = llmStatus ?? "未连接",
                arms = armStates
            };
        }

        public string DescribePayloads()
        {
            var builder = new StringBuilder();
            foreach (var item in ManipulablePayload.Active)
            {
                if (item == null) continue;
                if (builder.Length > 0) builder.Append("\n");
                builder.Append(item.PayloadName).Append("：");
                if (item.IsHeld) builder.Append("已被机械臂抓取");
                else
                {
                    Vector3 p = item.WorldPosition;
                    builder.Append("位置 (").Append(p.x.ToString("F2")).Append(", ")
                        .Append(p.y.ToString("F2")).Append(", ").Append(p.z.ToString("F2")).Append(") 米");
                }
            }
            return builder.Length == 0 ? "没有可抓取物体。" : builder.ToString();
        }

        public CalibrationSnapshot CreateCalibration()
        {
            return SimulatedRobotArm.CreateCalibration();
        }

        private void OnDestroy()
        {
            if (_paused) Time.timeScale = _timeScaleBeforePause;
        }
    }
}
