using UnityEngine;

namespace RustedFair
{
    /// <summary>Small procedural models for every collectible + a findability sparkle.</summary>
    public static class ItemVisuals
    {
        public static Pickup SpawnPickup(Transform parent, ItemId id, Vector3 pos, float yaw, bool bob = true)
        {
            var go = new GameObject("Pickup_" + id);
            go.transform.SetParent(parent, false);
            go.transform.position = pos + Vector3.up * 0.06f;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var model = Model(go.transform, id);
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale = Vector3.one * 1.5f; // readable on a phone screen
            var p = go.AddComponent<Pickup>();
            p.item = id;
            p.bob = bob;
            p.AddHitBox(new Vector3(0, 0.05f, 0), new Vector3(0.5f, 0.4f, 0.5f));
            Sparkle(go.transform, id == ItemId.Battery || Items.IsThrowable(id) ? 0.6f : 1f);
            if (ItemGlint.IsQuestItem(id) || ItemGlint.IsConsumable(id)) ItemGlint.Add(p); // task items (v1.2) and consumables (v1.6) read in the dark
            return p;
        }

        static void Sparkle(Transform t, float strength)
        {
            var g = new GameObject("Sparkle");
            g.transform.SetParent(t, false);
            g.transform.localPosition = new Vector3(0, 0.1f, 0);
            var ps = g.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 0.7f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new Color(1f, 0.9f, 0.7f, 0.55f * strength);
            main.maxParticles = 6;
            main.scalingMode = ParticleSystemScalingMode.Local;
            var em = ps.emission;
            em.rateOverTime = 1.6f * strength;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.12f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MatLib.Get("fx_glow");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        static GameObject P(PrimitiveType t, Transform parent, string mat, Vector3 pos, Vector3 scale, Vector3 euler = default)
            => RF.Prim(t, parent, mat, pos, scale, euler, false);

        public static GameObject Model(Transform parent, ItemId id)
        {
            switch (id)
            {
                case ItemId.Fuse: return Fuse(parent);
                case ItemId.Crank: return Crank(parent);
                case ItemId.Tape: return Tape(parent);
                case ItemId.LockerKey: return Key(parent, "chrome");
                case ItemId.KitchenKey: return Key(parent, "gold_trim", true);
                case ItemId.RideKey: return Key(parent, "gold_trim", false, true);
            }
            var root = new GameObject("Model");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            switch (id)
            {
                case ItemId.Token:
                    P(PrimitiveType.Cylinder, t, "gold_trim", Vector3.zero, new Vector3(0.09f, 0.006f, 0.09f), new Vector3(90, 0, 0));
                    P(PrimitiveType.Cylinder, t, "yellow_paint", new Vector3(0, 0, 0.004f), new Vector3(0.06f, 0.002f, 0.06f), new Vector3(90, 0, 0));
                    break;
                case ItemId.Hammer:
                    P(PrimitiveType.Cylinder, t, "wood", Vector3.zero, new Vector3(0.035f, 0.17f, 0.035f), new Vector3(0, 0, 90));
                    P(PrimitiveType.Cube, t, "metal_rust", new Vector3(0.17f, 0, 0), new Vector3(0.06f, 0.06f, 0.16f));
                    break;
                case ItemId.Gear:
                    var g = P(PrimitiveType.Cylinder, t, "gold_trim", Vector3.zero, new Vector3(0.18f, 0.015f, 0.18f));
                    for (int i = 0; i < 8; i++)
                        P(PrimitiveType.Cube, g.transform, "gold_trim", Quaternion.Euler(0, i * 45f, 0) * new Vector3(0, 0, 0.55f), new Vector3(0.2f, 1f, 0.22f), new Vector3(0, i * 45f, 0));
                    P(PrimitiveType.Cylinder, t, "black_matte", new Vector3(0, 0.002f, 0), new Vector3(0.05f, 0.017f, 0.05f));
                    break;
                case ItemId.Keycard:
                    P(PrimitiveType.Cube, t, "red_paint", Vector3.zero, new Vector3(0.09f, 0.004f, 0.055f));
                    P(PrimitiveType.Cube, t, "black_matte", new Vector3(0, 0.003f, -0.015f), new Vector3(0.09f, 0.002f, 0.012f));
                    P(PrimitiveType.Cube, t, "gold_trim", new Vector3(-0.025f, 0.003f, 0.01f), new Vector3(0.015f, 0.002f, 0.012f));
                    break;
                case ItemId.Mask:
                    P(PrimitiveType.Sphere, t, "plastic", new Vector3(0, 0.06f, 0), new Vector3(0.24f, 0.28f, 0.1f), new Vector3(-80, 0, 0));
                    P(PrimitiveType.Sphere, t, "black_matte", new Vector3(-0.05f, 0.1f, 0.06f), new Vector3(0.06f, 0.02f, 0.04f));
                    P(PrimitiveType.Sphere, t, "black_matte", new Vector3(0.05f, 0.1f, 0.06f), new Vector3(0.06f, 0.02f, 0.04f));
                    P(PrimitiveType.Cube, t, "red_paint", new Vector3(0, 0.1f, -0.06f), new Vector3(0.1f, 0.01f, 0.015f));
                    break;
                case ItemId.Battery:
                    P(PrimitiveType.Cylinder, t, "black_matte", Vector3.zero, new Vector3(0.035f, 0.045f, 0.035f), new Vector3(0, 0, 90));
                    P(PrimitiveType.Cylinder, t, "yellow_paint", new Vector3(0.025f, 0, 0), new Vector3(0.036f, 0.02f, 0.036f), new Vector3(0, 0, 90));
                    break;
                case ItemId.Oil:
                    P(PrimitiveType.Cylinder, t, "red_paint", new Vector3(0, 0.08f, 0), new Vector3(0.12f, 0.08f, 0.12f));
                    P(PrimitiveType.Cylinder, t, "chrome", new Vector3(0.05f, 0.2f, 0), new Vector3(0.015f, 0.07f, 0.015f), new Vector3(0, 0, -35));
                    P(PrimitiveType.Cube, t, "yellow_paint", new Vector3(0, 0.08f, 0.061f), new Vector3(0.07f, 0.06f, 0.002f));
                    break;
                case ItemId.Water:
                    P(PrimitiveType.Sphere, t, "blue_paint", new Vector3(0, 0.08f, 0), new Vector3(0.15f, 0.17f, 0.15f));
                    P(PrimitiveType.Cube, t, "blue_paint", new Vector3(0, 0.17f, 0), new Vector3(0.025f, 0.025f, 0.025f));
                    break;
                case ItemId.Toy:
                    P(PrimitiveType.Cube, t, "red_paint", new Vector3(0, 0.06f, 0), new Vector3(0.1f, 0.12f, 0.08f));
                    P(PrimitiveType.Sphere, t, "white_paint", new Vector3(0, 0.15f, 0), Vector3.one * 0.08f);
                    P(PrimitiveType.Cube, t, "gold_trim", new Vector3(0, 0.07f, -0.06f), new Vector3(0.08f, 0.02f, 0.02f));
                    break;
            }
            return root;
        }

        public static GameObject Fuse(Transform parent)
        {
            var root = new GameObject("Fuse");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            P(PrimitiveType.Cylinder, t, "glass", Vector3.zero, new Vector3(0.045f, 0.06f, 0.045f));
            P(PrimitiveType.Cylinder, t, "chrome", new Vector3(0, 0.06f, 0), new Vector3(0.05f, 0.015f, 0.05f));
            P(PrimitiveType.Cylinder, t, "chrome", new Vector3(0, -0.06f, 0), new Vector3(0.05f, 0.015f, 0.05f));
            P(PrimitiveType.Cylinder, t, "neon_yellow", Vector3.zero, new Vector3(0.006f, 0.055f, 0.006f));
            return root;
        }

        public static GameObject Crank(Transform parent)
        {
            var root = new GameObject("Crank");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            P(PrimitiveType.Cylinder, t, "metal_rust", new Vector3(0, 0.08f, 0), new Vector3(0.03f, 0.08f, 0.03f));
            P(PrimitiveType.Cube, t, "metal_rust", new Vector3(0.08f, 0.16f, 0), new Vector3(0.18f, 0.025f, 0.03f));
            P(PrimitiveType.Cylinder, t, "red_paint", new Vector3(0.16f, 0.21f, 0), new Vector3(0.03f, 0.05f, 0.03f));
            return root;
        }

        public static GameObject Tape(Transform parent)
        {
            var root = new GameObject("Tape");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            P(PrimitiveType.Cube, t, "black_matte", Vector3.zero, new Vector3(0.1f, 0.012f, 0.065f));
            P(PrimitiveType.Cube, t, "white_paint", new Vector3(0, 0.007f, 0.008f), new Vector3(0.085f, 0.002f, 0.03f));
            P(PrimitiveType.Cylinder, t, "chrome", new Vector3(-0.022f, 0.007f, -0.008f), new Vector3(0.016f, 0.002f, 0.016f));
            P(PrimitiveType.Cylinder, t, "chrome", new Vector3(0.022f, 0.007f, -0.008f), new Vector3(0.016f, 0.002f, 0.016f));
            return root;
        }

        public static GameObject Key(Transform parent, string mat, bool tag = false, bool ornate = false)
        {
            var root = new GameObject("Key");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            P(PrimitiveType.Cylinder, t, mat, new Vector3(-0.05f, 0, 0), new Vector3(ornate ? 0.06f : 0.045f, 0.005f, ornate ? 0.06f : 0.045f));
            P(PrimitiveType.Cylinder, t, "black_matte", new Vector3(-0.05f, 0.001f, 0), new Vector3(0.02f, 0.006f, 0.02f));
            P(PrimitiveType.Cube, t, mat, new Vector3(0.02f, 0, 0), new Vector3(0.1f, 0.008f, 0.012f));
            P(PrimitiveType.Cube, t, mat, new Vector3(0.06f, 0, -0.01f), new Vector3(0.012f, 0.008f, 0.02f));
            P(PrimitiveType.Cube, t, mat, new Vector3(0.045f, 0, -0.008f), new Vector3(0.008f, 0.008f, 0.016f));
            if (tag) P(PrimitiveType.Cube, t, "red_paint", new Vector3(-0.1f, 0, 0.02f), new Vector3(0.05f, 0.003f, 0.03f), new Vector3(0, 25, 0));
            if (ornate) P(PrimitiveType.Sphere, t, "neon_red", new Vector3(-0.05f, 0.004f, 0), Vector3.one * 0.015f);
            return root;
        }
    }
}
