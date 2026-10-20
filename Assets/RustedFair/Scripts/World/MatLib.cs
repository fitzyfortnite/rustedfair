using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RustedFair
{
    /// <summary>VertexLit / VertexUnlit: opaque vertex-coloured materials shared by merged untextured geometry on
    /// low/medium quality (v1.5). SimpleLit: Blinn-Phong reference (also used for all lit materials on low/medium).</summary>
    public enum MatShader { Lit, Unlit, ParticleAlpha, ParticleAdd, SimpleLit, VertexLit, VertexUnlit }

    public class MatSpec
    {
        public string name, tex, normal, emissionTex;
        /// <summary>v1.0.3: metallic (R) / occlusion (G) / smoothness (A) map - the robot atlases.</summary>
        public string mask;
        public Color color = Color.white;
        public Color emission = Color.black;
        public float smooth = 0.2f, metal = 0f, tile = 3f, normalScale = 1f, cutoff = 0.5f;
        public bool transparent, alphaClip, doubleSided;
        public MatShader shader = MatShader.Lit;
    }

    /// <summary>
    /// Every material in the game. The editor setup bakes these into .mat assets under
    /// Resources/RF/Materials (so shader variants are included in builds); at runtime they are
    /// loaded from there, falling back to creating them on the fly (editor play mode).
    /// </summary>
    public static class MatLib
    {
        static readonly Dictionary<string, MatSpec> specs = new Dictionary<string, MatSpec>();
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        static MatSpec S(string name, string tex, string normal = null, float tile = 3f, float smooth = 0.2f, float metal = 0f, Color? color = null)
        {
            var s = new MatSpec { name = name, tex = tex, normal = normal, tile = tile, smooth = smooth, metal = metal, color = color ?? Color.white };
            specs[name] = s;
            return s;
        }

        static MatSpec E(string name, Color baseCol, Color emission, string emissionTex = null, string tex = null)
        {
            var s = new MatSpec { name = name, color = baseCol, emission = emission, emissionTex = emissionTex, tex = tex, smooth = 0.5f };
            specs[name] = s;
            return s;
        }

        static MatLib()
        {
            // ---- architecture ----
            S("floor_concrete", "concrete_floor", "concrete_floor_n", 4f, 0.28f, 0f, new Color(0.78f, 0.76f, 0.74f));
            S("floor_tile", "tile_floor", "tile_floor_n", 5f, 0.5f, 0f, new Color(0.85f, 0.85f, 0.85f));
            S("floor_carpet", "carpet", null, 3f, 0.04f);
            S("floor_grate", "grate", "grate_n", 2f, 0.45f, 0.85f);
            S("floor_wood", "wood_planks", "wood_planks_n", 3f, 0.3f);
            S("wall_plaster", "wall_plaster", "wall_plaster_n", 3.5f, 0.12f, 0f, new Color(0.8f, 0.78f, 0.76f));
            S("wall_brick", "brick", "brick_n", 3f, 0.1f, 0f, new Color(0.8f, 0.72f, 0.7f));
            S("wall_metal", "metal_panel", "metal_panel_n", 3f, 0.45f, 0.6f, new Color(0.7f, 0.7f, 0.72f));
            S("metal_rust", "metal_rust", "metal_rust_n", 2f, 0.3f, 0.55f);
            S("ceiling", "ceiling", null, 4f, 0.1f, 0.2f, new Color(0.45f, 0.45f, 0.48f));
            S("wood", "wood_planks", "wood_planks_n", 2f, 0.3f);
            S("wood_painted", "wood_painted", null, 1.5f, 0.45f);
            S("tent", "tent_stripes", null, 2.5f, 0.1f).doubleSided = true;
            S("mirror", "mirror", null, 2f, 0.97f, 1f, new Color(0.55f, 0.6f, 0.65f));
            S("ice", "ice", "ice_n", 1.2f, 0.92f, 0f, new Color(0.75f, 0.9f, 1f));
            S("ground_wet", "ground_wet", "ground_wet_n", 6f, 0.72f, 0f, new Color(0.7f, 0.7f, 0.75f));
            S("fence", "fence", null, 2f, 0.4f, 0.8f).alphaClip = true;
            specs["fence"].doubleSided = true;

            // ---- creature / props ----
            S("plastic", "plastic_cracked", "plastic_cracked_n", 1f, 0.55f);
            S("plastic_dark", "plastic_cracked", "plastic_cracked_n", 1f, 0.5f, 0f, new Color(0.35f, 0.3f, 0.28f));
            S("fur", "fur_dirty", null, 1f, 0.05f);
            S("fur_dark", "fur_dirty", null, 1f, 0.05f, 0f, new Color(0.45f, 0.38f, 0.35f));
            S("fabric_purple", "fabric_purple", null, 1f, 0.1f);
            S("endo", "metal_rust", "metal_rust_n", 0.6f, 0.45f, 0.8f, new Color(0.55f, 0.5f, 0.48f));
            S("chrome", null, null, 1f, 0.85f, 1f, new Color(0.62f, 0.62f, 0.64f));
            S("gold_trim", null, null, 1f, 0.65f, 1f, new Color(0.83f, 0.62f, 0.25f));
            S("black_matte", null, null, 1f, 0.15f, 0f, new Color(0.03f, 0.03f, 0.03f));
            S("rubber", null, null, 1f, 0.25f, 0f, new Color(0.06f, 0.06f, 0.06f));
            S("teeth", null, null, 1f, 0.6f, 0f, new Color(0.82f, 0.78f, 0.62f));
            S("red_paint", null, null, 1f, 0.45f, 0.1f, new Color(0.45f, 0.05f, 0.04f));
            S("blue_paint", null, null, 1f, 0.45f, 0.1f, new Color(0.08f, 0.16f, 0.35f));
            S("yellow_paint", null, null, 1f, 0.45f, 0.1f, new Color(0.62f, 0.48f, 0.08f));
            S("green_paint", null, null, 1f, 0.45f, 0.1f, new Color(0.1f, 0.3f, 0.14f));
            S("white_paint", null, null, 1f, 0.4f, 0f, new Color(0.7f, 0.68f, 0.64f));
            S("paper", "note_paper", null, 1f, 0.05f).alphaClip = true;
            S("cloth_white", null, null, 1f, 0.05f, 0f, new Color(0.62f, 0.6f, 0.56f)).doubleSided = true;
            S("bone", null, null, 1f, 0.35f, 0f, new Color(0.78f, 0.74f, 0.62f));
            S("cardboard", null, null, 1f, 0.05f, 0f, new Color(0.45f, 0.34f, 0.22f));
            S("glass", null, null, 1f, 0.95f, 0f, new Color(0.6f, 0.7f, 0.75f, 0.18f)).transparent = true;

            for (int i = 1; i <= 4; i++) S("poster_" + i, "poster_" + i, null, 1f, 0.15f);

            // ---- emissive ----
            E("bulb_warm", new Color(0.2f, 0.18f, 0.15f), new Color(3.2f, 2.3f, 1.3f));
            E("bulb_main", new Color(0.2f, 0.18f, 0.15f), new Color(3.2f, 2.3f, 1.3f)); // v1.4: static ceiling bulbs, tinted by LightSystem with the power
            E("bulb_dead", new Color(0.15f, 0.14f, 0.13f), Color.black);
            E("emergency_red", new Color(0.2f, 0.02f, 0.02f), new Color(3f, 0.15f, 0.08f));
            E("neon_pink", new Color(0.2f, 0.05f, 0.1f), new Color(3.5f, 0.4f, 1.6f));
            E("neon_cyan", new Color(0.05f, 0.15f, 0.2f), new Color(0.3f, 2.4f, 3.2f));
            E("neon_yellow", new Color(0.2f, 0.18f, 0.05f), new Color(3.2f, 2.4f, 0.4f));
            E("neon_red", new Color(0.2f, 0.02f, 0.02f), new Color(4f, 0.3f, 0.2f));
            E("eye_red", new Color(0.3f, 0f, 0f), new Color(8f, 0.35f, 0.15f));
            E("eye_dim", new Color(0.15f, 0f, 0f), new Color(1.2f, 0.05f, 0.02f));
            E("screen_green", new Color(0.02f, 0.05f, 0.02f), new Color(0.4f, 1.3f, 0.5f));
            E("sign_exit", Color.white, new Color(1.8f, 1.8f, 1.8f), "sign_exit", "sign_exit");
            E("crt_static", Color.white, new Color(0.9f, 0.9f, 0.9f), "crt_static", "crt_static");
            E("cctv", Color.white, new Color(1.1f, 1.1f, 1.1f), "cctv_screen", "cctv_screen");
            for (int i = 1; i <= 3; i++) E("arcade_" + i, Color.white, new Color(1.6f, 1.6f, 1.6f), "arcade_screen_" + i, "arcade_screen_" + i);
            var logo = E("neon_logo", Color.white, new Color(4f, 4f, 4f), "sign_logo", "sign_logo");
            logo.transparent = true;
            logo.doubleSided = true;

            // ---- decals ----
            foreach (var d in new[] { "blood", "grime", "oil", "water" })
            {
                var s = S("decal_" + d, "decal_" + d, null, 1f, d == "oil" || d == "water" ? 0.95f : 0.2f);
                s.transparent = true;
            }

            // ---- fx ----
            specs["fx_rain"] = new MatSpec { name = "fx_rain", tex = "rain_streak", shader = MatShader.ParticleAlpha, color = new Color(0.75f, 0.8f, 0.9f, 0.45f) };
            specs["fx_glow"] = new MatSpec { name = "fx_glow", tex = "eye_glow", shader = MatShader.ParticleAdd, color = Color.white };
            specs["fx_dust"] = new MatSpec { name = "fx_dust", tex = "eye_glow", shader = MatShader.ParticleAlpha, color = new Color(0.8f, 0.78f, 0.7f, 0.25f) };
            specs["fx_spark"] = new MatSpec { name = "fx_spark", tex = "eye_glow", shader = MatShader.ParticleAdd, color = new Color(1f, 0.8f, 0.4f) };
            specs["fx_cloud"] = new MatSpec { name = "fx_cloud", tex = "cloud_layer", shader = MatShader.ParticleAlpha, color = new Color(0.32f, 0.33f, 0.4f, 1f) };
            specs["sky_flash"] = new MatSpec { name = "sky_flash", shader = MatShader.Unlit, color = new Color(0.02f, 0.025f, 0.04f) };
            // v1.5 phone path: baked as assets so their shaders are included in builds
            specs["vc_lit"] = new MatSpec { name = "vc_lit", shader = MatShader.VertexLit };
            specs["vc_unlit"] = new MatSpec { name = "vc_unlit", shader = MatShader.VertexUnlit };
            specs["simple_lit_ref"] = new MatSpec { name = "simple_lit_ref", shader = MatShader.SimpleLit, tex = "concrete_floor", tile = 4f };

            // v1.0.3: sculpted animatronics (Tools/robotgen) - one baked atlas set per robot; the _cut twin alpha-tests the torn holes
            foreach (var id in new[] { "grin", "rattle", "brutus" })
            {
                var o = S("robot_" + id, "robot_" + id, "robot_" + id + "_n", 1f, 1f, 1f);
                o.mask = "robot_" + id + "_ms";
                var c = S("robot_" + id + "_cut", "robot_" + id, "robot_" + id + "_n", 1f, 1f, 1f);
                c.mask = o.mask;
                c.alphaClip = true;
                c.cutoff = 0.5f;
            }
        }

        /// <summary>Hero materials (the animatronics) keep URP Lit on medium quality and their normal map on low.</summary>
        public static bool IsHero(string name) => name != null && name.StartsWith("robot_");

        public static MatSpec Spec(string name) => specs.TryGetValue(name, out var s) ? s : null;

        /// <summary>Drops cached materials (quality changed: the next night builds with the new shader set).</summary>
        public static void ClearCache() => cache.Clear();

        /// <summary>
        /// v1.5 (low/medium quality): untextured opaque materials are merged into one vertex-coloured material per chunk.
        /// Returns the vertex colour (linear) and whether it goes to the unlit (emissive) or the lit batch.
        /// The power-dimmed bulb material stays separate.
        /// </summary>
        public static bool VertexColorFor(string name, out Color32 col, out bool unlit)
        {
            col = new Color32(255, 255, 255, 255);
            unlit = false;
            if (GraphicsQuality.Level >= 2 || name == "bulb_main") return false;
            if (!specs.TryGetValue(name, out var s)) return false;
            if (s.shader != MatShader.Lit || !string.IsNullOrEmpty(s.tex) || s.transparent || s.alphaClip || s.doubleSided) return false;
            Color c;
            if (s.emission.maxColorComponent > 0.001f)
            {
                unlit = true;
                float mx = Mathf.Max(1f, s.emission.maxColorComponent);
                c = new Color(s.emission.r / mx, s.emission.g / mx, s.emission.b / mx, 1f);
            }
            else c = s.color;
            col = (Color32)c.linear; // vertex colours are not gamma-converted by the shader
            col.a = 255;
            return true;
        }

        /// <summary>v1.0.2: sub-folder of the material assets holding the baked Simple Lit copies (low/medium quality).</summary>
        public const string SimpleLitDir = "SimpleLit/";
        /// <summary>v1.0.3: medium quality - Simple Lit copies that keep the normal map.</summary>
        public const string SimpleLitNDir = "SimpleLitN/";

        /// <summary>Phones: URP Simple Lit (Blinn-Phong) without normal map, same textures/colours/emission/surface.
        /// Returns <paramref name="lit"/> unchanged when it does not use URP Lit. Also used by the editor baker.</summary>
        public static Material ToSimpleLit(Material lit, bool keepNormal = false)
        {
            if (lit == null || lit.shader == null || lit.shader.name != "Universal Render Pipeline/Lit") return lit;
            var sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (sh == null) return lit;
            var m = new Material(sh) { name = lit.name, enableInstancing = true };
            m.CopyPropertiesFromMaterial(lit);
            var kw = new List<string>(lit.shaderKeywords);
            bool hero = IsHero(lit.name);
            keepNormal |= hero;
            kw.Remove("_METALLICSPECGLOSSMAP");
            kw.Remove("_OCCLUSIONMAP");
            if (!keepNormal) kw.Remove("_NORMALMAP");
            if (hero)
            {
                // Blinn-Phong: a moderate uniform highlight instead of the PBR metallic/smoothness atlas
                m.SetFloat("_Smoothness", 0.45f);
                m.SetColor("_SpecColor", new Color(0.06f, 0.058f, 0.055f, 1f)); // dark cloth/fur must not turn grey under the flashlight
                if (!kw.Contains("_SPECULAR_COLOR")) kw.Add("_SPECULAR_COLOR");
            }
            m.shaderKeywords = kw.ToArray();
            if (!keepNormal && m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            m.renderQueue = lit.renderQueue;
            m.globalIlluminationFlags = lit.globalIlluminationFlags;
            if (lit.GetTag("RenderType", false) == "Transparent") m.SetOverrideTag("RenderType", "Transparent");
            return m;
        }

        public static IEnumerable<MatSpec> AllSpecs => specs.Values;

        public static float Tile(string name) => specs.TryGetValue(name, out var s) ? s.tile : 3f;

        public static Material Get(string name)
        {
            if (cache.TryGetValue(name, out var m) && m != null) return m;
            bool simple = GraphicsQuality.Level < (IsHero(name) ? 1 : 2);
            if (simple)
            {
                // v1.0.2: baked Simple Lit asset (Rebuild Materials). Being a material in the build, its keyword variants
                // (_EMISSION, _ALPHATEST_ON, transparent...) are compiled for Android; a copy made at runtime with
                // Shader.Find would hit stripped variants.
                var sl = Resources.Load<Material>(RF.MatPath + (GraphicsQuality.Level == 1 ? SimpleLitNDir : SimpleLitDir) + name);
                if (sl == null && GraphicsQuality.Level == 1) sl = Resources.Load<Material>(RF.MatPath + SimpleLitDir + name);
                if (sl != null) { cache[name] = sl; return sl; }
            }
            m = Resources.Load<Material>(RF.MatPath + name);
            if (m == null)
            {
                if (!specs.TryGetValue(name, out var spec))
                {
                    Debug.LogWarning("[RF] Unknown material " + name);
                    spec = S(name, null, null);
                }
                m = Create(spec);
            }
            if (simple) m = ToSimpleLit(m, GraphicsQuality.Level == 1);
            cache[name] = m;
            return m;
        }

        public static Shader ShaderFor(MatShader s)
        {
            switch (s)
            {
                case MatShader.Unlit: return Shader.Find("Universal Render Pipeline/Unlit");
                case MatShader.ParticleAlpha:
                case MatShader.ParticleAdd: return Shader.Find("Universal Render Pipeline/Particles/Unlit");
                case MatShader.SimpleLit: return Shader.Find("Universal Render Pipeline/Simple Lit");
                case MatShader.VertexLit: return Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
                case MatShader.VertexUnlit: return Shader.Find("Universal Render Pipeline/Particles/Unlit");
                default: return Shader.Find("Universal Render Pipeline/Lit");
            }
        }

        static Texture2D LoadTex(string name) => string.IsNullOrEmpty(name) ? null : RF.Tex(name);

        /// <summary>Creates a material from a spec. Used by the editor baker and as a runtime fallback.</summary>
        public static Material Create(MatSpec s, System.Func<string, Texture2D> texLoader = null)
        {
            texLoader = texLoader ?? LoadTex;
            var sh = ShaderFor(s.shader);
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit"); // "Standard" is magenta under URP
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = s.name, enableInstancing = true };
            var tex = texLoader(s.tex);

            if (s.shader == MatShader.ParticleAlpha || s.shader == MatShader.ParticleAdd)
            {
                if (tex != null) m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", s.color);
                SetTransparent(m, s.shader == MatShader.ParticleAdd);
                return m;
            }

            if (s.shader == MatShader.VertexLit || s.shader == MatShader.VertexUnlit)
            {
                // opaque; the colour comes from the vertices (particle shaders multiply by vertex colour)
                m.SetColor("_BaseColor", Color.white);
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = (int)RenderQueue.Geometry;
                return m;
            }

            if (s.shader == MatShader.Unlit)
            {
                if (tex != null) m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", s.color);
                return m;
            }

            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", s.color);
            m.SetFloat("_Smoothness", s.smooth);
            m.SetFloat("_Metallic", s.metal);
            var n = s.shader == MatShader.SimpleLit ? null : texLoader(s.normal);
            if (n != null)
            {
                m.SetTexture("_BumpMap", n);
                m.SetFloat("_BumpScale", s.normalScale);
                m.EnableKeyword("_NORMALMAP");
            }
            if (!string.IsNullOrEmpty(s.mask) && s.shader == MatShader.Lit)
            {
                var mt = texLoader(s.mask);
                if (mt != null)
                {
                    m.SetTexture("_MetallicGlossMap", mt);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetFloat("_Smoothness", 1f);
                    if (m.HasProperty("_SmoothnessTextureChannel")) m.SetFloat("_SmoothnessTextureChannel", 0f);
                    m.SetTexture("_OcclusionMap", mt);
                    m.SetFloat("_OcclusionStrength", 0.7f);
                    m.EnableKeyword("_OCCLUSIONMAP");
                }
                else
                {
                    m.SetFloat("_Smoothness", 0.4f);
                    m.SetFloat("_Metallic", 0f);
                }
            }
            if (s.emission.maxColorComponent > 0.001f)
            {
                m.SetColor("_EmissionColor", s.emission);
                var et = texLoader(s.emissionTex);
                if (et != null) m.SetTexture("_EmissionMap", et);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (s.doubleSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)CullMode.Off);
            if (s.alphaClip)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", s.cutoff);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
                m.SetOverrideTag("RenderType", "TransparentCutout");
            }
            if (s.transparent) SetTransparent(m, false);
            return m;
        }

        static void SetTransparent(Material m, bool additive)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        /// <summary>Per-renderer colour/emission change without breaking batching across the board.</summary>
        public static void Tint(Renderer r, Color? baseColor = null, Color? emission = null)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            if (baseColor.HasValue) mpb.SetColor("_BaseColor", baseColor.Value);
            if (emission.HasValue) mpb.SetColor("_EmissionColor", emission.Value);
            r.SetPropertyBlock(mpb);
        }
    }
}
