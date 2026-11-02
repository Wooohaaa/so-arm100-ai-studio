using System.IO;
using RobotArmStudio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RobotArmStudio.EditorTools
{
    [InitializeOnLoad]
    internal static class InitialSceneCreator
    {
        private const string ScenePath = "Assets/Scenes/RobotArmStudio.unity";

        static InitialSceneCreator()
        {
            EditorApplication.delayCall += EnsureStartupSceneNow;
        }

        public static void EnsureStartupSceneNow()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string scenesDirectory = Path.Combine(Application.dataPath, "Scenes");
            if (!Directory.Exists(scenesDirectory)) Directory.CreateDirectory(scenesDirectory);
            string absoluteScenePath = Path.GetFullPath(ScenePath);

            if (!File.Exists(absoluteScenePath))
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("RobotArmStudio");
                root.AddComponent<RobotArmStudioBootstrap>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log("[RobotArmStudio] Created the procedural starter scene at " + ScenePath);
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }
    }
}
