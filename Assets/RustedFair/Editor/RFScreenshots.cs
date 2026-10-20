using System.IO;
using UnityEditor;
using UnityEngine;

namespace RustedFair.EditorTools
{
    /// <summary>Rusted Fair ▸ Capture Store Screenshots (Play mode, during a night).</summary>
    public static class RFScreenshots
    {
        [MenuItem("Rusted Fair/Capture Store Screenshots", priority = 52)]
        public static void Capture()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Rusted Fair", "Press Play, start a night (Play ▸ briefing ▸ OK) and run this menu again.\n" +
                    "The tool waits for the wake-up intro and briefing, switches the power and the flashlight on for the shots, renders 6 shots in 3 sizes and returns to the main menu for the last one.", "OK");
                return;
            }
            if (StoreScreenshots.Running) return;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "StoreAssets", "Screenshots"));
            StoreScreenshots.Begin(root, msg =>
            {
                Debug.Log("[RF] " + msg);
                EditorUtility.RevealInFinder(root);
            });
        }

        [MenuItem("Rusted Fair/Test Jumpscares (Play mode)", priority = 53)]
        public static void TestJumpscares()
        {
            if (!EditorApplication.isPlaying || GameController.I == null)
            {
                EditorUtility.DisplayDialog("Rusted Fair", "Press Play, start a night and run this menu again.", "OK");
                return;
            }
            JumpscareTest.Begin();
        }

        [MenuItem("Rusted Fair/Render Key Art (Play mode)", priority = 54)]
        public static void RenderKeyArt()
        {
            if (!EditorApplication.isPlaying || GameController.I == null)
            {
                EditorUtility.DisplayDialog("Rusted Fair", "Press Play, start a night and run this menu again (shots come from StoreAssets/KeyArt/keyart.json).", "OK");
                return;
            }
            KeyArtRenderer.Begin();
        }

        [MenuItem("Rusted Fair/Capture Remove Ads Screen (Play mode)", priority = 55)]
        public static void CaptureRemoveAds()
        {
            if (!EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Rusted Fair", "Press Play and run this menu again.", "OK"); return; }
            StoreReviewShot.Begin();
        }

        [MenuItem("Rusted Fair/Debug/Toggle Letter Spacing (Play mode)", priority = 90)]
        public static void ToggleLetterSpacing()
        {
            int n = 0;
            foreach (var ls in Object.FindObjectsByType<LetterSpacing>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { ls.enabled = !ls.enabled; n++; }
            Debug.Log("[RF] Letter spacing toggled on " + n + " texts");
        }

        [MenuItem("Rusted Fair/Debug/Toggle Text Shadows (Play mode)", priority = 91)]
        public static void ToggleShadows()
        {
            int n = 0;
            foreach (var sh in Object.FindObjectsByType<UnityEngine.UI.Shadow>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { sh.enabled = !sh.enabled; n++; }
            Debug.Log("[RF] Shadows toggled on " + n + " graphics");
        }

        [MenuItem("Rusted Fair/Capture Store Screenshots", true)]
        static bool CaptureValidate() => !StoreScreenshots.Running;
    }
}
