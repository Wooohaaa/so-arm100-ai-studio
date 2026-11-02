using UnityEngine;

namespace RobotArmStudio
{
    /// <summary>Boundary between the arm fleet and a physical or simulated robot driver.</summary>
    public interface IRobotArmDriver
    {
        int Index { get; }
        string Name { get; }
        Vector3 Tcp { get; }
        float[] JointAnglesDeg { get; }
        float GripperOpen { get; }
        bool IsMoving { get; }
        string Holding { get; }
        string Status { get; }

        bool CanMoveTo(Vector3 target, out string reason);
        bool MoveTo(Vector3 target, out string reason);
        bool CanSetJointTargets(float[] targetsDeg, out string reason);
        bool SetJointTargets(float[] targetsDeg, out string reason);
        void SetGripper(bool open);
        void Home();
        void Stop();
        void SetPaused(bool paused);
        void Resume();
    }
}
