using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace RustedFair
{
    /// <summary>
    /// v1.0.3: sculpted animatronic meshes generated offline (Tools/robotgen). One file per robot and LOD in
    /// Resources/RF/Meshes (robot_grin_lod0.bytes ...): one rigid mesh per rig joint in joint-local space, with
    /// two sub-meshes (opaque, alpha-cut torn holes) that share one texture atlas per robot.
    /// </summary>
    public static class RobotMeshes
    {
        public const string Dir = "RF/Meshes/";
        static readonly Dictionary<string, Dictionary<string, Mesh>> cache = new Dictionary<string, Dictionary<string, Mesh>>();

        /// <summary>Joint name -> mesh, or null when the data is missing (the rig then falls back to primitives).</summary>
        public static Dictionary<string, Mesh> Load(string robot, int lod)
        {
            string key = robot + "_lod" + lod;
            if (cache.TryGetValue(key, out var d)) return d;
            d = null;
            var ta = Resources.Load<TextAsset>(Dir + "robot_" + key);
            if (ta != null)
            {
                try { d = Parse(ta.bytes, key); }
                catch (Exception e) { Debug.LogWarning("[RF] robot mesh " + key + ": " + e.Message); d = null; }
                Resources.UnloadAsset(ta);
            }
            cache[key] = d;
            return d;
        }

        static Dictionary<string, Mesh> Parse(byte[] bytes, string tag)
        {
            var res = new Dictionary<string, Mesh>();
            using (var br = new BinaryReader(new MemoryStream(bytes)))
            {
                var magic = br.ReadBytes(4);
                if (magic.Length < 4 || magic[0] != 'R' || magic[1] != 'F' || magic[2] != 'M' || magic[3] != '2')
                    throw new InvalidDataException("bad header");
                int parts = br.ReadInt32();
                for (int p = 0; p < parts; p++)
                {
                    string bone = System.Text.Encoding.UTF8.GetString(br.ReadBytes(br.ReadInt32()));
                    int vc = br.ReadInt32();
                    var pos = ReadFloats(br, vc * 3);
                    var nor = ReadFloats(br, vc * 3);
                    var uv = ReadFloats(br, vc * 2);
                    var v = new Vector3[vc];
                    var n = new Vector3[vc];
                    var t = new Vector2[vc];
                    for (int i = 0; i < vc; i++)
                    {
                        v[i] = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                        n[i] = new Vector3(nor[i * 3], nor[i * 3 + 1], nor[i * 3 + 2]);
                        t[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
                    }
                    int subs = br.ReadInt32();
                    var lists = new List<int[]>();
                    for (int s = 0; s < subs; s++)
                    {
                        int ic = br.ReadInt32();
                        var idx = new int[ic];
                        Buffer.BlockCopy(br.ReadBytes(ic * 4), 0, idx, 0, ic * 4);
                        lists.Add(idx);
                    }
                    var m = new Mesh { name = tag + "_" + bone };
                    m.indexFormat = vc > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    m.vertices = v;
                    m.normals = n;
                    m.uv = t;
                    // sub-mesh 0 opaque, 1 alpha-cut; an empty cut list is dropped (one material)
                    int used = lists.Count > 1 && lists[1].Length > 0 ? 2 : 1;
                    m.subMeshCount = used;
                    for (int s = 0; s < used; s++) m.SetTriangles(lists[s], s, false);
                    m.RecalculateBounds();
                    m.RecalculateTangents();
                    m.UploadMeshData(true);
                    res[bone] = m;
                }
            }
            return res;
        }

        static float[] ReadFloats(BinaryReader br, int count)
        {
            var f = new float[count];
            Buffer.BlockCopy(br.ReadBytes(count * 4), 0, f, 0, count * 4);
            return f;
        }
    }
}
