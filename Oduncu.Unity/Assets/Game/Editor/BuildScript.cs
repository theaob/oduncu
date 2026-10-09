using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Oduncu.Game.Editor
{
    /// <summary>
    /// Command-line entry points used by CI:
    ///   -executeMethod Oduncu.Game.Editor.BuildScript.BuildAndroid
    /// </summary>
    public static class BuildScript
    {
        private const string BundleId = "com.oduncu.game";

        [MenuItem("Oduncu/Build/Android (development)")]
        public static void BuildAndroidMenu() => BuildAndroid();

        public static void BuildAndroid()
        {
            if (!File.Exists(SceneBootstrap.ScenePath)) SceneBootstrap.CreateMainScene();
            ProjectSetup.Apply();

            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel28;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);

            string output = Environment.GetEnvironmentVariable("ODUNCU_BUILD_PATH");
            if (string.IsNullOrEmpty(output)) output = "Build/Android/oduncu.apk";
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { SceneBootstrap.ScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception("Android build failed: " + report.summary.result + " (" + report.summary.totalErrors + " errors)");
            }
            Debug.Log("Android build written to " + output + " (" + report.summary.totalSize / (1024 * 1024) + " MB)");
        }
    }
}
