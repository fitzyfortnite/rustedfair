using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    public struct Spot
    {
        public Vector3 pos;
        public float yaw;
        public Spot(Vector3 p, float y) { pos = p; yaw = y; }
        public Quaternion Rot => Quaternion.Euler(0, yaw, 0);
    }

    /// <summary>
    /// Builds the whole indoor fairground procedurally: rooms, walls, doors, lights, props,
    /// and registers anchor spots used by the quest/item placement.
    /// Map is 72 x 56 cells of 2 m (144 x 112 m).
    /// </summary>
    public partial class FairBuilder
    {
        public readonly FairMap map = new FairMap();
        public readonly Dictionary<string, List<Spot>> spots = new Dictionary<string, List<Spot>>();
        public readonly Dictionary<string, Transform> anchors = new Dictionary<string, Transform>();
        public readonly Dictionary<string, Door> doors = new Dictionary<string, Door>();

        public Transform root, staticRoot, dynRoot, colRoot;
        readonly WorldMesh wm = new WorldMesh();
        readonly LightSystem lights;

        const float DoorH = 2.5f;
        /// <summary>v1.7: room wall faces are drawn this far inside their own room (anti z-fighting).</summary>
        public const float WallInset = 0.02f;
        const float ArchMaxH = 4.6f;

        public FairBuilder(Transform parent, LightSystem lightSystem)
        {
            root = new GameObject("Fair").transform;
            root.SetParent(parent, false);
            staticRoot = new GameObject("Static").transform; staticRoot.SetParent(root, false);
            dynRoot = new GameObject("Dynamic").transform; dynRoot.SetParent(root, false);
            colRoot = new GameObject("Colliders").transform; colRoot.SetParent(root, false);
            WallColliderRoot = colRoot;
            lights = lightSystem;
        }

        /// <summary>Parent of the static wall/floor colliders (Default layer); props use LayerProps.</summary>
        public static Transform WallColliderRoot;

        /// <summary>True for room walls (and the main exit gate), the only things the robots' body probes avoid.</summary>
        public static bool IsWallCollider(Collider c)
        {
            if (c == null || c.isTrigger || c.gameObject.layer != RF.LayerDefault) return false;
            if (WallColliderRoot != null && c.transform.parent == WallColliderRoot) return true;
            return c.GetComponentInParent<RollingGate>() != null;
        }

        // ================================================================ public entry
        /// <summary>Builds the fair. bake=false leaves the static geometry unbaked so the game can first spawn the task
        /// items, clear small clutter around them (ClearAroundItems) and then call Bake() (v1.4).</summary>
        public void Build(bool bake = true)
        {
            DefineLayout();
            GenerateMaze();
            GenerateTunnel();
            BuildRoomShells();
            BuildInternalWalls();
            BuildWallColliders();
            BuildDoors();
            BuildRoomLights();
            BuildAllProps();
            MergeDynamicLeaves();
            if (bake) Bake();
        }

        bool baked;

        /// <summary>Merges the static geometry into 72 m chunks per material (one draw call each).</summary>
        public void Bake()
        {
            if (baked) return;
            baked = true;
            // loose static primitives (shelf heads, ghosts, statues, balloons...) merged per parent per material;
            // bulbs still driven by a light (outside street lamp) stay separate
            var keep = new HashSet<Renderer>();
            if (lights != null) lights.CollectBulbs(keep);
            LeafMerger.Merge(staticRoot, keep);
            wm.Build(staticRoot);
        }

        /// <summary>
        /// v1.4: small static clutter (bbox volume &lt; 0.25 m³ and longest side &lt; 0.75 m) touching a 0.32 m sphere
        /// around a task item is dropped before baking, so shelf boxes / debris never hide a fuse, token or gear.
        /// Returns the number of removed pieces.
        /// </summary>
        public int ClearAroundItems(List<Vector3> points, float radius = 0.32f)
        {
            if (baked || points == null || points.Count == 0) return 0;
            wm.ClearAround(points, radius);
            int removed = 0;
            // loose primitives (shelf heads, balloons...) under the static root
            foreach (var r in staticRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                var b = r.bounds;
                if (b.size.x * b.size.y * b.size.z >= 0.25f || Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) >= 0.75f) continue;
                foreach (var p in points)
                {
                    if ((b.ClosestPoint(p) - p).sqrMagnitude >= radius * radius) continue;
                    r.gameObject.SetActive(false);
                    removed++;
                    break;
                }
            }
            return removed;
        }

        /// <summary>v1.4: fewer draw calls - leaf meshes inside the carousel and the hide spots are merged per parent
        /// per material (hide-spot occluders stay separate so they can still be hidden from inside).</summary>
        void MergeDynamicLeaves()
        {
            if (carousel != null) LeafMerger.Merge(carousel.transform);
            // v1.5: the clock automaton, fortune teller and exit gate (scripted/animated parts stay separate)
            if (clock != null) LeafMerger.MergeKeepingReferenced(clock);
            if (fortuneTeller != null) LeafMerger.MergeKeepingReferenced(fortuneTeller);
            if (exitGate != null) LeafMerger.MergeKeepingReferenced(exitGate);
            foreach (var hs in dynRoot.GetComponentsInChildren<HideSpot>(true))
                LeafMerger.Merge(hs.transform, new HashSet<Renderer>(hs.occluders));
        }

        public void AddSpot(string key, Vector3 pos, float yaw = 0f)
        {
            if (!spots.TryGetValue(key, out var list)) { list = new List<Spot>(); spots[key] = list; }
            list.Add(new Spot(pos, yaw));
        }

        public Spot GetSpot(string key)
        {
            if (spots.TryGetValue(key, out var l) && l.Count > 0) return l[0];
            Debug.LogWarning("[RF] Missing spot " + key);
            return new Spot(new Vector3(60, 0, 10), 0);
        }

        public Transform Anchor(string key, Vector3 pos, float yaw)
        {
            var t = new GameObject("A_" + key).transform;
            t.SetParent(dynRoot, false);
            t.position = pos;
            t.rotation = Quaternion.Euler(0, yaw, 0);
            anchors[key] = t;
            return t;
        }

        RoomDef R(RoomId id) => map.Room(id);

        // ================================================================ layout
        void DefineLayout()
        {
            var m = map;
            var eh = m.AddRoom(RoomId.EntranceHall, 22, 0, 18, 11, 9f, "floor_tile", "wall_plaster", "ceiling");
            eh.lightSpacing = 9f; eh.light = new Color(1f, 0.8f, 0.6f);
            var so = m.AddRoom(RoomId.Security, 40, 0, 8, 7, 3.4f, "floor_concrete", "wall_plaster", "ceiling");
            so.lightSpacing = 6f; so.light = new Color(0.75f, 0.85f, 1f);
            var bb = m.AddRoom(RoomId.Broadcast, 40, 7, 8, 4, 3.4f, "floor_carpet", "wall_plaster", "ceiling");
            bb.lightSpacing = 6f;
            var fc = m.AddRoom(RoomId.FoodCourt, 48, 0, 22, 16, 7f, "floor_tile", "wall_brick", "ceiling");
            fc.lightSpacing = 10f; fc.light = new Color(1f, 0.85f, 0.65f);
            var ki = m.AddRoom(RoomId.Kitchen, 48, 16, 12, 8, 3.4f, "floor_tile", "wall_metal", "ceiling");
            ki.lightSpacing = 6f; ki.light = new Color(0.85f, 0.95f, 1f);
            var cr = m.AddRoom(RoomId.ColdRoom, 60, 16, 6, 6, 3.2f, "floor_grate", "wall_metal", "ceiling");
            cr.lightSpacing = 6f; cr.light = new Color(0.6f, 0.8f, 1f);
            var mm = m.AddRoom(RoomId.MirrorMaze, 4, 0, 18, 11, 4f, "floor_carpet", "wall_brick", "ceiling");
            mm.dark = true;
            var ch = m.AddRoom(RoomId.CarouselHall, 20, 11, 28, 23, 12f, "floor_tile", "wall_brick", "ceiling");
            ch.lightSpacing = 12f; ch.lightIntensity = 3f; ch.light = new Color(1f, 0.78f, 0.55f);
            var ar = m.AddRoom(RoomId.Arcade, 48, 24, 20, 14, 5f, "floor_carpet", "wall_plaster", "ceiling");
            ar.lightSpacing = 11f; ar.light = new Color(0.8f, 0.6f, 1f); ar.lightIntensity = 1.6f;
            var bc = m.AddRoom(RoomId.BumperCars, 46, 38, 18, 16, 7f, "floor_grate", "wall_metal", "ceiling");
            bc.lightSpacing = 11f; bc.light = new Color(1f, 0.7f, 0.5f);
            var bo = m.AddRoom(RoomId.BumperOffice, 64, 46, 6, 8, 3.2f, "floor_wood", "wall_plaster", "ceiling");
            bo.lightSpacing = 6f;
            var gt = m.AddRoom(RoomId.GhostTrain, 0, 11, 20, 25, 6f, "floor_concrete", "wall_brick", "ceiling");
            gt.lightSpacing = 10f; gt.light = new Color(0.55f, 1f, 0.6f); gt.lightIntensity = 1.4f;
            var wh = m.AddRoom(RoomId.Warehouse, 0, 36, 22, 20, 9f, "floor_concrete", "wall_metal", "ceiling");
            wh.lightSpacing = 12f; wh.light = new Color(0.9f, 0.85f, 0.7f); wh.lightIntensity = 1.8f;
            var cg = m.AddRoom(RoomId.ClockGallery, 22, 34, 12, 12, 6.5f, "floor_wood", "wall_plaster", "ceiling");
            cg.lightSpacing = 9f; cg.light = new Color(1f, 0.75f, 0.5f);
            var mt = m.AddRoom(RoomId.Maintenance, 34, 34, 12, 14, 5f, "floor_concrete", "wall_metal", "ceiling");
            mt.lightSpacing = 9f; mt.light = new Color(0.95f, 0.9f, 0.75f);
            var wo = m.AddRoom(RoomId.WarehouseOffice, 22, 48, 6, 8, 3.2f, "floor_wood", "wall_plaster", "ceiling");
            wo.lightSpacing = 6f;
            var sv = m.AddRoom(RoomId.ServiceHall, 28, 46, 6, 10, 3.4f, "floor_concrete", "wall_brick", "ceiling");
            sv.lightSpacing = 8f;
            var st = m.AddRoom(RoomId.StaffArea, 34, 48, 12, 8, 3.2f, "floor_tile", "wall_plaster", "ceiling");
            st.lightSpacing = 7f; st.light = new Color(0.85f, 0.95f, 1f);

            // wide openings (arches)
            m.Open(27, 10, 1, 8);  // entrance hall <-> carousel hall
            m.Open(19, 14, 0, 4);  // carousel hall <-> ghost train station
            m.Open(47, 12, 0, 3);  // carousel hall <-> food court
            m.Open(47, 26, 0, 4);  // carousel hall <-> arcade
            m.Open(55, 37, 1, 3);  // arcade <-> bumper cars
            m.Open(25, 33, 1, 3);  // carousel hall <-> clock gallery
            m.Open(39, 33, 1, 3);  // carousel hall <-> maintenance

            // doors (each is also an opening)
            foreach (var d in DoorDefs) m.Open(d.x, d.z, d.dir, 1);
        }

        struct DoorDef
        {
            public string key; public int x, z, dir; public string mat;
            public DoorDef(string k, int x, int z, int d, string m) { key = k; this.x = x; this.z = z; dir = d; mat = m; }
        }

        static readonly DoorDef[] DoorDefs =
        {
            new DoorDef("mirror",     21, 4, 0, "wood_painted"),
            new DoorDef("security",   39, 2, 0, "wall_metal"),
            new DoorDef("broadcast",  39, 8, 0, "wood"),
            new DoorDef("maze_ghost",  8, 10, 1, "wood"),
            new DoorDef("kitchen",    52, 15, 1, "wall_metal"),
            new DoorDef("cold",       59, 18, 0, "metal_rust"),
            new DoorDef("bumper_off", 63, 49, 0, "wood"),
            new DoorDef("staff_bc",   45, 51, 0, "wood"),
            new DoorDef("staff_mt",   40, 47, 1, "wall_metal"),
            new DoorDef("svc_staff",  33, 52, 0, "wood"),
            new DoorDef("svc_wo",     27, 51, 0, "wood"),
            new DoorDef("svc_cg",     30, 45, 1, "wood"),
            new DoorDef("wo_wh",      21, 50, 0, "wall_metal"),
            new DoorDef("wh_cg",      21, 40, 0, "metal_rust"),
            new DoorDef("mt_cg",      33, 40, 0, "wall_metal"),
            new DoorDef("mt_bc",      45, 42, 0, "wall_metal"),
        };

        // ================================================================ maze / tunnel
        public readonly List<Vector2Int> mazeDeadEnds = new List<Vector2Int>();

        void GenerateMaze()
        {
            var room = R(RoomId.MirrorMaze);
            int x0 = room.r.xMin, z0 = room.r.yMin, w = room.r.width, h = room.r.height;
            var visited = new bool[w, h];
            var passages = new HashSet<int>();
            var stack = new Stack<Vector2Int>();
            var start = new Vector2Int(w - 1, 4);
            visited[start.x, start.y] = true;
            stack.Push(start);
            int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
            var dirs = new List<int> { 0, 1, 2, 3 };
            while (stack.Count > 0)
            {
                var c = stack.Peek();
                RF.Shuffle(dirs);
                bool moved = false;
                foreach (int d in dirs)
                {
                    int nx = c.x + dx[d], nz = c.y + dz[d];
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h || visited[nx, nz]) continue;
                    visited[nx, nz] = true;
                    passages.Add(LocalEdge(c.x, c.y, nx, nz, w));
                    stack.Push(new Vector2Int(nx, nz));
                    moved = true;
                    break;
                }
                if (!moved) stack.Pop();
            }
            // block every internal edge that isn't a passage (with ~14% extra openings for loops)
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                {
                    if (x + 1 < w && !passages.Contains(LocalEdge(x, z, x + 1, z, w)) && Random.value > 0.14f)
                        map.BlockEdge(x0 + x, z0 + z, 0);
                    if (z + 1 < h && !passages.Contains(LocalEdge(x, z, x, z + 1, w)) && Random.value > 0.14f)
                        map.BlockEdge(x0 + x, z0 + z, 1);
                }
            // dead ends (for hidden items): cells with 3 blocked sides
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                {
                    int wx = x0 + x, wz = z0 + z;
                    int walls = 0;
                    if (map.WallBetween(wx, wz, wx + 1, wz)) walls++;
                    if (map.WallBetween(wx, wz, wx - 1, wz)) walls++;
                    if (map.WallBetween(wx, wz, wx, wz + 1)) walls++;
                    if (map.WallBetween(wx, wz, wx, wz - 1)) walls++;
                    if (walls >= 3 && Mathf.Abs(x - (w - 1)) + Mathf.Abs(z - 4) > 8) mazeDeadEnds.Add(new Vector2Int(wx, wz));
                }
            RF.Shuffle(mazeDeadEnds);
            if (mazeDeadEnds.Count == 0) mazeDeadEnds.Add(new Vector2Int(x0 + 1, z0 + 1));
        }

        static int LocalEdge(int ax, int az, int bx, int bz, int w)
        {
            int x = Mathf.Min(ax, bx), z = Mathf.Min(az, bz);
            int dir = ax != bx ? 0 : 1;
            return ((z * w) + x) * 2 + dir;
        }

        void GenerateTunnel()
        {
            // Ghost train: station z11..19, then a serpentine tunnel of four bands.
            for (int x = 0; x < 20; x++) if (x != 17) map.BlockEdge(x, 19, 1);
            for (int x = 3; x < 20; x++) map.BlockEdge(x, 23, 1);
            for (int x = 0; x < 17; x++) map.BlockEdge(x, 27, 1);
            for (int x = 3; x < 20; x++) map.BlockEdge(x, 31, 1);
        }

        // ================================================================ shells
        void BuildRoomShells()
        {
            foreach (var room in map.rooms)
            {
                BuildFloorCeiling(room);
                BuildRoomWalls(room);
            }
        }

        void BuildFloorCeiling(RoomDef room)
        {
            float c = FairMap.Cell;
            float x0 = room.r.xMin * c, x1 = room.r.xMax * c, z0 = room.r.yMin * c, z1 = room.r.yMax * c;
            float step = WorldMesh.ChunkSize;
            for (float cx = Mathf.Floor(x0 / step) * step; cx < x1; cx += step)
                for (float cz = Mathf.Floor(z0 / step) * step; cz < z1; cz += step)
                {
                    float ax = Mathf.Max(cx, x0), bx = Mathf.Min(cx + step, x1);
                    float az = Mathf.Max(cz, z0), bz = Mathf.Min(cz + step, z1);
                    if (bx - ax < 0.01f || bz - az < 0.01f) continue;
                    var center = new Vector3((ax + bx) * 0.5f, 0f, (az + bz) * 0.5f);
                    wm.Quad(room.floor, center, Vector3.up, Vector3.forward, bx - ax, bz - az, false);
                    if (room.hasCeiling)
                        wm.Quad(room.ceil, center + Vector3.up * room.height, Vector3.down, Vector3.forward, bx - ax, bz - az, false);
                }
            // floor collider (one per room)
            AddCollider(new Vector3((x0 + x1) * 0.5f, -0.25f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.5f, z1 - z0));
            AddCollider(new Vector3((x0 + x1) * 0.5f, room.height + 0.25f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.5f, z1 - z0));

            // trusses for tall halls
            if (room.height >= 6.5f)
            {
                for (float z = z0 + 6f; z < z1 - 2f; z += 8f)
                {
                    wm.Box("metal_rust", new Vector3((x0 + x1) * 0.5f, room.height - 0.5f, z), new Vector3(x1 - x0, 0.45f, 0.3f), 0, false);
                    wm.Box("metal_rust", new Vector3((x0 + x1) * 0.5f, room.height - 1.3f, z), new Vector3(x1 - x0, 0.12f, 0.12f), 0, false);
                    for (float x = x0 + 2f; x < x1; x += 4f)
                        wm.Tube("metal_rust", new Vector3(x, room.height - 1.3f, z), new Vector3(x + 2f, room.height - 0.5f, z), 0.05f, 5, false);
                }
            }
        }

        enum EdgeKind { Wall, Arch, Door }

        /// <summary>Cells on the south edge of the entrance hall that open to the outside (the main gate).</summary>
        public static readonly HashSet<int> GateCellsX = new HashSet<int> { 29, 30, 31, 32 };

        EdgeKind KindOf(int x, int z, int nx, int nz)
        {
            if (nz == -1 && z == 0 && GateCellsX.Contains(x) && map.roomAt[x, z] >= 0 && map.rooms[map.roomAt[x, z]].id == RoomId.EntranceHall)
                return EdgeKind.Arch;
            if (!map.WallBetween(x, z, nx, nz))
            {
                if (map.Floor(nx, nz) && map.roomAt[nx, nz] == map.roomAt[x, z]) return EdgeKind.Arch; // never happens on boundary
                int ex = Mathf.Min(x, nx), ez = Mathf.Min(z, nz), dir = nx != x ? 0 : 1;
                foreach (var d in DoorDefs) if (d.x == ex && d.z == ez && d.dir == dir) return EdgeKind.Door;
                return EdgeKind.Arch;
            }
            return EdgeKind.Wall;
        }

        float OpeningHeight(EdgeKind k, RoomDef a, RoomDef b)
        {
            if (k == EdgeKind.Door) return DoorH + 0.1f;
            float h = Mathf.Min(a.height, b != null ? b.height : a.height, ArchMaxH);
            return h;
        }

        void BuildRoomWalls(RoomDef room)
        {
            int idx = map.rooms.IndexOf(room);
            var r = room.r;
            float c = FairMap.Cell;
            // four sides: (start cell, step along, neighbour offset, inward normal, plane coordinate)
            BuildSide(room, idx, r.xMin, r.yMin, 1, 0, 0, -1, Vector3.forward, r.yMin * c, true);   // south side faces +z
            BuildSide(room, idx, r.xMin, r.yMax - 1, 1, 0, 0, 1, Vector3.back, r.yMax * c, true);  // north side faces -z
            BuildSide(room, idx, r.xMin, r.yMin, 0, 1, -1, 0, Vector3.right, r.xMin * c, false);   // west side faces +x
            BuildSide(room, idx, r.xMax - 1, r.yMin, 0, 1, 1, 0, Vector3.left, r.xMax * c, false); // east side faces -x
        }

        void BuildSide(RoomDef room, int idx, int sx, int sz, int stepX, int stepZ, int nX, int nZ, Vector3 inward, float plane, bool alongX)
        {
            int len = alongX ? room.r.width : room.r.height;
            float c = FairMap.Cell;
            int i = 0;
            while (i < len)
            {
                int x = sx + stepX * i, z = sz + stepZ * i;
                var kind = KindOf(x, z, x + nX, z + nZ);
                RoomDef other = map.Floor(x + nX, z + nZ) ? map.rooms[map.roomAt[x + nX, z + nZ]] : null;
                float openH = kind == EdgeKind.Wall ? 0f : OpeningHeight(kind, room, other);
                int j = i + 1;
                while (j < len)
                {
                    int x2 = sx + stepX * j, z2 = sz + stepZ * j;
                    var k2 = KindOf(x2, z2, x2 + nX, z2 + nZ);
                    if (k2 != kind) break;
                    if (kind == EdgeKind.Door) break; // doors are one cell
                    RoomDef o2 = map.Floor(x2 + nX, z2 + nZ) ? map.rooms[map.roomAt[x2 + nX, z2 + nZ]] : null;
                    if (o2 != other) break;
                    j++;
                }
                float a = (alongX ? sx : sz) * c + i * c;
                float b = (alongX ? sx : sz) * c + j * c;
                float mid = (a + b) * 0.5f;
                float width = b - a;
                Vector3 basePt = alongX ? new Vector3(mid, 0, plane) : new Vector3(plane, 0, mid);

                // v1.7: each room's wall face sits 2 cm inside its own room. On the cell boundary it was coplanar with
                // props flush against the same wall from the neighbouring room (kitchen counters / hood vs the Arcade)
                // and z-fought through as dark striped rectangles.
                Vector3 face = basePt + inward * WallInset;
                if (kind == EdgeKind.Wall)
                {
                    wm.Quad(room.wall, face + Vector3.up * room.height * 0.5f, inward, Vector3.up, width, room.height, true);
                    // baseboard
                    var bbSize = alongX ? new Vector3(width, 0.22f, 0.06f) : new Vector3(0.06f, 0.22f, width);
                    wm.Box("wood", face + Vector3.up * 0.11f + inward * 0.03f, bbSize, 0, false, 63 & ~8);
                    // dado rail on taller rooms
                    if (room.height > 4f)
                    {
                        var rail = alongX ? new Vector3(width, 0.08f, 0.05f) : new Vector3(0.05f, 0.08f, width);
                        wm.Box("metal_rust", face + Vector3.up * 1.2f + inward * 0.025f, rail, 0, false);
                    }
                }
                else
                {
                    if (room.height > openH + 0.01f)
                    {
                        float lh = room.height - openH;
                        wm.Quad(room.wall, face + Vector3.up * (openH + lh * 0.5f), inward, Vector3.up, width, lh, true);
                    }
                    // frame trims
                    Vector3 along = alongX ? Vector3.right : Vector3.forward;
                    var postSize = alongX ? new Vector3(0.18f, openH, 0.3f) : new Vector3(0.3f, openH, 0.18f);
                    string trim = kind == EdgeKind.Door ? "wood" : "metal_rust";
                    wm.Box(trim, basePt - along * (width * 0.5f - 0.09f) + Vector3.up * openH * 0.5f, postSize);
                    wm.Box(trim, basePt + along * (width * 0.5f - 0.09f) + Vector3.up * openH * 0.5f, postSize);
                    var top = alongX ? new Vector3(width, 0.2f, 0.32f) : new Vector3(0.32f, 0.2f, width);
                    wm.Box(trim, basePt + Vector3.up * (openH - 0.1f), top);
                }
                i = j;
            }
        }

        void BuildInternalWalls()
        {
            float c = FairMap.Cell;
            // dir 1 edges (walls running along x)
            for (int z = 0; z < FairMap.H; z++)
            {
                int x = 0;
                while (x < FairMap.W)
                {
                    if (!(map.IsBlockedEdge(x, z, 1) && map.Floor(x, z) && map.Floor(x, z + 1))) { x++; continue; }
                    int start = x;
                    var room = map.rooms[map.roomAt[x, z]];
                    while (x < FairMap.W && map.IsBlockedEdge(x, z, 1) && map.Floor(x, z) && map.Floor(x, z + 1) && map.rooms[map.roomAt[x, z]] == room) x++;
                    float a = start * c, b = x * c;
                    float hgt = room.id == RoomId.MirrorMaze ? 3.2f : room.height;
                    InternalWall(room, new Vector3((a + b) * 0.5f, 0, (z + 1) * c), new Vector3(b - a + 0.15f, hgt, 0.18f));
                }
            }
            // dir 0 edges (walls running along z)
            for (int x = 0; x < FairMap.W; x++)
            {
                int z = 0;
                while (z < FairMap.H)
                {
                    if (!(map.IsBlockedEdge(x, z, 0) && map.Floor(x, z) && map.Floor(x + 1, z))) { z++; continue; }
                    int start = z;
                    var room = map.rooms[map.roomAt[x, z]];
                    while (z < FairMap.H && map.IsBlockedEdge(x, z, 0) && map.Floor(x, z) && map.Floor(x + 1, z) && map.rooms[map.roomAt[x, z]] == room) z++;
                    float a = start * c, b = z * c;
                    float hgt = room.id == RoomId.MirrorMaze ? 3.2f : room.height;
                    InternalWall(room, new Vector3((x + 1) * c, 0, (a + b) * 0.5f), new Vector3(0.18f, hgt, b - a + 0.15f));
                }
            }
        }

        void InternalWall(RoomDef room, Vector3 basePos, Vector3 size)
        {
            string mat = room.id == RoomId.MirrorMaze ? "mirror" : "wall_brick";
            wm.Box(mat, basePos + Vector3.up * size.y * 0.5f, size, 0, true, 63 & ~8);
            if (room.id == RoomId.MirrorMaze)
            {
                // gilded frame strip on top
                wm.Box("gold_trim", basePos + Vector3.up * (size.y + 0.04f), new Vector3(size.x + 0.04f, 0.08f, size.z + 0.04f), 0, false);
            }
            AddCollider(basePos + Vector3.up * size.y * 0.5f, size);
        }

        void BuildWallColliders()
        {
            float c = FairMap.Cell;
            // vertical lines between x and x+1 (dir 0)
            for (int x = -1; x < FairMap.W; x++)
            {
                int z = 0;
                while (z < FairMap.H)
                {
                    if (!BoundaryWall(x, z, 0, out float hgt)) { z++; continue; }
                    int start = z;
                    float maxH = hgt;
                    while (z < FairMap.H && BoundaryWall(x, z, 0, out float h2)) { maxH = Mathf.Max(maxH, h2); z++; }
                    float a = start * c, b = z * c;
                    AddCollider(new Vector3((x + 1) * c, maxH * 0.5f, (a + b) * 0.5f), new Vector3(0.3f, maxH, b - a));
                }
            }
            // horizontal lines between z and z+1 (dir 1)
            for (int z = -1; z < FairMap.H; z++)
            {
                int x = 0;
                while (x < FairMap.W)
                {
                    if (!BoundaryWall(x, z, 1, out float hgt)) { x++; continue; }
                    int start = x;
                    float maxH = hgt;
                    while (x < FairMap.W && BoundaryWall(x, z, 1, out float h2)) { maxH = Mathf.Max(maxH, h2); x++; }
                    float a = start * c, b = x * c;
                    AddCollider(new Vector3((a + b) * 0.5f, maxH * 0.5f, (z + 1) * c), new Vector3(b - a, maxH, 0.3f));
                }
            }
            // lintels above openings (block robots' vision through upper walls, and stop climbing)
        }

        bool BoundaryWall(int x, int z, int dir, out float height)
        {
            height = 0f;
            int nx = dir == 0 ? x + 1 : x, nz = dir == 0 ? z : z + 1;
            bool fa = map.Floor(x, z), fb = map.Floor(nx, nz);
            if (!fa && !fb) return false;
            float ha = fa ? map.rooms[map.roomAt[x, z]].height : 0f;
            float hb = fb ? map.rooms[map.roomAt[nx, nz]].height : 0f;
            height = Mathf.Max(ha, hb);
            if (dir == 1 && z == -1 && GateCellsX.Contains(x)) return false;          // main gate opening (gate has its own collider)
            if (fa && fb && map.roomAt[x, z] == map.roomAt[nx, nz]) return false; // internal (handled separately)
            if (fa && fb && map.IsOpenEdge(Mathf.Min(x, nx), Mathf.Min(z, nz), dir)) return false;
            return true;
        }

        public void AddCollider(Vector3 center, Vector3 size, int layer = RF.LayerDefault)
        {
            var go = new GameObject("col");
            go.transform.SetParent(colRoot, false);
            go.transform.position = center;
            go.layer = layer;
            go.isStatic = true;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
        }

        public void AddColliderRot(Vector3 center, Vector3 size, float yaw, int layer = RF.LayerProps)
        {
            var go = new GameObject("col");
            go.transform.SetParent(colRoot, false);
            go.transform.position = center;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.layer = layer;
            go.isStatic = true;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
        }

        // ================================================================ doors
        void BuildDoors()
        {
            float c = FairMap.Cell;
            foreach (var d in DoorDefs)
            {
                Vector3 pos, fwd;
                if (d.dir == 0) { pos = new Vector3((d.x + 1) * c, 0, d.z * c + c * 0.5f); fwd = Vector3.right; }
                else { pos = new Vector3(d.x * c + c * 0.5f, 0, (d.z + 1) * c); fwd = Vector3.forward; }
                var door = Door.Create(dynRoot, pos, fwd, 1.62f, DoorH, d.mat, "Door_" + d.key);
                map.RegisterDoor(d.x, d.z, d.dir, door);
                doors[d.key] = door;
                // exit sign above most doors
                if (Random.value < 0.55f)
                {
                    wm.QuadUV("sign_exit", pos + Vector3.up * (DoorH + 0.42f) + fwd * 0.17f, fwd, Vector3.up, 0.6f, 0.3f);
                    wm.QuadUV("sign_exit", pos + Vector3.up * (DoorH + 0.42f) - fwd * 0.17f, -fwd, Vector3.up, 0.6f, 0.3f);
                }
            }
            // ghost train tunnel gate (inside the ghost train room)
            {
                var pos = new Vector3(17 * c + c * 0.5f, 0, 20 * c);
                var gate = Door.Create(dynRoot, pos, Vector3.forward, 1.62f, DoorH, "metal_rust", "Door_tunnel_gate");
                map.RegisterDoor(17, 19, 1, gate);
                doors["tunnel_gate"] = gate;
            }
        }

        // ================================================================ lights
        void BuildRoomLights()
        {
            foreach (var room in map.rooms)
            {
                float c = FairMap.Cell;
                float x0 = room.r.xMin * c, x1 = room.r.xMax * c, z0 = room.r.yMin * c, z1 = room.r.yMax * c;
                if (!room.dark && room.id != RoomId.GhostTrain)
                {
                    int nx = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / room.lightSpacing));
                    int nz = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / room.lightSpacing));
                    float hangY = Mathf.Min(room.height - 0.35f, 4.2f);
                    for (int i = 0; i < nx; i++)
                        for (int j = 0; j < nz; j++)
                        {
                            float x = Mathf.Lerp(x0, x1, (i + 0.5f) / nx);
                            float z = Mathf.Lerp(z0, z1, (j + 0.5f) / nz);
                            var p = new Vector3(x, hangY, z);
                            if (room.id == RoomId.CarouselHall && (p - room.Center).Flat().magnitude < 10f) continue; // carousel canopy
                            bool broken = Random.value < 0.12f;
                            bool flicker = !broken && Random.value < 0.28f;
                            HangingLamp(room, p, broken, flicker);
                        }
                }
                // emergency lights near every opening/door edge of this room
                EmergencyLights(room);
            }
            // ghost train station: sickly green strip lights
            var gt = R(RoomId.GhostTrain);
            for (int i = 0; i < 3; i++)
                lights.Add(gt.P(8f + i * 11f, 8f, 4.6f), staticRoot, new Color(0.45f, 1f, 0.5f), 1.5f, 11f, LightGroup.Main, i == 1);
            // tunnel: rare red "scare" lamps that are always on (dim)
            lights.Add(gt.P(3f, 21f, 3.5f), staticRoot, new Color(1f, 0.1f, 0.05f), 0.8f, 6f, LightGroup.Always, true);
            lights.Add(gt.P(37f, 29f, 3.5f), staticRoot, new Color(1f, 0.1f, 0.05f), 0.8f, 6f, LightGroup.Always, true);
            lights.Add(gt.P(3f, 37f, 3.5f), staticRoot, new Color(0.3f, 1f, 0.3f), 0.6f, 6f, LightGroup.Always, true);
            lights.Add(gt.P(36f, 46f, 3f), staticRoot, new Color(1f, 0.15f, 0.08f), 1.1f, 7f, LightGroup.Always, true);
            // mirror maze: a few faint purple lights
            var mm = R(RoomId.MirrorMaze);
            for (int i = 0; i < 4; i++)
                lights.Add(mm.P(5f + i * 9f, 5f + (i % 2) * 12f, 3.6f), staticRoot, new Color(0.6f, 0.3f, 1f), 0.9f, 7f, LightGroup.Main, true);
        }

        void HangingLamp(RoomDef room, Vector3 p, bool broken, bool flicker)
        {
            // cable + shade (static) and an emissive bulb (dynamic renderer for on/off)
            wm.Tube("black_matte", p + Vector3.up * 0.25f, new Vector3(p.x, room.height, p.z), 0.012f, 4, false);
            wm.Cyl("metal_rust", p + Vector3.up * 0.08f, 0.32f, 0.22f, 10, false, false, 0.08f);
            // v1.4: the bulb is static (batched); its shared material follows the power (LightSystem)
            wm.Ell(broken ? "bulb_dead" : "bulb_main", p, Vector3.one * 0.16f, Quaternion.identity, false, true);
            if (broken) return;
            float range = Mathf.Clamp(room.lightSpacing * 1.25f, 7f, 15f);
            if (room.height > 6f) range += 3f;
            lights.Add(p - Vector3.up * 0.1f, staticRoot, room.light, room.lightIntensity * 0.8f, range, LightGroup.Main, flicker, null, true); // v1.5: x0.8
        }

        void EmergencyLights(RoomDef room)
        {
            // one red caged light per room, near the middle of a wall, plus one for big rooms
            int count = room.r.width * room.r.height > 150 ? 2 : 1;
            float c = FairMap.Cell;
            for (int i = 0; i < count; i++)
            {
                // the wall centre may be an arch / door / the main gate: slide along the wall to a solid cell
                int zIn = i == 0 ? room.r.yMin : room.r.yMax - 1;
                int zOut = i == 0 ? zIn - 1 : zIn + 1;
                int cx0 = Mathf.Clamp(Mathf.FloorToInt(room.Center.x / c), room.r.xMin, room.r.xMax - 1);
                int cx = cx0;
                for (int k = 0; k < room.r.width * 2; k++)
                {
                    int cand = cx0 + ((k & 1) == 0 ? k / 2 : -(k / 2 + 1));
                    if (cand < room.r.xMin || cand >= room.r.xMax) continue;
                    if (KindOf(cand, zIn, cand, zOut) == EdgeKind.Wall) { cx = cand; break; }
                }
                float px = cx * c + c * 0.5f;
                Vector3 p;
                Vector3 n;
                if (i == 0) { p = new Vector3(px, Mathf.Min(room.height - 0.4f, 3.2f), room.r.yMin * c + 0.15f); n = Vector3.forward; }
                else { p = new Vector3(px, Mathf.Min(room.height - 0.4f, 3.2f), room.r.yMax * c - 0.15f); n = Vector3.back; }
                wm.Box("metal_rust", p - n * 0.05f, new Vector3(0.3f, 0.2f, 0.12f), 0, false);
                wm.Box("emergency_red", p + n * 0.06f, new Vector3(0.22f, 0.12f, 0.08f), 0, false); // static (batched)
                lights.Add(p + n * 0.4f, staticRoot, new Color(1f, 0.08f, 0.04f), 1.6f, 9f, LightGroup.Emergency, Random.value < 0.4f);
            }
        }
    }
}
