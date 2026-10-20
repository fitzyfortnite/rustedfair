#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RustedFair
{
    /// <summary>
    /// Editor-only (never in a build): renders the store screenshots from 6 preset viewpoints - carousel hall wide,
    /// arcade, ghost-train tunnel with the flashlight, Mister Grin close in a corridor with red eyes, the fuse box
    /// task and the menu exterior - at 1920x1080 (Google Play), 2868x1320 (iPhone 6.9") and 2752x2064 (iPad 13")
    /// into StoreAssets/Screenshots/&lt;size&gt;/. The HUD is drawn (its canvas temporarily renders through the
    /// camera); tips are hidden. Started from the editor menu while a night is running (Play mode).
    /// </summary>
    public class StoreScreenshots : MonoBehaviour
    {
        public static readonly Vector2Int[] Sizes = { new Vector2Int(1920, 1080), new Vector2Int(2868, 1320), new Vector2Int(2752, 2064) };
        public static bool Running { get; private set; }

        string outRoot;
        public System.Action<string> onFinished;

        public static void Begin(string outputRoot, System.Action<string> finished)
        {
            if (Running) return;
            var go = new GameObject("[RF Store Screenshots]");
            DontDestroyOnLoad(go);
            var s = go.AddComponent<StoreScreenshots>();
            s.outRoot = outputRoot;
            s.onFinished = finished;
            s.StartCoroutine(s.Run());
        }

        IEnumerator Run()
        {
            Running = true;
            int saved = 0;
            var log = new System.Text.StringBuilder();
            var gc = GameController.I;
            if (gc != null && gc.Player != null && gc.EditorBuilder != null)
            {
                // v1.0.2: shots taken during the wake-up came out black (closed eyelids, -1.5 EV, blur): wait for the
                // intro + briefing to finish (close the briefing with OK if it is open)
                float wait = 0f;
                if (!gc.EditorIntroDone) Debug.Log("[RF] Screenshots: waiting for the wake-up intro / briefing to finish...");
                while (gc != null && !gc.EditorIntroDone && wait < 90f) { wait += Time.unscaledDeltaTime; yield return null; }
                if (gc == null || gc.Player == null)
                {
                    Running = false;
                    onFinished?.Invoke("Night ended before the capture started.");
                    Destroy(gameObject);
                    yield break;
                }
                if (!gc.EditorIntroDone) log.AppendLine("warning: the intro/briefing did not finish within 90 s - shots may be dark");
                Time.timeScale = 1f;
                gc.EditorCaptureMode(true);
                var p = gc.Player;
                // readable shots: power on (restored afterwards), no surge/blackout, eyes open, +0.5 EV, no blur
                bool powerWas = Power.On;
                Power.Set(true);
                if (LightSystem.I != null) LightSystem.I.ClearEvents();
                gc.Hud.SetEyelids(1f);
                var fx = PostFX.I;
                float exposureWas = fx != null ? fx.exposure : 0f;
                if (fx != null) { fx.exposure = 0.5f; fx.blur = 0f; fx.red = 0f; }
                p.enabled = false; // the rig places the camera itself
                var tips = gc.Tips != null ? gc.Tips.gameObject : null;
                bool tipsWas = tips != null && tips.activeSelf;
                if (tips != null) tips.SetActive(false);
                bool flashWas = p.Flashlight != null && p.Flashlight.On;
                var fb = gc.EditorBuilder;
                var map = gc.Map;

                // 1 + 2: room wide shots
                yield return Shot("01_carousel_hall", RoomPose(map.Room(RoomId.CarouselHall)), p, gc.Hud.canvas, true, l => saved += l, log);
                yield return Shot("02_arcade", RoomPose(map.Room(RoomId.Arcade)), p, gc.Hud.canvas, true, l => saved += l, log);
                // 3: ghost-train tunnel, looking down the dark tunnel past the gate
                Pose tunnel = fb.doors.TryGetValue("tunnel_gate", out var gate) && gate != null
                    // v1.0.3: past the gate the first band of the tunnel runs west (-x) - looking north hit the brick wall
                    ? LookFrom(gate.transform.position + new Vector3(1.2f, 1.6f, 2.0f), gate.transform.position + new Vector3(-14f, 1.0f, 3.2f))
                    : RoomPose(map.Room(RoomId.GhostTrain));
                yield return Shot("03_ghost_train_tunnel", tunnel, p, gc.Hud.canvas, true, l => saved += l, log);
                // 4: Mister Grin close in a corridor, red eyes
                var grin = gc.EditorGrin;
                var hall = map.Room(RoomId.ServiceHall);
                Pose corridor = RoomPose(hall);
                if (grin != null)
                {
                    if (!grin.Active) { grin.Freeze(false); grin.Wake(); yield return new WaitForSeconds(3.6f); grin.Freeze(true); } // PlayerSafe stays on
                    Vector3 rp = corridor.position + corridor.rotation * Vector3.forward * 2.8f;
                    rp.y = 0f;
                    grin.ResetTo(rp);
                    grin.transform.rotation = Quaternion.LookRotation((corridor.position - rp).Flat(), Vector3.up);
                    grin.Rig.SetEyes(true);
                    corridor = LookFrom(corridor.position, rp + Vector3.up * 1.5f);
                }
                yield return Shot("04_mister_grin", corridor, p, gc.Hud.canvas, true, l => saved += l, log);
                // 5: task scene - the fuse box
                // 5 (v1.0.3): Brutus in the warehouse, flashlight on him (replaces the dull fuse-box shot)
                var brutus = gc.EditorRobot(RobotKind.Brutus);
                var wh = map.Room(RoomId.Warehouse);
                Pose bear = RoomPose(wh);
                if (brutus != null)
                {
                    if (!brutus.Active) { brutus.Freeze(false); brutus.Wake(); yield return new WaitForSeconds(3.6f); brutus.Freeze(true); }
                    Vector3 bp = bear.position + bear.rotation * Vector3.forward * 3.4f;
                    bp.y = 0f;
                    brutus.ResetTo(bp);
                    brutus.transform.rotation = Quaternion.LookRotation((bear.position - bp).Flat(), Vector3.up);
                    brutus.Rig.SetEyes(true);
                    bear = LookFrom(bear.position, bp + Vector3.up * 1.6f);
                }
                yield return Shot("05_brutus", bear, p, gc.Hud.canvas, true, l => saved += l, log);

                if (tips != null) tips.SetActive(tipsWas);
                if (p.Flashlight != null && p.Flashlight.On != flashWas) p.Flashlight.SetOn(flashWas);
                // camera + flashlight back on the player rig (the camera sits at the identity under its offset node)
                p.Cam.transform.localPosition = Vector3.zero;
                p.Cam.transform.localRotation = Quaternion.identity;
                if (p.Flashlight != null) p.Flashlight.SnapTo(p.Cam.transform);
                if (fx != null) fx.exposure = exposureWas;
                Power.Set(powerWas);
                p.enabled = true;
                gc.EditorCaptureMode(false);

                // 6: the menu exterior
                if (RFApp.I != null)
                {
                    RFApp.I.GoToMenu();
                    float t = 0f;
                    while ((RFApp.I.State != RFApp.AppState.Menu || FindFirstObjectByType<MenuController>() == null) && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
                    yield return new WaitForSeconds(3f);
                }
            }
            var menu = FindFirstObjectByType<MenuController>();
            if (menu != null && Camera.main != null)
            {
                Canvas ui = null;
                foreach (var c in menu.GetComponentsInChildren<Canvas>(true)) if (c.isRootCanvas) { ui = c; break; }
                yield return Capture("06_menu_exterior", Camera.main, ui, l => saved += l, log);
            }
            else log.AppendLine("menu: not found (start the capture during a night, or from the main menu for this shot only)");
            Running = false;
            onFinished?.Invoke("Saved " + saved + " screenshots to " + outRoot + "\n" + log);
            Destroy(gameObject);
        }

        IEnumerator Shot(string name, Pose pose, PlayerController p, Canvas hud, bool flashlight, System.Action<int> add, System.Text.StringBuilder log)
        {
            // a power surge / blackout (the intro's, or a random one) would black out the fixtures mid-capture
            float t = 0f;
            while (LightSystem.I != null && LightSystem.I.EventActive && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            p.Cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
            // the flashlight hangs off the camera's parent (the player body), not the camera: move it to the shot camera,
            // otherwise it keeps lighting the spot where the player stands
            if (p.Flashlight != null) p.Flashlight.SnapTo(p.Cam.transform);
            if (p.Flashlight != null && p.Flashlight.On != flashlight) p.Flashlight.SetOn(flashlight);
            if (PostFX.I != null) PostFX.I.blur = 0f;
            yield return new WaitForSeconds(0.6f); // lights / LOD / light culling settle
            t = 0f;
            while (LightSystem.I != null && LightSystem.I.EventActive && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            p.Cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (p.Flashlight != null) p.Flashlight.SnapTo(p.Cam.transform);
            yield return Capture(name, p.Cam, hud, add, log);
        }

        IEnumerator Capture(string name, Camera cam, Canvas ui, System.Action<int> add, System.Text.StringBuilder log)
        {
            var oldMode = ui != null ? ui.renderMode : RenderMode.ScreenSpaceOverlay;
            var oldCam = ui != null ? ui.worldCamera : null;
            float oldPlane = ui != null ? ui.planeDistance : 1f;
            foreach (var size in Sizes)
            {
                var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                cam.targetTexture = rt;
                if (ui != null)
                {
                    ui.renderMode = RenderMode.ScreenSpaceCamera;
                    ui.worldCamera = cam;
                    ui.planeDistance = cam.nearClipPlane + 0.1f;
                }
                // canvas scaler + layout pick up the new size; the bigger font sizes rebuild the dynamic font atlas, which
                // leaves garbled glyphs ("TASXS") for a few frames: wait them out
                for (int f = 0; f < 8; f++) yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                tex.Apply(false);
                RenderTexture.active = prev;
                cam.targetTexture = null;
                string dir = Path.Combine(outRoot, size.x + "x" + size.y);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
                rt.Release();
                Destroy(rt);
                add(1);
            }
            if (ui != null) { ui.renderMode = oldMode; ui.worldCamera = oldCam; ui.planeDistance = oldPlane; }
            log.AppendLine(name + ": ok");
        }

        // ------------------------------------------------------------------ viewpoints
        static Pose LookFrom(Vector3 from, Vector3 at) => new Pose(from, Quaternion.LookRotation(at - from, Vector3.up));

        /// <summary>A free corner of the room (inset 1.4 m, 2.3 m high) with a clear view of the room centre.</summary>
        static Pose RoomPose(RoomDef room)
        {
            if (room == null) return new Pose(Vector3.up * 1.7f, Quaternion.identity);
            float c = FairMap.Cell;
            Vector3 centre = room.Center + Vector3.up * 1.3f;
            var corners = new List<Vector3>
            {
                new Vector3(room.r.xMin * c + 1.4f, 2.3f, room.r.yMin * c + 1.4f), new Vector3(room.r.xMax * c - 1.4f, 2.3f, room.r.yMin * c + 1.4f),
                new Vector3(room.r.xMin * c + 1.4f, 2.3f, room.r.yMax * c - 1.4f), new Vector3(room.r.xMax * c - 1.4f, 2.3f, room.r.yMax * c - 1.4f)
            };
            foreach (var k in corners)
                if (!Physics.CheckSphere(k, 0.35f, RF.MaskWorld, QueryTriggerInteraction.Ignore) &&
                    !Physics.Linecast(k, centre, RF.MaskSight, QueryTriggerInteraction.Ignore))
                    return LookFrom(k, centre);
            return LookFrom(corners[0], centre);
        }

        /// <summary>1.7 m in front of a wall-mounted anchor (whichever side is open), eye height, looking at it.</summary>
        static Pose FacePose(Transform a, float dist)
        {
            Vector3 target = a.position;
            foreach (var dir in new[] { a.forward.Flat().normalized, -a.forward.Flat().normalized })
            {
                Vector3 from = target + dir * dist;
                from.y = 1.6f;
                if (!Physics.CheckSphere(from, 0.3f, RF.MaskWorld, QueryTriggerInteraction.Ignore))
                    return LookFrom(from, target);
            }
            return LookFrom(target + a.forward * dist + Vector3.up * 0.4f, target);
        }
    }
}
#endif
