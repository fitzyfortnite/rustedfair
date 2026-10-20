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

        [MenuItem("Rusted Fair/Capture Store Screenshots", true)]
        static bool CaptureValidate() => !StoreScreenshots.Running;
    }
}
