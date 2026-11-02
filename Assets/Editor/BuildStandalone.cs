using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RobotArmStudio.EditorTools
{
    public static class BuildStandalone
    {
        [MenuItem("Robot Arm Studio/Build Windows Player")]
        public static void BuildWindows()
        {
            InitialSceneCreator.EnsureStartupSceneNow();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDirectory = Path.Combine(projectRoot, "Build", "Windows");
            string outputPath = Path.Combine(outputDirectory, "RobotArmStudio.exe");
            Directory.CreateDirectory(outputDirectory);

            PlayerSettings.productName = "Robot Arm Studio";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/RobotArmStudio.unity" },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"Windows build failed: {report.summary.result} ({report.summary.totalErrors} errors).");

            Debug.Log($"[RobotArmStudio] Windows player created at {outputPath}");
        }
    }
}
