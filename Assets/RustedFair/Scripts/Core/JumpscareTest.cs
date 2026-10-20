#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

namespace RustedFair
{
    /// <summary>
    /// Editor-only: "Rusted Fair ▸ Test Jumpscares" runs each robot's catch in turn during a night and saves Game-view
    /// frames (HUD included) to Logs/jumpscare_&lt;robot&gt;_&lt;n&gt;.png, then respawns the player without costing a life.
    /// </summary>
    public class JumpscareTest : MonoBehaviour
    {
        public static bool Running { get; private set; }

        public static void Begin()
        {
            if (Running) return;
            var go = new GameObject("[RF Jumpscare Test]");
            DontDestroyOnLoad(go);
            go.AddComponent<JumpscareTest>().StartCoroutine(go.GetComponent<JumpscareTest>().Run());
        }

        static readonly float[] ShortTimes = { 0.12f, 0.5f, 1.2f };
        static readonly float[] GrabTimes = { 0.1f, 0.45f, 1.0f, 1.5f, 1.78f };

        IEnumerator Run()
        {
            Running = true;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(dir);
            var gc = GameController.I;
            float wait = 0f;
            while (gc != null && !gc.EditorIntroDone && wait < 90f) { wait += Time.unscaledDeltaTime; yield return null; }
            int saved = 0;
            if (gc != null && gc.Player != null)
            {
                foreach (var kind in new[] { RobotKind.Grin, RobotKind.Rattle, RobotKind.Brutus })
                {
                    var robot = gc.EditorRobot(kind);
                    if (robot == null) { Debug.LogWarning("[RF] Jumpscare test: no " + kind); continue; }
                    while (gc.EditorCatching) yield return null;
                    yield return new WaitForSeconds(0.5f);
                    if (gc.Player.Flashlight != null && !gc.Player.Flashlight.On) gc.Player.Flashlight.SetOn(true);
                    float t0 = Time.time;
                    gc.EditorForceCatch(robot);
                    var times = kind == RobotKind.Brutus ? GrabTimes : ShortTimes;
                    for (int i = 0; i < times.Length; i++)
                    {
                        while (Time.time - t0 < times[i]) yield return null;
                        yield return new WaitForEndOfFrame();
                        var tex = ScreenCapture.CaptureScreenshotAsTexture();
                        File.WriteAllBytes(Path.Combine(dir, "jumpscare_" + kind.ToString().ToLowerInvariant() + "_" + i + ".png"), tex.EncodeToPNG());
                        Destroy(tex);
                        saved++;
                    }
                    yield return new WaitForSeconds(3.2f); // sequence + AfterCatch -> death panel
                    gc.EditorRecover();
                    yield return new WaitForSeconds(1f);
                }
            }
            Debug.Log("[RF] Jumpscare test: saved " + saved + " frame(s) to " + dir);
            Running = false;
            Destroy(gameObject);
        }
    }
}
#endif
