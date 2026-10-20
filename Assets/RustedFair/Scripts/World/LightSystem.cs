using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    public enum LightGroup { Always, Emergency, Main }

    /// <summary>
    /// Manages every fairground light: power groups, flicker, horror events (surges, blackouts),
    /// robot-proximity interference and distance culling (keeps only the nearest lights active for 120 fps).
    /// </summary>
    public class LightSystem : MonoBehaviour
    {
        public static LightSystem I { get; private set; }

        class FL
        {
            public Light light;
            public LightGroup group;
            public float baseIntensity;  // URP units (after the falloff conversion)
            public float webIntensity;   // the web (Three.js) value: drives the halo alpha and LuxAt
            public bool flicker;
            public Renderer bulb;
            public Material onMat, offMat;
            public float seed;
            public bool wantOn;
            public float dist;
            public bool bulbLit = true;
            public AudioSource buzz;
            public bool lamp;      // ceiling lamp
            public float k;        // current power/flicker factor (shared by the real light and its fake glow)
            public Vector3 pos;
            public Color color;
        }

        readonly List<FL> lights = new List<FL>();
        readonly List<FL> sorted = new List<FL>();
        float cullTimer;
        float surgeUntil, blackoutUntil;
        Transform viewer;
        public readonly List<Transform> interferers = new List<Transform>();

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public void SetViewer(Transform t) => viewer = t;

        /// <summary>
        /// v1.0.2: the fair's light values come from the web build (Three.js r128, legacy lights: irradiance =
        /// I·(1−d/range)^decay). URP lights fall off as I/d² with a smooth window at the range, so the same numbers are
        /// ~10x too dark at a few metres. When set (the night; the menu keeps its own tuned look) Add() converts every
        /// intensity so the irradiance matches the web at <see cref="RefDistance"/> of the range.
        /// </summary>
        public bool webFalloff;
        public const float RefDistance = 0.3f;

        /// <summary>URP intensity giving the same irradiance as a Three.js legacy light (I, range, decay) at refDist.</summary>
        public static float WebToURP(float webIntensity, float range, float decay, float refDist)
        {
            range = Mathf.Max(range, 0.01f);
            float d = Mathf.Clamp(refDist, 0.05f, range * 0.95f);
            float x = d / range;
            float win = 1f - x * x * x * x;
            win *= win; // URP: smoothFactor = (1 - (d²/r²)²)²
            return webIntensity * Mathf.Pow(1f - x, decay) * d * d / Mathf.Max(win, 1e-3f);
        }

        /// <summary>True during a surge / blackout event (editor screenshot tool waits for it to end).</summary>
        public bool EventActive => Time.time < surgeUntil || Time.time < blackoutUntil;

        /// <summary>Cancels a running surge / blackout.</summary>
        public void ClearEvents() { surgeUntil = 0f; blackoutUntil = 0f; }

        public Light Add(Vector3 pos, Transform parent, Color color, float intensity, float range, LightGroup group, bool flicker = false, Renderer bulb = null, bool lamp = false)
        {
            var go = new GameObject("L_" + group);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            float scale = webFalloff ? WebToURP(1f, range, 2f, range * RefDistance) : 1f;
            l.intensity = intensity * scale;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
            Register(l, group, flicker, bulb);
            var f = lights[lights.Count - 1];
            f.lamp = lamp;
            f.webIntensity = intensity;
            return l;
        }

        public void Register(Light l, LightGroup group, bool flicker, Renderer bulb = null)
        {
            var f = new FL
            {
                light = l,
                group = group,
                baseIntensity = l.intensity,
                webIntensity = l.intensity,
                flicker = flicker,
                bulb = bulb,
                seed = Random.value * 100f,
                pos = l.transform.position,
                color = l.color
            };
            if (bulb != null)
            {
                f.onMat = bulb.sharedMaterial;
                f.offMat = MatLib.Get("bulb_dead");
            }
            if (flicker && Random.value < 0.6f)
            {
                f.buzz = AudioManager.Attach(l.gameObject, "electric_buzz", 0.25f, true, 1f, 7f, false);
            }
            l.enabled = false;
            lights.Add(f);
        }

        /// <summary>Renderers this system switches between on/off materials (must not be merged away).</summary>
        public void CollectBulbs(HashSet<Renderer> set)
        {
            foreach (var f in lights) if (f.bulb != null) set.Add(f.bulb);
        }

        public void Surge(float seconds) => surgeUntil = Time.time + seconds;
        public void Blackout(float seconds) => blackoutUntil = Time.time + seconds;

        bool GroupOn(FL f)
        {
            if (Time.time < blackoutUntil && f.group != LightGroup.Always) return false;
            switch (f.group)
            {
                case LightGroup.Main: return Power.On;
                default: return true;
            }
        }

        float GroupMul(FL f)
        {
            if (f.group == LightGroup.Emergency && Power.On) return 0.35f;
            return 1f;
        }

        /// <summary>Power group x flicker x surge x robot interference. Shared by the real lights and the fake glow,
        /// so a halo always flickers in step with its light.</summary>
        float Factor(FL f, float time, bool surge)
        {
            if (!GroupOn(f)) return 0f;
            float k = GroupMul(f);
            if (f.flicker || surge)
            {
                float n = Mathf.PerlinNoise(f.seed, time * (surge ? 18f : 7f));
                float drop = Mathf.PerlinNoise(f.seed + 33f, time * 1.3f);
                if (drop > 0.72f || (surge && n < 0.45f)) k *= n < 0.5f ? 0.03f : 0.4f;
                else k *= 0.8f + 0.25f * n;
            }
            // robots make lights stutter when near
            for (int i = 0; i < interferers.Count; i++)
            {
                var r = interferers[i];
                if (r == null) continue;
                if ((r.position - f.pos).sqrMagnitude < 64f && Mathf.PerlinNoise(f.seed, time * 22f) < 0.4f) k *= 0.05f;
            }
            return k;
        }

        void Update()
        {
            if (viewer == null) return;
            cullTimer -= Time.deltaTime;
            if (cullTimer <= 0f)
            {
                cullTimer = 0.2f;
                Cull();
                UpdateBulbMaterial();
            }

            float time = Time.time;
            bool surge = time < surgeUntil;
            if (fxBuilt) for (int i = 0; i < lights.Count; i++) lights[i].k = Factor(lights[i], time, surge);
            foreach (var f in sorted)
            {
                if (!f.wantOn) continue;
                float k = fxBuilt ? f.k : Factor(f, time, surge);
                f.light.intensity = f.baseIntensity * k;
                bool lit = k > 0.2f;
                if (f.bulb != null && lit != f.bulbLit)
                {
                    f.bulbLit = lit;
                    f.bulb.sharedMaterial = lit ? f.onMat : f.offMat;
                }
            }
        }

        void Cull()
        {
            Vector3 vp = viewer.position;
            float maxD = GraphicsQuality.LightCullDistance;
            int maxN = GraphicsQuality.MaxActiveLights;
            sorted.Clear();
            foreach (var f in lights)
            {
                if (f.light == null) continue;
                f.dist = (f.light.transform.position - vp).sqrMagnitude;
                bool on = GroupOn(f);
                if (!on)
                {
                    f.wantOn = false;
                    f.light.enabled = false;
                    if (f.bulb != null && f.bulbLit) { f.bulbLit = false; f.bulb.sharedMaterial = f.offMat; }
                    if (f.buzz != null && f.buzz.isPlaying) f.buzz.Stop();
                    continue;
                }
                if (f.bulb != null && !f.bulbLit) { f.bulbLit = true; f.bulb.sharedMaterial = f.onMat; }
                sorted.Add(f);
            }
            sorted.Sort((a, b) => a.dist.CompareTo(b.dist));
            float maxD2 = maxD * maxD;
            for (int i = 0; i < sorted.Count; i++)
            {
                var f = sorted[i];
                bool want = i < maxN && f.dist < maxD2 + f.light.range * f.light.range;
                f.wantOn = want;
                if (f.light.enabled != want) f.light.enabled = want;
                if (f.buzz != null)
                {
                    bool near = f.dist < 100f;
                    if (near && !f.buzz.isPlaying) f.buzz.Play();
                    else if (!near && f.buzz.isPlaying) f.buzz.Stop();
                }
            }
        }
    
        // ================================================================== static bulbs (v1.4)
        Material bulbMain;
        Color bulbOnBase, bulbOnEmit;
        bool bulbLitState = true;
        static readonly Color DeadBase = new Color(0.15f, 0.14f, 0.13f);

        /// <summary>Ceiling/sconce bulbs are static (batched) and share the "bulb_main" material: its colour follows the power.</summary>
        void UpdateBulbMaterial()
        {
            if (bulbMain == null)
            {
                bulbMain = MatLib.Get("bulb_main");
                if (bulbMain == null) return;
                bulbOnBase = bulbMain.HasProperty("_BaseColor") ? bulbMain.GetColor("_BaseColor") : Color.white;
                bulbOnEmit = bulbMain.HasProperty("_EmissionColor") ? bulbMain.GetColor("_EmissionColor") : Color.black;
            }
            bool lit = Power.On && Time.time >= blackoutUntil;
            if (lit == bulbLitState) return;
            bulbLitState = lit;
            if (bulbMain.HasProperty("_BaseColor")) bulbMain.SetColor("_BaseColor", lit ? bulbOnBase : DeadBase);
            if (bulbMain.HasProperty("_EmissionColor")) bulbMain.SetColor("_EmissionColor", lit ? bulbOnEmit : Color.black);
        }

        void OnDisable()
        {
            // bulb_main is shared (and may be a material asset in the editor): leave it lit
            if (bulbMain != null && !bulbLitState)
            {
                if (bulbMain.HasProperty("_BaseColor")) bulbMain.SetColor("_BaseColor", bulbOnBase);
                if (bulbMain.HasProperty("_EmissionColor")) bulbMain.SetColor("_EmissionColor", bulbOnEmit);
                bulbLitState = true;
            }
        }

        // ================================================================== cheap fake lighting (v1.4, trimmed in v1.5)
        // Every fixture gets a soft additive halo (one particle system = 1 draw call), so distant rooms still read as lit
        // while only the nearest few fixtures use real lights. Colours follow Factor() (power, flicker, blackout, surges,
        // robots). v1.5: the lamp light cones and floor glow pools were removed (they looked bad); halos are smaller/dimmer.
        ParticleSystem halos;
        ParticleSystem.Particle[] haloParts;
        const float HaloSize = 0.75f, HaloAlpha = 0.22f;
        bool fxBuilt;

        static Texture2D RadialTex()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "fx_radial" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Min(1f, new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f).magnitude / (n * 0.5f));
                    float a = d < 0.25f ? Mathf.Lerp(1f, 0.55f, d / 0.25f) : Mathf.Lerp(0.55f, 0f, (d - 0.25f) / 0.75f);
                    byte bb = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    px[y * n + x] = new Color32(bb, bb, bb, bb);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Builds the halo batch once every light exists. (The cones flag is kept for call compatibility.)</summary>
        public void BuildFx(Transform parent, bool cones = false)
        {
            if (fxBuilt || lights.Count == 0) return;
            fxBuilt = true;
            var tex = RadialTex();
            var hg = new GameObject("LightHalos");
            hg.transform.SetParent(parent, false);
            var ps = hg.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = lights.Count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1e6f;
            main.startSpeed = 0f;
            main.duration = 1e5f;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.enabled = false;
            var pr = hg.GetComponent<ParticleSystemRenderer>();
            var mat = MatLib.Create(new MatSpec { name = "fx_halo", tex = "fx", shader = MatShader.ParticleAdd, color = Color.white }, _ => tex);
            pr.sharedMaterial = mat;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.maxParticleSize = 0.2f; // v1.0.2 (was 4): fraction of the screen - a bulb right next to the camera must not fill the view
            pr.minParticleSize = 0f;
            ps.Play();
            halos = ps;
            haloParts = new ParticleSystem.Particle[lights.Count];
            for (int i = 0; i < lights.Count; i++)
                haloParts[i] = new ParticleSystem.Particle
                {
                    position = lights[i].pos + Vector3.up * 0.06f, startSize = HaloSize, startLifetime = 1e6f, remainingLifetime = 1e6f,
                    startColor = new Color32(0, 0, 0, 0)
                };
            ps.SetParticles(haloParts, lights.Count);
        }

        void LateUpdate()
        {
            if (halos == null || viewer == null) return;
            int n = Mathf.Min(lights.Count, haloParts.Length);
            for (int i = 0; i < n; i++)
            {
                var f = lights[i];
                float a = Mathf.Clamp01(Mathf.Min(1f, f.webIntensity * HaloAlpha) * f.k); // web units (not the URP-converted value)
                haloParts[i].startColor = new Color(f.color.r, f.color.g, f.color.b, a);
                haloParts[i].remainingLifetime = 1e6f;
            }
            halos.SetParticles(haloParts, n);
        }

        /// <summary>v1.5: summed contribution of the active real lights at a point, Σ intensity·(1−d/range)² (darkness check).
        /// Uses the web intensities, so the 0.3 threshold of the dark check keeps its meaning.</summary>
        public float LuxAt(Vector3 p)
        {
            float lux = 0f;
            foreach (var f in sorted)
            {
                if (!f.wantOn || f.light == null || !f.light.enabled || f.light.intensity <= 0f || f.baseIntensity <= 0f) continue;
                float d = (f.pos - p).magnitude, r = f.light.range;
                float web = f.light.intensity * (f.webIntensity / f.baseIntensity);
                if (d < r) { float k = 1f - d / r; lux += web * k * k; }
            }
            return lux;
        }
    }
}
