using System.IO;
using Oduncu.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oduncu.Game.Editor
{
    /// <summary>
    /// Creates the Main scene the first time the project is opened, so no hand-authored
    /// scene file needs to live in the repository. Re-run from the Oduncu menu at any time.
    /// </summary>
    public static class SceneBootstrap
    {
        public const string ScenePath = "Assets/Game/Scenes/Main.unity";

        [InitializeOnLoadMethod]
        private static void EnsureSceneExists()
        {
            if (File.Exists(ScenePath)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(ScenePath)) CreateMainScene();
            };
        }

        [MenuItem("Oduncu/Recreate Main Scene")]
        public static void CreateMainScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            float centre = MapGenerator.DefaultSize / 2f;

            Camera cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 14f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            cam.transform.rotation = Quaternion.Euler(50f, 45f, 0f);
            cam.transform.position = new Vector3(centre, 0f, centre) - cam.transform.forward * 80f;
            cam.backgroundColor = new Color(0.55f, 0.72f, 0.45f);
            cam.clearFlags = CameraClearFlags.SolidColor;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(centre, 0f, centre);
            ground.transform.localScale = new Vector3(MapGenerator.DefaultSize / 10f, 1f, MapGenerator.DefaultSize / 10f);
            var groundRenderer = ground.GetComponent<Renderer>();
            var groundMaterial = new Material(groundRenderer.sharedMaterial) { name = "Ground", color = new Color(0.45f, 0.62f, 0.35f) };
            groundRenderer.sharedMaterial = groundMaterial;

            var game = new GameObject("Game");
            game.AddComponent<SimRunner>();
            game.AddComponent<EntityPresenter>();
            var input = game.AddComponent<TouchInput>();
            input.Camera = cam;
            game.AddComponent<DeterminismProbe>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Oduncu: created " + ScenePath);
        }
    }
}
