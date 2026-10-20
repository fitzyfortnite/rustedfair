using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    public enum RoomId
    {
        EntranceHall, Security, Broadcast, FoodCourt, Kitchen, ColdRoom, MirrorMaze, CarouselHall,
        Arcade, BumperCars, BumperOffice, GhostTrain, Warehouse, WarehouseOffice, ClockGallery,
        Maintenance, ServiceHall, StaffArea
    }

    public class RoomDef
    {
        public RoomId id;
        public RectInt r;              // in cells
        public float height;
        public string floor, wall, ceil;
        public Color light = new Color(1f, 0.82f, 0.6f);
        public float lightSpacing = 10f;
        public float lightIntensity = 2.2f;
        public bool dark;              // no main lights at all (maze, tunnel)
        public bool hasCeiling = true;

        public Vector3 Min => new Vector3(r.xMin * FairMap.Cell, 0, r.yMin * FairMap.Cell);
        public Vector3 Max => new Vector3(r.xMax * FairMap.Cell, height, r.yMax * FairMap.Cell);
        public Vector3 Center => new Vector3((r.xMin + r.xMax) * 0.5f * FairMap.Cell, 0, (r.yMin + r.yMax) * 0.5f * FairMap.Cell);
        public Vector2 SizeM => new Vector2(r.width * FairMap.Cell, r.height * FairMap.Cell);

        /// <summary>World position from coordinates relative to the room's min corner (metres).</summary>
        public Vector3 P(float x, float z, float y = 0f) => new Vector3(r.xMin * FairMap.Cell + x, y, r.yMin * FairMap.Cell + z);
    }

    /// <summary>
    /// Grid model of the fairground (2 m cells). Used to build walls, and by the robots' A* pathfinding.
    /// </summary>
    public class FairMap
    {
        public const int W = 72, H = 56;
        public const float Cell = 2f;

        public readonly List<RoomDef> rooms = new List<RoomDef>();
        public readonly int[,] roomAt = new int[W, H];
        public readonly bool[,] solid = new bool[W, H];
        readonly HashSet<int> openEdges = new HashSet<int>();
        readonly HashSet<int> blockedEdges = new HashSet<int>();
        readonly Dictionary<int, Door> doors = new Dictionary<int, Door>();

        public FairMap()
        {
            for (int x = 0; x < W; x++)
                for (int z = 0; z < H; z++)
                    roomAt[x, z] = -1;
        }

        // ------------------------------------------------------------------ building API
        public RoomDef AddRoom(RoomId id, int x, int z, int w, int h, float height, string floor, string wall, string ceil)
        {
            var def = new RoomDef { id = id, r = new RectInt(x, z, w, h), height = height, floor = floor, wall = wall, ceil = ceil };
            int idx = rooms.Count;
            rooms.Add(def);
            for (int i = x; i < x + w; i++)
                for (int j = z; j < z + h; j++)
                    roomAt[i, j] = idx;
            return def;
        }

        public RoomDef Room(RoomId id) => rooms.Find(r => r.id == id);

        public static int EdgeId(int x, int z, int dir) => ((z * W) + x) * 2 + dir;

        /// <summary>Opening: dir 0 = between (x,z) and (x+1,z) repeated along +z; dir 1 = between (x,z) and (x,z+1) repeated along +x.</summary>
        public void Open(int x, int z, int dir, int count = 1)
        {
            for (int i = 0; i < count; i++)
                openEdges.Add(dir == 0 ? EdgeId(x, z + i, 0) : EdgeId(x + i, z, 1));
        }

        public void BlockEdge(int x, int z, int dir) => blockedEdges.Add(EdgeId(x, z, dir));
        public void UnblockEdge(int x, int z, int dir) => blockedEdges.Remove(EdgeId(x, z, dir));
        public bool IsBlockedEdge(int x, int z, int dir) => blockedEdges.Contains(EdgeId(x, z, dir));
        public bool IsOpenEdge(int x, int z, int dir) => openEdges.Contains(EdgeId(x, z, dir));

        public void RegisterDoor(int x, int z, int dir, Door d) => doors[EdgeId(x, z, dir)] = d;

        public void Block(int x0, int z0, int w, int h)
        {
            for (int x = x0; x < x0 + w; x++)
                for (int z = z0; z < z0 + h; z++)
                    if (In(x, z)) solid[x, z] = true;
        }

        /// <summary>Blocks every cell whose centre lies inside the world-space rectangle.</summary>
        public void BlockWorld(Vector3 center, Vector3 size, float yaw = 0f)
        {
            // conservative AABB of the rotated rectangle, shrunk slightly so walkways stay open
            float a = yaw * Mathf.Deg2Rad;
            float ex = Mathf.Abs(Mathf.Cos(a)) * size.x * 0.5f + Mathf.Abs(Mathf.Sin(a)) * size.z * 0.5f;
            float ez = Mathf.Abs(Mathf.Sin(a)) * size.x * 0.5f + Mathf.Abs(Mathf.Cos(a)) * size.z * 0.5f;
            int x0 = Mathf.FloorToInt((center.x - ex) / Cell), x1 = Mathf.FloorToInt((center.x + ex) / Cell);
            int z0 = Mathf.FloorToInt((center.z - ez) / Cell), z1 = Mathf.FloorToInt((center.z + ez) / Cell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!In(x, z)) continue;
                    Vector3 c = CellCenter(x, z);
                    if (Mathf.Abs(c.x - center.x) <= ex + 0.2f && Mathf.Abs(c.z - center.z) <= ez + 0.2f) solid[x, z] = true;
                }
        }

        // ------------------------------------------------------------------ queries
        public static bool In(int x, int z) => x >= 0 && z >= 0 && x < W && z < H;
        public bool Floor(int x, int z) => In(x, z) && roomAt[x, z] >= 0;
        public bool Walkable(int x, int z) => Floor(x, z) && !solid[x, z];

        public static Vector3 CellCenter(int x, int z) => new Vector3(x * Cell + Cell * 0.5f, 0f, z * Cell + Cell * 0.5f);
        public static Vector2Int CellOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));

        public RoomDef RoomAtPos(Vector3 p)
        {
            var c = CellOf(p);
            if (!In(c.x, c.y)) return null;
            int i = roomAt[c.x, c.y];
            return i >= 0 ? rooms[i] : null;
        }

        /// <summary>Is there a wall on the edge between two orthogonally adjacent cells? (ignores doors' state)</summary>
        public bool WallBetween(int x, int z, int nx, int nz)
        {
            int ex, ez, dir;
            EdgeOf(x, z, nx, nz, out ex, out ez, out dir);
            if (!Floor(x, z) || !Floor(nx, nz)) return true;
            if (blockedEdges.Contains(EdgeId(ex, ez, dir))) return true;
            if (roomAt[x, z] == roomAt[nx, nz]) return false;
            return !openEdges.Contains(EdgeId(ex, ez, dir));
        }

        static void EdgeOf(int x, int z, int nx, int nz, out int ex, out int ez, out int dir)
        {
            if (nx != x) { dir = 0; ex = Mathf.Min(x, nx); ez = z; }
            else { dir = 1; ex = x; ez = Mathf.Min(z, nz); }
        }

        /// <summary>Can a robot step between two orthogonally adjacent cells?</summary>
        public bool CanStep(int x, int z, int nx, int nz)
        {
            if (!Walkable(nx, nz)) return false;
            if (WallBetween(x, z, nx, nz)) return false;
            EdgeOf(x, z, nx, nz, out int ex, out int ez, out int dir);
            if (doors.TryGetValue(EdgeId(ex, ez, dir), out var d) && d != null && d.BlocksRobots) return false;
            return true;
        }

        public Door DoorBetween(int x, int z, int nx, int nz)
        {
            EdgeOf(x, z, nx, nz, out int ex, out int ez, out int dir);
            return doors.TryGetValue(EdgeId(ex, ez, dir), out var d) ? d : null;
        }

        // concrete collection type so foreach uses the struct enumerator (no per-frame boxing in RobotAI)
        public Dictionary<int, Door>.ValueCollection AllDoors => doors.Values;

        public Vector2Int NearestWalkable(Vector2Int c, int maxRadius = 6)
        {
            if (Walkable(c.x, c.y)) return c;
            for (int r = 1; r <= maxRadius; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
                        int x = c.x + dx, z = c.y + dz;
                        if (Walkable(x, z)) return new Vector2Int(x, z);
                    }
            return c;
        }

        public Vector3 RandomPointInRoom(RoomDef room)
        {
            for (int i = 0; i < 30; i++)
            {
                int x = Random.Range(room.r.xMin, room.r.xMax);
                int z = Random.Range(room.r.yMin, room.r.yMax);
                if (Walkable(x, z)) return CellCenter(x, z);
            }
            return room.Center;
        }

        // ------------------------------------------------------------------ A*
        int[] cameFrom;
        float[] g;
        int[] stamp, closedStamp;
        int curStamp;
        // heap entries carry their own priority: a node's f can drop after it was pushed, and keying the
        // heap by the shared f[] array would silently break the heap invariant (sub-optimal paths)
        struct HeapItem { public int i; public float f; }
        readonly List<HeapItem> heap = new List<HeapItem>();
        readonly List<Vector2Int> cellsTmp = new List<Vector2Int>();

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> result, int maxExpand = 6000)
        {
            result.Clear();
            int n = W * H;
            if (cameFrom == null)
            {
                cameFrom = new int[n]; g = new float[n]; stamp = new int[n]; closedStamp = new int[n];
            }
            curStamp++;
            var s = NearestWalkable(CellOf(from), 3);
            var e = NearestWalkable(CellOf(to), 6);
            if (!Walkable(s.x, s.y) || !Walkable(e.x, e.y)) return false;
            int start = s.y * W + s.x, goal = e.y * W + e.x;
            heap.Clear();
            g[start] = 0; cameFrom[start] = -1; stamp[start] = curStamp;
            Push(start, H_(s.x, s.y, e.x, e.y));
            int expanded = 0;
            bool found = false;
            while (heap.Count > 0)
            {
                int cur = Pop();
                if (cur == goal) { found = true; break; }
                if (closedStamp[cur] == curStamp) continue;
                closedStamp[cur] = curStamp;
                if (++expanded > maxExpand) break;
                int cx = cur % W, cz = cur / W;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], nz = cz + DZ[k];
                    if (!In(nx, nz)) continue;
                    float cost;
                    if (k < 4)
                    {
                        if (!CanStep(cx, cz, nx, nz)) continue;
                        cost = 1f;
                    }
                    else
                    {
                        // diagonal only when both orthogonal routes are clear (no corner cutting)
                        if (!CanStep(cx, cz, nx, cz) || !CanStep(nx, cz, nx, nz)) continue;
                        if (!CanStep(cx, cz, cx, nz) || !CanStep(cx, nz, nx, nz)) continue;
                        cost = 1.4142f;
                    }
                    int ni = nz * W + nx;
                    if (closedStamp[ni] == curStamp) continue;
                    float ng = g[cur] + cost;
                    if (stamp[ni] != curStamp || ng < g[ni])
                    {
                        stamp[ni] = curStamp;
                        g[ni] = ng;
                        cameFrom[ni] = cur;
                        Push(ni, ng + H_(nx, nz, e.x, e.y));
                    }
                }
            }
            if (!found) return false;

            var cells = cellsTmp;
            cells.Clear();
            for (int c = goal; c != -1; c = cameFrom[c]) cells.Add(new Vector2Int(c % W, c / W));
            cells.Reverse();

            // string pulling for natural movement
            int anchor = 0;
            result.Add(CellCenter(cells[0].x, cells[0].y));
            for (int i = 2; i < cells.Count; i++)
            {
                if (!LineClear(cells[anchor], cells[i]))
                {
                    anchor = i - 1;
                    result.Add(CellCenter(cells[anchor].x, cells[anchor].y));
                }
            }
            Vector3 end = to;
            var endCell = CellOf(to);
            if (endCell != e) end = CellCenter(e.x, e.y);
            result.Add(new Vector3(end.x, 0, end.z));
            return true;
        }

        static float H_(int x, int z, int gx, int gz)
        {
            int dx = Mathf.Abs(x - gx), dz = Mathf.Abs(z - gz);
            return (dx + dz) + (1.4142f - 2f) * Mathf.Min(dx, dz);
        }

        void Push(int i, float pf)
        {
            heap.Add(new HeapItem { i = i, f = pf });
            int c = heap.Count - 1;
            while (c > 0)
            {
                int p = (c - 1) / 2;
                if (heap[p].f <= heap[c].f) break;
                (heap[p], heap[c]) = (heap[c], heap[p]);
                c = p;
            }
        }

        int Pop()
        {
            int top = heap[0].i;
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int c = 0;
            while (true)
            {
                int l = c * 2 + 1, r = l + 1, m = c;
                if (l < heap.Count && heap[l].f < heap[m].f) m = l;
                if (r < heap.Count && heap[r].f < heap[m].f) m = r;
                if (m == c) break;
                (heap[m], heap[c]) = (heap[c], heap[m]);
                c = m;
            }
            return top;
        }

        /// <summary>Grid walk between two cells (supercover) checking every crossing.</summary>
        public bool LineClear(Vector2Int a, Vector2Int b)
        {
            int x = a.x, z = a.y;
            int dx = Mathf.Abs(b.x - a.x), dz = Mathf.Abs(b.y - a.y);
            int sx = b.x > a.x ? 1 : -1, sz = b.y > a.y ? 1 : -1;
            int err = dx - dz;
            int guard = 0;
            while ((x != b.x || z != b.y) && guard++ < 200)
            {
                int e2 = 2 * err;
                if (e2 > -dz && e2 < dx)
                {
                    // diagonal step: require both orthogonal paths
                    if (!CanStep(x, z, x + sx, z) || !CanStep(x + sx, z, x + sx, z + sz)) return false;
                    if (!CanStep(x, z, x, z + sz) || !CanStep(x, z + sz, x + sx, z + sz)) return false;
                    err -= dz; err += dx;
                    x += sx; z += sz;
                }
                else if (e2 > -dz)
                {
                    if (!CanStep(x, z, x + sx, z)) return false;
                    err -= dz; x += sx;
                }
                else
                {
                    if (!CanStep(x, z, x, z + sz)) return false;
                    err += dx; z += sz;
                }
            }
            return true;
        }

        public bool LineClear(Vector3 a, Vector3 b) => LineClear(CellOf(a), CellOf(b));
    }
}
