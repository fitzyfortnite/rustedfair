#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RustedFair
{
    /// <summary>
    /// Editor-only (never in a build): "Rusted Fair ▸ Render Key Art" poses the robots, adds temporary lights and renders
    /// marketing shots during a night (Play mode). Everything comes from StoreAssets/KeyArt/keyart.json, so a shot can be
    /// re-framed and re-rendered without recompiling. Output: StoreAssets/KeyArt/&lt;shot&gt;.png; a "matte" shot renders only
    /// the listed robots twice (black and white background, no post / fog) as &lt;shot&gt;_k.png / &lt;shot&gt;_w.png for an alpha
    /// cut-out. A log goes to Logs/keyart_done.txt.
    /// </summary>
    [DefaultExecutionOrder(10000)] // after the robots' own Update / LateUpdate: the configured pose wins
    public class KeyArtRenderer : MonoBehaviour
    {
        [Serializable] public class JointSet { public string name; public float[] euler; public bool mul; }
        [Serializable] public class RobotSet { public string kind; public float[] pos; public float yaw; public float scare; public bool eyes = true; public JointSet[] joints; }
        [Serializable] public class LightSet { public string type = "Spot"; public float[] pos; public float[] target; public float[] color; public float intensity = 50f; public float range = 20f; public float angle = 50f; public bool shadows; }
        [Serializable] public class ShotSet { public string name; public float[] pos; public float[] target; public float fov = 40f; public int w = 1920; public int h = 1080; public bool matte; public string[] robots; public string[] hide; public float exposure = -99f; }
        [Serializable] public class MenuSet { public RobotSet[] robots; public LightSet[] lights; public ShotSet[] shots; }
        [Serializable] public class Config { public float exposure = 0.3f; public bool power = true; public bool fog = true; public float settle = 1.5f; public RobotSet[] robots; public LightSet[] lights; public ShotSet[] shots; public MenuSet menu; }

        public static bool Running { get; private set; }
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static void Begin()
        {
            if (Running) return;
            var go = new GameObject("[RF Key Art]");
            DontDestroyOnLoad(go); // survives the trip to the menu scene
            go.AddComponent<KeyArtRenderer>();
        }

        void Start() => StartCoroutine(Run());

        static Vector3 V(float[] a, Vector3 d = default) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : d;

        readonly List<GameObject> temp = new List<GameObject>();
        readonly List<(RobotRig rig, RobotSet set)> posed = new List<(RobotRig, RobotSet)>();

        IEnumerator Run()
        {
            Running = true;
            var log = new StringBuilder();
            string outDir = Path.Combine(Root, "StoreAssets", "KeyArt");
            Directory.CreateDirectory(outDir);
            string cfgPath = Path.Combine(outDir, "keyart.json");
            Config cfg = null;
            try { cfg = JsonUtility.FromJson<Config>(File.ReadAllText(cfgPath)); }
            catch (Exception e) { log.AppendLine("config error: " + e.Message); }
            var gc = GameController.I;
            if (cfg == null || gc == null || gc.Player == null) { Finish(log.AppendLine("no config or no night running")); yield break; }

            float wait = 0f;
            while (!gc.EditorIntroDone && wait < 90f) { wait += Time.unscaledDeltaTime; yield return null; }
            Time.timeScale = 1f;
            gc.EditorCaptureMode(true);
            var p = gc.Player;
            if (cfg.power) Power.Set(true);
            if (LightSystem.I != null) LightSystem.I.ClearEvents();
            gc.Hud.SetEyelids(1f);
            var fx = PostFX.I;
            if (fx != null) { fx.exposure = cfg.exposure; fx.blur = 0f; fx.red = 0f; fx.fear = 0f; }
            if (p.Flashlight != null && p.Flashlight.On) p.Flashlight.SetOn(false);
            p.enabled = false;
            RobotAI.GlowScale = 1f;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp0) urp0.renderScale = 1f;

            // robots: wake (eyes on, standing), then freeze where the config puts them
            if (cfg.robots != null)
                foreach (var rs in cfg.robots)
                {
                    if (!Enum.TryParse(rs.kind, out RobotKind k)) { log.AppendLine("unknown robot " + rs.kind); continue; }
                    var r = gc.EditorRobot(k);
                    if (r == null) { log.AppendLine("robot not in this night: " + rs.kind); continue; }
                    if (!r.Active) { r.Freeze(false); r.Wake(); yield return new WaitForSeconds(3.6f); }
                    r.Freeze(true);
                    r.ResetTo(V(rs.pos));
                    r.transform.rotation = Quaternion.Euler(0f, rs.yaw, 0f);
                    r.Rig.SetEyes(rs.eyes);
                    posed.Add((r.Rig, rs));
                }
            AddLights(cfg.lights);
            yield return new WaitForSeconds(cfg.settle);

            var cam = p.Cam;
            var camData = cam.GetComponent<UniversalAdditionalCameraData>();
            foreach (var shot in cfg.shots ?? new ShotSet[0])
            {
                Pose(); // the frozen robots re-run their idle pose every Update
                cam.transform.SetPositionAndRotation(V(shot.pos), Quaternion.LookRotation(V(shot.target) - V(shot.pos), Vector3.up));
                float fovWas = cam.fieldOfView;
                cam.fieldOfView = shot.fov;
                if (fx != null) fx.exposure = shot.exposure > -50f ? shot.exposure : cfg.exposure;
                var hidden = SetHidden(shot.hide, true);
                yield return new WaitForSeconds(0.3f);
                if (!shot.matte)
                {
                    yield return Save(cam, shot.w, shot.h, Path.Combine(outDir, shot.name + ".png"));
                    log.AppendLine(shot.name + ": ok");
                }
                else
                {
                    // only the listed robots, on a matte layer, no post / fog: black and white backgrounds -> alpha
                    var layers = new List<(GameObject, int)>();
                    foreach (var (rig, set) in posed)
                        if (shot.robots == null || Array.IndexOf(shot.robots, set.kind) >= 0)
                        {
                            var root = rig.GetComponentInParent<RobotAI>() != null ? rig.GetComponentInParent<RobotAI>().transform : rig.transform;
                            foreach (var t in root.GetComponentsInChildren<Transform>(true)) { layers.Add((t.gameObject, t.gameObject.layer)); t.gameObject.layer = 30; }
                        }
                    var flags = cam.clearFlags; var bg = cam.backgroundColor; int mask = cam.cullingMask;
                    bool post = camData != null && camData.renderPostProcessing;
                    bool fog = RenderSettings.fog;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.cullingMask = 1 << 30;
                    if (camData != null) camData.renderPostProcessing = false;
                    RenderSettings.fog = false;
                    cam.backgroundColor = Color.black;
                    yield return Save(cam, shot.w, shot.h, Path.Combine(outDir, shot.name + "_k.png"));
                    cam.backgroundColor = Color.white;
                    yield return Save(cam, shot.w, shot.h, Path.Combine(outDir, shot.name + "_w.png"));
                    cam.clearFlags = flags; cam.backgroundColor = bg; cam.cullingMask = mask;
                    if (camData != null) camData.renderPostProcessing = post;
                    RenderSettings.fog = fog;
                    foreach (var (go, l) in layers) go.layer = l;
                    log.AppendLine(shot.name + ": matte ok");
                }
                SetHidden(hidden, false);
                cam.fieldOfView = fovWas;
            }
            foreach (var go in temp) if (go != null) Destroy(go);
            temp.Clear();
            if (cfg.menu != null && cfg.menu.shots != null && cfg.menu.shots.Length > 0) yield return MenuShots(cfg.menu, outDir, log);
            foreach (var go in temp) if (go != null) Destroy(go);
            Finish(log);
        }

        void AddLights(LightSet[] list)
        {
            if (list == null) return;
            foreach (var ls in list)
            {
                var go = new GameObject("KeyArtLight");
                temp.Add(go);
                var l = go.AddComponent<Light>();
                l.type = ls.type == "Point" ? LightType.Point : LightType.Spot;
                go.transform.position = V(ls.pos);
                if (ls.target != null) go.transform.rotation = Quaternion.LookRotation(V(ls.target) - go.transform.position, Vector3.up);
                var c = ls.color != null && ls.color.Length >= 3 ? new Color(ls.color[0], ls.color[1], ls.color[2]) : Color.white;
                l.color = c; l.intensity = ls.intensity; l.range = ls.range;
                l.spotAngle = ls.angle; l.innerSpotAngle = ls.angle * 0.55f;
                l.shadows = ls.shadows ? LightShadows.Soft : LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
            }
        }

        /// <summary>Menu scene (the rainy exterior): stand-alone rigs (no AI) in front of the menu camera.</summary>
        IEnumerator MenuShots(MenuSet m, string outDir, StringBuilder log)
        {
            if (RFApp.I == null) { log.AppendLine("menu: no RFApp"); yield break; }
            RFApp.I.GoToMenu();
            float t = 0f;
            while ((RFApp.I.State != RFApp.AppState.Menu || FindFirstObjectByType<MenuController>() == null || Camera.main == null) && t < 25f) { t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSeconds(3f);
            var cam = Camera.main;
            if (cam == null) { log.AppendLine("menu: no camera"); yield break; }
            posed.Clear();
            var rigs = new List<GameObject>();
            if (m.robots != null)
                foreach (var rs in m.robots)
                {
                    if (!Enum.TryParse(rs.kind, out RobotKind k)) continue;
                    var holder = new GameObject("KeyArtRig_" + rs.kind);
                    rigs.Add(holder);
                    holder.transform.SetPositionAndRotation(V(rs.pos), Quaternion.Euler(0f, rs.yaw, 0f));
                    var rig = RobotRig.Build(holder.transform, k);
                    rig.dormant = 0f; // standing, not the powered-down slump
                    rig.SetEyes(rs.eyes);
                    posed.Add((rig, rs));
                }
            menuMode = true;
            AddLights(m.lights);
            foreach (var shot in m.shots ?? new ShotSet[0])
            {
                menuCam = cam;
                menuPose = new Pose(V(shot.pos), Quaternion.LookRotation(V(shot.target) - V(shot.pos), Vector3.up));
                float fovWas = cam.fieldOfView;
                cam.fieldOfView = shot.fov;
                yield return new WaitForSeconds(0.5f);
                yield return Save(cam, shot.w, shot.h, Path.Combine(outDir, shot.name + ".png"));
                cam.fieldOfView = fovWas;
                log.AppendLine(shot.name + ": menu ok");
            }
            menuCam = null;
            menuMode = false;
            foreach (var g in rigs) Destroy(g);
        }

        bool menuMode;
        Camera menuCam;
        Pose menuPose;

        string[] SetHidden(string[] kinds, bool hide)
        {
            if (kinds == null) return null;
            foreach (var (rig, set) in posed)
                if (Array.IndexOf(kinds, set.kind) >= 0)
                    foreach (var r in rig.GetComponentsInChildren<Renderer>(true)) r.enabled = !hide;
            return kinds;
        }

        /// <summary>Applies the configured scare blend and joint rotations on top of the idle pose.</summary>
        void Pose()
        {
            foreach (var (rig, set) in posed)
            {
                if (rig == null) continue;
                rig.jumpscare = set.scare;
                if (set.joints == null) continue;
                foreach (var j in set.joints)
                {
                    var f = typeof(RobotRig).GetField(j.name);
                    var t = f != null ? f.GetValue(rig) as Transform : null;
                    if (t == null || j.euler == null || j.euler.Length < 3) continue;
                    var q = Quaternion.Euler(j.euler[0], j.euler[1], j.euler[2]);
                    t.localRotation = j.mul ? t.localRotation * q : q;
                }
            }
        }

        void LateUpdate()
        {
            // full resolution: the game's dynamic resolution drops the render scale while these big renders run
            // (low fps), which came out as blocky mosaic bands
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.renderScale = 1f;
            if (menuMode) foreach (var (rig, set) in posed) if (rig != null) { rig.dormant = 0f; rig.Animate(0f, false, Time.deltaTime); }
            if (menuCam != null) menuCam.transform.SetPositionAndRotation(menuPose.position, menuPose.rotation);
            if (menuMode)
            {
                // the neon logo stutters on and off: keep it lit for the shot
                var mc = FindFirstObjectByType<MenuController>();
                if (mc != null)
                {
                    var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    if (typeof(MenuController).GetField("logo", bf)?.GetValue(mc) is Renderer lr) lr.enabled = true;
                    if (typeof(MenuController).GetField("logoLight", bf)?.GetValue(mc) is Light ll) ll.intensity = 3.6f;
                }
            }
            Pose();
        }

        IEnumerator Save(Camera cam, int w, int h, string path)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            var prevT = cam.targetTexture;
            cam.targetTexture = rt;
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            cam.targetTexture = prevT;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            rt.Release();
            Destroy(rt);
        }

        void Finish(StringBuilder log)
        {
            try { File.WriteAllText(Path.Combine(Root, "Logs", "keyart_done.txt"), DateTime.Now.ToString("s") + "\n" + log); } catch { }
            Debug.Log("[RF] Key art: " + log);
            GraphicsQuality.SetDynamicScale(GraphicsQuality.DynamicScale);
            Running = false;
            Destroy(gameObject);
        }
    }
}
#endif
