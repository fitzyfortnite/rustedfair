using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RustedFair
{
    /// <summary>
    /// v1.2 extra dressing: wall clutter (props with their back to a real wall), free-standing props on open floor
    /// (only cells whose 8 neighbours are open floor of the same room, each blocked for A*), carousel-hall centrepieces,
    /// entrance lamp posts, wall blood smears and dim sconces. Nothing is placed on a collider, near a door, a quest/item
    /// spot, an anchor, a hiding spot or any interactable. Also hosts the reachability self-check.
    /// Front of every prop = local +z (yaw in degrees, like the rest of the builder).
    /// </summary>
    public partial class FairBuilder
    {
        struct Circle
        {
            public float x, z, r;
            public Circle(float x, float z, float r) { this.x = x; this.z = z; this.r = r; }
        }

        struct WallSlot { public Vector3 c, n, t, wall, edge; }

        delegate void PropFn(Vector3 p, float yaw);

        struct KitEntry
        {
            public float w, d; public PropFn fn;
            public KitEntry(float w, float d, PropFn fn) { this.w = w; this.d = d; this.fn = fn; }
        }

        readonly List<Circle> keepZones = new List<Circle>();
        readonly List<Circle> claimed = new List<Circle>();
        Dictionary<string, KitEntry> wallKit, floorKit;

        /// <summary>"placed/wanted" per room (shown by the reachability self-check).</summary>
        public readonly Dictionary<string, string> dressLog = new Dictionary<string, string>();

        // ================================================================== entry
        void ExtraDressing()
        {
            Physics.SyncTransforms(); // hide spots, doors etc. were moved after their colliders were created
            BuildKits();
            BuildKeepZones();
            dressLog.Clear();
            ExtraHides();

            WallDress(RoomId.EntranceHall, new[] { "bin", "plant", "boxPile", "chairStack", "bin", "plant", "junk", "filing", "costumeRack", "claw", "prizeShelf" }, 16);
            WallDress(RoomId.Security, new[] { "filing", "cooler", "boxPile", "filing", "toolCab" }, 6);
            WallDress(RoomId.FoodCourt, new[] { "bin", "chairStack", "bin", "mop", "boxPile", "cooler", "chairStack", "junk", "bin", "plant", "fridge", "claw", "stove" }, 18);
            WallDress(RoomId.Kitchen, new[] { "stove", "fridge", "stove", "boxPile", "mop", "bin", "gasCyl", "barrels", "fridge" }, 10);
            WallDress(RoomId.CarouselHall, new[] { "prizeShelf", "costumeRack", "junk", "claw", "boxPile", "barrels", "bin", "prizeShelf", "claw", "junk", "costumeRack", "chairStack", "bin", "plant", "prizeShelf", "claw", "pallets", "tires", "junk" }, 30);
            WallDress(RoomId.Arcade, new[] { "claw", "junk", "bin", "claw", "boxPile" }, 7);
            WallDress(RoomId.BumperCars, new[] { "tires", "barrels", "bin", "boxPile", "tires", "junk", "claw", "tires", "bin", "pallets", "gasCyl", "ladder" }, 17);
            WallDress(RoomId.BumperOffice, new[] { "tires", "filing", "boxPile", "costumeRack", "cooler" }, 6);
            WallDress(RoomId.GhostTrain, new[] { "tomb", "coffinUp", "tomb", "junk", "tomb", "tomb", "coffinUp", "tomb", "junk", "tomb", "tomb", "coffinUp", "tomb", "tomb", "costumeRack", "junk" }, 24);
            WallDress(RoomId.Warehouse, new[] { "pallets", "boxPile", "junk", "pallets", "barrels", "ladder", "boxPile", "pallets", "gasCyl", "junk", "tires", "toolCab", "costumeRack" }, 20);
            WallDress(RoomId.ClockGallery, new[] { "grandClock", "plant", "grandClock", "grandClock", "chairStack", "grandClock", "boxPile", "prizeShelf" }, 11);
            WallDress(RoomId.Maintenance, new[] { "toolCab", "gasCyl", "boxPile", "ladder", "toolCab", "junk", "barrels", "pallets", "tires", "filing" }, 15);
            WallDress(RoomId.WarehouseOffice, new[] { "filing", "boxPile", "plant", "filing", "cooler", "boxPile" }, 7);
            WallDress(RoomId.ServiceHall, new[] { "boxPile", "mop", "ladder", "junk", "barrels", "tires" }, 7);
            WallDress(RoomId.StaffArea, new[] { "sofa", "costumeRack", "bin", "chairStack", "cooler", "costumeRack", "filing", "boxPile" }, 10);
            WallDress(RoomId.Broadcast, new[] { "boxPile", "filing", "cooler" }, 3);
            WallDress(RoomId.ColdRoom, new[] { "barrels", "gasCyl", "boxPile" }, 3); // small 6x6 room: 5 props boxed a cell in
            foreach (var rid in DetailRooms) WallDetail(rid);

            WallDecor(RoomId.EntranceHall, 5, 2, 0xffa64d);
            WallDecor(RoomId.FoodCourt, 5, 2, 0xffb366);
            WallDecor(RoomId.Kitchen, 3, 0, 0);
            WallDecor(RoomId.CarouselHall, 8, 6, 0xffa64d);
            WallDecor(RoomId.GhostTrain, 8, 4, 0xff2a1a);
            WallDecor(RoomId.Warehouse, 4, 0, 0);
            WallDecor(RoomId.ClockGallery, 4, 3, 0xffcc80);
            WallDecor(RoomId.Maintenance, 3, 1, 0xfff2cc);
            WallDecor(RoomId.StaffArea, 3, 1, 0xffd9a6);
            WallDecor(RoomId.ServiceHall, 3, 0, 0);
            WallDecor(RoomId.BumperCars, 4, 0, 0);
            WallDecor(RoomId.Arcade, 3, 0, 0);
            WallDecor(RoomId.Security, 2, 0, 0);
            WallDecor(RoomId.WarehouseOffice, 2, 0, 0);
            WallDecor(RoomId.BumperOffice, 2, 0, 0);

            // the hall's centrepieces go first so the generic floor clutter can't take their spots
            CarouselHallFeatures();
            EntranceHallExtras();

            FloorDress(RoomId.CarouselHall, new[] { "balloons", "toppled", "claw", "picnic", "junk", "crates", "stanchions", "picnic", "balloons", "barrels", "toppled", "claw", "picnic", "junk", "crates", "balloons", "toppled", "junk", "barrels", "claw", "spool", "pallets" }, 44);
            FloorDress(RoomId.Maintenance, new[] { "spool", "pallets", "crates", "toolCab", "barrels", "gasCyl", "junk", "spool", "crates", "pallets", "toolCab", "barrels" }, 17);
            FloorDress(RoomId.Warehouse, new[] { "pallets", "spool", "junk", "barrels", "pallets", "crates", "junk", "spool", "crates", "barrels" }, 32);
            FloorDress(RoomId.StaffArea, new[] { "picnic", "chairStack", "bin", "picnic", "toppled" }, 7);
            FloorDress(RoomId.GhostTrain, new[] { "tomb", "coffin", "tomb", "junk", "tomb", "stanchions", "tomb", "coffin", "junk", "tomb", "crates", "toppled" }, 40);
            FloorDress(RoomId.EntranceHall, new[] { "stanchions", "bin", "stanchions", "toppled", "balloons", "junk", "picnic" }, 14);
            FloorDress(RoomId.ClockGallery, new[] { "stanchions", "chairStack", "stanchions", "junk", "toppled" }, 7);
            var bc = R(RoomId.BumperCars);
            FloorDress(RoomId.BumperCars, new[] { "bin", "barrels", "junk", "toppled", "crates", "spool", "pallets" }, 16,
                c => !(c.x > bc.Min.x + 5f && c.x < bc.Min.x + 31f && c.z > bc.Min.z + 6f && c.z < bc.Min.z + 26f)); // not in the rink
            FloorDress(RoomId.ServiceHall, new[] { "crates", "pallets" }, 3);
            FloorDress(RoomId.FoodCourt, new[] { "toppled", "bin", "balloons", "junk", "counter", "stanchions" }, 16);
            FloorDress(RoomId.Kitchen, new[] { "counter", "counter", "barrels", "crates" }, 5);
            CeilingJunk(RoomId.CarouselHall, 22); CeilingJunk(RoomId.Warehouse, 14); CeilingJunk(RoomId.GhostTrain, 16); CeilingJunk(RoomId.BumperCars, 10);
            CeilingJunk(RoomId.FoodCourt, 8); CeilingJunk(RoomId.EntranceHall, 6); CeilingJunk(RoomId.Maintenance, 8);
            Physics.SyncTransforms();
            PruneMiscSpots();
        }

        // ================================================================== placement rules
        void BuildKeepZones()
        {
            keepZones.Clear();
            claimed.Clear();
            foreach (var d in map.AllDoors) if (d != null) Keep(d.transform.position, 2.3f);
            foreach (var kv in spots) foreach (var sp in kv.Value) Keep(sp.pos, 1.15f);
            foreach (var kv in anchors) if (kv.Value != null) Keep(kv.Value.position, kv.Key == "player_start" || kv.Key == "respawn" ? 3f : 1.5f);
            foreach (var h in dynRoot.GetComponentsInChildren<HideSpot>(true))
            {
                Keep(h.transform.position, 1.4f);
                if (h.exitPoint != null) Keep(h.exitPoint.position, 1.2f);
            }
            foreach (var it in dynRoot.GetComponentsInChildren<Interactable>(true)) Keep(it.transform.position, 1.3f);
            Keep(new Vector3(62f, 0f, 1.5f), 4.5f); // in front of the exit gate (the escape run)
            Keep(new Vector3(62f, 0f, 7f), 3.2f);   // the gap in the turnstile line
        }

        void Keep(Vector3 p, float r) => keepZones.Add(new Circle(p.x, p.z, r));

        bool Kept(Vector3 p, float rad)
        {
            for (int i = 0; i < keepZones.Count; i++)
            {
                var k = keepZones[i];
                float dx = k.x - p.x, dz = k.z - p.z, rr = k.r + rad;
                if (dx * dx + dz * dz < rr * rr) return true;
            }
            return false;
        }

        bool Claim(Vector3 p, float rad)
        {
            for (int i = 0; i < claimed.Count; i++)
            {
                var u = claimed[i];
                float dx = u.x - p.x, dz = u.z - p.z, rr = u.r + rad;
                if (dx * dx + dz * dz < rr * rr) return false;
            }
            claimed.Add(new Circle(p.x, p.z, rad));
            return true;
        }

        void Unclaim() { if (claimed.Count > 0) claimed.RemoveAt(claimed.Count - 1); }

        /// <summary>Any wall/prop collider at body height (0.1..1.2 m) at this point?</summary>
        static bool InCollider(float x, float z)
            => Physics.CheckBox(new Vector3(x, 0.65f, z), new Vector3(0.02f, 0.55f, 0.02f), Quaternion.identity, RF.MaskWorld, QueryTriggerInteraction.Ignore);

        /// <summary>w x d footprint: 9 samples on floor of the same room and outside every collider, and clear of the keep zones.</summary>
        bool Fits(Vector3 p, float yaw, float w, float d)
        {
            var pc = FairMap.CellOf(p);
            if (!map.Floor(pc.x, pc.y)) return false;
            int room = map.roomAt[pc.x, pc.y];
            var q = Yaw(yaw);
            for (int ix = -1; ix <= 1; ix++)
                for (int iz = 0; iz < 3; iz++)
                {
                    float fx = ix * 0.5f, fz = iz == 0 ? -0.45f : (iz == 1 ? 0f : 0.5f);
                    var s = p + q * new Vector3(fx * (w - 0.08f), 0f, fz * (d - 0.08f));
                    var c = FairMap.CellOf(s);
                    if (!map.Floor(c.x, c.y) || map.roomAt[c.x, c.y] != room || InCollider(s.x, s.z)) return false;
                }
            return !Kept(p, Mathf.Max(w, d) * 0.5f);
        }

        /// <summary>Walkable cells whose edge is a real wall (not a door, arch, the main gate or a partition inside the room).</summary>
        List<WallSlot> Slots(RoomDef room)
        {
            var list = new List<WallSlot>();
            var r = room.r;
            for (int x = r.xMin; x < r.xMax; x++)
                for (int z = r.yMin; z < r.yMax; z++)
                {
                    if (!map.Walkable(x, z)) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int dx = k == 0 ? 1 : (k == 1 ? -1 : 0), dz = k == 2 ? 1 : (k == 3 ? -1 : 0);
                        int nx = x + dx, nz = z + dz;
                        if (KindOf(x, z, nx, nz) != EdgeKind.Wall) continue;
                        if (map.Floor(nx, nz) && map.roomAt[nx, nz] == map.roomAt[x, z]) continue; // maze / tunnel partition
                        var c = FairMap.CellCenter(x, z);
                        var dir = new Vector3(dx, 0f, dz);
                        list.Add(new WallSlot { c = c, n = -dir, t = new Vector3(dz, 0f, -dx), wall = c + dir * 0.85f, edge = c + dir * (FairMap.Cell * 0.5f) });
                    }
                }
            RF.Shuffle(list);
            return list;
        }

        void WallDress(RoomId id, string[] list, int n)
        {
            var room = R(id);
            int placed = 0, ki = 0, fails = 0;
            foreach (var s0 in Slots(room))
            {
                if (placed >= n) break;
                var kit = wallKit[list[ki % list.Length]];
                float span = FairMap.Cell - 0.2f - kit.w;
                float along = span > 0f ? Rnd(-span * 0.5f, span * 0.5f) : 0f;
                var p = s0.wall + s0.n * (kit.d * 0.5f + 0.04f) + s0.t * along;
                p.y = 0f;
                float yaw = FaceYaw(s0.n);
                if (!Fits(p, yaw, kit.w, kit.d) || !Claim(p, Mathf.Max(kit.w, kit.d) * 0.6f))
                {
                    if (++fails >= 4) { ki++; fails = 0; }
                    continue;
                }
                kit.fn(p, yaw);
                Physics.SyncTransforms();
                placed++; ki++; fails = 0;
                // v1.6: 45 % of the wall props leave a free 'misc' item spot on the floor 0.35 m in front of them
                if (Random.value < 0.45f)
                {
                    var q = p + s0.n * (kit.d * 0.5f + 0.35f);
                    q.y = 0.02f;
                    var qc = FairMap.CellOf(q);
                    if (map.Walkable(qc.x, qc.y) && !InCollider(q.x, q.z)) { AddSpot("misc", q, yaw); Keep(q, 1.15f); }
                }
            }
            dressLog[id.ToString()] = placed + "/" + n;
        }

        /// <summary>Blood smears / handprints on the walls and the odd dim wall sconce.</summary>
        void WallDecor(RoomId id, int n, int sconces, int color)
        {
            var room = R(id);
            int k = 0;
            foreach (var s0 in Slots(room))
            {
                if (k >= n + sconces) break;
                var e = s0.edge + s0.t * Rnd(-0.6f, 0.6f) + s0.n * WallInset; // on the (inset) wall face
                if (Kept(e, 0.4f)) continue;
                if (k < sconces)
                {
                    var q = new Vector3(e.x, Mathf.Min(2.5f, room.height - 0.5f), e.z) + s0.n * 0.1f;
                    wm.Box("metal_rust", q, new Vector3(0.18f, 0.28f, 0.2f), FaceYaw(s0.n), false);
                    Lamp(q + Vector3.down * 0.08f + s0.n * 0.1f, 0.09f, q + s0.n * 0.35f, Hex(color), 0.55f, 5f, Random.value < 0.5f);
                }
                else WallDecal("decal_blood", new Vector3(e.x, Rnd(0.6f, 1.8f), e.z), s0.n, Rnd(0.5f, 1.1f), Rnd(0.6f, 1.3f));
                k++;
            }
        }

        // ================================================================== v1.0.3: denser rooms
        /// <summary>More hiding places: every bigger room gets a few, placed like wall props, with their exits kept clear.</summary>
        void ExtraHides()
        {
            ExtraHide(RoomId.EntranceHall, "cabinet:wood", "crate");
            ExtraHide(RoomId.FoodCourt, "locker", "cabinet:wood_painted");
            ExtraHide(RoomId.CarouselHall, "crate", "cabinet:wood", "crate");
            ExtraHide(RoomId.BumperCars, "crate", "locker");
            ExtraHide(RoomId.GhostTrain, "coffin", "coffin");
            ExtraHide(RoomId.Warehouse, "crate", "crate");
            ExtraHide(RoomId.Kitchen, "locker");
            ExtraHide(RoomId.Maintenance, "locker");
            ExtraHide(RoomId.ClockGallery, "cabinet:wood");
            ExtraHide(RoomId.ServiceHall, "locker");
            Physics.SyncTransforms();
        }

        void ExtraHide(RoomId id, params string[] kinds)
        {
            var room = R(id);
            int placed = 0;
            foreach (var s0 in Slots(room))
            {
                if (placed >= kinds.Length) break;
                var parts = kinds[placed].Split(':');
                string kind = parts[0], mat = parts.Length > 1 ? parts[1] : null;
                float w = kind == "locker" ? 0.8f : kind == "cabinet" ? 1.2f : kind == "crate" ? 1.5f : 0.9f;
                float d = kind == "locker" ? 0.65f : kind == "cabinet" ? 0.7f : kind == "crate" ? 1.5f : 0.6f;
                var p = s0.wall + s0.n * (d * 0.5f + 0.06f);
                p.y = 0f;
                float yaw = FaceYaw(s0.n);
                var ex = p + s0.n * (d * 0.5f + 1f);
                var ec = FairMap.CellOf(ex);
                if (!map.Walkable(ec.x, ec.y) || Kept(ex, 0.5f) || !Fits(p, yaw, w, d) || !Claim(p, Mathf.Max(w, d) * 0.7f)) continue;
                if (!TryBlock(p, new Vector3(w, 1f, d), yaw)) { Unclaim(); continue; }
                if (kind == "locker") HideSpot.Locker(dynRoot, p, yaw, "wall_metal");
                else if (kind == "cabinet") BuildCabinet(p, yaw, mat ?? "wood");
                else if (kind == "crate") BuildCrateHide(p, yaw);
                else Coffin(p, yaw, true);
                Keep(p, 1.4f);
                Keep(ex, 1.2f);
                placed++;
            }
            dressLog["hide:" + id] = placed + "/" + kinds.Length;
        }

        static readonly RoomId[] DetailRooms =
        {
            RoomId.EntranceHall, RoomId.Security, RoomId.Broadcast, RoomId.FoodCourt, RoomId.Kitchen, RoomId.ColdRoom, RoomId.CarouselHall,
            RoomId.Arcade, RoomId.BumperCars, RoomId.BumperOffice, RoomId.GhostTrain, RoomId.Warehouse, RoomId.ClockGallery,
            RoomId.Maintenance, RoomId.WarehouseOffice, RoomId.ServiceHall, RoomId.StaffArea,
        };
        static readonly HashSet<RoomId> PipeRooms = new HashSet<RoomId> { RoomId.Kitchen, RoomId.ColdRoom, RoomId.Maintenance, RoomId.Warehouse, RoomId.WarehouseOffice,
            RoomId.ServiceHall, RoomId.BumperCars, RoomId.BumperOffice, RoomId.Security, RoomId.Broadcast, RoomId.StaffArea };
        static readonly HashSet<RoomId> WoodyRooms = new HashSet<RoomId> { RoomId.EntranceHall, RoomId.FoodCourt, RoomId.CarouselHall, RoomId.Arcade, RoomId.ClockGallery, RoomId.GhostTrain };

        /// <summary>Baseboards, pipe runs, sagging cables, junction boxes, vents and grime on every real wall (no colliders: all flush or overhead).</summary>
        void WallDetail(RoomId id)
        {
            var room = R(id);
            float top = Mathf.Min(room.height - 0.5f, 3.7f);
            bool pipes = PipeRooms.Contains(id), wood = WoodyRooms.Contains(id);
            foreach (var s0 in Slots(room))
            {
                float yaw = FaceYaw(s0.n);
                var f = s0.edge + s0.n * (FairBuilder.WallInset + 0.03f);
                f.y = 0f;
                wm.Box(wood ? "wood" : "metal_rust", f + Vector3.up * 0.08f, new Vector3(1.98f, 0.16f, 0.05f), yaw, false);
                if (wood && Random.value < 0.85f) wm.Box("wood_painted", f + Vector3.up * 1.05f, new Vector3(1.98f, 0.06f, 0.04f), yaw, false);
                Vector3 a = f - s0.t * 0.99f, c = f + s0.t * 0.99f;
                if (pipes)
                {
                    Vector3 pa = a + s0.n * 0.12f + Vector3.up * top, pc = c + s0.n * 0.12f + Vector3.up * top;
                    wm.Tube("metal_rust", pa, pc, 0.075f, 8, false);
                    if (Random.value < 0.65f) wm.Tube("chrome", pa + Vector3.down * 0.24f, pc + Vector3.down * 0.24f, 0.035f, 6, false);
                    wm.Box("black_matte", f + s0.n * 0.07f + Vector3.up * (top - 0.1f), new Vector3(0.06f, 0.4f, 0.14f), yaw, false);
                    if (Random.value < 0.12f)
                    {
                        var v = Vector3.Lerp(pa, pc, Rnd(0.2f, 0.8f));
                        if (!Kept(v, 0.3f)) wm.Tube("metal_rust", v, new Vector3(v.x, 0.12f, v.z), 0.06f, 8, false);
                    }
                }
                if (Random.value < 0.38f)
                {
                    Vector3 ca = a + s0.n * 0.04f + Vector3.up * (top - 0.45f), cc = c + s0.n * 0.04f + Vector3.up * (top - 0.45f), prev = ca;
                    float sag = Rnd(0.1f, 0.4f);
                    for (int i = 1; i <= 4; i++)
                    {
                        float t = i / 4f;
                        var q = Vector3.Lerp(ca, cc, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * sag);
                        wm.Tube("black_matte", prev, q, 0.012f, 3, false);
                        prev = q;
                    }
                }
                float k = Random.value;
                if (k < 0.12f && !Kept(f, 0.3f))
                {
                    var q = f + s0.t * Rnd(-0.6f, 0.6f) + s0.n * 0.06f + Vector3.up * Rnd(1.5f, 2f);
                    wm.Box("wall_metal", q, new Vector3(0.36f, 0.46f, 0.12f), yaw, false);
                    wm.Box(Random.value < 0.5f ? "yellow_paint" : "red_paint", q + s0.n * 0.065f + Vector3.up * 0.12f, new Vector3(0.14f, 0.08f, 0.01f), yaw, false);
                    wm.Tube("metal_rust", q + Vector3.up * 0.23f, new Vector3(q.x, room.height, q.z), 0.025f, 5, false);
                }
                else if (k < 0.2f)
                {
                    var q = f + s0.t * Rnd(-0.5f, 0.5f) + s0.n * 0.015f + Vector3.up * (Random.value < 0.5f ? 0.42f : top - 0.8f);
                    wm.Box("black_matte", q, new Vector3(0.66f, 0.4f, 0.02f), yaw, false);
                    wm.Box("floor_grate", q + s0.n * 0.012f, new Vector3(0.6f, 0.34f, 0.005f), yaw, false);
                }
                else if (k < 0.48f)
                    WallDecal("decal_grime", f + s0.t * Rnd(-0.5f, 0.5f) + Vector3.up * Rnd(0.5f, 1.7f), s0.n, Rnd(0.8f, 1.7f), Rnd(0.8f, 1.6f));
            }
        }

        /// <summary>Chains, dead bulbs and animatronic parts hanging from the ceilings of the big halls (always above head height).</summary>
        void CeilingJunk(RoomId id, int n)
        {
            var room = R(id);
            for (int i = 0; i < n; i++)
            {
                var p = map.RandomPointInRoom(room) + new Vector3(Rnd(-0.7f, 0.7f), 0f, Rnd(-0.7f, 0.7f));
                float y0 = room.height, y1 = Mathf.Max(2.5f, y0 - Rnd(0.7f, 2.2f));
                wm.Tube("metal_rust", new Vector3(p.x, y0, p.z), new Vector3(p.x, y1, p.z), 0.014f, 4, false);
                float kd = Random.value;
                var hang = new Vector3(p.x, y1, p.z);
                if (kd < 0.3f)
                {
                    var rot = Quaternion.Euler(Rnd(-30f, 30f), Rnd(0f, 360f), Rnd(-30f, 30f));
                    wm.Ell(Random.value < 0.5f ? "plastic" : (Random.value < 0.5f ? "fur" : "fur_dark"), hang + Vector3.down * 0.18f, Vector3.one * 0.34f, rot);
                    wm.Ell("black_matte", hang + Vector3.down * 0.18f + rot * new Vector3(-0.07f, 0.04f, 0.14f), Vector3.one * 0.07f, rot, false, true);
                    wm.Ell("black_matte", hang + Vector3.down * 0.18f + rot * new Vector3(0.07f, 0.04f, 0.14f), Vector3.one * 0.07f, rot, false, true);
                }
                else if (kd < 0.55f) wm.Tube("endo", hang, hang + new Vector3(Rnd(-0.15f, 0.15f), -0.6f, Rnd(-0.15f, 0.15f)), 0.03f, 6);
                else if (kd < 0.75f)
                {
                    wm.Box("black_matte", hang + Vector3.down * 0.06f, new Vector3(0.12f, 0.12f, 0.12f), 0f, false);
                    wm.Ell("bulb_dead", hang + Vector3.down * 0.2f, Vector3.one * 0.13f, Quaternion.identity, false, true);
                }
                else wm.Box("metal_rust", hang + Vector3.down * 0.05f, new Vector3(0.05f, 0.12f, 0.05f), 0f, false);
            }
        }

        /// <summary>Steel prep counter with pots and boxes (kitchen / food court islands).</summary>
        void Counter(Vector3 p, float yaw)
        {
            wm.Box("chrome", new Vector3(p.x, 0.88f, p.z), new Vector3(1.8f, 0.05f, 0.8f), yaw);
            wm.Box("chrome", L(p, yaw, 0f, 0.25f, 0f), new Vector3(1.7f, 0.03f, 0.7f), yaw, false);
            foreach (var o in new[] { new Vector2(-0.85f, -0.35f), new Vector2(0.85f, -0.35f), new Vector2(-0.85f, 0.35f), new Vector2(0.85f, 0.35f) })
                wm.Box("chrome", L(p, yaw, o.x, 0.44f, o.y), new Vector3(0.04f, 0.88f, 0.04f), yaw, false);
            int n = Rndi(2, 4);
            for (int i = 0; i < n; i++)
            {
                var q = L(p, yaw, Rnd(-0.7f, 0.7f), 0.9f, Rnd(-0.25f, 0.25f));
                if (Random.value < 0.5f) wm.Cyl("metal_rust", q, Rnd(0.12f, 0.2f), Rnd(0.12f, 0.3f), 10);
                else wm.Box(Random.value < 0.4f ? "cardboard" : (Random.value < 0.5f ? "white_paint" : "metal_rust"), q + Vector3.up * 0.06f, new Vector3(Rnd(0.2f, 0.4f), 0.12f, Rnd(0.2f, 0.3f)), yaw + Rnd(-30f, 30f), false);
            }
            Col(p, 0.9f, 0.4f, yaw, 0f, 0.95f);
        }

        /// <summary>3x3 block of walkable cells of one room with no wall between any of them.</summary>
        bool OpenBlock(int x, int z, int room)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int cx = x + dx, cz = z + dz;
                    if (!map.Walkable(cx, cz) || map.roomAt[cx, cz] != room) return false;
                }
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int cx = x + dx, cz = z + dz;
                    if (dx < 1 && map.WallBetween(cx, cz, cx + 1, cz)) return false;
                    if (dz < 1 && map.WallBetween(cx, cz, cx, cz + 1)) return false;
                }
            return true;
        }

        /// <summary>Free-standing clutter. The blocked centre cell keeps its 8 open neighbours, so no route can be walled off.</summary>
        void FloorDress(RoomId id, string[] list, int n, Func<Vector3, bool> ok = null)
        {
            var room = R(id);
            int ri = map.rooms.IndexOf(room);
            var r = room.r;
            var cells = new List<Vector2Int>();
            for (int x = r.xMin + 1; x < r.xMax - 1; x++)
                for (int z = r.yMin + 1; z < r.yMax - 1; z++)
                    if (OpenBlock(x, z, ri)) cells.Add(new Vector2Int(x, z));
            RF.Shuffle(cells);
            int placed = 0, ki = 0, fails = 0;
            foreach (var cell in cells)
            {
                if (placed >= n) break;
                if (!OpenBlock(cell.x, cell.y, ri)) continue; // an earlier prop blocked a neighbour
                var c = FairMap.CellCenter(cell.x, cell.y) + new Vector3(Rnd(-0.25f, 0.25f), 0f, Rnd(-0.25f, 0.25f));
                if (ok != null && !ok(c)) continue;
                var kit = floorKit[list[ki % list.Length]];
                float yaw = Rnd(0f, 360f);
                if (!Fits(c, yaw, kit.w, kit.d) || !Claim(c, Mathf.Max(kit.w, kit.d) * 0.7f + 0.4f))
                {
                    if (++fails >= 4) { ki++; fails = 0; }
                    continue;
                }
                kit.fn(c, yaw);
                map.BlockWorld(c, new Vector3(0.6f, 1f, 0.6f)); // just this cell
                Physics.SyncTransforms();
                placed++; ki++; fails = 0;
            }
            dressLog["floor:" + id] = placed + "/" + n;
        }

        /// <summary>Blocks a multi-cell footprint for A*, unless that would cut off any cell reachable from the respawn point.</summary>
        bool TryBlock(Vector3 c, Vector3 size, float yaw)
        {
            Vector3 from = RespawnPos();
            var before = Reach(from);
            var snap = (bool[,])map.solid.Clone();
            map.BlockWorld(c, size, yaw);
            var after = Reach(from);
            for (int x = 0; x < FairMap.W; x++)
                for (int z = 0; z < FairMap.H; z++)
                    if (before[x, z] && !map.solid[x, z] && !after[x, z])
                    {
                        Array.Copy(snap, map.solid, snap.Length);
                        return false;
                    }
            return true;
        }

        bool Feature(RoomDef r, float lx, float lz, float w, float d, PropFn fn, float yaw)
        {
            var p = r.P(lx, lz);
            if (!Fits(p, yaw, w, d) || !Claim(p, Mathf.Max(w, d) * 0.6f)) return false;
            if ((w > 1.1f || d > 1.1f) && !TryBlock(p, new Vector3(w, 1f, d), yaw)) { Unclaim(); return false; }
            fn(p, yaw);
            Physics.SyncTransforms();
            return true;
        }

        // ================================================================== reachability
        Vector3 RespawnPos() => anchors.TryGetValue("respawn", out var a) && a != null ? a.position : R(RoomId.EntranceHall).Center;

        /// <summary>Cells the robots' A* grid can reach from a point (orthogonal steps, same rules as pathfinding).</summary>
        bool[,] Reach(Vector3 from)
        {
            var reach = new bool[FairMap.W, FairMap.H];
            var s = map.NearestWalkable(FairMap.CellOf(from), 3);
            if (!map.Walkable(s.x, s.y)) return reach;
            var q = new Queue<Vector2Int>();
            q.Enqueue(s);
            reach[s.x, s.y] = true;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.x + (k == 0 ? 1 : (k == 1 ? -1 : 0)), nz = c.y + (k == 2 ? 1 : (k == 3 ? -1 : 0));
                    if (!FairMap.In(nx, nz) || reach[nx, nz] || !map.CanStep(c.x, c.y, nx, nz)) continue;
                    reach[nx, nz] = true;
                    q.Enqueue(new Vector2Int(nx, nz));
                }
            }
            return reach;
        }

        /// <summary>
        /// Self-check (dev builds log it on start; editor menu Rusted Fair ▸ Check Fair Reachability runs several seeds):
        /// every quest/item spot, every anchor and every room must be reachable by A* from the respawn point.
        /// Returns the number of problems; details go to the log.
        /// </summary>
        public int VerifyReachability(StringBuilder log)
        {
            int fails = 0;
            Vector3 from = RespawnPos();
            var reach = Reach(from);
            var path = new List<Vector3>();

            bool Reachable(Vector3 p) => SpotReachable(p, from, reach, path);

            foreach (var kv in spots)
                for (int i = 0; i < kv.Value.Count; i++)
                    if (!Reachable(kv.Value[i].pos)) { fails++; log.AppendLine("  unreachable spot " + kv.Key + "#" + i + " at " + kv.Value[i].pos); }
            foreach (var kv in anchors)
            {
                if (kv.Value == null) continue;
                var c = FairMap.CellOf(kv.Value.position);
                if (!map.Floor(c.x, c.y)) continue; // outside the building (escape point, rain box)
                if (!Reachable(kv.Value.position)) { fails++; log.AppendLine("  unreachable anchor " + kv.Key + " at " + kv.Value.position); }
            }
            foreach (var room in map.rooms)
            {
                int walk = 0, ok = 0;
                for (int x = room.r.xMin; x < room.r.xMax; x++)
                    for (int z = room.r.yMin; z < room.r.yMax; z++)
                        if (map.Walkable(x, z)) { walk++; if (reach[x, z]) ok++; }
                if (ok == 0) { fails++; log.AppendLine("  room " + room.id + " is not reachable at all"); }
                else if (ok < walk) log.AppendLine("  room " + room.id + ": " + (walk - ok) + " walkable cell(s) not reachable (enclosed by furniture)");
            }
            return fails;
        }

        bool SpotReachable(Vector3 p, Vector3 from, bool[,] reach, List<Vector3> path)
        {
            var c = FairMap.CellOf(p);
            int room = map.Floor(c.x, c.y) ? map.roomAt[c.x, c.y] : -1;
            bool near = false;
            for (int rad = 0; rad <= 2 && !near; rad++)
                for (int dx = -rad; dx <= rad && !near; dx++)
                    for (int dz = -rad; dz <= rad; dz++)
                    {
                        int x = c.x + dx, z = c.y + dz;
                        if (!map.Walkable(x, z) || !reach[x, z]) continue;
                        if (room >= 0 && map.roomAt[x, z] != room) continue;
                        near = true;
                        break;
                    }
            return near && map.FindPath(from, p, path, FairMap.W * FairMap.H * 2);
        }

        /// <summary>v1.0.3: the denser dressing can wall in an optional 'misc' item spot; drop those so no item spawns out of reach.</summary>
        void PruneMiscSpots()
        {
            if (!spots.TryGetValue("misc", out var list)) return;
            Vector3 from = RespawnPos();
            var reach = Reach(from);
            var path = new List<Vector3>();
            int removed = list.RemoveAll(sp => !SpotReachable(sp.pos, from, reach, path));
            if (removed > 0) dressLog["prunedMisc"] = removed.ToString();
        }

        /// <summary>Static clutter pieces dropped around task items in the last Bake().</summary>
        public int ClearedStatic => wm.ClearedCount;

        /// <summary>v1.4: a task item that starts inside a collider (shelf board, counter...) is lifted until it is free.</summary>
        public bool LiftOutOfColliders(Transform t)
        {
            Vector3 p = t.position;
            for (int i = 0; i <= 12; i++)
            {
                var c = p + Vector3.up * (0.1f + i * 0.05f);
                if (!Physics.CheckSphere(c, 0.07f, RF.MaskWorld, QueryTriggerInteraction.Ignore))
                {
                    if (i > 0) t.position = p + Vector3.up * (i * 0.05f);
                    return i > 0;
                }
            }
            return false;
        }

        /// <summary>
        /// v1.4 dev check: every task item lying in the open must be visible (no collider in the way) from at least 6 free
        /// standing positions around it at eye heights 1.3–2.2 m. Items in drawers / locker 13 are skipped.
        /// </summary>
        public int VerifyItemVisibility(List<Pickup> items, StringBuilder log)
        {
            Physics.SyncTransforms();
            int bad = 0;
            float[] radii = { 1.2f, 2f, 3f, 4f };
            float[] eyes = { 1.3f, 1.75f, 2.2f };
            foreach (var p in items)
            {
                if (p == null || p.InDrawer != null || p.item == ItemId.Crank || p.item == ItemId.Keycard) continue;
                Vector3 target = p.transform.position + Vector3.up * 0.08f;
                int seen = 0;
                foreach (float r in radii)
                    for (int a = 0; a < 16; a++)
                    {
                        float ang = a / 16f * Mathf.PI * 2f;
                        var stand = new Vector3(target.x + Mathf.Cos(ang) * r, 0f, target.z + Mathf.Sin(ang) * r);
                        var cell = FairMap.CellOf(stand);
                        if (!map.Floor(cell.x, cell.y)) continue;
                        if (Physics.CheckCapsule(stand + Vector3.up * 0.4f, stand + Vector3.up * 1.5f, 0.3f, RF.MaskWorld, QueryTriggerInteraction.Ignore)) continue;
                        foreach (float e in eyes)
                            if (!Physics.Linecast(stand + Vector3.up * e, target, RF.MaskWorld, QueryTriggerInteraction.Ignore)) { seen++; break; }
                    }
                if (seen < 6)
                {
                    bad++;
                    var room = map.RoomAtPos(target);
                    log.AppendLine("  " + p.item + " at " + target + " (" + (room != null ? room.id.ToString() : "?") + ") visible from only " + seen + " position(s)");
                }
            }
            return bad;
        }

        public string DressSummary()
        {
            var sb = new StringBuilder();
            foreach (var kv in dressLog) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append("  ");
            return sb.ToString();
        }

        // ================================================================== helpers
        static float Rnd(float a, float b) => Random.Range(a, b);
        static int Rndi(int a, int b) => Random.Range(a, b + 1);
        static string PickS(params string[] a) => a[Random.Range(0, a.Length)];
        static float FaceYaw(Vector3 n) => Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
        static Vector3 L(Vector3 p, float yaw, float x, float y, float z) => p + Yaw(yaw) * new Vector3(x, y, z);
        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        static Quaternion RandRot(float tilt) => Quaternion.Euler(Rnd(-tilt, tilt), Rnd(0f, 360f), Rnd(-tilt, tilt));

        /// <summary>Prop collider from a footprint (half extents) and a height range.</summary>
        void Col(Vector3 p, float hx, float hz, float yaw, float y0, float y1)
            => AddColliderRot(new Vector3(p.x, (y0 + y1) * 0.5f, p.z), new Vector3(hx * 2f, Mathf.Max(0.05f, y1 - y0), hz * 2f), yaw);

        void Ell(string mat, Vector3 c, Vector3 size, Quaternion rot, bool lowPoly = false) => wm.Ell(mat, c, size, rot, true, lowPoly);

        /// <summary>Ellipsoid placed like a child transform of a scaled sphere (eye sockets on a head...).</summary>
        void EllChild(string mat, Vector3 pc, Quaternion pr, Vector3 ps, Vector3 lp, Vector3 ls)
            => wm.Ell(mat, pc + pr * Vector3.Scale(lp, ps), Vector3.Scale(ls, ps), pr, false, true);

        void BoxChild(string mat, Vector3 pc, Quaternion pr, Vector3 ps, Vector3 lp, Vector3 ls)
            => wm.BoxRot(mat, pc + pr * Vector3.Scale(lp, ps), Vector3.Scale(ls, ps), pr, false);

        void Paper(Vector3 p)
            => wm.For("paper", p, false).QuadUV(new Vector3(p.x, 0.016f + Random.value * 0.003f, p.z), Vector3.up, Yaw(Rnd(0f, 360f)) * Vector3.forward, 0.28f, 0.36f, new Rect(0, 0, 1, 1));

        /// <summary>A static bulb (shared power-tinted material) plus its light.</summary>
        void Lamp(Vector3 bulbPos, float bulbSize, Vector3 lightPos, Color col, float intensity, float range, bool flicker)
        {
            wm.Ell("bulb_main", bulbPos, Vector3.one * bulbSize, Quaternion.identity, false, true); // static; follows the power
            lights.Add(lightPos, staticRoot, col, intensity, range, LightGroup.Main, flicker);
        }

        /// <summary>Head with empty sockets (one maybe dimly lit) and optional teeth.</summary>
        void JunkHead(string mat, Vector3 c, float s, Quaternion rot, bool teeth)
        {
            var ps = Vector3.one * s;
            Ell(mat, c, ps, rot);
            EllChild("black_matte", c, rot, ps, new Vector3(-0.2f, 0.12f, 0.4f), Vector3.one * 0.2f);
            EllChild(Random.value < 0.25f ? "eye_dim" : "black_matte", c, rot, ps, new Vector3(0.2f, 0.12f, 0.4f), Vector3.one * 0.2f);
            if (teeth) BoxChild("teeth", c, rot, ps, new Vector3(0f, -0.22f, 0.42f), new Vector3(0.42f, 0.08f, 0.08f));
        }

        // ================================================================== kit
        void BuildKits()
        {
            wallKit = new Dictionary<string, KitEntry>
            {
                { "boxPile", new KitEntry(1.0f, 0.7f, BoxPile) },
                { "bin", new KitEntry(0.62f, 0.62f, Bin) },
                { "chairStack", new KitEntry(0.55f, 0.55f, ChairStack) },
                { "junk", new KitEntry(1.15f, 0.75f, Junk) },
                { "filing", new KitEntry(0.55f, 0.62f, Filing) },
                { "prizeShelf", new KitEntry(1.65f, 0.5f, PrizeShelf) },
                { "costumeRack", new KitEntry(1.55f, 0.5f, CostumeRack) },
                { "grandClock", new KitEntry(0.62f, 0.46f, GrandClock) },
                { "tomb", new KitEntry(0.7f, 0.3f, Tomb) },
                { "coffinUp", new KitEntry(0.75f, 0.4f, CoffinUp) },
                { "claw", new KitEntry(0.95f, 0.8f, Claw) },
                { "pallets", new KitEntry(1.25f, 0.85f, Pallets) },
                { "mop", new KitEntry(0.9f, 0.5f, Mop) },
                { "plant", new KitEntry(0.6f, 0.6f, Plant) },
                { "tires", new KitEntry(0.72f, 0.72f, Tires) },
                { "stove", new KitEntry(1.05f, 0.7f, Stove) },
                { "fridge", new KitEntry(0.9f, 0.7f, Fridge) },
                { "toolCab", new KitEntry(1.25f, 0.55f, ToolCab) },
                { "gasCyl", new KitEntry(1.0f, 0.32f, GasCyl) },
                { "sofa", new KitEntry(2.0f, 0.8f, Sofa) },
                { "cooler", new KitEntry(0.45f, 0.45f, Cooler) },
                { "ladder", new KitEntry(0.5f, 0.4f, Ladder) },
                { "barrels", new KitEntry(1.35f, 0.66f, Barrels) },
            };
            floorKit = new Dictionary<string, KitEntry>
            {
                { "crates", new KitEntry(1.2f, 1.2f, FloorCrates) },
                { "barrels", new KitEntry(1.35f, 0.7f, Barrels) },
                { "junk", new KitEntry(1.15f, 0.75f, Junk) },
                { "pallets", new KitEntry(1.25f, 0.85f, Pallets) },
                { "spool", new KitEntry(1.0f, 1.0f, Spool) },
                { "stanchions", new KitEntry(1.4f, 0.3f, Stanchions) },
                { "toppled", new KitEntry(1.0f, 0.7f, ToppledBin) },
                { "picnic", new KitEntry(1.7f, 1.6f, Picnic) },
                { "tomb", new KitEntry(0.7f, 0.3f, Tomb) },
                { "coffin", new KitEntry(0.75f, 0.4f, CoffinUp) },
                { "claw", new KitEntry(0.95f, 0.8f, Claw) },
                { "balloons", new KitEntry(0.6f, 0.6f, (p, y) => BalloonStand(p, 7, 0.5f, 2.4f, 3.3f, 0.28f)) },
                { "bin", new KitEntry(0.62f, 0.62f, Bin) },
                { "chairStack", new KitEntry(0.55f, 0.55f, ChairStack) },
                { "toolCab", new KitEntry(1.25f, 0.55f, ToolCab) },
                { "gasCyl", new KitEntry(1.0f, 0.32f, GasCyl) },
                { "counter", new KitEntry(1.85f, 0.85f, Counter) },
            };
        }

        void BoxPile(Vector3 p, float yaw)
        {
            float y = 0f;
            int n = Rndi(2, 4);
            for (int i = 0; i < n; i++)
            {
                float sz = Rnd(0.38f, 0.6f);
                wm.Box(Random.value < 0.8f ? "cardboard" : "wood", L(p, yaw, Rnd(-0.25f, 0.25f), y + sz * 0.4f, Rnd(-0.1f, 0.1f)), new Vector3(sz, sz * 0.8f, sz), yaw + Rnd(-17f, 17f));
                y += sz * 0.8f;
                if (Random.value < 0.3f) break;
            }
            Col(p, 0.45f, 0.32f, yaw, 0f, Mathf.Max(0.5f, y));
        }

        void Bin(Vector3 p, float yaw)
        {
            wm.Cyl("metal_rust", p, 0.27f, 0.85f, 10, true, true, 0.3f);
            wm.Cyl("black_matte", p + Vector3.up * 0.85f, 0.31f, 0.05f, 10);
            if (Random.value < 0.6f) Paper(p + new Vector3(Rnd(-0.6f, 0.6f), 0f, Rnd(-0.6f, 0.6f)));
            Col(p, 0.28f, 0.28f, 0f, 0f, 0.9f);
        }

        void ChairStack(Vector3 p, float yaw)
        {
            int n = Rndi(3, 6);
            for (int i = 0; i < n; i++)
            {
                float y = 0.45f + i * 0.12f, yy = yaw + i * 2.3f;
                wm.Box("red_paint", new Vector3(p.x, y, p.z), new Vector3(0.45f, 0.05f, 0.45f), yy, false);
                wm.Box("red_paint", L(p, yy, 0f, y + 0.27f, -0.2f), new Vector3(0.45f, 0.5f, 0.04f), yy, false);
            }
            foreach (var o in new[] { new Vector2(-0.19f, -0.19f), new Vector2(0.19f, -0.19f), new Vector2(-0.19f, 0.19f), new Vector2(0.19f, 0.19f) })
                wm.Box("metal_rust", L(p, yaw, o.x, 0.22f, o.y), new Vector3(0.03f, 0.44f, 0.03f), yaw, false);
            Col(p, 0.25f, 0.25f, yaw, 0f, 1.3f);
        }

        /// <summary>Pile of dead animatronic parts: heads with empty sockets / dim eyes, endo limbs, an oil stain.</summary>
        void Junk(Vector3 p, float yaw)
        {
            int heads = Rndi(2, 4);
            for (int i = 0; i < heads; i++)
                JunkHead(PickS("plastic", "fur", "fur_dark", "plastic"), L(p, yaw, Rnd(-0.4f, 0.4f), Rnd(0.18f, 0.5f), Rnd(-0.2f, 0.15f)), Rnd(0.26f, 0.36f), RandRot(34f), Random.value < 0.6f);
            int limbs = Rndi(2, 4);
            for (int i = 0; i < limbs; i++)
            {
                var c = L(p, yaw, Rnd(-0.45f, 0.45f), 0.08f, Rnd(-0.25f, 0.25f));
                float len = Rnd(0.5f, 0.9f);
                var dir = Quaternion.Euler(Rnd(-17f, 17f), Rnd(0f, 360f), 0f) * Vector3.forward;
                wm.Tube("endo", c - dir * len * 0.5f, c + dir * len * 0.5f, 0.035f, 6);
            }
            Decal("decal_oil", p, 1.2f, Rnd(0f, 360f));
            Col(p, 0.55f, 0.35f, yaw, 0f, 0.7f);
        }

        void Filing(Vector3 p, float yaw)
        {
            wm.Box("wall_metal", new Vector3(p.x, 0.66f, p.z), new Vector3(0.5f, 1.32f, 0.58f), yaw);
            for (int i = 0; i < 4; i++) wm.Box("chrome", L(p, yaw, 0f, 0.2f + i * 0.32f, 0.3f), new Vector3(0.16f, 0.03f, 0.02f), yaw, false);
            Col(p, 0.26f, 0.3f, yaw, 0f, 1.35f);
        }

        void PrizeShelf(Vector3 p, float yaw)
        {
            foreach (float sx in new[] { -0.78f, 0.78f }) wm.Box("wood_painted", L(p, yaw, sx, 0.95f, 0f), new Vector3(0.05f, 1.9f, 0.45f), yaw, false);
            foreach (float y in new[] { 0.3f, 0.85f, 1.4f })
            {
                wm.Box("wood", L(p, yaw, 0f, y, 0f), new Vector3(1.6f, 0.04f, 0.45f), yaw, false);
                for (float x = -0.6f; x <= 0.6f; x += Rnd(0.28f, 0.42f))
                {
                    if (Random.value < 0.2f) continue;
                    float sz = Rnd(0.18f, 0.26f);
                    var c = L(p, yaw, x, y + 0.02f + sz * 0.5f, Rnd(-0.05f, 0.08f));
                    var rot = Quaternion.Euler(Rnd(-23f, 23f), yaw + Rnd(-34f, 34f), Rnd(-29f, 29f));
                    var ps = Vector3.one * sz;
                    Ell(PickS("fur", "fur_dark", "plastic", "fabric_purple"), c, ps, rot, true);
                    EllChild("black_matte", c, rot, ps, new Vector3(-0.18f, 0.12f, 0.44f), Vector3.one * 0.16f);
                    EllChild("black_matte", c, rot, ps, new Vector3(0.18f, 0.12f, 0.44f), Vector3.one * 0.16f);
                }
            }
            wm.Box(PickS("neon_pink", "neon_yellow", "neon_cyan"), L(p, yaw, 0f, 1.98f, 0.1f), new Vector3(1.2f, 0.08f, 0.03f), yaw, false);
            Col(p, 0.8f, 0.24f, yaw, 0f, 1.9f);
        }

        /// <summary>Clothes rail with empty mascot suits, some with their heads hung on top.</summary>
        void CostumeRack(Vector3 p, float yaw)
        {
            Vector3 a = L(p, yaw, -0.7f, 0f, 0f), c = L(p, yaw, 0.7f, 0f, 0f);
            wm.Tube("chrome", a, a + Vector3.up * 1.8f, 0.025f, 6);
            wm.Tube("chrome", c, c + Vector3.up * 1.8f, 0.025f, 6);
            wm.Tube("chrome", a + Vector3.up * 1.75f, c + Vector3.up * 1.75f, 0.02f, 6);
            for (int i = 0; i < 3; i++)
            {
                float lx = -0.45f + i * 0.45f;
                Ell(PickS("fur", "fur_dark", "fabric_purple"), L(p, yaw, lx, 1.05f, 0f), new Vector3(0.42f, 1.25f, 0.3f), Quaternion.Euler(0f, yaw + Rnd(-17f, 17f), Rnd(-4.6f, 4.6f)));
                if (Random.value < 0.5f)
                {
                    var hc = L(p, yaw, lx, 1.95f, 0.02f);
                    var rot = Quaternion.Euler(Rnd(-29f, 29f), yaw + Rnd(-23f, 23f), 23f);
                    var ps = Vector3.one * 0.24f;
                    Ell("fur", hc, ps, rot);
                    EllChild("black_matte", hc, rot, ps, new Vector3(-0.2f, 0.1f, 0.42f), Vector3.one * 0.22f);
                    EllChild("black_matte", hc, rot, ps, new Vector3(0.2f, 0.1f, 0.42f), Vector3.one * 0.22f);
                }
            }
            Col(p, 0.75f, 0.22f, yaw, 0f, 1.9f);
        }

        void GrandClock(Vector3 p, float yaw)
        {
            var q = Yaw(yaw);
            Vector3 fwd = q * Vector3.forward;
            wm.Box("wood", new Vector3(p.x, 1.1f, p.z), new Vector3(0.6f, 2.2f, 0.42f), yaw);
            wm.Quad("white_paint", L(p, yaw, 0f, 1.8f, 0.215f), fwd, Vector3.up, 0.4f, 0.4f);
            wm.BoxRot("black_matte", L(p, yaw, 0f, 1.82f, 0.225f), new Vector3(0.02f, 0.16f, 0.01f), q * Quaternion.Euler(0f, 0f, Rnd(0f, 360f)), false);
            wm.Box("gold_trim", L(p, yaw, 0f, 0.9f, 0.22f), new Vector3(0.12f, 0.6f, 0.02f), yaw, false);
            wm.Box("gold_trim", new Vector3(p.x, 2.24f, p.z), new Vector3(0.66f, 0.06f, 0.46f), yaw, false);
            Col(p, 0.3f, 0.22f, yaw, 0f, 2.2f);
        }

        void Tomb(Vector3 p, float yaw)
        {
            float h = Rnd(0.7f, 1.05f), ty = yaw + Rnd(-7f, 7f);
            string mat = PickS("wall_plaster", "wall_brick");
            wm.Box(mat, new Vector3(p.x, h * 0.5f, p.z), new Vector3(0.65f, h, 0.18f), ty);
            Ell(mat, new Vector3(p.x, h - 0.02f, p.z), new Vector3(0.65f, 0.42f, 0.18f), Yaw(ty), true); // rounded top
            if (Random.value < 0.5f)
            {
                var c = L(p, yaw, 0f, h + 0.35f, 0f);
                wm.Box("bone", c, new Vector3(0.06f, 0.5f, 0.05f), ty, false);
                wm.Box("bone", c + Vector3.up * 0.08f, new Vector3(0.3f, 0.06f, 0.05f), ty, false);
            }
            Col(p, 0.34f, 0.12f, yaw, 0f, h);
        }

        /// <summary>Upright coffin, lid slightly ajar, a skull nailed to the front.</summary>
        void CoffinUp(Vector3 p, float yaw)
        {
            wm.Box("wood", new Vector3(p.x, 1f, p.z), new Vector3(0.7f, 2f, 0.35f), yaw);
            wm.Box("black_matte", L(p, yaw, 0.05f, 1f, 0.2f), new Vector3(0.6f, 1.85f, 0.04f), yaw + 3f, false);
            var sk = L(p, yaw, 0f, 1.62f, 0.3f);
            var rot = Yaw(yaw);
            var ps = new Vector3(0.18f, 0.21f, 0.19f);
            Ell("bone", sk, ps, rot, true);
            EllChild("black_matte", sk, rot, ps, new Vector3(-0.2f, 0.08f, 0.4f), Vector3.one * 0.28f);
            EllChild("black_matte", sk, rot, ps, new Vector3(0.2f, 0.08f, 0.4f), Vector3.one * 0.28f);
            Col(p, 0.36f, 0.2f, yaw, 0f, 2f);
        }

        void Claw(Vector3 p, float yaw)
        {
            wm.Box("red_paint", new Vector3(p.x, 0.45f, p.z), new Vector3(0.9f, 0.9f, 0.75f), yaw);
            wm.Box("glass", L(p, yaw, 0f, 1.35f, 0f), new Vector3(0.85f, 0.9f, 0.7f), yaw, false);
            wm.Box("yellow_paint", L(p, yaw, 0f, 1.88f, 0f), new Vector3(0.92f, 0.16f, 0.77f), yaw);
            for (int i = 0; i < 6; i++)
                Ell(PickS("fur", "fur_dark", "plastic"), L(p, yaw, Rnd(-0.3f, 0.3f), 0.97f, Rnd(-0.25f, 0.25f)), Vector3.one * 0.13f, RandRot(30f), true);
            wm.Box(PickS("neon_pink", "neon_cyan"), L(p, yaw, 0f, 1.88f, 0.39f), new Vector3(0.7f, 0.07f, 0.02f), yaw, false);
            Col(p, 0.46f, 0.38f, yaw, 0f, 1.95f);
        }

        void Pallets(Vector3 p, float yaw)
        {
            int n = Rndi(2, 5);
            for (int i = 0; i < n; i++)
                wm.Box("wood", L(p, yaw, Rnd(-0.05f, 0.05f), 0.07f + i * 0.15f, 0f), new Vector3(1.2f, 0.12f, 0.8f), yaw + Rnd(-3.4f, 3.4f));
            Col(p, 0.6f, 0.4f, yaw, 0f, 0.15f * n);
        }

        void Mop(Vector3 p, float yaw)
        {
            var bk = L(p, yaw, -0.2f, 0f, 0f);
            wm.Cyl("yellow_paint", bk, 0.2f, 0.35f, 10);
            wm.Tube("wood", bk + Vector3.up * 0.3f, L(p, yaw, -0.32f, 1.3f, -0.15f), 0.015f, 4);
            var q = Yaw(yaw);
            foreach (float t in new[] { -1f, 1f }) // folding wet-floor sign
                wm.BoxRot("yellow_paint", L(p, yaw, 0.28f, 0.32f, 0.05f + t * 0.09f), new Vector3(0.3f, 0.62f, 0.02f), q * Quaternion.Euler(t * 16f, 0f, 0f), false);
            Decal("decal_water", L(p, yaw, 0f, 0f, 0.2f), 1f, Rnd(0f, 360f));
            Col(p, 0.42f, 0.22f, yaw, 0f, 0.7f);
        }

        void Plant(Vector3 p, float yaw)
        {
            wm.Cyl("black_matte", p, 0.24f, 0.5f, 10, true, true, 0.3f);
            for (int i = 0; i < 6; i++)
                wm.Tube("wood", p + Vector3.up * 0.45f, p + new Vector3(Rnd(-0.5f, 0.5f), Rnd(1.0f, 1.7f), Rnd(-0.5f, 0.5f)), 0.012f, 3);
            Col(p, 0.28f, 0.28f, 0f, 0f, 0.9f);
        }

        void Tires(Vector3 p, float yaw)
        {
            int n = Rndi(2, 5);
            for (int i = 0; i < n; i++) wm.Cyl("rubber", p + new Vector3(Rnd(-0.03f, 0.03f), i * 0.22f, Rnd(-0.03f, 0.03f)), 0.34f, 0.2f, 12);
            wm.Cyl("black_matte", p + Vector3.up * (n * 0.22f), 0.16f, 0.01f, 8, false);
            Col(p, 0.34f, 0.34f, 0f, 0f, n * 0.22f);
        }

        void Stove(Vector3 p, float yaw)
        {
            wm.Box("chrome", new Vector3(p.x, 0.45f, p.z), new Vector3(1f, 0.9f, 0.68f), yaw);
            foreach (var o in new[] { new Vector2(-0.25f, -0.1f), new Vector2(0.25f, -0.1f), new Vector2(-0.25f, 0.18f), new Vector2(0.25f, 0.18f) })
                wm.Cyl("black_matte", L(p, yaw, o.x, 0.9f, o.y), 0.12f, 0.02f, 10, false);
            wm.Box("black_matte", L(p, yaw, 0f, 0.75f, 0.35f), new Vector3(0.8f, 0.06f, 0.02f), yaw, false);
            if (Random.value < 0.6f) wm.Cyl("metal_rust", L(p, yaw, Rnd(-0.25f, 0.25f), 0.92f, -0.05f), 0.17f, 0.2f, 10);
            Col(p, 0.5f, 0.34f, yaw, 0f, 0.95f);
        }

        void Fridge(Vector3 p, float yaw)
        {
            wm.Box("white_paint", new Vector3(p.x, 1f, p.z), new Vector3(0.85f, 2f, 0.66f), yaw);
            wm.Box("chrome", L(p, yaw, 0.32f, 1.2f, 0.34f), new Vector3(0.03f, 0.5f, 0.03f), yaw, false);
            WallDecal("decal_blood", L(p, yaw, -0.1f, 0.7f, 0.33f), Yaw(yaw) * Vector3.forward, 0.5f, 0.6f); // smear on the door
            Col(p, 0.43f, 0.33f, yaw, 0f, 2f);
        }

        void ToolCab(Vector3 p, float yaw)
        {
            wm.Box("red_paint", new Vector3(p.x, 0.55f, p.z), new Vector3(1.2f, 1.1f, 0.5f), yaw);
            for (int i = 0; i < 4; i++) wm.Box("chrome", L(p, yaw, 0f, 0.2f + i * 0.24f, 0.26f), new Vector3(0.9f, 0.025f, 0.02f), yaw, false);
            for (int i = 0; i < 3; i++)
                wm.Box(PickS("metal_rust", "black_matte", "yellow_paint"), L(p, yaw, Rnd(-0.4f, 0.4f), 1.15f, Rnd(-0.1f, 0.1f)), new Vector3(Rnd(0.15f, 0.35f), 0.08f, 0.1f), yaw + Rnd(-57f, 57f), false);
            Col(p, 0.6f, 0.26f, yaw, 0f, 1.15f);
        }

        void GasCyl(Vector3 p, float yaw)
        {
            for (int i = 0; i < 3; i++)
            {
                var q = L(p, yaw, -0.32f + i * 0.32f, 0f, 0f);
                wm.Cyl(i == 1 ? "red_paint" : "green_paint", q, 0.13f, 1.25f, 10);
                wm.Cyl("chrome", q + Vector3.up * 1.25f, 0.04f, 0.1f, 6, false);
            }
            Col(p, 0.48f, 0.15f, yaw, 0f, 1.3f);
        }

        void Sofa(Vector3 p, float yaw)
        {
            wm.Box("fabric_purple", new Vector3(p.x, 0.22f, p.z), new Vector3(1.9f, 0.44f, 0.75f), yaw);
            wm.Box("fabric_purple", L(p, yaw, 0f, 0.62f, -0.3f), new Vector3(1.9f, 0.5f, 0.18f), yaw);
            foreach (float sx in new[] { -0.92f, 0.92f }) wm.Box("fabric_purple", L(p, yaw, sx, 0.5f, 0f), new Vector3(0.16f, 0.25f, 0.75f), yaw, false);
            Decal("decal_blood", L(p, yaw, Rnd(-0.5f, 0.5f), 0f, 0.65f), 0.8f, Rnd(0f, 360f));
            Col(p, 0.97f, 0.38f, yaw, 0f, 0.9f);
        }

        void Cooler(Vector3 p, float yaw)
        {
            wm.Box("white_paint", new Vector3(p.x, 0.5f, p.z), new Vector3(0.4f, 1f, 0.4f), yaw);
            wm.Cyl("glass", p + Vector3.up * 1f, 0.15f, 0.42f, 10, false);
            Col(p, 0.22f, 0.22f, yaw, 0f, 1.4f);
        }

        void Ladder(Vector3 p, float yaw)
        {
            foreach (float sx in new[] { -0.22f, 0.22f }) wm.Tube("metal_rust", L(p, yaw, sx, 0f, 0.18f), L(p, yaw, sx, 2.4f, -0.12f), 0.02f, 4);
            for (float y = 0.3f; y < 2.3f; y += 0.32f)
                wm.Box("metal_rust", L(p, yaw, 0f, y, 0.18f - y / 2.4f * 0.3f), new Vector3(0.44f, 0.03f, 0.03f), yaw, false);
            Col(p, 0.25f, 0.12f, yaw, 0f, 1.6f);
        }

        void Barrels(Vector3 p, float yaw)
        {
            Barrel(L(p, yaw, -0.33f, 0f, 0f));
            Barrel(L(p, yaw, 0.33f, 0f, 0f), Random.value < 0.4f ? "red_paint" : "metal_rust");
        }

        // ---- free-standing extras
        void FloorCrates(Vector3 p, float yaw)
        {
            Crate(p, 1.1f, yaw, false); // the floor dressing blocks the cell itself
            if (Random.value < 0.7f) Crate(p + Vector3.up * 1.1f + Yaw(yaw) * new Vector3(Rnd(-0.1f, 0.1f), 0f, 0f), 0.9f, yaw + Rnd(-15f, 15f));
        }

        void Spool(Vector3 p, float yaw)
        {
            wm.Cyl("wood", p, 0.5f, 0.08f, 12);
            wm.Cyl("black_matte", p + Vector3.up * 0.08f, 0.28f, 0.6f, 10);
            wm.Cyl("wood", p + Vector3.up * 0.68f, 0.5f, 0.08f, 12);
            Col(p, 0.48f, 0.48f, 0f, 0f, 0.8f);
        }

        /// <summary>Queue stanchions with a sagging rope.</summary>
        void Stanchions(Vector3 p, float yaw)
        {
            Vector3 a = L(p, yaw, -0.6f, 0f, 0f), c = L(p, yaw, 0.6f, 0f, 0f);
            foreach (var q in new[] { a, c })
            {
                wm.Cyl("gold_trim", q, 0.035f, 0.95f, 6);
                wm.Cyl("black_matte", q, 0.16f, 0.04f, 10, false);
            }
            var mid = (a + c) * 0.5f + Vector3.up * 0.62f;
            wm.Tube("red_paint", a + Vector3.up * 0.88f, mid, 0.02f, 4);
            wm.Tube("red_paint", mid, c + Vector3.up * 0.88f, 0.02f, 4);
            Col(p, 0.65f, 0.12f, yaw, 0f, 0.95f);
        }

        void ToppledBin(Vector3 p, float yaw)
        {
            var axis = Yaw(yaw) * Vector3.right;
            var c = p + Vector3.up * 0.28f;
            wm.Tube("metal_rust", c - axis * 0.42f, c + axis * 0.42f, 0.28f, 10); // open ends: the spilled mouth
            for (int i = 0; i < 5; i++) Paper(L(p, yaw, Rnd(0.4f, 1.1f), 0f, Rnd(-0.6f, 0.6f)));
            Col(p, 0.45f, 0.3f, yaw, 0f, 0.55f);
        }

        void Picnic(Vector3 p, float yaw)
        {
            wm.Box("wood", L(p, yaw, 0f, 0.74f, 0f), new Vector3(1.6f, 0.06f, 0.75f), yaw);
            foreach (float sz in new[] { -0.62f, 0.62f }) wm.Box("wood", L(p, yaw, 0f, 0.44f, sz), new Vector3(1.6f, 0.05f, 0.3f), yaw);
            foreach (float sx in new[] { -0.65f, 0.65f }) wm.Box("metal_rust", L(p, yaw, sx, 0.36f, 0f), new Vector3(0.06f, 0.72f, 1.5f), yaw, false);
            if (Random.value < 0.5f) wm.Cyl("red_paint", L(p, yaw, Rnd(-0.5f, 0.5f), 0.77f, Rnd(-0.2f, 0.2f)), 0.05f, 0.13f, 8, false);
            Col(p, 0.82f, 0.8f, yaw, 0f, 0.8f);
        }

        void BalloonStand(Vector3 p, int count, float spread, float yMin, float yMax, float size)
        {
            wm.Box("metal_rust", new Vector3(p.x, 0.12f, p.z), new Vector3(0.4f, 0.24f, 0.4f));
            for (int i = 0; i < count; i++)
            {
                var q = p + new Vector3(Rnd(-spread, spread), Rnd(yMin, yMax), Rnd(-spread, spread));
                wm.Tube("black_matte", p + Vector3.up * 0.24f, q, 0.003f, 3, false);
                Ell(PickS("red_paint", "blue_paint", "yellow_paint", "green_paint", "red_paint"), q + Vector3.up * 0.2f, new Vector3(size, size * 1.2f, size), Quaternion.identity);
            }
            Col(p, 0.22f, 0.22f, 0f, 0f, 0.6f);
        }

        // ================================================================== carousel hall centrepieces
        void CarouselHallFeatures()
        {
            var r = R(RoomId.CarouselHall);
            var tries = new (float lx, float lz, float w, float d, PropFn fn, float yaw)[]
            {
                (16f, 8f, 1.3f, 1.0f, Popcorn, 20f), (44f, 30f, 1.3f, 1.0f, Popcorn, -140f), (22f, 33f, 1.0f, 1.0f, Striker, 0f),
                (48f, 18f, 3.4f, 2.0f, RingToss, -90f), (9f, 22f, 3.4f, 2.0f, RingToss, 90f),
                (35f, 10f, 0.8f, 0.8f, (p, y) => BalloonStand(p, 9, 0.6f, 2.6f, 3.6f, 0.3f), 0f), (12f, 30f, 0.8f, 0.8f, (p, y) => BalloonStand(p, 9, 0.6f, 2.6f, 3.6f, 0.3f), 0f),
                (50f, 40f, 2.6f, 2.6f, DuckPond, 0f), (6f, 8f, 2.6f, 2.6f, DuckPond, 0f), (41f, 41f, 1.7f, 1.0f, ClownCutout, -150f),
                (28f, 6f, 1.4f, 1.4f, KiddieRide, 30f), (38f, 37f, 1.4f, 1.4f, KiddieRide, 200f),
            };
            int placed = 0;
            foreach (var t in tries)
                for (int k = 0; k < 6; k++)
                    if (Feature(r, t.lx + (k > 0 ? Rnd(-3f, 3f) : 0f), t.lz + (k > 0 ? Rnd(-3f, 3f) : 0f), t.w, t.d, t.fn, t.yaw)) { placed++; break; }
            int lamps = 0;
            foreach (var lp in new[] { new Vector2(18f, 12f), new Vector2(38f, 12f), new Vector2(18f, 36f), new Vector2(38f, 36f), new Vector2(10f, 16f), new Vector2(46f, 26f) })
                for (int k = 0; k < 5; k++)
                    if (Feature(r, lp.x + (k > 0 ? Rnd(-2f, 2f) : 0f), lp.y + (k > 0 ? Rnd(-2f, 2f) : 0f), 0.5f, 0.5f, LampPost, 0f)) { lamps++; break; }
            dressLog["features:CarouselHall"] = placed + "/" + tries.Length + " lamps " + lamps + "/6";
            StringLights(r.P(2f, 2f, 7f), r.P(54f, 2f, 7f), 30, 1.4f);
            StringLights(r.P(2f, 44f, 7f), r.P(54f, 44f, 7f), 30, 1.4f);
        }

        void Popcorn(Vector3 p, float yaw)
        {
            wm.Box("red_paint", new Vector3(p.x, 0.55f, p.z), new Vector3(1.1f, 0.7f, 0.7f), yaw);
            wm.Box("glass", L(p, yaw, 0f, 1.25f, 0f), new Vector3(1.0f, 0.7f, 0.6f), yaw, false);
            for (int i = 0; i < 14; i++)
                wm.Box("white_paint", L(p, yaw, Rnd(-0.4f, 0.4f), 0.95f + Rnd(0f, 0.15f), Rnd(-0.2f, 0.2f)), Vector3.one * 0.05f, Rnd(0f, 170f), false);
            wm.Box("tent", L(p, yaw, 0f, 1.75f, 0f), new Vector3(1.3f, 0.12f, 0.9f), yaw);
            foreach (float sx in new[] { -0.45f, 0.45f }) wm.Tube("black_matte", L(p, yaw, sx - 0.04f, 0.15f, 0.36f), L(p, yaw, sx + 0.04f, 0.15f, 0.36f), 0.15f, 10);
            wm.Box("neon_yellow", L(p, yaw, 0f, 2.0f, 0f), new Vector3(0.9f, 0.18f, 0.04f), yaw, false);
            Col(p, 0.6f, 0.4f, yaw, 0f, 1.8f);
        }

        /// <summary>High striker: pole with a bell, light strip, puck and a sledgehammer lying in front.</summary>
        void Striker(Vector3 p, float yaw)
        {
            wm.Box("wood_painted", new Vector3(p.x, 0.15f, p.z), new Vector3(1f, 0.3f, 1f), yaw);
            wm.Cyl("red_paint", p + Vector3.up * 0.3f, 0.09f, 4.2f, 8);
            wm.Cyl("gold_trim", p + Vector3.up * 4.5f, 0.2f, 0.18f, 10, true, true, 0.05f);
            for (int i = 0; i < 8; i++) wm.Box(i % 2 == 1 ? "neon_yellow" : "neon_red", L(p, yaw, 0f, 0.8f + i * 0.45f, 0.1f), new Vector3(0.24f, 0.08f, 0.03f), yaw, false);
            wm.Box("red_paint", L(p, yaw, 0f, 0.32f, 0.25f), new Vector3(0.4f, 0.06f, 0.3f), yaw, false);
            var hm = L(p, yaw, 0.55f, 0.05f, 0.4f);
            wm.Tube("wood", hm, L(p, yaw, 0.7f, 0.1f, 1.3f), 0.03f, 4);
            wm.Box("metal_rust", new Vector3(hm.x, 0.1f, hm.z), new Vector3(0.35f, 0.18f, 0.18f), yaw + 17f);
            Col(p, 0.5f, 0.5f, yaw, 0f, 1.2f);
        }

        void RingToss(Vector3 p, float yaw)
        {
            wm.Box("wood_painted", L(p, yaw, 0f, 1.4f, -0.7f), new Vector3(3f, 2.8f, 0.1f), yaw);
            wm.Box("red_paint", L(p, yaw, 0f, 0.5f, 0.6f), new Vector3(3f, 1f, 0.3f), yaw);
            wm.Box("wood", L(p, yaw, 0f, 0.45f, -0.1f), new Vector3(2.6f, 0.9f, 1.0f), yaw);
            for (int i = 0; i < 18; i++)
            {
                if (Random.value < 0.15f) continue;
                wm.Cyl(PickS("green_paint", "glass", "green_paint"), L(p, yaw, -1.1f + (i % 6) * 0.44f, 0.9f, -0.45f + (i / 6) * 0.35f), 0.06f, 0.3f, 6, false, true, 0.03f);
            }
            wm.Box("tent", L(p, yaw, 0f, 2.9f, 0f), new Vector3(3.3f, 0.1f, 1.9f), yaw);
            wm.Box("neon_cyan", L(p, yaw, 0f, 2.6f, 0.95f), new Vector3(2.4f, 0.12f, 0.04f), yaw, false);
            foreach (float sx in new[] { -1.5f, 1.5f }) wm.Box("gold_trim", L(p, yaw, sx, 1.4f, 0.75f), new Vector3(0.08f, 2.8f, 0.08f), yaw, false);
            Col(p, 1.55f, 0.85f, yaw, 0f, 2.8f);
        }

        void DuckPond(Vector3 p, float yaw)
        {
            wm.Cyl("blue_paint", p, 1.2f, 0.55f, 18);
            wm.Cyl("glass", p + Vector3.up * 0.5f, 1.12f, 0.04f, 18, false);
            for (int i = 0; i < 7; i++)
            {
                float a = Rnd(0f, Mathf.PI * 2f), rr = Rnd(0.3f, 0.9f);
                Ell("yellow_paint", new Vector3(p.x + Mathf.Cos(a) * rr, 0.6f, p.z + Mathf.Sin(a) * rr), new Vector3(0.12f, 0.1f, 0.16f), Yaw(Rnd(0f, 360f)), true);
            }
            Decal("decal_water", p + new Vector3(1.3f, 0f, 0f), 1.4f, Rnd(0f, 360f));
            Col(p, 1.15f, 1.15f, 0f, 0f, 0.6f);
        }

        /// <summary>Clown photo cut-out board on a strut.</summary>
        void ClownCutout(Vector3 p, float yaw)
        {
            var fwd = Yaw(yaw) * Vector3.forward;
            var q = L(p, yaw, 0f, 1.2f, 0f);
            wm.QuadUV("poster_3", q + fwd * 0.03f, fwd, Vector3.up, 1.5f, 2.2f);
            wm.Box("wood", q, new Vector3(1.6f, 2.3f, 0.05f), yaw, false);
            wm.Box("wood", L(p, yaw, 0f, 0.5f, -0.4f), new Vector3(0.05f, 1f, 0.8f), yaw, false);
            Col(L(p, yaw, 0f, 0f, -0.35f), 0.8f, 0.45f, yaw, 0f, 2.3f);
        }

        void KiddieRide(Vector3 p, float yaw)
        {
            wm.Box("black_matte", new Vector3(p.x, 0.12f, p.z), new Vector3(1.3f, 0.24f, 1.3f), yaw);
            wm.Box(PickS("red_paint", "blue_paint"), L(p, yaw, 0f, 0.55f, 0f), new Vector3(0.7f, 0.5f, 1.1f), yaw + 11.5f);
            var hd = L(p, yaw, 0f, 0.95f, 0.45f);
            Ell("plastic", hd, Vector3.one * 0.3f, Quaternion.Euler(23f, yaw, 29f));
            Ell("eye_dim", hd + Yaw(yaw) * new Vector3(0f, 0.05f, 0.13f), Vector3.one * 0.06f, Quaternion.identity, true);
            Col(p, 0.65f, 0.65f, yaw, 0f, 1.2f);
        }

        /// <summary>Lamp post with a dim, warm (often flickering) light.</summary>
        void LampPost(Vector3 p, float yaw)
        {
            wm.Cyl("black_matte", p, 0.07f, 3.6f, 8);
            wm.Cyl("black_matte", p, 0.2f, 0.15f, 10);
            Lamp(p + Vector3.up * 3.7f, 0.22f, p + Vector3.up * 3.5f, Hex(0xffb366), 1.0f, 9f, Random.value < 0.6f);
            Col(p, 0.12f, 0.12f, 0f, 0f, 3f);
        }

        // ================================================================== entrance hall
        void EntranceHallExtras()
        {
            var r = R(RoomId.EntranceHall);
            foreach (var lp in new[] { new Vector2(8f, 13f), new Vector2(28f, 13f) })
            {
                var p = r.P(lp.x, lp.y);
                if (!Fits(p, 0f, 0.5f, 0.5f) || !Claim(p, 0.4f)) continue;
                wm.Cyl("black_matte", p, 0.07f, 3.4f, 8);
                Lamp(p + Vector3.up * 3.5f, 0.2f, p + Vector3.up * 3.3f, Hex(0xffa64d), 0.8f, 8f, true);
                Col(p, 0.12f, 0.12f, 0f, 0f, 3f);
            }
            // info board with a faded fair map / poster
            var ib = r.P(25f, 17.6f);
            if (Fits(ib, 180f, 1.8f, 0.4f) && Claim(ib, 1f))
            {
                wm.Box("black_matte", new Vector3(ib.x, 1.3f, ib.z), new Vector3(1.7f, 1.2f, 0.08f), 180f);
                wm.QuadUV("poster_2", new Vector3(ib.x, 1.3f, ib.z - 0.05f), Vector3.back, Vector3.up, 1.5f, 1.05f);
                foreach (float sx in new[] { -0.8f, 0.8f }) wm.Cyl("black_matte", new Vector3(ib.x + sx, 0f, ib.z), 0.04f, 1.9f, 6);
                Col(ib, 0.85f, 0.08f, 0f, 0f, 1.9f);
            }
            Physics.SyncTransforms();
        }
    }
}
