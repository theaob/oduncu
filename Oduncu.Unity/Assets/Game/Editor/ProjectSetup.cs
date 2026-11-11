using UnityEditor;
using UnityEngine;

namespace Oduncu.Game.Editor
{
    /// <summary>
    /// Project settings the game depends on, applied in code because ProjectSettings.asset is
    /// not in the repository: landscape only (design section 7), and both input backends on,
    /// so the Input System drives gestures while UI Toolkit keeps its default event handling.
    /// </summary>
    public static class ProjectSetup
    {
        /// <summary>ProjectSettings' activeInputHandler: 0 old Input Manager, 1 Input System, 2 both.</summary>
        private const int BothInputHandlers = 2;

        [InitializeOnLoadMethod]
        private static void ApplyOnLoad()
        {
            EditorApplication.delayCall += () => Apply();
        }

        [MenuItem("Oduncu/Apply Project Settings")]
        public static void ApplyMenu() => Apply();

        /// <summary>Returns true when the input setting changed (the editor needs a restart to pick it up).</summary>
        public static bool Apply()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return false;
            var settings = new SerializedObject(assets[0]);
            SerializedProperty input = settings.FindProperty("activeInputHandler");
            if (input == null || input.intValue == BothInputHandlers) return false;
            input.intValue = BothInputHandlers;
            settings.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("Oduncu: enabled both input handlers (Input System and Input Manager). Restart the editor for play mode to use them.");
            return true;
        }
    }
}
