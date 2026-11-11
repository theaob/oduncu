using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oduncu.Game.Editor
{
    /// <summary>
    /// Creates the Skirmish scene the first time the project is opened, so no hand-authored
    /// scene file needs to live in the repository. Re-run from the Oduncu menu at any time.
    /// </summary>
    public static class SceneBootstrap
    {
        public const string ScenePath = "Assets/Game/Scenes/Skirmish.unity";

        [InitializeOnLoadMethod]
        private static void EnsureSceneExists()
        {
            if (File.Exists(ScenePath)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(ScenePath)) CreateMainScene();
            };
        }

        [MenuItem("Oduncu/Recreate Skirmish Scene")]
        public static void CreateMainScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // GameRoot builds the camera rig, ground, views and HUD at runtime.
            Camera cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.orthographic = true;

            var game = new GameObject("Game");
            game.AddComponent<SimRunner>();
            game.AddComponent<GameRoot>();
            game.AddComponent<DeterminismProbe>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Oduncu: created " + ScenePath);
        }
    }
}
