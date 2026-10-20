using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    static class PieceUtil
    {
        public static GameObject MeshObj(Transform parent, string name, MeshBuilder mb, string mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MatLib.Get(mat);
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }

    // ===================================================================== Carousel
    public class Carousel : MonoBehaviour
    {
        public Transform spin;
        public Transform hatch;
        readonly List<Transform> horses = new List<Transform>();
        readonly List<float> horsePhase = new List<float>();
        float speed; // deg/s
        float bobT;
        AudioSource music;
        public bool Spinning { get; private set; }

        public static Carousel Create(Transform parent, Vector3 center, float r)
        {
            var root = new GameObject("Carousel").transform;
            root.SetParent(parent, false);
            root.position = center;
            var c = root.gameObject.AddComponent<Carousel>();

            // static outer step + hatch
            var step = new MeshBuilder();
            step.Cylinder(Vector3.zero, r + 0.45f, 0.3f, 40, 1.5f);
            PieceUtil.MeshObj(root, "Step", step, "wood");
            var hatchHinge = new GameObject("HatchHinge").transform;
            hatchHinge.SetParent(root, false);
            hatchHinge.localPosition = new Vector3(0, 0.31f, -(r + 0.05f));
            RF.Prim(PrimitiveType.Cube, hatchHinge, "gold_trim", new Vector3(0, 0, -0.3f), new Vector3(0.8f, 0.04f, 0.6f));
            c.hatch = hatchHinge;

            c.spin = new GameObject("Spin").transform;
            c.spin.SetParent(root, false);
            var plat = new MeshBuilder();
            plat.Cylinder(new Vector3(0, 0.3f, 0), r, 0.25f, 40, 1.5f);
            PieceUtil.MeshObj(c.spin, "Platform", plat, "wood_painted");
            var pole = new MeshBuilder();
            pole.Cylinder(new Vector3(0, 0.5f, 0), 0.7f, 6.7f, 16, 1.5f);
            PieceUtil.MeshObj(c.spin, "Core", pole, "wood_painted");
            // mirror panels around the core
            var mirrors = new MeshBuilder();
            mirrors.Cylinder(new Vector3(0, 1.5f, 0), 0.74f, 3.5f, 12, 1f, false);
            PieceUtil.MeshObj(c.spin, "CoreMirrors", mirrors, "mirror");
            // canopy
            var canopy = new MeshBuilder();
            canopy.Cylinder(new Vector3(0, 7.2f, 0), r + 0.35f, 2.6f, 32, 3f, false, 0.4f);
            PieceUtil.MeshObj(c.spin, "Canopy", canopy, "tent");
            var under = new MeshBuilder();
            under.Cylinder(new Vector3(0, 7.15f, 0), r + 0.35f, 0.05f, 32, 3f, true);
            PieceUtil.MeshObj(c.spin, "CanopyUnder", under, "wood_painted", false);
            var rim = new MeshBuilder();
            rim.Cylinder(new Vector3(0, 6.4f, 0), r + 0.4f, 0.8f, 32, 1.5f, false);
            PieceUtil.MeshObj(c.spin, "Rim", rim, "red_paint");
            var top = new MeshBuilder();
            top.Cylinder(new Vector3(0, 9.8f, 0), 0.15f, 1.2f, 8, 1f);
            PieceUtil.MeshObj(c.spin, "Finial", top, "gold_trim");
            // rim bulbs
            for (int i = 0; i < 28; i++)
            {
                float a = i / 28f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * (r + 0.42f), 6.8f, Mathf.Sin(a) * (r + 0.42f));
                RF.Prim(PrimitiveType.Sphere, c.spin, i % 5 == 3 ? "bulb_dead" : "bulb_warm", p, Vector3.one * 0.14f, default, false);
            }
            // horses
            int n = 12;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * 360f;
                float rad = i % 2 == 0 ? r - 1.1f : r - 2.6f;
                var hp = Quaternion.Euler(0, a, 0) * new Vector3(0, 0, rad);
                var poleT = new GameObject("Horse_" + i).transform;
                poleT.SetParent(c.spin, false);
                poleT.localPosition = hp;
                poleT.localRotation = Quaternion.Euler(0, a + 90f, 0);
                RF.Prim(PrimitiveType.Cylinder, poleT, "gold_trim", new Vector3(0, 3.6f, 0), new Vector3(0.07f, 3.1f, 0.07f), default, false);
                var horse = new GameObject("Body").transform;
                horse.SetParent(poleT, false);
                horse.localPosition = new Vector3(0, 1.45f, 0);
                BuildHorse(horse, i);
                c.horses.Add(horse);
                c.horsePhase.Add(i * 0.9f);
            }
            return c;
        }

        static void BuildHorse(Transform h, int i)
        {
            string body = i % 4 == 1 ? "plastic" : "white_paint";
            RF.Prim(PrimitiveType.Capsule, h, body, Vector3.zero, new Vector3(0.45f, 0.75f, 0.45f), new Vector3(0, 0, 90));
            // neck + head (some are broken)
            if (i % 5 != 2)
            {
                RF.Prim(PrimitiveType.Capsule, h, body, new Vector3(0.62f, 0.35f, 0), new Vector3(0.22f, 0.35f, 0.22f), new Vector3(0, 0, -35));
                var head = RF.Prim(PrimitiveType.Cube, h, body, new Vector3(0.88f, 0.62f, 0), new Vector3(0.48f, 0.24f, 0.2f), new Vector3(0, 0, -20));
                RF.Prim(PrimitiveType.Sphere, head.transform, i % 3 == 0 ? "eye_dim" : "black_matte", new Vector3(0.1f, 0.25f, 0.52f), new Vector3(0.18f, 0.35f, 0.1f), default, false);
                RF.Prim(PrimitiveType.Sphere, head.transform, i % 3 == 0 ? "eye_dim" : "black_matte", new Vector3(0.1f, 0.25f, -0.52f), new Vector3(0.18f, 0.35f, 0.1f), default, false);
                RF.Prim(PrimitiveType.Cube, h, "red_paint", new Vector3(0.6f, 0.55f, 0), new Vector3(0.3f, 0.1f, 0.24f), new Vector3(0, 0, -35), false);
            }
            else
            {
                // snapped neck with endoskeleton showing
                RF.Prim(PrimitiveType.Cylinder, h, "endo", new Vector3(0.55f, 0.3f, 0), new Vector3(0.06f, 0.25f, 0.06f), new Vector3(0, 0, -40), false);
            }
            // legs
            foreach (var lp in new[] { new Vector3(-0.4f, -0.4f, 0.12f), new Vector3(-0.4f, -0.4f, -0.12f), new Vector3(0.35f, -0.4f, 0.12f), new Vector3(0.35f, -0.4f, -0.12f) })
                RF.Prim(PrimitiveType.Capsule, h, body, lp, new Vector3(0.1f, 0.3f, 0.1f), new Vector3(0, 0, UnityEngine.Random.Range(-35f, 35f)), false);
            // saddle
            RF.Prim(PrimitiveType.Cube, h, i % 2 == 0 ? "red_paint" : "blue_paint", new Vector3(0, 0.22f, 0), new Vector3(0.4f, 0.08f, 0.5f), default, false);
            RF.Prim(PrimitiveType.Cube, h, "gold_trim", new Vector3(0, 0.12f, 0), new Vector3(0.42f, 0.04f, 0.52f), default, false);
        }

        public void Spin(float seconds, Action onStopped)
        {
            StartCoroutine(SpinRoutine(seconds, onStopped));
        }

        IEnumerator SpinRoutine(float seconds, Action onStopped)
        {
            Spinning = true;
            music = AudioManager.Attach(gameObject, "mus_carousel", 1f, true, 6f, 55f);
            if (music != null) music.time = 0f;
            float t = 0;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Min(1f, t / 2.5f) * Mathf.Min(1f, (seconds - t) / 3f);
                speed = 34f * k;
                if (music != null) music.pitch = Mathf.Lerp(0.55f, 1f, k);
                if (UnityEngine.Random.value < Time.deltaTime * 0.8f) Noise.Emit(transform.position, 40f, NoiseKind.Loud);
                yield return null;
            }
            speed = 0f;
            if (music != null) { Destroy(music); music = null; }
            if (AudioManager.I != null) AudioManager.I.PlayAt("lever_pull", hatch.position, 1f, 2f, 20f);
            // open the hatch
            float h = 0;
            while (h < 1f)
            {
                h += Time.deltaTime * 2f;
                hatch.localRotation = Quaternion.Euler(110f * Mathf.SmoothStep(0, 1, h), 0, 0); // flap (local -z) swings up, not into the step
                yield return null;
            }
            Spinning = false;
            onStopped?.Invoke();
        }

        void Update()
        {
            if (speed <= 0.01f) return;
            spin.Rotate(0, -speed * Time.deltaTime, 0, Space.Self);
            bobT += Time.deltaTime * speed / 34f;
            for (int i = 0; i < horses.Count; i++)
                horses[i].localPosition = new Vector3(0, 1.45f + Mathf.Sin(bobT * 2.4f + horsePhase[i]) * 0.35f, 0);
        }
    }

    // ===================================================================== Fortune teller
    public class FortuneTeller : MonoBehaviour
    {
        Transform head, handL, handR, card;
        Renderer eyeL, eyeR;
        Light ballLight;
        bool active;
        float t;

        public static FortuneTeller Create(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("FortuneTeller").transform;
            root.SetParent(parent, false);
            root.position = pos;
            root.rotation = Quaternion.Euler(0, yaw, 0);
            var f = root.gameObject.AddComponent<FortuneTeller>();
            RF.Prim(PrimitiveType.Cube, root, "wood_painted", new Vector3(0, 0.5f, 0), new Vector3(2f, 1f, 1.4f));
            RF.Prim(PrimitiveType.Cube, root, "gold_trim", new Vector3(0, 1.02f, 0), new Vector3(2.04f, 0.05f, 1.44f), default, false);
            RF.Prim(PrimitiveType.Cube, root, "glass", new Vector3(0, 1.8f, 0.1f), new Vector3(1.9f, 1.5f, 1.2f), default, false);
            RF.Prim(PrimitiveType.Cube, root, "wood_painted", new Vector3(0, 2.6f, 0), new Vector3(2.1f, 0.15f, 1.5f));
            RF.Prim(PrimitiveType.Cube, root, "fabric_purple", new Vector3(0, 1.8f, -0.55f), new Vector3(1.9f, 1.5f, 0.05f), default, false);
            RF.Prim(PrimitiveType.Cube, root, "neon_pink", new Vector3(0, 2.75f, 0.7f), new Vector3(1.6f, 0.12f, 0.05f), default, false);
            for (int i = -1; i <= 1; i += 2)
                RF.Prim(PrimitiveType.Cube, root, "gold_trim", new Vector3(i * 0.97f, 1.8f, 0.68f), new Vector3(0.06f, 1.55f, 0.06f), default, false);
            // the figure
            RF.Prim(PrimitiveType.Capsule, root, "fabric_purple", new Vector3(0, 1.45f, -0.15f), new Vector3(0.62f, 0.42f, 0.42f));
            var neck = new GameObject("Neck").transform;
            neck.SetParent(root, false);
            neck.localPosition = new Vector3(0, 1.92f, -0.15f);
            f.head = neck;
            RF.Prim(PrimitiveType.Sphere, neck, "plastic", new Vector3(0, 0.18f, 0), new Vector3(0.36f, 0.42f, 0.36f));
            RF.Prim(PrimitiveType.Sphere, neck, "red_paint", new Vector3(0, 0.3f, -0.03f), new Vector3(0.42f, 0.36f, 0.42f), default, false);
            f.eyeL = RF.Prim(PrimitiveType.Sphere, neck, "eye_dim", new Vector3(-0.08f, 0.2f, 0.16f), Vector3.one * 0.06f, default, false).GetComponent<Renderer>();
            f.eyeR = RF.Prim(PrimitiveType.Sphere, neck, "eye_dim", new Vector3(0.08f, 0.2f, 0.16f), Vector3.one * 0.06f, default, false).GetComponent<Renderer>();
            RF.Prim(PrimitiveType.Cube, neck, "teeth", new Vector3(0, 0.06f, 0.17f), new Vector3(0.14f, 0.03f, 0.02f), default, false);
            // crystal ball
            RF.Prim(PrimitiveType.Sphere, root, "glass", new Vector3(0, 1.25f, 0.3f), Vector3.one * 0.32f, default, false);
            var ball = new GameObject("BallLight").AddComponent<Light>();
            ball.transform.SetParent(root, false);
            ball.transform.localPosition = new Vector3(0, 1.3f, 0.3f);
            ball.type = LightType.Point; ball.range = 2.5f; ball.intensity = 0.4f; ball.color = new Color(0.7f, 0.3f, 1f);
            ball.shadows = LightShadows.None;
            f.ballLight = ball;
            f.handL = RF.Prim(PrimitiveType.Sphere, root, "plastic", new Vector3(-0.22f, 1.45f, 0.28f), new Vector3(0.12f, 0.06f, 0.16f), default, false).transform;
            f.handR = RF.Prim(PrimitiveType.Sphere, root, "plastic", new Vector3(0.22f, 1.45f, 0.28f), new Vector3(0.12f, 0.06f, 0.16f), default, false).transform;
            // card slot
            RF.Prim(PrimitiveType.Cube, root, "black_matte", new Vector3(0.6f, 0.85f, 0.71f), new Vector3(0.35f, 0.04f, 0.02f), default, false);
            f.card = RF.Prim(PrimitiveType.Cube, root, "paper", new Vector3(0.6f, 0.85f, 0.65f), new Vector3(0.3f, 0.005f, 0.2f), default, false).transform;
            // coin slot
            RF.Prim(PrimitiveType.Cube, root, "gold_trim", new Vector3(-0.75f, 1.05f - 0.01f, 0.72f), new Vector3(0.12f, 0.2f, 0.02f), default, false);
            return f;
        }

        public void Activate(Action onCard)
        {
            if (active) return;
            active = true;
            StartCoroutine(Run(onCard));
        }

        IEnumerator Run(Action onCard)
        {
            eyeL.sharedMaterial = MatLib.Get("eye_red");
            eyeR.sharedMaterial = MatLib.Get("eye_red");
            ballLight.intensity = 2.5f;
            if (AudioManager.I != null) AudioManager.I.PlayAt("fortune_laugh", head.position, 1f, 3f, 30f);
            Noise.Emit(transform.position, 22f, NoiseKind.Loud);
            float k = 0;
            while (k < 4.2f)
            {
                k += Time.deltaTime;
                head.localRotation = Quaternion.Euler(Mathf.Sin(k * 9f) * 8f, Mathf.Sin(k * 2.2f) * 35f, Mathf.Sin(k * 13f) * 6f);
                handL.localPosition = new Vector3(-0.2f + Mathf.Sin(k * 4f) * 0.06f, 1.45f + Mathf.Cos(k * 4f) * 0.05f, 0.3f);
                handR.localPosition = new Vector3(0.2f - Mathf.Sin(k * 4f) * 0.06f, 1.45f + Mathf.Cos(k * 4f) * 0.05f, 0.3f);
                ballLight.intensity = 2f + Mathf.PerlinNoise(k * 6f, 0) * 2f;
                yield return null;
            }
            if (AudioManager.I != null) AudioManager.I.PlayAt("fortune_card", card.position, 0.9f, 2f, 15f);
            Vector3 a = card.localPosition, b = a + new Vector3(0, 0, 0.25f);
            k = 0;
            while (k < 1f) { k += Time.deltaTime * 0.6f; card.localPosition = Vector3.Lerp(a, b, k); yield return null; }
            card.gameObject.SetActive(false);
            head.localRotation = Quaternion.Euler(20f, 0, 8f); // slumps forward
            ballLight.intensity = 0.3f;
            onCard?.Invoke();
        }

        void Update()
        {
            if (active || head == null) return;
            t += Time.deltaTime;
            // tiny unnerving twitches while idle
            if (Mathf.PerlinNoise(t * 0.7f, 3.3f) > 0.78f)
                head.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 30f) * 4f, 0);
        }
    }

    // ===================================================================== Clock automaton
    public class ClockAutomaton : MonoBehaviour
    {
        Transform hourHand, minHand, doorL, doorR, doll, pendulum;
        public readonly List<GameObject> gearVisuals = new List<GameObject>();
        bool active;

        public static ClockAutomaton Create(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("ClockAutomaton").transform;
            root.SetParent(parent, false);
            root.position = pos;
            root.rotation = Quaternion.Euler(0, yaw, 0);
            var c = root.gameObject.AddComponent<ClockAutomaton>();
            RF.Prim(PrimitiveType.Cube, root, "wood", new Vector3(0, 2.6f, 0), new Vector3(3.6f, 5.2f, 2.2f));
            RF.Prim(PrimitiveType.Cube, root, "gold_trim", new Vector3(0, 5.25f, 0), new Vector3(3.9f, 0.15f, 2.4f), default, false);
            RF.Prim(PrimitiveType.Cube, root, "wood_painted", new Vector3(0, 5.7f, 0.2f), new Vector3(2.6f, 0.8f, 1.6f));
            // face
            var face = RF.Prim(PrimitiveType.Cylinder, root, "white_paint", new Vector3(0, 3.9f, 1.11f), new Vector3(1.8f, 0.03f, 1.8f), new Vector3(90, 0, 0), false);
            face.name = "Face";
            RF.Prim(PrimitiveType.Cylinder, root, "gold_trim", new Vector3(0, 3.9f, 1.105f), new Vector3(1.95f, 0.02f, 1.95f), new Vector3(90, 0, 0), false);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                RF.Prim(PrimitiveType.Cube, root, "black_matte", new Vector3(Mathf.Sin(a) * 0.75f, 3.9f + Mathf.Cos(a) * 0.75f, 1.14f), new Vector3(0.05f, 0.14f, 0.01f), new Vector3(0, 0, -i * 30f), false);
            }
            c.hourHand = new GameObject("Hour").transform; c.hourHand.SetParent(root, false); c.hourHand.localPosition = new Vector3(0, 3.9f, 1.15f);
            c.minHand = new GameObject("Min").transform; c.minHand.SetParent(root, false); c.minHand.localPosition = new Vector3(0, 3.9f, 1.16f);
            RF.Prim(PrimitiveType.Cube, c.hourHand, "black_matte", new Vector3(0, 0.22f, 0), new Vector3(0.06f, 0.45f, 0.01f), default, false);
            RF.Prim(PrimitiveType.Cube, c.minHand, "black_matte", new Vector3(0, 0.32f, 0), new Vector3(0.04f, 0.65f, 0.01f), default, false);
            c.hourHand.localRotation = Quaternion.Euler(0, 0, -100f);
            c.minHand.localRotation = Quaternion.Euler(0, 0, -260f);
            // little doors (the doll appears from here)
            c.doorL = new GameObject("DoorL").transform; c.doorL.SetParent(root, false); c.doorL.localPosition = new Vector3(-0.5f, 1.6f, 1.12f);
            c.doorR = new GameObject("DoorR").transform; c.doorR.SetParent(root, false); c.doorR.localPosition = new Vector3(0.5f, 1.6f, 1.12f);
            RF.Prim(PrimitiveType.Cube, c.doorL, "red_paint", new Vector3(0.25f, 0.45f, 0), new Vector3(0.5f, 0.9f, 0.04f), default, false);
            RF.Prim(PrimitiveType.Cube, c.doorR, "red_paint", new Vector3(-0.25f, 0.45f, 0), new Vector3(0.5f, 0.9f, 0.04f), default, false);
            // the doll on its track
            c.doll = new GameObject("Doll").transform; c.doll.SetParent(root, false); c.doll.localPosition = new Vector3(0, 1.6f, 0.6f);
            RF.Prim(PrimitiveType.Capsule, c.doll, "white_paint", new Vector3(0, 0.3f, 0), new Vector3(0.28f, 0.28f, 0.28f));
            var dh = RF.Prim(PrimitiveType.Sphere, c.doll, "plastic", new Vector3(0, 0.75f, 0), Vector3.one * 0.26f);
            RF.Prim(PrimitiveType.Sphere, dh.transform, "black_matte", new Vector3(-0.18f, 0.1f, 0.42f), Vector3.one * 0.2f, default, false);
            RF.Prim(PrimitiveType.Sphere, dh.transform, "black_matte", new Vector3(0.18f, 0.1f, 0.42f), Vector3.one * 0.2f, default, false);
            RF.Prim(PrimitiveType.Capsule, c.doll, "white_paint", new Vector3(0.12f, 0.45f, 0.18f), new Vector3(0.06f, 0.16f, 0.06f), new Vector3(70, 0, 0), false);
            c.doll.gameObject.SetActive(false);
            // pendulum window
            RF.Prim(PrimitiveType.Cube, root, "glass", new Vector3(0, 0.75f, 1.11f), new Vector3(1f, 1.1f, 0.02f), default, false);
            c.pendulum = new GameObject("Pendulum").transform; c.pendulum.SetParent(root, false); c.pendulum.localPosition = new Vector3(0, 1.3f, 1.0f);
            RF.Prim(PrimitiveType.Cube, c.pendulum, "gold_trim", new Vector3(0, -0.35f, 0), new Vector3(0.03f, 0.7f, 0.02f), default, false);
            RF.Prim(PrimitiveType.Cylinder, c.pendulum, "gold_trim", new Vector3(0, -0.72f, 0), new Vector3(0.22f, 0.02f, 0.22f), new Vector3(90, 0, 0), false);
            // three empty gear slots on the right side panel
            for (int i = 0; i < 3; i++)
            {
                var slotPos = new Vector3(1.0f + i * 0f, 1.25f + i * 0.45f, 1.12f);
                RF.Prim(PrimitiveType.Cylinder, root, "black_matte", slotPos, new Vector3(0.36f, 0.02f, 0.36f), new Vector3(90, 0, 0), false);
                var g = RF.Prim(PrimitiveType.Cylinder, root, "gold_trim", slotPos + new Vector3(0, 0, 0.02f), new Vector3(0.32f, 0.03f, 0.32f), new Vector3(90, 0, 0), false);
                for (int t = 0; t < 8; t++)
                    RF.Prim(PrimitiveType.Cube, g.transform, "gold_trim", Quaternion.Euler(0, t * 45f, 0) * new Vector3(0, 0, 0.55f), new Vector3(0.18f, 1f, 0.2f), new Vector3(0, t * 45f, 0), false);
                g.SetActive(false);
                c.gearVisuals.Add(g);
            }
            return c;
        }

        void Update()
        {
            if (pendulum != null) pendulum.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 2.4f) * (active ? 25f : 10f));
            for (int i = 0; i < gearVisuals.Count; i++)
                if (gearVisuals[i].activeSelf && active) gearVisuals[i].transform.Rotate(0, 90f * Time.deltaTime * (i % 2 == 0 ? 1 : -1), 0, Space.Self);
        }

        public void Activate(Action onDollOut)
        {
            if (active) return;
            active = true;
            StartCoroutine(Run(onDollOut));
        }

        IEnumerator Run(Action onDollOut)
        {
            float k = 0;
            Quaternion h0 = hourHand.localRotation, m0 = minHand.localRotation;
            while (k < 3f)
            {
                k += Time.deltaTime;
                float e = Mathf.SmoothStep(0, 1, k / 3f);
                hourHand.localRotation = Quaternion.Slerp(h0, Quaternion.identity, e);
                minHand.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-260f, -720f, e));
                yield return null;
            }
            if (AudioManager.I != null) AudioManager.I.PlayAt("mus_clock_chime", transform.position + Vector3.up * 4f, 1f, 6f, 70f);
            Noise.Emit(transform.position, 45f, NoiseKind.Loud);
            yield return new WaitForSeconds(2.5f);
            k = 0;
            while (k < 1f)
            {
                k += Time.deltaTime * 1.5f;
                doorL.localRotation = Quaternion.Euler(0, -100f * k, 0);
                doorR.localRotation = Quaternion.Euler(0, 100f * k, 0);
                yield return null;
            }
            doll.gameObject.SetActive(true);
            Vector3 a = doll.localPosition, b = a + new Vector3(0, 0, 0.9f);
            k = 0;
            while (k < 1f)
            {
                k += Time.deltaTime * 0.5f;
                doll.localPosition = Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, k));
                doll.localRotation = Quaternion.Euler(0, Mathf.Sin(k * 20f) * 10f, 0);
                yield return null;
            }
            // the head snaps to look at the player
            var head = doll.GetChild(1);
            head.localRotation = Quaternion.Euler(0, 0, 25f);
            if (AudioManager.I != null) AudioManager.I.PlayAt("servo_1", doll.position, 0.9f, 1f, 10f);
            onDollOut?.Invoke();
        }
    }

    // ===================================================================== Rolling gate (exit)
    public class RollingGate : MonoBehaviour
    {
        Transform shutter;
        BoxCollider col;
        float height;
        public bool IsOpen { get; private set; }

        public static RollingGate Create(Transform parent, Vector3 center, float width, float height)
        {
            var root = new GameObject("ExitGate").transform;
            root.SetParent(parent, false);
            root.position = center;
            var g = root.gameObject.AddComponent<RollingGate>();
            g.height = height;
            g.shutter = new GameObject("Shutter").transform;
            g.shutter.SetParent(root, false);
            var mb = new MeshBuilder();
            for (float y = 0.1f; y < height; y += 0.2f)
                mb.Box(new Vector3(0, y, 0), new Vector3(width, 0.18f, 0.08f), 1.5f);
            PieceUtil.MeshObj(g.shutter, "Slats", mb, "wall_metal");
            RF.Prim(PrimitiveType.Cube, g.shutter, "yellow_paint", new Vector3(0, 0.08f, 0.05f), new Vector3(width, 0.16f, 0.04f), default, false);
            for (int i = 0; i < 5; i++)
                RF.Prim(PrimitiveType.Cube, g.shutter, "black_matte", new Vector3(-width * 0.5f + 0.8f + i * 1.6f, 0.08f, 0.075f), new Vector3(0.7f, 0.15f, 0.01f), new Vector3(0, 0, 0), false);
            // housing
            RF.Prim(PrimitiveType.Cube, root, "metal_rust", new Vector3(0, height + 0.3f, 0.15f), new Vector3(width + 0.4f, 0.6f, 0.5f));
            RF.Prim(PrimitiveType.Cube, root, "metal_rust", new Vector3(-width * 0.5f - 0.1f, height * 0.5f, 0.12f), new Vector3(0.2f, height, 0.3f));
            RF.Prim(PrimitiveType.Cube, root, "metal_rust", new Vector3(width * 0.5f + 0.1f, height * 0.5f, 0.12f), new Vector3(0.2f, height, 0.3f));
            var colGo = new GameObject("GateCollider");
            colGo.transform.SetParent(root, false);
            g.col = colGo.AddComponent<BoxCollider>();
            g.col.center = new Vector3(0, height * 0.5f, 0);
            g.col.size = new Vector3(width, height, 0.4f);
            // warning light
            return g;
        }

        public void Open(float seconds, Action onOpen)
        {
            if (IsOpen) return;
            StartCoroutine(OpenRoutine(seconds, onOpen));
        }

        IEnumerator OpenRoutine(float seconds, Action onOpen)
        {
            if (AudioManager.I != null) AudioManager.I.PlayAt("gate_open", transform.position + Vector3.up * 2f, 1f, 5f, 60f);
            float t = 0;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float y = Mathf.Lerp(0, height - 0.25f, Mathf.SmoothStep(0, 1, t / seconds));
                shutter.localPosition = new Vector3(0, y, 0);
                shutter.localScale = new Vector3(1, Mathf.Lerp(1f, 0.08f, t / seconds), 1);
                col.center = new Vector3(0, height * 0.5f + y, 0);
                yield return null;
            }
            col.enabled = false;
            IsOpen = true;
            onOpen?.Invoke();
        }
    }

    // ===================================================================== Safe
    public class SafeBox : MonoBehaviour
    {
        public Transform door;
        public Transform inside;
        public bool IsOpen { get; private set; }

        public static SafeBox Create(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("Safe").transform;
            root.SetParent(parent, false);
            root.position = pos;
            root.rotation = Quaternion.Euler(0, yaw, 0);
            var s = root.gameObject.AddComponent<SafeBox>();
            float w = 1f, h = 1.25f, d = 0.85f;
            HideSpot.AddSolid(root, "black_matte", new Vector3(0, h * 0.5f, -d * 0.5f + 0.05f), new Vector3(w, h, 0.1f));
            HideSpot.AddSolid(root, "black_matte", new Vector3(-w * 0.5f + 0.05f, h * 0.5f, 0), new Vector3(0.1f, h, d));
            HideSpot.AddSolid(root, "black_matte", new Vector3(w * 0.5f - 0.05f, h * 0.5f, 0), new Vector3(0.1f, h, d));
            HideSpot.AddSolid(root, "black_matte", new Vector3(0, h - 0.05f, 0), new Vector3(w, 0.1f, d));
            HideSpot.AddSolid(root, "black_matte", new Vector3(0, 0.05f, 0), new Vector3(w, 0.1f, d));
            RF.Prim(PrimitiveType.Cube, root, "metal_rust", new Vector3(0, 0.55f, 0), new Vector3(w - 0.2f, 0.03f, d - 0.2f), default, false);
            s.inside = RF.Child(root, "Inside", new Vector3(0, 0.6f, 0)).transform;
            s.door = new GameObject("DoorHinge").transform;
            s.door.SetParent(root, false);
            s.door.localPosition = new Vector3(-w * 0.5f, 0, d * 0.5f);
            var dp = HideSpot.AddSolid(s.door, "chrome", new Vector3(w * 0.5f, h * 0.5f, 0.04f), new Vector3(w, h, 0.08f));
            dp.layer = RF.LayerDefault;
            RF.Prim(PrimitiveType.Cylinder, dp.transform, "black_matte", new Vector3(0.1f, 0.15f, 0.7f), new Vector3(0.22f, 0.4f, 0.18f), new Vector3(90, 0, 0), false);
            RF.Prim(PrimitiveType.Cube, dp.transform, "black_matte", new Vector3(-0.2f, -0.15f, 0.6f), new Vector3(0.35f, 0.3f, 0.3f), default, false);
            RF.Prim(PrimitiveType.Cube, dp.transform, "screen_green", new Vector3(-0.2f, -0.05f, 0.85f), new Vector3(0.28f, 0.08f, 0.1f), default, false);
            RF.Prim(PrimitiveType.Cube, dp.transform, "chrome", new Vector3(0.35f, 0f, 0.8f), new Vector3(0.04f, 0.25f, 0.5f), default, false);
            return s;
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            StartCoroutine(OpenRoutine());
        }

        IEnumerator OpenRoutine()
        {
            if (AudioManager.I != null) AudioManager.I.PlayAt("safe_open", transform.position, 1f, 2f, 18f);
            yield return new WaitForSeconds(0.8f);
            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime * 0.7f;
                door.localRotation = Quaternion.Euler(0, -105f * Mathf.SmoothStep(0, 1, t), 0);
                yield return null;
            }
        }
    }

    // ===================================================================== Generator
    public class Generator : MonoBehaviour
    {
        Transform body;
        AudioSource hum;
        bool running;
        Vector3 basePos;
        public Transform lever;

        public static Generator Create(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("Generator").transform;
            root.SetParent(parent, false);
            root.position = pos;
            root.rotation = Quaternion.Euler(0, yaw, 0);
            var g = root.gameObject.AddComponent<Generator>();
            RF.Prim(PrimitiveType.Cube, root, "metal_rust", new Vector3(0, 0.15f, 0), new Vector3(4.8f, 0.3f, 2.8f));
            g.body = new GameObject("Body").transform;
            g.body.SetParent(root, false);
            RF.Prim(PrimitiveType.Cube, g.body, "green_paint", new Vector3(-0.6f, 1.1f, 0), new Vector3(2.8f, 1.6f, 2f));
            RF.Prim(PrimitiveType.Cylinder, g.body, "metal_rust", new Vector3(1.3f, 1f, 0), new Vector3(1.2f, 0.7f, 1.2f), new Vector3(0, 0, 90));
            for (int i = 0; i < 6; i++)
                RF.Prim(PrimitiveType.Cube, g.body, "black_matte", new Vector3(-1.8f + i * 0.45f, 1.95f, 0), new Vector3(0.08f, 0.12f, 1.9f), default, false);
            RF.Prim(PrimitiveType.Cylinder, g.body, "metal_rust", new Vector3(-1.4f, 2.8f, -0.6f), new Vector3(0.3f, 1f, 0.3f));
            RF.Prim(PrimitiveType.Cube, g.body, "black_matte", new Vector3(0, 1.3f, -1.02f), new Vector3(1.4f, 0.9f, 0.05f), default, false);
            for (int i = 0; i < 3; i++)
                RF.Prim(PrimitiveType.Cylinder, g.body, "white_paint", new Vector3(-0.45f + i * 0.45f, 1.45f, -1.06f), new Vector3(0.28f, 0.01f, 0.28f), new Vector3(90, 0, 0), false);
            // the big lever
            g.lever = new GameObject("LeverPivot").transform;
            g.lever.SetParent(root, false);
            g.lever.localPosition = new Vector3(0, 1.1f, -1.4f);
            RF.Prim(PrimitiveType.Cube, root, "black_matte", new Vector3(0, 0.6f, -1.4f), new Vector3(0.4f, 1.2f, 0.3f));
            RF.Prim(PrimitiveType.Cylinder, g.lever, "red_paint", new Vector3(0, 0.35f, 0), new Vector3(0.07f, 0.35f, 0.07f), default, false);
            RF.Prim(PrimitiveType.Sphere, g.lever, "black_matte", new Vector3(0, 0.72f, 0), Vector3.one * 0.14f, default, false);
            g.lever.localRotation = Quaternion.Euler(-35f, 0, 0);
            g.basePos = g.body.localPosition;
            return g;
        }

        public void SetLever(float t01) { if (lever != null) lever.localRotation = Quaternion.Euler(Mathf.Lerp(-35f, 40f, t01), 0, 0); }

        public void StartUp()
        {
            if (running) return;
            running = true;
            SetLever(1f);
            if (AudioManager.I != null) AudioManager.I.PlayAt("generator_start", transform.position, 1f, 4f, 45f);
            hum = AudioManager.Attach(gameObject, "electric_buzz", 0.6f, true, 3f, 25f);
            if (hum != null) hum.pitch = 0.45f;
        }

        void Update()
        {
            if (!running) return;
            body.localPosition = basePos + new Vector3(Mathf.PerlinNoise(Time.time * 30f, 0) - 0.5f, Mathf.PerlinNoise(0, Time.time * 30f) - 0.5f, 0) * 0.012f;
        }
    }

    // ===================================================================== Tunnel scares
    public class GhostScare : MonoBehaviour
    {
        public enum Kind { Skeleton, Shriek }
        Kind kind;
        Bounds area;
        bool fired;
        Transform prop;

        public static GhostScare Create(Transform parent, Vector3 center, Vector3 size, Kind kind)
        {
            var go = new GameObject("Scare_" + kind);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var s = go.AddComponent<GhostScare>();
            s.kind = kind;
            s.area = new Bounds(center + Vector3.up * size.y * 0.5f, size);
            if (kind == Kind.Skeleton)
            {
                s.prop = new GameObject("DropSkeleton").transform;
                s.prop.SetParent(go.transform, false);
                s.prop.localPosition = new Vector3(0, 6.5f, 1f);
                RF.Prim(PrimitiveType.Sphere, s.prop, "bone", new Vector3(0, -0.15f, 0), new Vector3(0.28f, 0.32f, 0.3f));
                RF.Prim(PrimitiveType.Sphere, s.prop, "eye_red", new Vector3(-0.06f, -0.12f, 0.12f), Vector3.one * 0.06f, default, false);
                RF.Prim(PrimitiveType.Sphere, s.prop, "eye_red", new Vector3(0.06f, -0.12f, 0.12f), Vector3.one * 0.06f, default, false);
                RF.Prim(PrimitiveType.Capsule, s.prop, "bone", new Vector3(0, -0.65f, 0), new Vector3(0.25f, 0.35f, 0.14f));
                RF.Prim(PrimitiveType.Capsule, s.prop, "bone", new Vector3(-0.28f, -0.6f, 0.15f), new Vector3(0.06f, 0.4f, 0.06f), new Vector3(-60, 0, 20), false);
                RF.Prim(PrimitiveType.Capsule, s.prop, "bone", new Vector3(0.28f, -0.6f, 0.15f), new Vector3(0.06f, 0.4f, 0.06f), new Vector3(-60, 0, -20), false);
            }
            else
            {
                s.prop = RF.Prim(PrimitiveType.Capsule, go.transform, "cloth_white", new Vector3(-5f, 2.2f, 1.5f), new Vector3(0.8f, 1f, 0.5f), default, false).transform;
                RF.Prim(PrimitiveType.Sphere, s.prop, "eye_red", new Vector3(-0.18f, 0.4f, 0.42f), new Vector3(0.14f, 0.1f, 0.1f), default, false);
                RF.Prim(PrimitiveType.Sphere, s.prop, "eye_red", new Vector3(0.18f, 0.4f, 0.42f), new Vector3(0.14f, 0.1f, 0.1f), default, false);
                s.prop.gameObject.SetActive(false);
            }
            return s;
        }

        void Update()
        {
            if (fired || PlayerController.I == null) return;
            if (!area.Contains(PlayerController.I.transform.position + Vector3.up * 0.5f)) return;
            fired = true;
            StartCoroutine(kind == Kind.Skeleton ? Drop() : Fly());
        }

        IEnumerator Drop()
        {
            if (AudioManager.I != null) AudioManager.I.PlayAt("skeleton_drop", prop.position + Vector3.down * 3f, 1f, 2f, 18f);
            float t = 0;
            Vector3 a = prop.localPosition, b = new Vector3(0, 2.3f, 1f);
            while (t < 1f)
            {
                t += Time.deltaTime * 2.8f;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                prop.localPosition = Vector3.LerpUnclamped(a, b, e) + Vector3.up * Mathf.Sin(t * 25f) * (1f - t) * 0.3f;
                yield return null;
            }
            if (AudioManager.I != null) AudioManager.I.PlayAt("ghost_scare", prop.position, 0.8f, 2f, 15f);
            PlayerController.I?.Shake(0.4f, 0.4f);
            t = 0;
            while (t < 8f) { t += Time.deltaTime; prop.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 2f) * 25f, Mathf.Sin(t * 3f) * 8f); yield return null; }
        }

        IEnumerator Fly()
        {
            prop.gameObject.SetActive(true);
            if (AudioManager.I != null) AudioManager.I.PlayAt("ghost_scare", prop.position, 1f, 2f, 18f);
            PlayerController.I?.Shake(0.3f, 0.35f);
            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime * 1.4f;
                prop.localPosition = new Vector3(Mathf.Lerp(-5f, 6f, t), 2.2f + Mathf.Sin(t * 12f) * 0.2f, 1.5f);
                yield return null;
            }
            prop.gameObject.SetActive(false);
        }
    }
}
