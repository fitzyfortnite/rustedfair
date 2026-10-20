using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RustedFair
{
    /// <summary>Accumulates geometry with world-space UVs so tiled materials line up seamlessly.</summary>
    public class MeshBuilder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
        // v1.5: per-vertex colours for merged untextured geometry (empty unless SetPaint was used)
        readonly List<Color32> col = new List<Color32>();
        Color32 paint = new Color32(255, 255, 255, 255);
        bool painted;

        public int VertexCount => v.Count;

        /// <summary>Colour for the vertices added from now on (earlier ones keep the previous paint).</summary>
        public void SetPaint(Color32 c)
        {
            SyncColors();
            paint = c;
            painted = true;
        }

        void SyncColors()
        {
            if (!painted) return;
            while (col.Count < v.Count) col.Add(paint);
        }

        /// <summary>Quad centred at c, facing N, with "up" V, size w x h. UVs are world-projected using tile metres.</summary>
        public void Quad(Vector3 c, Vector3 N, Vector3 V, float w, float h, float tile)
        {
            Vector3 R = Vector3.Cross(N, V).normalized;
            V = V.normalized;
            Vector3 o = c - R * (w * 0.5f) - V * (h * 0.5f);
            Vector3 a = o, b = o + V * h, cc = o + R * w + V * h, d = o + R * w;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            for (int k = 0; k < 4; k++) n.Add(N);
            float inv = 1f / Mathf.Max(0.01f, tile);
            uv.Add(new Vector2(Vector3.Dot(a, R), Vector3.Dot(a, V)) * inv);
            uv.Add(new Vector2(Vector3.Dot(b, R), Vector3.Dot(b, V)) * inv);
            uv.Add(new Vector2(Vector3.Dot(cc, R), Vector3.Dot(cc, V)) * inv);
            uv.Add(new Vector2(Vector3.Dot(d, R), Vector3.Dot(d, V)) * inv);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Quad with explicit 0..1 UVs (posters, signs, screens).</summary>
        public void QuadUV(Vector3 c, Vector3 N, Vector3 V, float w, float h, Rect uvRect)
        {
            Vector3 R = Vector3.Cross(N, V).normalized;
            V = V.normalized;
            Vector3 o = c - R * (w * 0.5f) - V * (h * 0.5f);
            int i = v.Count;
            v.Add(o); v.Add(o + V * h); v.Add(o + R * w + V * h); v.Add(o + R * w);
            for (int k = 0; k < 4; k++) n.Add(N);
            uv.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            uv.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            uv.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            uv.Add(new Vector2(uvRect.xMax, uvRect.yMin));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Axis-aligned or yawed box. faces bitmask: 1=+x 2=-x 4=+y 8=-y 16=+z 32=-z.</summary>
        public void Box(Vector3 center, Vector3 size, float tile, float yaw = 0f, int faces = 63)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 X = q * Vector3.right, Y = Vector3.up, Z = q * Vector3.forward;
            Vector3 h = size * 0.5f;
            if ((faces & 1) != 0) Quad(center + X * h.x, X, Y, size.z, size.y, tile);
            if ((faces & 2) != 0) Quad(center - X * h.x, -X, Y, size.z, size.y, tile);
            if ((faces & 4) != 0) Quad(center + Y * h.y, Y, Z, size.x, size.z, tile);
            if ((faces & 8) != 0) Quad(center - Y * h.y, -Y, Z, size.x, size.z, tile);
            if ((faces & 16) != 0) Quad(center + Z * h.z, Z, Y, size.x, size.y, tile);
            if ((faces & 32) != 0) Quad(center - Z * h.z, -Z, Y, size.x, size.y, tile);
        }

        /// <summary>Box with an arbitrary rotation.</summary>
        public void BoxRot(Vector3 center, Vector3 size, Quaternion q, float tile)
        {
            Vector3 X = q * Vector3.right, Y = q * Vector3.up, Z = q * Vector3.forward;
            Vector3 h = size * 0.5f;
            Quad(center + X * h.x, X, Y, size.z, size.y, tile);
            Quad(center - X * h.x, -X, Y, size.z, size.y, tile);
            Quad(center + Y * h.y, Y, Z, size.x, size.z, tile);
            Quad(center - Y * h.y, -Y, Z, size.x, size.z, tile);
            Quad(center + Z * h.z, Z, Y, size.x, size.y, tile);
            Quad(center - Z * h.z, -Z, Y, size.x, size.y, tile);
        }

        /// <summary>Vertical cylinder from base point upward.</summary>
        public void Cylinder(Vector3 basePos, float radius, float height, int segments, float tile, bool caps = true, float topRadius = -1f)
        {
            if (topRadius < 0f) topRadius = radius;
            float inv = 1f / Mathf.Max(0.01f, tile);
            int start = v.Count;
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 slope = new Vector3(0, (radius - topRadius), 0);
                Vector3 nn = (dir * height + slope).normalized;
                float u = a * radius * inv;
                v.Add(basePos + dir * radius); n.Add(nn); uv.Add(new Vector2(u, 0));
                v.Add(basePos + dir * topRadius + Vector3.up * height); n.Add(nn); uv.Add(new Vector2(u, height * inv));
            }
            for (int s = 0; s < segments; s++)
            {
                int i0 = start + s * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                // outward-facing, clockwise when seen from outside
                t.Add(i0); t.Add(i1); t.Add(i3);
                t.Add(i0); t.Add(i3); t.Add(i2);
            }
            if (!caps) return;
            Cap(basePos + Vector3.up * height, topRadius, segments, Vector3.up, inv);
            Cap(basePos, radius, segments, Vector3.down, inv);
        }

        void Cap(Vector3 c, float r, int segments, Vector3 N, float inv)
        {
            if (r <= 0.0001f) return;
            int ci = v.Count;
            v.Add(c); n.Add(N); uv.Add(new Vector2(c.x, c.z) * inv);
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                v.Add(p); n.Add(N); uv.Add(new Vector2(p.x, p.z) * inv);
            }
            for (int s = 0; s < segments; s++)
            {
                if (N.y > 0) { t.Add(ci); t.Add(ci + s + 2); t.Add(ci + s + 1); }
                else { t.Add(ci); t.Add(ci + s + 1); t.Add(ci + s + 2); }
            }
        }

        /// <summary>A cylinder between two arbitrary points (pipes, poles, bars).</summary>
        public void Tube(Vector3 a, Vector3 b, float radius, int segments, float tile)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 0.001f) return;
            Vector3 dir = axis / len;
            Vector3 side = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
            Vector3 X = Vector3.Cross(dir, side).normalized;
            Vector3 Y = Vector3.Cross(dir, X).normalized;
            float inv = 1f / Mathf.Max(0.01f, tile);
            int start = v.Count;
            for (int s = 0; s <= segments; s++)
            {
                float ang = s / (float)segments * Mathf.PI * 2f;
                Vector3 nn = X * Mathf.Cos(ang) + Y * Mathf.Sin(ang);
                float u = ang * radius * inv;
                v.Add(a + nn * radius); n.Add(nn); uv.Add(new Vector2(u, 0));
                v.Add(b + nn * radius); n.Add(nn); uv.Add(new Vector2(u, len * inv));
            }
            for (int s = 0; s < segments; s++)
            {
                int i0 = start + s * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                t.Add(i0); t.Add(i3); t.Add(i1);
                t.Add(i0); t.Add(i2); t.Add(i3);
            }
        }

        /// <summary>Ellipsoid (radii per local axis, then rotated). Used for merged static heads, plush, balloons.</summary>
        public void Ellipsoid(Vector3 c, Vector3 radii, Quaternion q, int segments, int rings, float tile)
        {
            int start = v.Count;
            Vector3 inv = new Vector3(1f / Mathf.Max(1e-4f, radii.x), 1f / Mathf.Max(1e-4f, radii.y), 1f / Mathf.Max(1e-4f, radii.z));
            float uvScale = Mathf.Max(radii.x, radii.z) * 2f * Mathf.PI / Mathf.Max(0.01f, tile);
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                float sy = Mathf.Cos(phi), sr = Mathf.Sin(phi);
                for (int s = 0; s <= segments; s++)
                {
                    float th = s / (float)segments * Mathf.PI * 2f;
                    var u = new Vector3(Mathf.Cos(th) * sr, sy, Mathf.Sin(th) * sr);
                    v.Add(c + q * Vector3.Scale(u, radii));
                    n.Add((q * Vector3.Scale(u, inv)).normalized);
                    uv.Add(new Vector2(s / (float)segments * uvScale, (1f - r / (float)rings) * uvScale * 0.5f));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int i0 = start + r * (segments + 1) + s, i1 = i0 + 1, i2 = i0 + segments + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i1); t.Add(i3);
                    t.Add(i0); t.Add(i3); t.Add(i2);
                }
        }

        public void Append(MeshBuilder o)
        {
            int off = v.Count;
            v.AddRange(o.v); n.AddRange(o.n); uv.AddRange(o.uv);
            foreach (var i in o.t) t.Add(i + off);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            if (painted) { SyncColors(); m.SetColors(col); }
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            if (v.Count > 0) m.RecalculateTangents();
            return m;
        }
    }

    /// <summary>
    /// Groups static geometry by material and 16 m spatial chunks, so each renderer is small enough for
    /// good culling and per-object lighting.
    /// </summary>
    public class WorldMesh
    {
        /// <summary>v1.4: 72 m chunks (was 16): far fewer draw calls; Forward+ has no per-object light limit.</summary>
        public const float ChunkSize = 72f;

        struct Key
        {
            public string mat; public int cx, cz; public bool shadows;
            public override int GetHashCode() => (mat.GetHashCode() * 397) ^ (cx * 7919) ^ (cz * 104729) ^ (shadows ? 1 : 0);
            public override bool Equals(object obj) => obj is Key k && k.mat == mat && k.cx == cx && k.cz == cz && k.shadows == shadows;
        }

        readonly Dictionary<Key, MeshBuilder> builders = new Dictionary<Key, MeshBuilder>();

        // v1.4: small clutter is kept aside until Build(), so pieces overlapping a task item can be dropped (ClearAround)
        struct Pending { public Vector3 c, e; public System.Action emit; }
        readonly List<Pending> pending = new List<Pending>();
        readonly List<Vector4> clearSpheres = new List<Vector4>();
        public int ClearedCount { get; private set; }

        static bool IsSmall(Vector3 e) => (e.x * e.y * e.z * 8f) < 0.25f && Mathf.Max(e.x, Mathf.Max(e.y, e.z)) * 2f < 0.75f;

        bool Defer(Vector3 c, Vector3 e, System.Action emit)
        {
            if (!IsSmall(e)) return false;
            pending.Add(new Pending { c = c, e = e, emit = emit });
            return true;
        }

        /// <summary>Small clutter whose bounding box touches one of these spheres is not built (call before Build).</summary>
        public void ClearAround(IEnumerable<Vector3> points, float radius)
        {
            foreach (var p in points) clearSpheres.Add(new Vector4(p.x, p.y, p.z, radius));
        }

        static Vector3 YawExtents(Vector3 size, float yaw)
        {
            float a = yaw * Mathf.Deg2Rad, c = Mathf.Abs(Mathf.Cos(a)), s = Mathf.Abs(Mathf.Sin(a));
            return new Vector3(c * size.x * 0.5f + s * size.z * 0.5f, size.y * 0.5f, s * size.x * 0.5f + c * size.z * 0.5f);
        }

        static Vector3 RotExtents(Vector3 size, Quaternion q)
        {
            Vector3 h = size * 0.5f, X = q * Vector3.right, Y = q * Vector3.up, Z = q * Vector3.forward;
            return new Vector3(Mathf.Abs(X.x) * h.x + Mathf.Abs(Y.x) * h.y + Mathf.Abs(Z.x) * h.z,
                               Mathf.Abs(X.y) * h.x + Mathf.Abs(Y.y) * h.y + Mathf.Abs(Z.y) * h.z,
                               Mathf.Abs(X.z) * h.x + Mathf.Abs(Y.z) * h.y + Mathf.Abs(Z.z) * h.z);
        }

        public MeshBuilder For(string mat, Vector3 worldPos, bool shadows = true)
        {
            // v1.5 phones: untextured opaque materials share one vertex-coloured material per chunk (lit / unlit)
            bool vc = MatLib.VertexColorFor(mat, out var vcol, out bool vcUnlit);
            if (vc) mat = vcUnlit ? "vc_unlit" : "vc_lit";
            var k = new Key
            {
                mat = mat,
                cx = Mathf.FloorToInt(worldPos.x / ChunkSize),
                cz = Mathf.FloorToInt(worldPos.z / ChunkSize),
                shadows = shadows && GraphicsQuality.FlashlightShadows // no shadows -> don't split batches by it
            };
            if (!builders.TryGetValue(k, out var b)) { b = new MeshBuilder(); builders[k] = b; }
            if (vc) b.SetPaint(vcol);
            return b;
        }

        public float Tile(string mat) => MatLib.Tile(mat);

        // convenience wrappers ---------------------------------------------------------
        public void Box(string mat, Vector3 center, Vector3 size, float yaw = 0f, bool shadows = true, int faces = 63)
        {
            if (Defer(center, YawExtents(size, yaw), () => For(mat, center, shadows).Box(center, size, Tile(mat), yaw, faces))) return;
            For(mat, center, shadows).Box(center, size, Tile(mat), yaw, faces);
        }

        public void Quad(string mat, Vector3 c, Vector3 N, Vector3 V, float w, float h, bool shadows = false)
            => For(mat, c, shadows).Quad(c, N, V, w, h, Tile(mat));

        public void QuadUV(string mat, Vector3 c, Vector3 N, Vector3 V, float w, float h, bool shadows = false)
            => For(mat, c, shadows).QuadUV(c, N, V, w, h, new Rect(0, 0, 1, 1));

        public void Cyl(string mat, Vector3 basePos, float r, float h, int seg = 12, bool shadows = true, bool caps = true, float topR = -1f)
        {
            float rr = Mathf.Max(r, topR);
            if (Defer(basePos + Vector3.up * (h * 0.5f), new Vector3(rr, h * 0.5f, rr), () => For(mat, basePos, shadows).Cylinder(basePos, r, h, seg, Tile(mat), caps, topR))) return;
            For(mat, basePos, shadows).Cylinder(basePos, r, h, seg, Tile(mat), caps, topR);
        }

        public void Tube(string mat, Vector3 a, Vector3 b, float r, int seg = 8, bool shadows = true)
        {
            var e = new Vector3(Mathf.Abs(a.x - b.x) * 0.5f + r, Mathf.Abs(a.y - b.y) * 0.5f + r, Mathf.Abs(a.z - b.z) * 0.5f + r);
            if (Defer((a + b) * 0.5f, e, () => For(mat, (a + b) * 0.5f, shadows).Tube(a, b, r, seg, Tile(mat)))) return;
            For(mat, (a + b) * 0.5f, shadows).Tube(a, b, r, seg, Tile(mat));
        }

        /// <summary>Static ellipsoid: size = full diameters along the local axes (like a scaled Unity sphere).</summary>
        public void Ell(string mat, Vector3 center, Vector3 size, Quaternion rot, bool shadows = true, bool lowPoly = false)
        {
            if (Defer(center, RotExtents(size, rot), () => For(mat, center, shadows).Ellipsoid(center, size * 0.5f, rot, lowPoly ? 7 : 10, lowPoly ? 5 : 7, Tile(mat)))) return;
            For(mat, center, shadows).Ellipsoid(center, size * 0.5f, rot, lowPoly ? 7 : 10, lowPoly ? 5 : 7, Tile(mat));
        }

        public void BoxRot(string mat, Vector3 center, Vector3 size, Quaternion rot, bool shadows = true)
        {
            if (Defer(center, RotExtents(size, rot), () => For(mat, center, shadows).BoxRot(center, size, rot, Tile(mat)))) return;
            For(mat, center, shadows).BoxRot(center, size, rot, Tile(mat));
        }

        public int Build(Transform parent)
        {
            // emit the deferred small clutter, except pieces touching a task item
            ClearedCount = 0;
            foreach (var pd in pending)
            {
                bool hit = false;
                foreach (var sp in clearSpheres)
                {
                    float dx = Mathf.Max(Mathf.Abs(sp.x - pd.c.x) - pd.e.x, 0f), dy = Mathf.Max(Mathf.Abs(sp.y - pd.c.y) - pd.e.y, 0f), dz = Mathf.Max(Mathf.Abs(sp.z - pd.c.z) - pd.e.z, 0f);
                    if (dx * dx + dy * dy + dz * dz < sp.w * sp.w) { hit = true; break; }
                }
                if (hit) ClearedCount++; else pd.emit();
            }
            pending.Clear();
            clearSpheres.Clear();
            int count = 0;
            foreach (var kv in builders)
            {
                var b = kv.Value;
                if (b.VertexCount == 0) continue;
                var go = new GameObject($"geo_{kv.Key.mat}_{kv.Key.cx}_{kv.Key.cz}");
                go.transform.SetParent(parent, false);
                go.isStatic = true;
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = b.ToMesh(go.name);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MatLib.Get(kv.Key.mat);
                mr.shadowCastingMode = kv.Key.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.receiveShadows = true;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                count++;
            }
            builders.Clear();
            return count;
        }
    }

    /// <summary>
    /// v1.4: merges leaf meshes (renderers without children) per parent per material into one renderer in the parent's
    /// space, so moving parents (carousel, hide-spot doors) still animate. Renderers in "keep" (hide-spot occluders)
    /// stay separate. Colliders on merged primitives are kept.
    /// </summary>
    public static class LeafMerger
    {
        /// <summary>
        /// v1.5: merges the leaves of a scripted set piece (clock, fortune teller, exit gate) while keeping every object
        /// its script references (animated hands, the card, eye renderers, gear visuals...) as separate renderers.
        /// </summary>
        public static int MergeKeepingReferenced(MonoBehaviour owner)
        {
            if (owner == null) return 0;
            var keep = new HashSet<Renderer>();
            const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            for (var type = owner.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (var fi in type.GetFields(bf | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    object v = fi.GetValue(owner);
                    if (v is string) continue;
                    if (v is System.Collections.IEnumerable list && !(v is Transform)) { foreach (var e in list) KeepOf(e, keep); }
                    else KeepOf(v, keep);
                }
            return Merge(owner.transform, keep);
        }

        static void KeepOf(object o, HashSet<Renderer> keep)
        {
            GameObject go = null;
            if (o is Renderer r0) { keep.Add(r0); return; }
            if (o is Component c && c != null) go = c.gameObject;
            else if (o is GameObject g && g != null) go = g;
            if (go == null) return;
            // the referenced object itself, and (for referenced visuals like gear models) all renderers below it
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) keep.Add(r);
        }

        public static int Merge(Transform root, ICollection<Renderer> keep = null)
        {
            if (root == null) return 0;
            var parents = new HashSet<Transform>();
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.transform.childCount == 0 && mr.transform.parent != null) parents.Add(mr.transform.parent);
            int merged = 0;
            var byMat = new Dictionary<Material, List<MeshFilter>>();
            foreach (var par in parents)
            {
                byMat.Clear();
                for (int i = 0; i < par.childCount; i++)
                {
                    var c = par.GetChild(i);
                    if (c.childCount > 0 || !c.gameObject.activeSelf) continue;
                    var mr = c.GetComponent<MeshRenderer>();
                    var mf = c.GetComponent<MeshFilter>();
                    if (mr == null || mf == null || mf.sharedMesh == null || !mr.enabled || mr.sharedMaterial == null) continue;
                    if (keep != null && keep.Contains(mr)) continue;
                    if (!byMat.TryGetValue(mr.sharedMaterial, out var list)) { list = new List<MeshFilter>(); byMat[mr.sharedMaterial] = list; }
                    list.Add(mf);
                }
                foreach (var kv in byMat)
                {
                    var list = kv.Value;
                    if (list.Count < 2) continue;
                    var ci = new CombineInstance[list.Count];
                    var w2l = par.worldToLocalMatrix;
                    int verts = 0;
                    for (int i = 0; i < list.Count; i++)
                    {
                        ci[i].mesh = list[i].sharedMesh;
                        ci[i].transform = w2l * list[i].transform.localToWorldMatrix;
                        verts += list[i].sharedMesh.vertexCount;
                    }
                    var mesh = new Mesh { name = "merged_" + kv.Key.name };
                    if (verts > 65000) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.CombineMeshes(ci, true, true);
                    var first = list[0].GetComponent<MeshRenderer>();
                    var go = new GameObject("Merged_" + kv.Key.name);
                    go.layer = par.gameObject.layer;
                    go.transform.SetParent(par, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var nmr = go.AddComponent<MeshRenderer>();
                    nmr.sharedMaterial = kv.Key;
                    nmr.shadowCastingMode = first.shadowCastingMode;
                    nmr.receiveShadows = first.receiveShadows;
                    foreach (var mf in list)
                    {
                        var r = mf.GetComponent<MeshRenderer>();
                        r.enabled = false;
                        if (Application.isPlaying) { Object.Destroy(r); Object.Destroy(mf); }
                        else { Object.DestroyImmediate(r); Object.DestroyImmediate(mf); }
                    }
                    merged += list.Count;
                }
            }
            return merged;
        }
    }
}
