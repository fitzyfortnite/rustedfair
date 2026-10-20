using UnityEngine;

namespace RustedFair
{
    /// <summary>Reusable prop builders (static geometry merged into the world mesh).</summary>
    public partial class FairBuilder
    {
        static Quaternion Yaw(float y) => Quaternion.Euler(0, y, 0);

        /// <summary>Static box with collider; optionally blocks the AI grid.</summary>
        void Solid(string mat, Vector3 center, Vector3 size, float yaw = 0f, bool block = true, bool shadows = true)
        {
            wm.Box(mat, center, size, yaw, shadows);
            AddColliderRot(center, size, yaw);
            if (block) map.BlockWorld(center, size, yaw);
        }

        void Deco(string mat, Vector3 center, Vector3 size, float yaw = 0f, bool shadows = false)
            => wm.Box(mat, center, size, yaw, shadows);

        void Decal(string mat, Vector3 pos, float size, float yaw)
        {
            wm.For(mat, pos, false).QuadUV(new Vector3(pos.x, 0.012f + Random.value * 0.004f, pos.z), Vector3.up, Yaw(yaw) * Vector3.forward, size, size, new Rect(0, 0, 1, 1));
        }

        void WallDecal(string mat, Vector3 pos, Vector3 normal, float w, float h)
            => wm.For(mat, pos, false).QuadUV(pos + normal * 0.012f, normal, Vector3.up, w, h, new Rect(0, 0, 1, 1));

        void Poster(Vector3 pos, Vector3 normal, int idx, float scale = 1f)
        {
            float w = 1.1f * scale, h = 1.65f * scale;
            wm.For("poster_" + idx, pos, false).QuadUV(pos + normal * 0.015f, normal, Vector3.up, w, h, new Rect(0, 0, 1, 1));
            wm.Box("wood", pos + normal * 0.006f, Abs(Yaw0(normal) * new Vector3(w + 0.08f, h + 0.08f, 0.01f)), 0, false);
        }

        static Quaternion Yaw0(Vector3 normal) => Quaternion.LookRotation(normal, Vector3.up);
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        void ScatterDecals(RoomDef room, int blood, int grime)
        {
            for (int i = 0; i < blood; i++)
            {
                var p = map.RandomPointInRoom(room) + new Vector3(Random.Range(-0.8f, 0.8f), 0, Random.Range(-0.8f, 0.8f));
                Decal("decal_blood", p, Random.Range(0.6f, 1.6f), Random.Range(0, 360f));
            }
            for (int i = 0; i < grime; i++)
            {
                var p = map.RandomPointInRoom(room) + new Vector3(Random.Range(-0.8f, 0.8f), 0, Random.Range(-0.8f, 0.8f));
                Decal("decal_grime", p, Random.Range(1.2f, 3.2f), Random.Range(0, 360f));
            }
        }

        void Table(Vector3 p, bool round, bool chairs = true, bool knocked = false)
        {
            if (round)
            {
                wm.Cyl("metal_rust", p, 0.07f, 0.72f, 6, false, false);
                wm.Cyl("wood_painted", p + Vector3.up * 0.72f, 0.55f, 0.05f, 14);
                wm.Cyl("metal_rust", p, 0.3f, 0.03f, 10, false);
            }
            else
            {
                wm.Box("wood", p + Vector3.up * 0.74f, new Vector3(1.4f, 0.05f, 0.8f));
                foreach (var o in new[] { new Vector3(-0.62f, 0, -0.32f), new Vector3(0.62f, 0, -0.32f), new Vector3(-0.62f, 0, 0.32f), new Vector3(0.62f, 0, 0.32f) })
                    wm.Box("metal_rust", p + o + Vector3.up * 0.36f, new Vector3(0.05f, 0.72f, 0.05f), 0, false);
            }
            AddColliderRot(p + Vector3.up * 0.4f, new Vector3(round ? 1.05f : 1.4f, 0.8f, round ? 1.05f : 0.8f), 0);
            if (!chairs) return;
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + Random.Range(-25f, 25f);
                var cp = p + Yaw(a) * Vector3.forward * 0.95f;
                if (knocked && i == 1) ChairFallen(cp, a); else Chair(cp, a + 180f);
            }
        }

        void Chair(Vector3 p, float yaw)
        {
            var q = Yaw(yaw);
            wm.Box("red_paint", p + Vector3.up * 0.45f, new Vector3(0.45f, 0.05f, 0.45f), yaw, false);
            wm.Box("red_paint", p + q * new Vector3(0, 0.72f, -0.2f), new Vector3(0.45f, 0.5f, 0.05f), yaw, false);
            foreach (var o in new[] { new Vector3(-0.19f, 0, -0.19f), new Vector3(0.19f, 0, -0.19f), new Vector3(-0.19f, 0, 0.19f), new Vector3(0.19f, 0, 0.19f) })
                wm.Box("metal_rust", p + q * o + Vector3.up * 0.22f, new Vector3(0.03f, 0.44f, 0.03f), yaw, false);
            AddColliderRot(p + Vector3.up * 0.5f, new Vector3(0.48f, 1.0f, 0.48f), yaw); // v1.2: chairs were walk-through
        }

        void ChairFallen(Vector3 p, float yaw)
        {
            var q = Yaw(yaw) * Quaternion.Euler(90f, 0, 0);
            wm.For("red_paint", p, false).BoxRot(p + Vector3.up * 0.25f, new Vector3(0.45f, 0.05f, 0.45f), q, 1f);
            wm.For("red_paint", p, false).BoxRot(p + Vector3.up * 0.25f + Yaw(yaw) * new Vector3(0, 0, 0.3f), new Vector3(0.45f, 0.5f, 0.05f), q, 1f);
            AddColliderRot(p + Vector3.up * 0.25f, new Vector3(0.48f, 0.5f, 0.24f), yaw);
        }

        void Bench(Vector3 p, float yaw)
        {
            var q = Yaw(yaw);
            wm.Box("wood", p + Vector3.up * 0.45f, new Vector3(2f, 0.06f, 0.45f), yaw);
            wm.Box("wood", p + q * new Vector3(0, 0.75f, -0.22f), new Vector3(2f, 0.35f, 0.05f), yaw, false);
            wm.Box("metal_rust", p + q * new Vector3(-0.85f, 0.22f, 0), new Vector3(0.06f, 0.45f, 0.45f), yaw, false);
            wm.Box("metal_rust", p + q * new Vector3(0.85f, 0.22f, 0), new Vector3(0.06f, 0.45f, 0.45f), yaw, false);
            AddColliderRot(p + Vector3.up * 0.4f, new Vector3(2f, 0.8f, 0.5f), yaw);
        }

        void Crate(Vector3 p, float s, float yaw, bool block = false)
        {
            Solid("wood", p + Vector3.up * s * 0.5f, new Vector3(s, s, s), yaw, block);
            wm.Box("metal_rust", p + Vector3.up * s * 0.5f, new Vector3(s + 0.02f, 0.06f, s + 0.02f), yaw, false);
        }

        void CrateStack(Vector3 p, float yaw)
        {
            Crate(p, 1.1f, yaw, true);
            if (Random.value < 0.7f) Crate(p + Vector3.up * 1.1f + Yaw(yaw) * new Vector3(Random.Range(-0.1f, 0.1f), 0, 0), 0.9f, yaw + Random.Range(-15f, 15f));
            if (Random.value < 0.4f) Crate(p + Yaw(yaw) * new Vector3(1.15f, 0, 0), 1f, yaw + Random.Range(-10f, 10f), true);
        }

        void Barrel(Vector3 p, string mat = "metal_rust")
        {
            wm.Cyl(mat, p, 0.3f, 0.9f, 12);
            wm.Cyl("black_matte", p + Vector3.up * 0.3f, 0.305f, 0.04f, 12, false, false);
            wm.Cyl("black_matte", p + Vector3.up * 0.62f, 0.305f, 0.04f, 12, false, false);
            AddColliderRot(p + Vector3.up * 0.45f, new Vector3(0.6f, 0.9f, 0.6f), 0);
        }

        void Shelf(Vector3 p, float yaw, float len, float height = 2.2f, bool parts = false)
        {
            var q = Yaw(yaw);
            float depth = 0.6f;
            for (float x = -len * 0.5f; x <= len * 0.5f + 0.01f; x += len / Mathf.Max(1, Mathf.Round(len / 1.6f)))
            {
                wm.Box("metal_rust", p + q * new Vector3(x, height * 0.5f, -depth * 0.5f), new Vector3(0.06f, height, 0.06f), yaw, false);
                wm.Box("metal_rust", p + q * new Vector3(x, height * 0.5f, depth * 0.5f), new Vector3(0.06f, height, 0.06f), yaw, false);
            }
            for (float y = 0.15f; y < height; y += height / 3.2f)
            {
                wm.Box("metal_rust", p + q * new Vector3(0, y, 0), new Vector3(len, 0.04f, depth), yaw, false);
                // stuff on shelves
                for (float x = -len * 0.5f + 0.4f; x < len * 0.5f - 0.3f; x += Random.Range(0.45f, 0.9f))
                {
                    if (Random.value < 0.25f) continue;
                    float s = Random.Range(0.22f, 0.42f);
                    if (parts && Random.value < 0.25f)
                    {
                        // a broken animatronic head staring out of the shelf
                        var hp = p + q * new Vector3(x, y + 0.2f, 0);
                        var head = RF.Prim(PrimitiveType.Sphere, staticRoot, Random.value < 0.5f ? "plastic" : "fur_dark", hp, Vector3.one * 0.36f, new Vector3(0, yaw + 180 + Random.Range(-30, 30), Random.Range(-20, 20)), false);
                        RF.Prim(PrimitiveType.Sphere, head.transform, "black_matte", new Vector3(-0.18f, 0.08f, 0.42f), Vector3.one * 0.18f, default, false);
                        RF.Prim(PrimitiveType.Sphere, head.transform, Random.value < 0.3f ? "eye_dim" : "black_matte", new Vector3(0.18f, 0.08f, 0.42f), Vector3.one * 0.18f, default, false);
                    }
                    else
                        wm.Box(Random.value < 0.7f ? "cardboard" : "wood", p + q * new Vector3(x, y + s * 0.5f + 0.02f, Random.Range(-0.1f, 0.1f)), new Vector3(s, s * Random.Range(0.6f, 1f), s), yaw + Random.Range(-12, 12), false);
                }
            }
            // v1.4: slimmer than the boards (0.36 of 0.6 m) so an item on the front lip isn't inside the collider
            // (the interaction ray would hit the shelf first and the item could never be taken)
            AddColliderRot(p + Vector3.up * height * 0.5f, new Vector3(len, height, depth * 0.6f), yaw);
            // a 0.6 m shelf on a cell boundary blocked no cell at all (robots walked through warehouse racks)
            map.BlockWorld(p, new Vector3(len, 1, Mathf.Max(depth, 1.6f)), yaw);
        }

        /// <summary>A food stall: counter, back wall, striped canopy and a neon sign strip.</summary>
        void Stall(Vector3 p, float yaw, string neon, float width = 5f, string spotKey = null)
        {
            var q = Yaw(yaw);
            float d = 2.4f;
            Solid("wood_painted", p + q * new Vector3(0, 0.55f, d * 0.5f - 0.3f), new Vector3(width, 1.1f, 0.6f), yaw, true);
            wm.Box("metal_rust", p + q * new Vector3(0, 1.12f, d * 0.5f - 0.3f), new Vector3(width + 0.1f, 0.05f, 0.7f), yaw, false);
            Solid("wall_plaster", p + q * new Vector3(0, 1.6f, -d * 0.5f), new Vector3(width, 3.2f, 0.15f), yaw, true);
            wm.Box("wood_painted", p + q * new Vector3(-width * 0.5f, 1.6f, 0), new Vector3(0.15f, 3.2f, d), yaw);
            wm.Box("wood_painted", p + q * new Vector3(width * 0.5f, 1.6f, 0), new Vector3(0.15f, 3.2f, d), yaw);
            AddColliderRot(p + q * new Vector3(-width * 0.5f, 1.6f, 0), new Vector3(0.15f, 3.2f, d), yaw);
            AddColliderRot(p + q * new Vector3(width * 0.5f, 1.6f, 0), new Vector3(0.15f, 3.2f, d), yaw);
            // canopy (sloped stripes)
            var canopyRot = Yaw(yaw) * Quaternion.Euler(-18f, 0, 0);
            wm.For("tent", p, false).BoxRot(p + q * new Vector3(0, 3.35f, 0.35f), new Vector3(width + 0.4f, 0.05f, d + 0.6f), canopyRot, MatLib.Tile("tent"));
            // scalloped valance
            for (float x = -width * 0.5f; x < width * 0.5f; x += 0.5f)
                wm.For("tent", p, false).QuadUV(p + q * new Vector3(x + 0.25f, 2.95f, d * 0.5f + 0.62f), q * Vector3.forward, Vector3.up, 0.5f, 0.35f, new Rect(0, 0, 0.2f, 0.25f));
            // neon sign
            wm.Box(neon, p + q * new Vector3(0, 3.0f, d * 0.5f + 0.5f), new Vector3(width * 0.7f, 0.12f, 0.06f), yaw, false);
            // clutter on counter
            for (int i = 0; i < 3; i++)
                wm.Cyl(Random.value < 0.5f ? "red_paint" : "white_paint", p + q * new Vector3(Random.Range(-width * 0.4f, width * 0.4f), 1.15f, d * 0.5f - 0.3f), 0.05f, 0.14f, 8, false);
            if (spotKey != null) AddSpot(spotKey, p + q * new Vector3(Random.Range(-1f, 1f), 1.2f, d * 0.5f - 0.3f), yaw);
            // the space behind the counter is enclosed: block it for the AI
            map.BlockWorld(p, new Vector3(width, 1, d), yaw);
        }

        void ArcadeCab(Vector3 p, float yaw, int screen)
        {
            var q = Yaw(yaw);
            Solid("black_matte", p + Vector3.up * 0.95f, new Vector3(0.75f, 1.9f, 0.8f), yaw, false);
            wm.Box(screen % 2 == 0 ? "red_paint" : "blue_paint", p + q * new Vector3(-0.38f, 0.95f, 0), new Vector3(0.02f, 1.9f, 0.82f), yaw, false);
            wm.Box(screen % 2 == 0 ? "red_paint" : "blue_paint", p + q * new Vector3(0.38f, 0.95f, 0), new Vector3(0.02f, 1.9f, 0.82f), yaw, false);
            var screenRot = Yaw(yaw) * Quaternion.Euler(-15f, 0, 0);
            wm.For("arcade_" + (screen % 3 + 1), p, false).QuadUV(p + q * new Vector3(0, 1.38f, 0.41f), screenRot * Vector3.forward, screenRot * Vector3.up, 0.58f, 0.46f, new Rect(0, 0, 1, 1));
            wm.Box("black_matte", p + q * new Vector3(0, 1.0f, 0.5f), new Vector3(0.7f, 0.06f, 0.3f), yaw, false);
            wm.Box(screen % 3 == 0 ? "neon_pink" : (screen % 3 == 1 ? "neon_cyan" : "neon_yellow"), p + q * new Vector3(0, 1.82f, 0.4f), new Vector3(0.66f, 0.14f, 0.04f), yaw, false);
        }

        void StringLights(Vector3 a, Vector3 b, int bulbs, float sag = 0.8f)
        {
            Vector3 prev = a;
            string[] mats = { "bulb_warm", "neon_red", "neon_yellow", "bulb_warm", "neon_cyan" };
            for (int i = 1; i <= bulbs; i++)
            {
                float t = i / (float)bulbs;
                Vector3 p = Vector3.Lerp(a, b, t) - Vector3.up * (Mathf.Sin(t * Mathf.PI) * sag);
                wm.Tube("black_matte", prev, p, 0.008f, 3, false);
                if (i < bulbs && Random.value > 0.25f) wm.Box(mats[i % mats.Length], p - Vector3.up * 0.06f, new Vector3(0.07f, 0.09f, 0.07f), 0, false);
                prev = p;
            }
        }

        void Pipe(Vector3 a, Vector3 b, float r = 0.08f, string mat = "metal_rust") => wm.Tube(mat, a, b, r, 8, false);

        void Debris(RoomDef room, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var p = map.RandomPointInRoom(room) + new Vector3(Random.Range(-0.8f, 0.8f), 0, Random.Range(-0.8f, 0.8f));
                float s = Random.Range(0.08f, 0.3f);
                string mat = Random.value < 0.4f ? "cardboard" : (Random.value < 0.5f ? "paper" : "metal_rust");
                if (mat == "paper")
                    wm.For("paper", p, false).QuadUV(p + Vector3.up * 0.015f, Vector3.up, Yaw(Random.Range(0, 360f)) * Vector3.forward, 0.25f, 0.32f, new Rect(0, 0, 1, 1));
                else
                    wm.Box(mat, p + Vector3.up * s * 0.25f, new Vector3(s, s * 0.5f, s * 0.8f), Random.Range(0, 360f), false);
            }
        }

        void Lockers(Vector3 start, Vector3 along, float yaw, int count, int hideEvery = 2)
        {
            for (int i = 0; i < count; i++)
            {
                var p = start + along * (i * 0.85f);
                if (i % hideEvery == 0) HideSpot.Locker(dynRoot, p, yaw);
                else
                {
                    var q = Yaw(yaw);
                    Solid("wall_metal", p + Vector3.up * 1.02f, new Vector3(0.8f, 2.05f, 0.65f), yaw, false);
                    for (int v = 0; v < 5; v++)
                        wm.Box("black_matte", p + q * new Vector3(0, 1.75f - v * 0.07f, 0.33f), new Vector3(0.55f, 0.025f, 0.01f), yaw, false);
                }
            }
            map.BlockWorld(start + along * ((count - 1) * 0.85f * 0.5f), new Vector3(count * 0.85f, 1, 0.65f), yaw);
        }

        void Mannequin(Vector3 p, float yaw, string bodyMat = "plastic")
        {
            // an eerie broken mascot statue (static)
            var root = new GameObject("Statue").transform;
            root.SetParent(staticRoot, false);
            root.position = p;
            root.rotation = Yaw(yaw);
            RF.Prim(PrimitiveType.Capsule, root, bodyMat, new Vector3(0, 1.0f, 0), new Vector3(0.6f, 0.65f, 0.45f));
            var head = RF.Prim(PrimitiveType.Sphere, root, bodyMat, new Vector3(0.05f, 1.95f, 0.02f), new Vector3(0.6f, 0.58f, 0.58f), new Vector3(0, 0, 18));
            RF.Prim(PrimitiveType.Sphere, head.transform, "black_matte", new Vector3(-0.2f, 0.1f, 0.42f), Vector3.one * 0.2f, default, false);
            RF.Prim(PrimitiveType.Sphere, head.transform, "black_matte", new Vector3(0.2f, 0.1f, 0.42f), Vector3.one * 0.2f, default, false);
            RF.Prim(PrimitiveType.Cube, head.transform, "teeth", new Vector3(0, -0.2f, 0.45f), new Vector3(0.45f, 0.1f, 0.1f), default, false);
            RF.Prim(PrimitiveType.Capsule, root, bodyMat, new Vector3(-0.42f, 1.1f, 0.1f), new Vector3(0.18f, 0.4f, 0.18f), new Vector3(20, 0, 30));
            RF.Prim(PrimitiveType.Capsule, root, "endo", new Vector3(0.4f, 0.95f, 0.15f), new Vector3(0.08f, 0.38f, 0.08f), new Vector3(-30, 0, -15));
            AddColliderRot(p + Vector3.up * 1.1f, new Vector3(0.8f, 2.2f, 0.6f), yaw);
        }

        void Cobweb(Vector3 corner, Vector3 n1, Vector3 n2)
        {
            var c = corner + (n1 + n2) * 0.01f;
            wm.For("cloth_white", c, false).QuadUV(c + (n1 + n2) * 0.25f, ((n1 + n2).normalized + Vector3.down * 0.5f).normalized, Vector3.up, 0.8f, 0.5f, new Rect(0, 0, 0.3f, 0.3f));
        }
    }
}
