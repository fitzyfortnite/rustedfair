using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RustedFair
{
    /// <summary>Three quality tiers tuned for a 120 fps target on modern phones.</summary>
    public static class GraphicsQuality
    {
        public static int Level { get; private set; } = 2;
        public static event Action<int> OnChanged;

        public static int AutoDetect()
        {
            if (!Application.isMobilePlatform) return 2;
            int mem = SystemInfo.systemMemorySize;
            int cores = SystemInfo.processorCount;
            string model = SystemInfo.deviceModel ?? "";
            // v1.0.3 iOS: Apple chips are judged by generation (6-core CPUs failed the old "8 cores" test,
            // so even an iPhone 17 Pro Max landed on medium). iPhone14,x = A15 (iPhone 13/14) and newer -> high.
            int iphone = ModelMajor(model, "iPhone");
            if (iphone > 0) return iphone >= 14 ? 2 : (iphone >= 12 ? 1 : 0);
            int ipad = ModelMajor(model, "iPad");
            if (ipad > 0) return ipad >= 13 ? 2 : (ipad >= 11 ? 1 : 0);
            if (Application.platform == RuntimePlatform.IPhonePlayer) return mem >= 5500 ? 2 : 1;
            // Android: GPU family first, memory as a fallback
            string gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
            int adreno = GpuNumber(gpu, "adreno");
            if (adreno > 0)
            {
                if (adreno >= 730 && mem >= 5500) return 2;   // Snapdragon 8 Gen 1 and newer
                if (adreno >= 640) return 1;                  // 8xx from 2019, 7-series
                return 0;
            }
            if (gpu.Contains("immortalis") || gpu.Contains("xclipse")) return mem >= 5500 ? 2 : 1;
            int mali = GpuNumber(gpu, "mali-g");
            if (mali > 0)
            {
                if ((mali >= 710 && mali < 1000) || mali >= 1000) return mem >= 7000 ? 2 : 1; // G710/G715/G720/G925...
                if (mali >= 76 && mali < 100) return 1;                                       // G76/G77/G78
                if (mali >= 610) return 1;                                                    // G610/G615
                return 0;
            }
            if (mem >= 7000 && cores >= 8) return 2;
            if (mem >= 4000) return 1;
            return 0;
        }

        /// <summary>"iPhone16,2" -> 16 (0 when the prefix does not match).</summary>
        static int ModelMajor(string model, string prefix)
        {
            if (!model.StartsWith(prefix)) return 0;
            int i = prefix.Length, n = 0;
            while (i < model.Length && char.IsDigit(model[i])) { n = n * 10 + (model[i] - '0'); i++; }
            return n;
        }

        /// <summary>First number after <paramref name="key"/> in the GPU name ("adreno (tm) 740" -> 740).</summary>
        static int GpuNumber(string gpu, string key)
        {
            int i = gpu.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return 0;
            i += key.Length;
            int stop = Mathf.Min(gpu.Length, i + 12);
            while (i < stop && !char.IsDigit(gpu[i])) i++;
            int n = 0;
            while (i < gpu.Length && char.IsDigit(gpu[i])) { n = n * 10 + (gpu[i] - '0'); i++; }
            return n;
        }

        public static void Apply(int requested)
        {
            int level = requested < 0 ? AutoDetect() : Mathf.Clamp(requested, 0, 2);
            if (requested < 0)
            {
                SaveSystem.Data.quality = level;
                SaveSystem.Save();
            }
            if (level != Level) MatLib.ClearCache(); // shader set differs per tier (Simple Lit / vertex colour on low+medium)
            Level = level;

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                switch (level)
                {
                    case 0:
                        urp.renderScale = BaseScaleFor(0);
                        urp.msaaSampleCount = 1;
                        urp.shadowDistance = 18f;
                        break;
                    case 1:
                        urp.renderScale = BaseScaleFor(1);
                        urp.msaaSampleCount = 1;
                        urp.shadowDistance = 25f;
                        break;
                    default:
                        urp.renderScale = BaseScaleFor(2);
                        urp.msaaSampleCount = Application.isMobilePlatform ? 2 : 4;
                        urp.shadowDistance = 30f;
                        break;
                }
                urp.supportsHDR = level > 0;
            }
            DynamicScale = 1f;
            QualitySettings.lodBias = level == 0 ? 0.6f : (level == 1 ? 0.85f : 1.1f);
            QualitySettings.anisotropicFiltering = level == 2 ? AnisotropicFiltering.Enable : AnisotropicFiltering.Disable;
            QualitySettings.globalTextureMipmapLimit = level == 0 ? 1 : 0; // v1.0.3: half-size textures on low-end memory budgets
            OnChanged?.Invoke(level);
        }

        /// <summary>How far dynamic point lights stay active around the player.</summary>
        public static float LightCullDistance => Level == 0 ? 22f : (Level == 1 ? 28f : 34f);
        /// <summary>v1.4: pooled real point lights (low/med/high = 4/6/8); the rest of the fixtures use the fake glow.</summary>
        public static int MaxActiveLights => Level == 0 ? 3 : (Level == 1 ? 5 : 8); // v1.5: 3 / 5 / 8

        /// <summary>Approximate device pixel ratio (physical pixels per "CSS" / density-independent pixel).</summary>
        public static float DevicePixelRatio
        {
            get
            {
                if (!Application.isMobilePlatform) return 1f;
                float dpi = Screen.dpi;
                return dpi > 1f ? Mathf.Max(1f, dpi / 160f) : 2.5f;
            }
        }

        /// <summary>
        /// v1.5 (web parity): on phones the render resolution is capped in density-independent pixels:
        /// low 0.7x, medium 1.0x, high 1.75x of the dp resolution (never above native, never below 0.4 of it).
        /// Desktop keeps native resolution.
        /// </summary>
        static float BaseScaleFor(int level)
        {
            if (!Application.isMobilePlatform) return 1f;
            float cap = level == 0 ? 0.7f : (level == 1 ? 1f : 1.75f);
            return Mathf.Clamp(cap / DevicePixelRatio, 0.4f, 1f);
        }

        /// <summary>Render scale of the current preset (dynamic resolution multiplies it).</summary>
        public static float BaseRenderScale => BaseScaleFor(Level);
        public static float DynamicScale { get; private set; } = 1f;

        /// <summary>Dynamic resolution: URP render scale = preset scale x s (s in 0.5..1).</summary>
        public static void SetDynamicScale(float s)
        {
            DynamicScale = Mathf.Clamp(s, 0.5f, 1f);
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) urp.renderScale = BaseRenderScale * DynamicScale;
        }
        public static bool FlashlightShadows => Level >= 1;
        public static bool HeavyPostFx => Level >= 2;
    }
}
