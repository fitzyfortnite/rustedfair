using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RustedFair
{
    /// <summary>
    /// Runtime-built global post-processing: filmic grade, bloom on neon, grain, fear-driven vignette and
    /// chromatic aberration, eye-blur for waking up, and the red jumpscare grade.
    /// </summary>
    public class PostFX : MonoBehaviour
    {
        public static PostFX I { get; private set; }

        Volume volume;
        VolumeProfile profile;
        Vignette vig;
        ChromaticAberration ca;
        ColorAdjustments color;
        DepthOfField dof;
        FilmGrain grain;
        Bloom bloom;
        LensDistortion lens;

        public float fear;        // 0..1
        public float blur;        // 0..1 (eyes)
        public float red;         // 0..1 jumpscare grade
        public float mask;        // 0..1 mask on face
        public float exposure;    // stops
        float f, b, rd, mk;
        bool menuMode;

        public static PostFX Create(Transform parent, bool menu)
        {
            var go = new GameObject("PostFX");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<PostFX>();
            p.menuMode = menu;
            p.Build();
            return p;
        }

        void Awake() { I = this; }
        void OnDestroy()
        {
            if (I == this) I = null;
            if (profile != null) Destroy(profile);
        }

        void Build()
        {
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile = profile;
            // v1.0.2: Add<T>(false) - only the parameters set with Override() below are overridden (every value that
            // Update() writes is one of them), the rest keep the URP defaults instead of being forced on at default values.

            var tone = profile.Add<Tonemapping>(false);
            tone.mode.Override(TonemappingMode.Neutral); // v1.0.1: ACES + LGG crushed the dark scene to black on device

            color = profile.Add<ColorAdjustments>(false);
            color.postExposure.Override(menuMode ? 0.45f : 0.6f);
            color.contrast.Override(6f);
            color.saturation.Override(-15f);
            color.colorFilter.Override(new Color(0.92f, 0.97f, 1f));


            bloom = profile.Add<Bloom>(false);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(menuMode ? 1.1f : 0.85f);
            bloom.scatter.Override(0.68f);
            bloom.tint.Override(new Color(1f, 0.9f, 0.85f));

            vig = profile.Add<Vignette>(false);
            vig.intensity.Override(0.36f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(Color.black);

            ca = profile.Add<ChromaticAberration>(false);
            ca.intensity.Override(0.06f);

            grain = profile.Add<FilmGrain>(false);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.32f);
            grain.response.Override(0.75f);

            lens = profile.Add<LensDistortion>(false);
            lens.intensity.Override(0f);

            dof = profile.Add<DepthOfField>(false);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(0f);
            dof.gaussianEnd.Override(60f);
            dof.gaussianMaxRadius.Override(1.5f);
            dof.highQualitySampling.Override(false);
            dof.active = false;

            ApplyQuality(GraphicsQuality.Level);
        }

        // Build() runs after AddComponent (i.e. after OnEnable); ApplyQuality is null-guarded
        void OnEnable() { GraphicsQuality.OnChanged -= ApplyQuality; GraphicsQuality.OnChanged += ApplyQuality; }
        void OnDisable() => GraphicsQuality.OnChanged -= ApplyQuality;

        void ApplyQuality(int level)
        {
            if (bloom == null || grain == null || ca == null || lens == null) return;
            bloom.active = level >= 1;
            grain.active = level >= 1;
            ca.active = level >= 1;
            lens.active = level >= 2;
        }

        void Update()
        {
            if (vig == null) return;
            float dt = Time.unscaledDeltaTime;
            f = RF.Damp(f, fear, 3f, dt);
            b = RF.Damp(b, blur, 6f, dt);
            rd = RF.Damp(rd, red, 14f, dt);
            mk = RF.Damp(mk, mask, 8f, dt);

            float pulse = Mathf.Sin(Time.time * (4f + f * 6f)) * 0.5f + 0.5f;
            vig.intensity.value = Mathf.Clamp01(0.36f + f * 0.16f + f * pulse * 0.06f + rd * 0.25f + mk * 0.32f);
            vig.smoothness.value = Mathf.Lerp(0.45f, 0.75f, Mathf.Max(rd, mk));
            vig.color.value = Color.Lerp(Color.black, new Color(0.5f, 0f, 0f), Mathf.Max(f * 0.6f, rd)); // red only at the edges
            ca.intensity.value = 0.06f + f * 0.35f + rd * 0.6f;
            if (lens.active) lens.intensity.value = -0.18f * rd - 0.06f * mk;
            color.colorFilter.value = Color.Lerp(new Color(0.92f, 0.97f, 1f), new Color(1f, 0.84f, 0.8f), rd); // v1.0.3: was a full red tint that hid the face
            color.saturation.value = Mathf.Lerp(-15f, 10f, rd) - f * 10f;
            color.postExposure.value = (menuMode ? 0.45f : 0.6f) + exposure;

            bool needDof = b > 0.01f && GraphicsQuality.Level >= 1;
            if (dof.active != needDof) dof.active = needDof;
            if (needDof) dof.gaussianEnd.value = Mathf.Lerp(60f, 0.05f, b);
        }
    }
}
