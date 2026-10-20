using System;
using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    public enum RobotKind { Grin, Rattle, Brutus }

    /// <summary>
    /// Procedurally modelled and animated animatronics. Three body plans:
    ///  • Mister Grin — a towering ringmaster mascot, half his face plate torn off showing the metal skull.
    ///  • Rattle — the ripped-in-half rabbit mascot that drags itself on long arms.
    ///  • Brutus — a hulking rotten bear with a shredded belly, chains and one empty eye socket.
    /// </summary>
    public class RobotRig : MonoBehaviour
    {
        public RobotKind kind;
        public Transform pelvis, spine, chest, neck, head, jaw;
        public Transform armL, foreL, armR, foreR;
        public Transform thighL, shinL, thighR, shinR;
        public Transform eyeLight;
        /// <summary>v1.4: anchor of the eye glow. The real light is ONE shared PointLight (RobotAI.UpdateSharedGlow)
        /// that follows the nearest awake robot, instead of one per robot.</summary>
        public Transform glowAnchor;
        public float GlowIntensity { get; private set; }
        public float GlowRange { get; private set; }
        public readonly List<Renderer> eyes = new List<Renderer>();
        public event Action<Vector3> OnFootstep;
        public float Height { get; private set; }

        float phase, prevSin, twitchT, twitchAmt;
        Vector3 pelvisBase;
        Quaternion headBase = Quaternion.identity;
        public float jumpscare;       // 0..1 blend
        public float stunned;         // 0..1 blend
        public float slipped;         // 0..1 blend
        public float dormant = 1f;    // 1 = slumped
        float scareT;

        // ------------------------------------------------------------------ build
        public static RobotRig Build(Transform parent, RobotKind kind)
        {
            var root = new GameObject("Rig_" + kind).transform;
            root.SetParent(parent, false);
            var rig = root.gameObject.AddComponent<RobotRig>();
            rig.kind = kind;
            // v1.0.3: sculpted, atlas-textured meshes; the primitive build stays as a fallback
            if (!rig.BuildFromMeshes())
            {
                switch (kind)
                {
                    case RobotKind.Grin: rig.BuildGrin(); break;
                    case RobotKind.Rattle: rig.BuildRattle(); break;
                    default: rig.BuildBrutus(); break;
                }
            }
            // v1.2: the crawler was ~2.7 m long with a 0.38 m collision radius and poked its head through walls;
            // scaled down it fits the 2 m maze corridors and doorways (RobotAI adds wall probes for the rest)
            if (kind == RobotKind.Rattle) root.localScale = Vector3.one * 0.82f;
            rig.pelvisBase = rig.pelvis.localPosition;
            if (rig.head != null) rig.headBase = rig.head.localRotation;
            // eye glow light
            var lgo = new GameObject("EyeGlow");
            lgo.transform.SetParent(rig.head, false);
            lgo.transform.localPosition = new Vector3(0, 0.05f, 0.45f);
            rig.glowAnchor = lgo.transform;
            rig.GlowRange = kind == RobotKind.Brutus ? 3.6f : 2.8f;
            rig.GlowIntensity = 0f;
            rig.SetEyes(false);
            return rig;
        }

        static Transform J(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static GameObject P(PrimitiveType t, Transform parent, string mat, Vector3 pos, Vector3 scale, Vector3 euler = default, bool shadows = true)
            => RF.Prim(t, parent, mat, pos, scale, euler, shadows);

        void Eye(Transform parentT, Vector3 pos, float size)
        {
            var e = P(PrimitiveType.Sphere, parentT, "eye_red", pos, Vector3.one * size, default, false);
            eyes.Add(e.GetComponent<Renderer>());
            // a soft glow sprite (additive) for the classic "eyes in the dark" look
            var g = new GameObject("Flare");
            g.transform.SetParent(e.transform, false);
            var ps = g.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = size * 6f;
            main.maxParticles = 1;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.startColor = new Color(1f, 0.15f, 0.08f, 0.55f);
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 1, 0, 1f) });
            var shape = ps.shape;
            shape.enabled = false;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MatLib.Get("fx_glow");
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        public void SetEyes(bool on)
        {
            foreach (var e in eyes)
            {
                if (e == null) continue;
                e.sharedMaterial = MatLib.Get(on ? "eye_red" : "eye_dim");
                var flare = e.transform.childCount > 0 ? e.transform.GetChild(0).gameObject : null;
                if (flare != null) flare.SetActive(on);
            }
            GlowIntensity = on ? (kind == RobotKind.Brutus ? 2.2f : 1.6f) : 0f;
        }

        // ------------------------------------------------------------------ v1.0.3 mesh build
        public static string MeshId(RobotKind k) => k == RobotKind.Grin ? "grin" : (k == RobotKind.Rattle ? "rattle" : "brutus");

        bool BuildFromMeshes()
        {
            string id = MeshId(kind);
            var lod0 = RobotMeshes.Load(id, 0);
            if (lod0 == null || lod0.Count == 0) return false;
            var lod1 = RobotMeshes.Load(id, 1);
            var bones = new Dictionary<string, Transform>();
            Transform B(Transform parent, string name, Vector3 pos)
            {
                var t = J(parent, name, pos);
                bones[name] = t;
                return t;
            }
            switch (kind)
            {
                case RobotKind.Grin:
                    Height = 2.45f;
                    pelvis = B(transform, "Pelvis", new Vector3(0, 1.18f, 0));
                    thighL = B(pelvis, "ThighL", new Vector3(-0.14f, -0.08f, 0));
                    thighR = B(pelvis, "ThighR", new Vector3(0.14f, -0.08f, 0));
                    shinL = B(thighL, "ShinL", new Vector3(0, -0.55f, 0));
                    shinR = B(thighR, "ShinR", new Vector3(0, -0.55f, 0));
                    spine = B(pelvis, "Spine", new Vector3(0, 0.12f, 0));
                    chest = B(spine, "Chest", new Vector3(0, 0.32f, 0));
                    armL = B(chest, "ArmL", new Vector3(-0.36f, 0.48f, 0));
                    armR = B(chest, "ArmR", new Vector3(0.36f, 0.48f, 0));
                    foreL = B(armL, "ForeL", new Vector3(0, -0.52f, 0));
                    foreR = B(armR, "ForeR", new Vector3(0, -0.52f, 0));
                    neck = B(chest, "Neck", new Vector3(0, 0.6f, 0));
                    head = B(neck, "Head", new Vector3(0, 0.2f, 0));
                    jaw = B(head, "Jaw", new Vector3(0, 0.02f, 0.05f));
                    break;
                case RobotKind.Rattle:
                    Height = 1.0f;
                    pelvis = B(transform, "Torso", new Vector3(0, 0.48f, -0.2f));
                    spine = B(pelvis, "Spine", new Vector3(0, -0.05f, -0.35f));
                    thighL = B(spine, "Stump", new Vector3(0.1f, -0.1f, -0.3f));
                    chest = B(pelvis, "Chest", new Vector3(0, 0.05f, 0.42f));
                    armL = B(chest, "ArmL", new Vector3(-0.27f, 0.05f, 0.05f));
                    armR = B(chest, "ArmR", new Vector3(0.27f, 0.05f, 0.05f));
                    foreL = B(armL, "ForeL", new Vector3(0, -0.6f, 0));
                    foreR = B(armR, "ForeR", new Vector3(0, -0.6f, 0));
                    neck = B(chest, "Neck", new Vector3(0, 0.12f, 0.2f));
                    head = B(neck, "Head", new Vector3(0, 0.08f, 0.12f));
                    jaw = B(head, "Jaw", new Vector3(0, -0.06f, 0.05f));
                    head.localRotation = Quaternion.Euler(-20, 0, 28);
                    break;
                default:
                    Height = 2.75f;
                    pelvis = B(transform, "Pelvis", new Vector3(0, 1.15f, 0));
                    thighL = B(pelvis, "ThighL", new Vector3(-0.26f, -0.12f, 0));
                    thighR = B(pelvis, "ThighR", new Vector3(0.26f, -0.12f, 0));
                    shinL = B(thighL, "ShinL", new Vector3(0, -0.52f, 0));
                    shinR = B(thighR, "ShinR", new Vector3(0, -0.52f, 0));
                    spine = B(pelvis, "Spine", new Vector3(0, 0.2f, 0));
                    chest = B(spine, "Chest", new Vector3(0, 0.38f, 0));
                    armL = B(chest, "ArmL", new Vector3(-0.58f, 0.45f, 0));
                    armR = B(chest, "ArmR", new Vector3(0.58f, 0.45f, 0));
                    foreL = B(armL, "ForeL", new Vector3(0, -0.62f, 0));
                    foreR = B(armR, "ForeR", new Vector3(0, -0.62f, 0));
                    neck = B(chest, "Neck", new Vector3(0, 0.62f, 0.05f));
                    head = B(neck, "Head", new Vector3(0, 0.22f, 0.05f));
                    jaw = B(head, "Jaw", new Vector3(0, -0.04f, 0.12f));
                    break;
            }
            var mat = MatLib.Get("robot_" + id);
            var matCut = MatLib.Get("robot_" + id + "_cut");
            var r0 = new List<Renderer>();
            var r1 = new List<Renderer>();
            foreach (var kv in bones)
            {
                if (lod0.TryGetValue(kv.Key, out var m0)) r0.Add(AttachMesh(kv.Value, m0, "LOD0", mat, matCut));
                if (lod1 != null && lod1.TryGetValue(kv.Key, out var m1)) r1.Add(AttachMesh(kv.Value, m1, "LOD1", mat, matCut));
            }
            if (r1.Count > 0)
            {
                // close up / chase distance: full mesh; further away the light version (lodBias scales this per quality tier)
                var lg = gameObject.AddComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(0.16f, r0.ToArray()), new LOD(0.008f, r1.ToArray()) });
                lg.RecalculateBounds();
            }
            switch (kind)
            {
                case RobotKind.Grin:
                    Eye(head, new Vector3(-0.12f, 0.22f, 0.235f), 0.06f);
                    Eye(head, new Vector3(0.14f, 0.2f, 0.215f), 0.05f);
                    break;
                case RobotKind.Rattle:
                    Eye(head, new Vector3(-0.1f, 0.12f, 0.205f), 0.045f);
                    Eye(head, new Vector3(0.1f, 0.1f, 0.2f), 0.035f);
                    break;
                default:
                    Eye(head, new Vector3(-0.15f, 0.28f, 0.3f), 0.075f);
                    Eye(head, new Vector3(0.16f, 0.27f, 0.25f), 0.035f);
                    break;
            }
            eyeLight = head;
            meshed = true;
            return true;
        }

        static Renderer AttachMesh(Transform joint, Mesh mesh, string name, Material mat, Material matCut)
        {
            var go = new GameObject(name);
            go.transform.SetParent(joint, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mesh.subMeshCount > 1 ? new[] { mat, matCut } : new[] { mat };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return r;
        }

        void BuildGrin()
        {
            Height = 2.45f;
            pelvis = J(transform, "Pelvis", new Vector3(0, 1.18f, 0));
            P(PrimitiveType.Cube, pelvis, "fabric_purple", Vector3.zero, new Vector3(0.42f, 0.26f, 0.26f));
            // legs (bare endoskeleton under ragged trousers)
            thighL = J(pelvis, "ThighL", new Vector3(-0.14f, -0.08f, 0));
            thighR = J(pelvis, "ThighR", new Vector3(0.14f, -0.08f, 0));
            foreach (var th in new[] { thighL, thighR })
            {
                P(PrimitiveType.Cylinder, th, "fabric_purple", new Vector3(0, -0.22f, 0), new Vector3(0.17f, 0.22f, 0.17f));
                P(PrimitiveType.Cylinder, th, "endo", new Vector3(0, -0.42f, 0), new Vector3(0.06f, 0.12f, 0.06f), default, false);
                var sh = J(th, "Shin", new Vector3(0, -0.55f, 0));
                P(PrimitiveType.Sphere, sh, "endo", Vector3.zero, Vector3.one * 0.1f, default, false);
                P(PrimitiveType.Cylinder, sh, "endo", new Vector3(0, -0.27f, 0), new Vector3(0.05f, 0.27f, 0.05f));
                P(PrimitiveType.Cylinder, sh, "rubber", new Vector3(0.03f, -0.3f, 0), new Vector3(0.012f, 0.25f, 0.012f), new Vector3(0, 0, 6), false);
                P(PrimitiveType.Cube, sh, "black_matte", new Vector3(0, -0.56f, 0.08f), new Vector3(0.14f, 0.08f, 0.32f));
                if (th == thighL) shinL = sh; else shinR = sh;
            }
            spine = J(pelvis, "Spine", new Vector3(0, 0.12f, 0));
            P(PrimitiveType.Cylinder, spine, "endo", new Vector3(0, 0.15f, 0), new Vector3(0.08f, 0.15f, 0.08f), default, false);
            chest = J(spine, "Chest", new Vector3(0, 0.32f, 0));
            P(PrimitiveType.Cube, chest, "fabric_purple", new Vector3(0, 0.25f, 0), new Vector3(0.62f, 0.62f, 0.34f));
            P(PrimitiveType.Cube, chest, "white_paint", new Vector3(0, 0.3f, 0.16f), new Vector3(0.22f, 0.5f, 0.04f), default, false);
            P(PrimitiveType.Cube, chest, "red_paint", new Vector3(0, 0.5f, 0.19f), new Vector3(0.2f, 0.08f, 0.04f), default, false);
            for (int i = 0; i < 3; i++) P(PrimitiveType.Sphere, chest, "gold_trim", new Vector3(0.14f, 0.12f + i * 0.13f, 0.18f), Vector3.one * 0.05f, default, false);
            // coat tails + torn hole showing ribs
            P(PrimitiveType.Cube, chest, "fabric_purple", new Vector3(-0.15f, -0.35f, -0.15f), new Vector3(0.2f, 0.7f, 0.04f), new Vector3(8, 0, 4));
            P(PrimitiveType.Cube, chest, "fabric_purple", new Vector3(0.15f, -0.32f, -0.15f), new Vector3(0.2f, 0.62f, 0.04f), new Vector3(8, 0, -6));
            P(PrimitiveType.Cube, chest, "black_matte", new Vector3(-0.2f, 0.22f, 0.165f), new Vector3(0.16f, 0.22f, 0.02f), default, false);
            for (int i = 0; i < 3; i++) P(PrimitiveType.Cube, chest, "endo", new Vector3(-0.2f, 0.15f + i * 0.07f, 0.17f), new Vector3(0.14f, 0.02f, 0.02f), default, false);
            // arms
            armL = J(chest, "ArmL", new Vector3(-0.36f, 0.48f, 0));
            armR = J(chest, "ArmR", new Vector3(0.36f, 0.48f, 0));
            foreach (var a in new[] { armL, armR })
            {
                bool left = a == armL;
                P(PrimitiveType.Sphere, a, "fabric_purple", Vector3.zero, Vector3.one * 0.18f);
                P(PrimitiveType.Cylinder, a, left ? "fabric_purple" : "endo", new Vector3(0, -0.26f, 0), left ? new Vector3(0.13f, 0.26f, 0.13f) : new Vector3(0.05f, 0.26f, 0.05f));
                var f = J(a, "Fore", new Vector3(0, -0.52f, 0));
                P(PrimitiveType.Cylinder, f, "endo", new Vector3(0, -0.26f, 0), new Vector3(0.05f, 0.26f, 0.05f));
                P(PrimitiveType.Sphere, f, "white_paint", new Vector3(0, -0.56f, 0), new Vector3(0.13f, 0.14f, 0.09f));
                for (int k = 0; k < 4; k++)
                    P(PrimitiveType.Cylinder, f, "endo", new Vector3(-0.045f + k * 0.03f, -0.72f, 0.02f), new Vector3(0.015f, 0.12f, 0.015f), new Vector3(10, 0, -8 + k * 5), false);
                if (left) foreL = f; else foreR = f;
            }
            neck = J(chest, "Neck", new Vector3(0, 0.6f, 0));
            P(PrimitiveType.Cylinder, neck, "endo", new Vector3(0, 0.08f, 0), new Vector3(0.07f, 0.1f, 0.07f), default, false);
            head = J(neck, "Head", new Vector3(0, 0.2f, 0));
            // left half: cracked yellowed mascot face; right half: exposed metal skull
            P(PrimitiveType.Sphere, head, "plastic", new Vector3(-0.06f, 0.16f, 0), new Vector3(0.52f, 0.56f, 0.5f));
            P(PrimitiveType.Sphere, head, "endo", new Vector3(0.12f, 0.15f, 0.04f), new Vector3(0.36f, 0.42f, 0.4f));
            P(PrimitiveType.Sphere, head, "black_matte", new Vector3(0.14f, 0.2f, 0.18f), new Vector3(0.14f, 0.12f, 0.08f), default, false);
            P(PrimitiveType.Sphere, head, "black_matte", new Vector3(-0.12f, 0.22f, 0.21f), new Vector3(0.13f, 0.11f, 0.06f), default, false);
            Eye(head, new Vector3(-0.12f, 0.22f, 0.235f), 0.06f);
            Eye(head, new Vector3(0.14f, 0.2f, 0.215f), 0.05f);
            // wide painted grin + jaw
            P(PrimitiveType.Cube, head, "red_paint", new Vector3(-0.04f, 0.06f, 0.22f), new Vector3(0.34f, 0.05f, 0.06f), new Vector3(0, 0, -6), false);
            jaw = J(head, "Jaw", new Vector3(0, 0.02f, 0.05f));
            P(PrimitiveType.Cube, jaw, "plastic", new Vector3(-0.04f, -0.05f, 0.1f), new Vector3(0.36f, 0.08f, 0.24f));
            for (int i = 0; i < 9; i++)
            {
                P(PrimitiveType.Cube, head, "teeth", new Vector3(-0.19f + i * 0.042f, 0.035f, 0.225f), new Vector3(0.03f, 0.05f, 0.02f), default, false);
                P(PrimitiveType.Cube, jaw, "teeth", new Vector3(-0.19f + i * 0.042f, -0.0f, 0.21f), new Vector3(0.03f, 0.05f, 0.02f), default, false);
            }
            // top hat, tilted
            var hat = J(head, "Hat", new Vector3(-0.04f, 0.42f, -0.02f));
            hat.localRotation = Quaternion.Euler(-8, 0, 14);
            P(PrimitiveType.Cylinder, hat, "black_matte", new Vector3(0, 0.02f, 0), new Vector3(0.48f, 0.012f, 0.48f));
            P(PrimitiveType.Cylinder, hat, "black_matte", new Vector3(0, 0.2f, 0), new Vector3(0.3f, 0.18f, 0.3f));
            P(PrimitiveType.Cylinder, hat, "red_paint", new Vector3(0, 0.07f, 0), new Vector3(0.305f, 0.035f, 0.305f), default, false);
            eyeLight = head;
        }

        void BuildBrutus()
        {
            Height = 2.75f;
            pelvis = J(transform, "Pelvis", new Vector3(0, 1.15f, 0));
            P(PrimitiveType.Sphere, pelvis, "fur", Vector3.zero, new Vector3(0.85f, 0.5f, 0.6f));
            thighL = J(pelvis, "ThighL", new Vector3(-0.26f, -0.12f, 0));
            thighR = J(pelvis, "ThighR", new Vector3(0.26f, -0.12f, 0));
            foreach (var th in new[] { thighL, thighR })
            {
                P(PrimitiveType.Capsule, th, "fur", new Vector3(0, -0.26f, 0), new Vector3(0.36f, 0.32f, 0.36f));
                var sh = J(th, "Shin", new Vector3(0, -0.52f, 0));
                P(PrimitiveType.Capsule, sh, th == thighL ? "fur" : "fur_dark", new Vector3(0, -0.24f, 0), new Vector3(0.3f, 0.28f, 0.3f));
                if (th == thighR) P(PrimitiveType.Cylinder, sh, "endo", new Vector3(0.12f, -0.25f, 0.1f), new Vector3(0.05f, 0.22f, 0.05f), default, false);
                P(PrimitiveType.Sphere, sh, "fur_dark", new Vector3(0, -0.56f, 0.1f), new Vector3(0.34f, 0.16f, 0.48f));
                for (int k = 0; k < 3; k++) P(PrimitiveType.Cube, sh, "bone", new Vector3(-0.08f + k * 0.08f, -0.58f, 0.34f), new Vector3(0.04f, 0.04f, 0.1f), default, false);
                if (th == thighL) shinL = sh; else shinR = sh;
            }
            spine = J(pelvis, "Spine", new Vector3(0, 0.2f, 0));
            chest = J(spine, "Chest", new Vector3(0, 0.38f, 0));
            P(PrimitiveType.Sphere, chest, "fur", new Vector3(0, 0.15f, 0), new Vector3(1.15f, 1.05f, 0.85f));
            P(PrimitiveType.Sphere, chest, "plastic_dark", new Vector3(0, 0.0f, 0.22f), new Vector3(0.65f, 0.7f, 0.45f));
            // ripped belly with ribs and wires
            P(PrimitiveType.Sphere, chest, "black_matte", new Vector3(0.15f, -0.05f, 0.4f), new Vector3(0.36f, 0.42f, 0.12f), default, false);
            for (int i = 0; i < 4; i++) P(PrimitiveType.Cube, chest, "endo", new Vector3(0.15f, -0.18f + i * 0.09f, 0.45f), new Vector3(0.3f, 0.025f, 0.04f), default, false);
            P(PrimitiveType.Cylinder, chest, "red_paint", new Vector3(0.2f, -0.35f, 0.42f), new Vector3(0.015f, 0.18f, 0.015f), new Vector3(0, 0, 20), false);
            // chains
            for (int i = 0; i < 7; i++)
                P(PrimitiveType.Cylinder, chest, "metal_rust", new Vector3(-0.35f + i * 0.11f, 0.55f - Mathf.Sin(i / 6f * Mathf.PI) * 0.35f, 0.38f), new Vector3(0.05f, 0.04f, 0.05f), new Vector3(90, i * 40, 0), false);
            armL = J(chest, "ArmL", new Vector3(-0.58f, 0.45f, 0));
            armR = J(chest, "ArmR", new Vector3(0.58f, 0.45f, 0));
            foreach (var a in new[] { armL, armR })
            {
                P(PrimitiveType.Capsule, a, "fur", new Vector3(0, -0.3f, 0), new Vector3(0.34f, 0.34f, 0.34f));
                var f = J(a, "Fore", new Vector3(0, -0.62f, 0));
                P(PrimitiveType.Capsule, f, a == armL ? "fur_dark" : "fur", new Vector3(0, -0.28f, 0), new Vector3(0.28f, 0.3f, 0.28f));
                P(PrimitiveType.Sphere, f, "fur_dark", new Vector3(0, -0.62f, 0.03f), new Vector3(0.3f, 0.24f, 0.24f));
                for (int k = 0; k < 4; k++) P(PrimitiveType.Cube, f, "bone", new Vector3(-0.09f + k * 0.06f, -0.74f, 0.1f), new Vector3(0.03f, 0.12f, 0.03f), new Vector3(25, 0, 0), false);
                if (a == armL) foreL = f; else foreR = f;
            }
            neck = J(chest, "Neck", new Vector3(0, 0.62f, 0.05f));
            head = J(neck, "Head", new Vector3(0, 0.22f, 0.05f));
            P(PrimitiveType.Sphere, head, "fur", new Vector3(0, 0.15f, 0), new Vector3(0.78f, 0.7f, 0.7f));
            P(PrimitiveType.Sphere, head, "fur", new Vector3(-0.3f, 0.48f, 0), new Vector3(0.24f, 0.24f, 0.1f));
            P(PrimitiveType.Sphere, head, "endo", new Vector3(0.3f, 0.45f, 0), new Vector3(0.12f, 0.2f, 0.06f), default, false);
            P(PrimitiveType.Sphere, head, "plastic_dark", new Vector3(0, 0.05f, 0.3f), new Vector3(0.38f, 0.28f, 0.3f));
            P(PrimitiveType.Sphere, head, "black_matte", new Vector3(0, 0.12f, 0.45f), new Vector3(0.12f, 0.08f, 0.06f), default, false);
            // one glaring eye, one deep empty socket with a red light inside
            P(PrimitiveType.Sphere, head, "black_matte", new Vector3(-0.15f, 0.28f, 0.27f), new Vector3(0.15f, 0.13f, 0.08f), default, false);
            Eye(head, new Vector3(-0.15f, 0.28f, 0.3f), 0.075f);
            P(PrimitiveType.Sphere, head, "black_matte", new Vector3(0.16f, 0.27f, 0.27f), new Vector3(0.16f, 0.15f, 0.1f), default, false);
            Eye(head, new Vector3(0.16f, 0.27f, 0.25f), 0.035f);
            jaw = J(head, "Jaw", new Vector3(0, -0.04f, 0.12f));
            P(PrimitiveType.Sphere, jaw, "plastic_dark", new Vector3(0, -0.06f, 0.18f), new Vector3(0.34f, 0.12f, 0.28f));
            for (int i = 0; i < 7; i++)
            {
                P(PrimitiveType.Cube, head, "teeth", new Vector3(-0.13f + i * 0.043f, -0.02f, 0.42f), new Vector3(0.03f, 0.07f, 0.03f), new Vector3(0, 0, i % 2 == 0 ? 6 : -6), false);
                P(PrimitiveType.Cube, jaw, "teeth", new Vector3(-0.11f + i * 0.037f, 0.0f, 0.3f), new Vector3(0.028f, 0.06f, 0.03f), default, false);
            }
            eyeLight = head;
        }

        void BuildRattle()
        {
            Height = 1.0f;
            // the "pelvis" is the torso centre lying almost horizontally
            pelvis = J(transform, "Torso", new Vector3(0, 0.48f, -0.2f));
            P(PrimitiveType.Capsule, pelvis, "plastic", new Vector3(0, 0, 0.1f), new Vector3(0.48f, 0.42f, 0.4f), new Vector3(80, 0, 0));
            P(PrimitiveType.Sphere, pelvis, "black_matte", new Vector3(0.1f, 0.1f, -0.1f), new Vector3(0.25f, 0.18f, 0.3f), default, false);
            // torn waist: spine and wires dragging behind
            spine = J(pelvis, "Spine", new Vector3(0, -0.05f, -0.35f));
            for (int i = 0; i < 4; i++)
                P(PrimitiveType.Cylinder, spine, "endo", new Vector3(0, -0.05f * i, -0.12f * i), new Vector3(0.07f, 0.06f, 0.07f), new Vector3(80, 0, 0), i == 0);
            for (int i = 0; i < 4; i++)
                P(PrimitiveType.Cylinder, spine, i % 2 == 0 ? "red_paint" : "rubber", new Vector3(-0.08f + i * 0.05f, -0.18f, -0.4f - i * 0.05f), new Vector3(0.012f, 0.3f, 0.012f), new Vector3(70 + i * 5, 0, i * 10 - 15), false);
            // a single broken leg stump
            thighL = J(spine, "Stump", new Vector3(0.1f, -0.1f, -0.3f));
            P(PrimitiveType.Cylinder, thighL, "endo", new Vector3(0, -0.15f, -0.1f), new Vector3(0.05f, 0.2f, 0.05f), new Vector3(60, 0, 0), false);
            chest = J(pelvis, "Chest", new Vector3(0, 0.05f, 0.42f));
            P(PrimitiveType.Sphere, chest, "plastic", Vector3.zero, new Vector3(0.52f, 0.42f, 0.42f));
            // long arms
            armL = J(chest, "ArmL", new Vector3(-0.27f, 0.05f, 0.05f));
            armR = J(chest, "ArmR", new Vector3(0.27f, 0.05f, 0.05f));
            foreach (var a in new[] { armL, armR })
            {
                P(PrimitiveType.Cylinder, a, "plastic", new Vector3(0, -0.3f, 0), new Vector3(0.1f, 0.3f, 0.1f));
                var f = J(a, "Fore", new Vector3(0, -0.6f, 0));
                P(PrimitiveType.Cylinder, f, "endo", new Vector3(0, -0.32f, 0), new Vector3(0.045f, 0.32f, 0.045f));
                P(PrimitiveType.Sphere, f, "plastic_dark", new Vector3(0, -0.66f, 0.02f), new Vector3(0.12f, 0.06f, 0.16f));
                for (int k = 0; k < 4; k++) P(PrimitiveType.Cylinder, f, "endo", new Vector3(-0.04f + k * 0.027f, -0.7f, 0.1f), new Vector3(0.012f, 0.08f, 0.012f), new Vector3(70, 0, 0), false);
                if (a == armL) foreL = f; else foreR = f;
            }
            neck = J(chest, "Neck", new Vector3(0, 0.12f, 0.2f));
            head = J(neck, "Head", new Vector3(0, 0.08f, 0.12f));
            P(PrimitiveType.Sphere, head, "plastic", new Vector3(0, 0.05f, 0), new Vector3(0.42f, 0.38f, 0.44f));
            // ears: one tall, one snapped and dangling
            P(PrimitiveType.Capsule, head, "plastic", new Vector3(-0.12f, 0.42f, -0.04f), new Vector3(0.1f, 0.3f, 0.06f), new Vector3(-10, 0, 10));
            P(PrimitiveType.Capsule, head, "plastic_dark", new Vector3(0.22f, 0.1f, -0.05f), new Vector3(0.09f, 0.25f, 0.05f), new Vector3(0, 0, -120), false);
            P(PrimitiveType.Sphere, head, "black_matte", new Vector3(-0.1f, 0.12f, 0.19f), new Vector3(0.11f, 0.12f, 0.06f), default, false);
            P(PrimitiveType.Sphere, head, "black_matte", new Vector3(0.1f, 0.1f, 0.19f), new Vector3(0.1f, 0.11f, 0.06f), default, false);
            Eye(head, new Vector3(-0.1f, 0.12f, 0.205f), 0.045f);
            Eye(head, new Vector3(0.1f, 0.1f, 0.2f), 0.035f);
            jaw = J(head, "Jaw", new Vector3(0, -0.06f, 0.05f));
            P(PrimitiveType.Sphere, jaw, "plastic", new Vector3(0, -0.05f, 0.08f), new Vector3(0.28f, 0.1f, 0.24f));
            for (int i = 0; i < 8; i++)
            {
                P(PrimitiveType.Cube, head, "teeth", new Vector3(-0.1f + i * 0.028f, -0.04f, 0.2f), new Vector3(0.02f, 0.05f, 0.02f), default, false);
                P(PrimitiveType.Cube, jaw, "teeth", new Vector3(-0.09f + i * 0.026f, 0.0f, 0.18f), new Vector3(0.02f, 0.045f, 0.02f), default, false);
            }
            head.localRotation = Quaternion.Euler(-20, 0, 28);
            eyeLight = head;
        }

        // ------------------------------------------------------------------ animate
        /// <summary>speed in m/s; running = chasing gait.</summary>
        public void Animate(float speed, bool running, float dt)
        {
            float t = Time.time;
            float speedNorm = Mathf.Clamp(speed / (kind == RobotKind.Brutus ? 2.2f : 2.6f), 0f, 2.4f);
            float freq = kind == RobotKind.Brutus ? 1.5f : (kind == RobotKind.Rattle ? 2.6f : 1.8f);
            phase += dt * freq * Mathf.Max(0.0f, speedNorm) * Mathf.PI;
            float s = Mathf.Sin(phase);
            float c = Mathf.Cos(phase);
            float swing = Mathf.Min(1f, speedNorm) * (running ? 1.25f : 1f);

            // footsteps when the gait crosses zero
            if (speed > 0.25f && ((prevSin < 0f && s >= 0f) || (prevSin > 0f && s <= 0f)))
                OnFootstep?.Invoke(transform.position + transform.forward * 0.3f);
            prevSin = s;

            // random servo twitches (unnatural jerks)
            twitchT -= dt;
            if (twitchT <= 0f)
            {
                twitchT = UnityEngine.Random.Range(0.6f, 3.2f);
                twitchAmt = UnityEngine.Random.Range(-1f, 1f);
            }
            float twitch = twitchAmt * Mathf.Clamp01(1f - (twitchT % 0.25f) * 6f);

            switch (kind)
            {
                case RobotKind.Rattle: AnimateCrawler(s, c, swing, running, twitch, t); break;
                default: AnimateBiped(s, c, swing, running, twitch, t); break;
            }
            ApplyOverlays(dt, t);
        }

        void AnimateBiped(float s, float c, float swing, bool running, float twitch, float t)
        {
            bool brute = kind == RobotKind.Brutus;
            float legAmp = (brute ? 28f : 34f) * swing;
            thighL.localRotation = Quaternion.Euler(s * legAmp, 0, 0);
            thighR.localRotation = Quaternion.Euler(-s * legAmp, 0, 0);
            shinL.localRotation = Quaternion.Euler(-Mathf.Max(0f, -c) * legAmp * 1.4f, 0, 0);
            shinR.localRotation = Quaternion.Euler(-Mathf.Max(0f, c) * legAmp * 1.4f, 0, 0);
            float bob = Mathf.Abs(c) * 0.05f * swing * (brute ? 1.4f : 1f);
            pelvis.localPosition = pelvisBase + new Vector3(0, bob - (running ? 0.05f : 0f), 0);
            pelvis.localRotation = Quaternion.Euler(0, s * 6f * swing, (brute ? s * 7f : 0f) * swing);
            float lean = running ? (brute ? 18f : 22f) : 6f;
            spine.localRotation = Quaternion.Euler(lean * Mathf.Min(1f, swing + 0.2f), -s * 8f * swing, 0);
            float armAmp = (running ? 50f : 28f) * swing;
            armL.localRotation = Quaternion.Euler(-s * armAmp - (running ? 25f : 0f), 0, -8f);
            armR.localRotation = Quaternion.Euler(s * armAmp - (running ? 25f : 0f), 0, 8f);
            foreL.localRotation = Quaternion.Euler(-30f - Mathf.Max(0, s) * 30f * swing, 0, 0);
            foreR.localRotation = Quaternion.Euler(-30f - Mathf.Max(0, -s) * 30f * swing, 0, 0);
            if (kind == RobotKind.Grin)
            {
                // unnervingly long arms dangle and sway
                armL.localRotation *= Quaternion.Euler(0, 0, -Mathf.Sin(t * 1.3f) * 5f);
                armR.localRotation *= Quaternion.Euler(0, 0, Mathf.Sin(t * 1.1f) * 5f);
            }
            head.localRotation = headBase * Quaternion.Euler(Mathf.Sin(t * 0.7f) * 4f + twitch * 12f, Mathf.Sin(t * 0.43f) * 14f * (1f - swing * 0.5f) + twitch * 25f, twitch * 18f);
            jaw.localRotation = Quaternion.Euler(6f + Mathf.Max(0, Mathf.Sin(t * 3.7f)) * 6f + Mathf.Abs(twitch) * 20f, 0, 0);
        }

        void AnimateCrawler(float s, float c, float swing, bool running, float twitch, float t)
        {
            // arms alternate reaching forward and pulling back
            float reach = 55f * swing;
            armL.localRotation = Quaternion.Euler(-70f + s * reach, 0, -10f);
            armR.localRotation = Quaternion.Euler(-70f - s * reach, 0, 10f);
            foreL.localRotation = Quaternion.Euler(-20f - Mathf.Max(0, c) * 50f * swing, 0, 0);
            foreR.localRotation = Quaternion.Euler(-20f - Mathf.Max(0, -c) * 50f * swing, 0, 0);
            pelvis.localPosition = pelvisBase + new Vector3(Mathf.Sin(phase * 0.5f) * 0.05f * swing, Mathf.Abs(s) * 0.07f * swing, 0);
            pelvis.localRotation = Quaternion.Euler(-12f + Mathf.Abs(c) * 6f, s * 12f * swing, s * 8f * swing);
            spine.localRotation = Quaternion.Euler(Mathf.Sin(t * 5f) * 4f, -s * 18f * swing, 0);
            head.localRotation = headBase * Quaternion.Euler(Mathf.Sin(t * 9f) * 3f + twitch * 25f, twitch * 35f, Mathf.Sin(t * 1.7f) * 10f + twitch * 30f);
            jaw.localRotation = Quaternion.Euler(18f + Mathf.Abs(Mathf.Sin(t * 13f)) * 12f, 0, 0);
        }

        void ApplyOverlays(float dt, float t)
        {
            if (dormant > 0.001f)
            {
                // slumped, powered-down pose
                spine.localRotation = Quaternion.Slerp(spine.localRotation, Quaternion.Euler(kind == RobotKind.Rattle ? 0 : 35f, 0, 6f), dormant);
                head.localRotation = Quaternion.Slerp(head.localRotation, headBase * Quaternion.Euler(40f, 10f, 20f), dormant);
                armL.localRotation = Quaternion.Slerp(armL.localRotation, Quaternion.Euler(-5f, 0, -4f), dormant);
                armR.localRotation = Quaternion.Slerp(armR.localRotation, Quaternion.Euler(-5f, 0, 4f), dormant);
                jaw.localRotation = Quaternion.Slerp(jaw.localRotation, Quaternion.Euler(25f, 0, 0), dormant);
            }
            if (stunned > 0.001f)
            {
                float j = Mathf.PerlinNoise(t * 25f, 1.3f) - 0.5f;
                head.localRotation *= Quaternion.Euler(j * 60f * stunned, (Mathf.PerlinNoise(t * 30f, 9f) - 0.5f) * 80f * stunned, j * 40f * stunned);
                armL.localRotation *= Quaternion.Euler(j * 70f * stunned, 0, 0);
                armR.localRotation *= Quaternion.Euler(-j * 70f * stunned, 0, 0);
                jaw.localRotation = Quaternion.Euler(30f * stunned + j * 20f, 0, 0);
            }
            if (slipped > 0.001f)
            {
                // fallen over sideways
                pelvis.localPosition = Vector3.Lerp(pelvis.localPosition, new Vector3(0, 0.35f, 0), slipped);
                pelvis.localRotation = Quaternion.Slerp(pelvis.localRotation, Quaternion.Euler(-80f, 0, 25f), slipped);
            }
            if (jumpscare > 0.001f)
            {
                scareT += dt;
                float j1 = (Mathf.PerlinNoise(scareT * 40f, 0.5f) - 0.5f) * 2f;
                float j2 = (Mathf.PerlinNoise(0.5f, scareT * 40f) - 0.5f) * 2f;
                spine.localRotation = Quaternion.Slerp(spine.localRotation, Quaternion.Euler(kind == RobotKind.Rattle ? -30f : 25f, 0, 0), jumpscare);
                head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(j1 * 14f - (kind == RobotKind.Rattle ? 30f : 5f), j2 * 18f, j1 * 22f), jumpscare);
                jaw.localRotation = Quaternion.Slerp(jaw.localRotation, Quaternion.Euler(55f + j2 * 12f, 0, 0), jumpscare);
                armL.localRotation = Quaternion.Slerp(armL.localRotation, Quaternion.Euler(-120f + j1 * 15f, -20f, -25f), jumpscare);
                armR.localRotation = Quaternion.Slerp(armR.localRotation, Quaternion.Euler(-120f + j2 * 15f, 20f, 25f), jumpscare);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-40f, 0, 0), jumpscare);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-40f, 0, 0), jumpscare);
            }
            else scareT = 0f;
        }

        /// <summary>World position of the face (jumpscare framing).</summary>
        /// <summary>v1.0.3: the sculpted heads are big - frame the face surface, not the neck pivot.</summary>
        public Vector3 FacePosition => meshed
            ? head.TransformPoint(kind == RobotKind.Grin ? new Vector3(-0.03f, 0.17f, 0.24f) : (kind == RobotKind.Rattle ? new Vector3(0f, 0.06f, 0.22f) : new Vector3(0f, 0.13f, 0.42f)))
            : head.position + head.up * (kind == RobotKind.Brutus ? 0.15f : 0.12f);
        bool meshed;

        /// <summary>Brutus' mouth (the grab jumpscare carries the camera into it).</summary>
        public Vector3 MouthPosition => head.TransformPoint(new Vector3(0f, -0.04f, 0.44f));

        /// <summary>v1.0.3: world-space 2-bone IK (shoulder, elbow, paw point in elbow space), blended by k.</summary>
        public static void IK2(Transform arm, Transform fore, Vector3 pawLocal, Vector3 target, Vector3 pole, float k)
        {
            Quaternion a0 = arm.localRotation, f0 = fore.localRotation;
            Vector3 S = arm.position, E0 = fore.position, P0 = fore.TransformPoint(pawLocal);
            float L1 = Vector3.Distance(S, E0), L2 = Vector3.Distance(E0, P0);
            Vector3 dv = target - S;
            float d = Mathf.Clamp(dv.magnitude, Mathf.Abs(L1 - L2) + 0.01f, L1 + L2 - 0.01f);
            Vector3 dir = dv.normalized;
            float ca = Mathf.Clamp((L1 * L1 + d * d - L2 * L2) / (2f * L1 * d), -1f, 1f), sa = Mathf.Sqrt(1f - ca * ca);
            Vector3 perp = (pole - Vector3.Dot(pole, dir) * dir).normalized;
            Vector3 E = S + dir * (ca * L1) + perp * (sa * L1);
            arm.rotation = Quaternion.FromToRotation(E0 - S, E - S) * arm.rotation;
            Vector3 E1 = fore.position, P1 = fore.TransformPoint(pawLocal), T = S + dir * d;
            fore.rotation = Quaternion.FromToRotation(P1 - E1, T - E1) * fore.rotation;
            if (k < 1f)
            {
                arm.localRotation = Quaternion.Slerp(a0, arm.localRotation, k);
                fore.localRotation = Quaternion.Slerp(f0, fore.localRotation, k);
            }
        }
    }
}
