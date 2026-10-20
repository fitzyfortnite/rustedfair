using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace RustedFair
{
    /// <summary>
    /// Main menu: the fairground's exterior at night in a thunderstorm (rain, lightning, slowly turning
    /// ferris wheel, broken neon sign), a slow mysterious theme, and a clean horror-styled UI.
    /// </summary>
    public class MenuController : MonoBehaviour
    {
        Camera cam;
        Light moon;
        Transform wheel, camRig;
        Renderer logo;
        Light logoLight;
        Material skyMat;
        Renderer skyQuad;
        readonly List<Transform> clouds = new List<Transform>();
        float nextLightning = 4f;
        float flash;
        Canvas canvas;
        RectTransform mainList;
        bool starting;
        Cubemap reflection;
        Vector3 camBase;
        System.Action entitlementHandler;

        void Start()
        {
            BuildWorld();
            BuildUI();
            if (AudioManager.I != null)
            {
                AudioManager.I.SetLayer("menu", "mus_menu", 0.85f, 3f, true);
                AudioManager.I.SetLayer("rainOut", "amb_rain_ext", 0.55f, 2f, false);
            }
        }

        void OnDestroy()
        {
            if (entitlementHandler != null) IAPManager.OnEntitlementChanged -= entitlementHandler;
            if (skyMat != null) Destroy(skyMat);
            if (reflection != null) Destroy(reflection);
        }

        // ================================================================== world
        void BuildWorld()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.018f;
            RenderSettings.fogColor = new Color(0.03f, 0.035f, 0.05f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.075f, 0.1f);
            RenderSettings.skybox = null;
            reflection = new Cubemap(8, TextureFormat.RGBA32, false);
            var px = new Color[64];
            for (int i = 0; i < 64; i++) px[i] = new Color(0.05f, 0.06f, 0.09f);
            for (int f = 0; f < 6; f++) reflection.SetPixels(px, (CubemapFace)f);
            reflection.Apply();
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = reflection;

            var lights = gameObject.AddComponent<LightSystem>();
            var wm = new WorldMesh();
            var root = transform;

            // camera
            camRig = new GameObject("CamRig").transform;
            camRig.SetParent(root, false);
            camRig.position = new Vector3(0, 1.75f, -9f);
            var camGo = new GameObject("MenuCamera");
            camGo.transform.SetParent(camRig, false);
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 58f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 220f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.015f, 0.018f, 0.03f);
            var urp = cam.GetUniversalAdditionalCameraData();
            urp.renderPostProcessing = true;
            urp.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var listener = camGo.AddComponent<AudioListener>();
            if (AudioManager.I != null) AudioManager.I.RegisterListener(listener);
            camGo.transform.LookAt(new Vector3(0, 5.5f, 30f));
            camBase = camRig.position;
            lights.SetViewer(camGo.transform);
            PostFX.Create(root, true);

            // moon / lightning light
            moon = new GameObject("Moon").AddComponent<Light>();
            moon.transform.SetParent(root, false);
            moon.type = LightType.Directional;
            moon.transform.rotation = Quaternion.Euler(35f, -30f, 0);
            moon.color = new Color(0.55f, 0.62f, 0.85f);
            moon.intensity = 0.12f;
            moon.shadows = LightShadows.None;

            // sky backdrop
            skyMat = new Material(MatLib.Get("sky_flash"));
            var sky = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(sky.GetComponent<Collider>());
            sky.transform.SetParent(root, false);
            sky.transform.position = new Vector3(0, 40f, 160f);
            sky.transform.localScale = new Vector3(520f, 220f, 1f);
            skyQuad = sky.GetComponent<Renderer>();
            skyQuad.sharedMaterial = skyMat;
            skyQuad.shadowCastingMode = ShadowCastingMode.Off;
            for (int i = 0; i < 3; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(root, false);
                c.transform.position = new Vector3(-120f + i * 120f, 50f + i * 6f, 150f - i * 10f);
                c.transform.localScale = new Vector3(260f, 110f, 1f);
                c.GetComponent<Renderer>().sharedMaterial = MatLib.Get("fx_cloud");
                c.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                clouds.Add(c.transform);
            }

            // ground
            wm.Quad("ground_wet", new Vector3(0, 0, 40), Vector3.up, Vector3.forward, 200f, 140f, false);
            // facade
            wm.Box("wall_brick", new Vector3(-18f, 7f, 30.5f), new Vector3(28f, 14f, 1f));
            wm.Box("wall_brick", new Vector3(18f, 7f, 30.5f), new Vector3(28f, 14f, 1f));
            wm.Box("wall_brick", new Vector3(0, 11f, 30.5f), new Vector3(8f, 6f, 1f));
            wm.Box("metal_rust", new Vector3(0, 14.4f, 30.2f), new Vector3(64f, 0.8f, 1.4f), 0, false);
            for (int i = -3; i <= 3; i++) if (i != 0) wm.Box("wall_brick", new Vector3(i * 9f, 7f, 29.7f), new Vector3(1.2f, 14f, 0.8f));
            // entrance arch with striped canopy and closed shutter
            wm.Box("metal_rust", new Vector3(-4.3f, 4f, 29.6f), new Vector3(0.6f, 8f, 0.8f));
            wm.Box("metal_rust", new Vector3(4.3f, 4f, 29.6f), new Vector3(0.6f, 8f, 0.8f));
            var mb = wm.For("wall_metal", new Vector3(0, 0, 30), true);
            for (float y = 0.1f; y < 7.8f; y += 0.2f) mb.Box(new Vector3(0, y, 30f), new Vector3(8f, 0.18f, 0.08f), 1.5f);
            wm.Box("yellow_paint", new Vector3(0, 0.1f, 29.9f), new Vector3(8f, 0.2f, 0.05f), 0, false);
            for (int i = 0; i < 10; i++)
                wm.For("tent", new Vector3(0, 8f, 29f), false).BoxRot(new Vector3(-4.5f + i, 8.3f, 28.7f), new Vector3(1f, 0.05f, 2.2f), Quaternion.Euler(-20f, 0, 0), 1f);
            // posters on the facade
            for (int i = 0; i < 4; i++)
            {
                var pp = new Vector3(-14f + (i < 2 ? i * 5f : 18f + (i - 2) * 5f), 3.2f, 29.97f);
                wm.For("poster_" + (i + 1), pp, false).QuadUV(pp, Vector3.back, Vector3.up, 2f, 3f, new Rect(0, 0, 1, 1));
            }
            // ticket booths outside
            for (int s = -1; s <= 1; s += 2)
            {
                var bp = new Vector3(s * 8f, 0, 24f);
                wm.Box("wood_painted", bp + Vector3.up * 1.3f, new Vector3(2.4f, 2.6f, 2f));
                wm.Box("tent", bp + Vector3.up * 2.9f, new Vector3(2.8f, 0.1f, 2.4f));
                wm.Box(s < 0 ? "neon_red" : "neon_cyan", bp + new Vector3(0, 2.5f, -1.02f), new Vector3(1.8f, 0.1f, 0.04f), 0, false);
                wm.QuadUV("crt_static", bp + new Vector3(0, 1.6f, -1.01f), Vector3.back, Vector3.up, 1.6f, 0.9f);
            }
            // fences
            for (float x = -60f; x < 60f; x += 2f)
            {
                if (Mathf.Abs(x) < 34f) continue;
                wm.For("fence", new Vector3(x, 1.5f, 24f), false).Quad(new Vector3(x + 1f, 1.5f, 24f), Vector3.back, Vector3.up, 2f, 3f, 2f);
            }
            // lamp posts
            foreach (var lp in new[] { new Vector3(-12f, 0, 12f), new Vector3(12f, 0, 12f), new Vector3(-26f, 0, 20f), new Vector3(26f, 0, 20f) })
            {
                wm.Cyl("black_matte", lp, 0.09f, 5f, 8);
                wm.Box("black_matte", lp + new Vector3(0, 5f, 0), new Vector3(0.5f, 0.15f, 0.5f));
                var bulb = RF.Prim(PrimitiveType.Sphere, root, "bulb_warm", lp + new Vector3(0, 4.8f, 0), Vector3.one * 0.3f, default, false);
                lights.Add(lp + Vector3.up * 4.6f, root, new Color(1f, 0.75f, 0.5f), 2.2f, 13f, LightGroup.Always, lp.x > 20f, bulb.GetComponent<Renderer>());
            }
            // the logo sign above the entrance
            var sign = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(sign.GetComponent<Collider>());
            sign.transform.SetParent(root, false);
            sign.transform.position = new Vector3(0, 12.2f, 29.9f);
            sign.transform.rotation = Quaternion.identity; // v1.0.3: a Unity quad already faces -z (the camera); the web's 180° (three.js planes face +z) showed its mirrored back
            sign.transform.localScale = new Vector3(16f, 4f, 1f);
            logo = sign.GetComponent<Renderer>();
            logo.sharedMaterial = MatLib.Get("neon_logo");
            logo.shadowCastingMode = ShadowCastingMode.Off;
            logoLight = new GameObject("LogoLight").AddComponent<Light>();
            logoLight.transform.SetParent(root, false);
            logoLight.transform.position = new Vector3(0, 11f, 27f);
            logoLight.type = LightType.Point;
            logoLight.color = new Color(1f, 0.25f, 0.12f);
            logoLight.range = 16f;
            logoLight.intensity = 3f;
            logoLight.shadows = LightShadows.None;
            // ferris wheel behind the building
            wheel = new GameObject("FerrisWheel").transform;
            wheel.SetParent(root, false);
            wheel.position = new Vector3(-26f, 24f, 60f);
            var wb = new MeshBuilder();
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f, a2 = (i + 1) / 16f * Mathf.PI * 2f;
                var p1 = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 18f;
                var p2 = new Vector3(Mathf.Cos(a2), Mathf.Sin(a2), 0) * 18f;
                wb.Tube(Vector3.zero, p1, 0.18f, 5, 2f);
                wb.Tube(p1, p2, 0.3f, 5, 2f);
                wb.Box(p1 + Vector3.down * 1.2f, new Vector3(1.6f, 1.4f, 1.4f), 1f);
            }
            var wgo = new GameObject("WheelMesh");
            wgo.transform.SetParent(wheel, false);
            wgo.AddComponent<MeshFilter>().sharedMesh = wb.ToMesh("Wheel");
            var wr = wgo.AddComponent<MeshRenderer>();
            wr.sharedMaterial = MatLib.Get("metal_rust");
            for (int i = 0; i < 16; i += 2)
            {
                float a = i / 16f * Mathf.PI * 2f;
                RF.Prim(PrimitiveType.Sphere, wheel, i % 4 == 0 ? "bulb_warm" : "neon_red", new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 18f, Vector3.one * 0.7f, default, false);
            }
            wm.Tube("metal_rust", new Vector3(-34f, 0, 60f), new Vector3(-26f, 24f, 60f), 0.6f, 8);
            wm.Tube("metal_rust", new Vector3(-18f, 0, 60f), new Vector3(-26f, 24f, 60f), 0.6f, 8);
            // circus tent tops behind
            wm.Cyl("tent", new Vector3(22f, 13f, 52f), 9f, 9f, 24, true, false, 0.3f);
            wm.Cyl("tent", new Vector3(34f, 11f, 70f), 7f, 7f, 24, true, false, 0.3f);
            // puddles near the camera
            for (int i = 0; i < 8; i++)
                wm.For("decal_water", Vector3.zero, false).QuadUV(new Vector3(Random.Range(-10f, 10f), 0.015f, Random.Range(-2f, 22f)), Vector3.up, Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward, Random.Range(1.5f, 3.5f), Random.Range(1.5f, 3f), new Rect(0, 0, 1, 1));
            wm.Build(root);

            // rain around the camera
            var rainRoot = new GameObject("RainRoot").transform;
            rainRoot.SetParent(camRig, false);
            rainRoot.localPosition = new Vector3(0, 9f, 10f);
            RainFX.Make(rainRoot, new Vector3(36f, 1f, 30f), 2200);
        }

        // ================================================================== UI
        void BuildUI()
        {
            canvas = UIKit.MakeCanvas("MenuUI", 50, transform);
            var safe = UIKit.SafeRoot(canvas.transform);
            var shade = UIKit.Img(canvas.transform, "Shade", "vignette", new Color(0, 0, 0, 0.7f));
            shade.transform.SetAsFirstSibling();
            UIKit.Stretch(shade.rectTransform, -300);
            if (shade.sprite == null) shade.color = new Color(0, 0, 0, 0.35f);

            // v1.1: one centred column (logo, tagline, Play, "Difficulty · Language", Settings, How to play, small extras).
            // UIFit scales the whole column down on short landscape phones (e.g. 1920x864 + notch) so nothing is clipped.
            mainList = UIKit.Rect(safe, "Main");
            UIKit.Place(mainList, 0.5f, 0.5f, Vector2.zero, new Vector2(1000, 900));
            mainList.gameObject.AddComponent<UIFit>().margin = 12f;
            UIKit.VList(mainList, 6f, TextAnchor.MiddleCenter);

            // v1.6: the drippy Creepster title image is replaced by "RUSTED FAIR" in Cinzel Black, red with a top-lit
            // gradient (#FF5A44 -> #B3150E -> #5E0805), wide tracking, soft red glow + drop shadow
            // v1.9: literal "RUSTED FAIR" constant, never run through Loc.Upper (the Turkish i/İ rule would make it "FAİR")
            var title = UIKit.Txt(mainList, "Title", RF.GameTitle, 120, Color.white, TextAnchor.MiddleCenter, FontRole.Heavy, false);
            title.font = Fonts.Logo;
            title.rectTransform.sizeDelta = new Vector2(1000, 170);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 48;
            title.resizeTextMaxSize = 120;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.raycastTarget = false;
            // the default drop shadow from UIKit.Txt would be tinted by the gradient: replace it (added after the gradient)
            var defShadow = title.GetComponent<Shadow>();
            if (defShadow != null) { defShadow.enabled = false; Destroy(defShadow); }
            var grad = title.gameObject.AddComponent<VerticalGradient>();
            grad.top = new Color(1f, 0.353f, 0.267f);
            grad.mid = new Color(0.702f, 0.082f, 0.055f);
            grad.bottom = new Color(0.369f, 0.031f, 0.02f);
            grad.midAt = 0.55f;
            LetterSpacing.Add(title, 0.12f);
            var glow = title.gameObject.AddComponent<Outline>();
            glow.effectColor = new Color(1f, 0.16f, 0.08f, 0.22f);
            glow.effectDistance = new Vector2(4f, -4f);
            var drop = title.gameObject.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0f, 0.85f);
            drop.effectDistance = new Vector2(0f, -5f);
            title.gameObject.AddComponent<TitleFlicker>();
            var tag = UIKit.Txt(mainList, "Tagline", "tagline", 30, UIKit.Dim, TextAnchor.MiddleCenter);
            tag.rectTransform.sizeDelta = new Vector2(1000, 50);
            UIKit.Rect(mainList, "Gap").sizeDelta = new Vector2(10, 14);

            UIKit.Btn(mainList, "Play", "play", OnPlay, new Vector2(600, 124), true, 64);

            var d = SaveSystem.Data;
            var row = UIKit.Rect(mainList, "DiffLang");
            row.sizeDelta = new Vector2(1000, 74);
            var diff = UIKit.DynBtn(row, "Difficulty", () => Loc.T("difficulty") + ": " + Loc.T(d.difficulty == 0 ? "diff_easy" : d.difficulty == 2 ? "diff_hard" : "diff_normal"),
                () => { d.difficulty = (d.difficulty + 1) % 3; SaveSystem.Save(); }, new Vector2(470, 74), 34);
            UIKit.Place((RectTransform)diff.transform, 0.5f, 0.5f, new Vector2(-250, 0), new Vector2(470, 74));
            var dot = UIKit.Txt(row, "Dot", "·", 44, UIKit.Dim, TextAnchor.MiddleCenter, FontRole.UIBold, false);
            UIKit.Place(dot.rectTransform, 0.5f, 0.5f, Vector2.zero, new Vector2(30, 74));
            // v1.10: opens the 14-language grid (a cycle button was tedious with 14 entries)
            var lang = UIKit.DynBtn(row, "Language", () => Loc.Languages[Loc.CurrentIndex()].native, () => Panels.LanguagePicker(canvas.transform, null),
                new Vector2(470, 74), 34);
            UIKit.Place((RectTransform)lang.transform, 0.5f, 0.5f, new Vector2(250, 0), new Vector2(470, 74));

            // v1.9: Custom Night (locked look + hint toast until the first escape)
            bool unlocked = Achievements.CustomNightUnlocked;
            var cn = UIKit.Btn(mainList, "CustomNight", "custom_night", () =>
            {
                if (!Achievements.CustomNightUnlocked) { Toasts.Message(Loc.T("custom_locked")); return; }
                Panels.CustomNight(canvas.transform, null, OnPlayCustom);
            }, new Vector2(600, 76), false, 40);
            if (!unlocked)
            {
                var lbl = cn.GetComponentInChildren<Text>();
                if (lbl != null) lbl.color = new Color(UIKit.Bone.r, UIKit.Bone.g, UIKit.Bone.b, 0.32f);
                var lk = UIKit.Img(cn.transform, "Lock", "icon_lock", new Color(UIKit.Bone.r, UIKit.Bone.g, UIKit.Bone.b, 0.32f));
                lk.raycastTarget = false;
                lk.preserveAspect = true;
                UIKit.Place(lk.rectTransform, 0.5f, 0.5f, Vector2.zero, new Vector2(30, 30));
                var lb = lk.gameObject.AddComponent<LockBadge>();
                lb.label = lbl;
            }

            // v1.9: Settings · Achievements · How to play on one small row
            var small = UIKit.Rect(mainList, "SmallRow");
            small.sizeDelta = new Vector2(1000, 66);
            var sh = small.gameObject.AddComponent<HorizontalLayoutGroup>();
            sh.childAlignment = TextAnchor.MiddleCenter;
            sh.spacing = 6f;
            sh.childControlWidth = sh.childControlHeight = false;
            sh.childForceExpandWidth = sh.childForceExpandHeight = false;
            UIKit.Btn(small, "Settings", "settings", () => Panels.Settings(canvas.transform, null), new Vector2(290, 62), false, 30);
            Sep(small);
            UIKit.Btn(small, "Achievements", "achievements", () => Panels.AchievementsPanel(canvas.transform, null), new Vector2(330, 62), false, 30);
            Sep(small);
            UIKit.Btn(small, "HowToPlay", "how_to_play", () => Panels.HowToPlay(canvas.transform, null), new Vector2(330, 62), false, 30);

            // small dim extras on one line: Remove ads · Quit
            var extras = UIKit.Rect(mainList, "Extras");
            extras.sizeDelta = new Vector2(1000, 66);
            var h = extras.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = 18f;
            h.childControlWidth = h.childControlHeight = false;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            if (!d.AdsRemoved)
            {
                var store = UIKit.AltBtn(extras, "Store", "remove_ads", () => Panels.Store(canvas.transform, null), new Vector2(310, 62), 30);
                // static event: keep the handler so OnDestroy can remove it (the menu is rebuilt every time)
                entitlementHandler = () => { if (store != null && SaveSystem.Data.AdsRemoved) store.gameObject.SetActive(false); };
                IAPManager.OnEntitlementChanged += entitlementHandler;
            }
#if UNITY_ANDROID || UNITY_STANDALONE
            UIKit.AltBtn(extras, "Quit", "quit", Application.Quit, new Vector2(240, 62), 30);
#endif
            var stats = UIKit.Txt(safe, "Stats", Loc.F("menu_stats", d.escapes, Achievements.UnlockedCount(), Achievements.All.Count), 22, UIKit.Dim, TextAnchor.LowerRight, FontRole.UI, false);
            UIKit.Place(stats.rectTransform, 1f, 0f, new Vector2(-32, 34), new Vector2(700, 36), 1f, 0f);
            var ver = UIKit.Txt(safe, "Version", "v" + Application.version, 18, new Color(1, 1, 1, 0.3f), TextAnchor.LowerRight, FontRole.UI, false);
            UIKit.Place(ver.rectTransform, 1f, 0f, new Vector2(-32, 8), new Vector2(300, 28), 1f, 0f);
        }

        static void Sep(RectTransform row)
        {
            var dot = UIKit.Txt(row, "Sep", "·", 36, UIKit.Dim, TextAnchor.MiddleCenter, FontRole.UIBold, false);
            dot.rectTransform.sizeDelta = new Vector2(24, 62);
        }

        /// <summary>Main-menu Play is always the story night (v1.9).</summary>
        void OnPlay()
        {
            if (starting) return;
            GameMode.Custom = false;
            starting = true;
            StartCoroutine(PlayRoutine());
        }

        /// <summary>v1.9: "Start the night" in the Custom Night panel.</summary>
        void OnPlayCustom()
        {
            if (starting) return;
            GameMode.Custom = true;
            starting = true;
            StartCoroutine(PlayRoutine());
        }

        IEnumerator PlayRoutine()
        {
            if (AudioManager.I != null)
            {
                AudioManager.I.FadeLayer("menu", 0f, 1.2f);
                AudioManager.I.Play2D("door_knock", 1f);
            }
            var cg = canvas.gameObject.AddComponent<CanvasGroup>();
            cg.interactable = false;
            float t = 0;
            while (t < 1f) { t += Time.deltaTime; cg.alpha = 1f - t; yield return null; }
            // the three knocks: the camera flinches each time
            float[] knocks = { 0.15f, 0.95f, 1.75f };
            float k = 0;
            int ki = 0;
            while (k < 2.6f)
            {
                k += Time.deltaTime;
                if (ki < knocks.Length && k > knocks[ki]) { ki++; flash = 0.25f; }
                yield return null;
            }
            if (RFApp.I != null) RFApp.I.StartGame();
        }

        // ================================================================== animation
        void Update()
        {
            float t = Time.time;
            // handheld drift
            camRig.position = camBase + new Vector3(Mathf.Sin(t * 0.13f) * 0.6f, Mathf.Sin(t * 0.21f) * 0.12f, Mathf.Sin(t * 0.07f) * 0.8f);
            cam.transform.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.17f) * 0.8f - 6f, Mathf.Sin(t * 0.11f) * 2.2f, Mathf.Sin(t * 0.23f) * 0.4f);
            if (wheel != null) wheel.Rotate(0, 0, -2.2f * Time.deltaTime, Space.Self);
            for (int i = 0; i < clouds.Count; i++)
            {
                var c = clouds[i];
                c.position += Vector3.right * Time.deltaTime * (1.2f + i * 0.4f);
                if (c.position.x > 240f) c.position -= Vector3.right * 480f;
            }
            // neon sign stutter
            float n = Mathf.PerlinNoise(t * 3f, 0.3f);
            bool on = n > 0.28f || Mathf.PerlinNoise(t * 17f, 2f) > 0.6f;
            logo.enabled = on;
            logoLight.intensity = on ? 3f + Mathf.PerlinNoise(t * 8f, 1f) : 0.2f;

            // lightning
            nextLightning -= Time.deltaTime;
            if (nextLightning <= 0f)
            {
                nextLightning = Random.Range(7f, 16f);
                StartCoroutine(Lightning());
            }
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 2.5f);
            moon.intensity = 0.12f + flash * 4f;
            skyMat.SetColor("_BaseColor", Color.Lerp(new Color(0.02f, 0.025f, 0.045f), new Color(0.75f, 0.8f, 1f), flash));
            RenderSettings.ambientLight = Color.Lerp(new Color(0.07f, 0.075f, 0.1f), new Color(0.5f, 0.55f, 0.7f), flash * 0.7f);
            if (PostFX.I != null) PostFX.I.exposure = flash * 0.6f;
        }

        IEnumerator Lightning()
        {
            int strikes = Random.Range(1, 4);
            for (int i = 0; i < strikes; i++)
            {
                flash = Random.Range(0.6f, 1f);
                yield return new WaitForSeconds(Random.Range(0.05f, 0.18f));
                flash *= 0.3f;
                yield return new WaitForSeconds(Random.Range(0.04f, 0.12f));
            }
            bool close = Random.value < 0.35f;
            yield return new WaitForSeconds(close ? Random.Range(0.1f, 0.4f) : Random.Range(0.8f, 2.2f));
            if (AudioManager.I != null)
            {
                if (close) AudioManager.I.Play2D("lightning_crack", 0.7f);
                AudioManager.I.Play2D("thunder_" + Random.Range(1, 4), close ? 0.95f : 0.6f);
            }
        }
    }

    /// <summary>Keeps the lock icon just left of the (centred, localized) Custom Night label.</summary>
    public class LockBadge : MonoBehaviour
    {
        public Text label;
        RectTransform rt;
        void Awake() => rt = (RectTransform)transform;
        void LateUpdate()
        {
            if (label == null) return;
            float w = Mathf.Min(label.preferredWidth, ((RectTransform)label.transform).rect.width);
            rt.anchoredPosition = new Vector2(-w * 0.5f - 26f, 0f);
        }
    }

    public class TitleFlicker : MonoBehaviour
    {
        Graphic g;
        void Awake() => g = GetComponent<Graphic>();
        void Update()
        {
            if (g == null) return;
            float n = Mathf.PerlinNoise(Time.time * 2.3f, 0.7f);
            float a = n < 0.18f ? 0.55f : 1f;
            g.color = new Color(1f, 1f, 1f, a); // the logo's colours come from its VerticalGradient
        }
    }

    public class PulseScale : MonoBehaviour
    {
        void Update()
        {
            float s = 1f + Mathf.Sin(Time.unscaledTime * 2.2f) * 0.018f;
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
