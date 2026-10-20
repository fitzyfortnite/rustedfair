using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    /// <summary>Room-by-room dressing and gameplay anchors.</summary>
    public partial class FairBuilder
    {
        public Carousel carousel;
        public FortuneTeller fortuneTeller;
        public ClockAutomaton clock;
        public RollingGate exitGate;
        public SafeBox safe;
        public Generator generator;
        public readonly List<Transform> pods = new List<Transform>();

        void BuildAllProps()
        {
            EntranceHall();
            SecurityOffice();
            BroadcastBooth();
            FoodCourt();
            Kitchen();
            ColdRoom();
            MirrorMaze();
            CarouselHall();
            Arcade();
            BumperCars();
            BumperOffice();
            GhostTrain();
            Warehouse();
            WarehouseOffice();
            ClockGallery();
            MaintenanceRoom();
            ServiceHall();
            StaffArea();
            ExtraDressing(); // v1.2: wall clutter, open-floor props, hall centrepieces (FairDressing.cs)
            Outside();
        }

        // ------------------------------------------------------------------ Entrance hall (36 x 22, h9)
        void EntranceHall()
        {
            var r = R(RoomId.EntranceHall);
            // the main gate (south wall, world x 58..66)
            var gateCenter = new Vector3(62f, 0f, 0.05f);
            exitGate = RollingGate.Create(dynRoot, gateCenter, 8f, ArchMaxH);
            Anchor("gate_reader", new Vector3(67.2f, 1.3f, 0.12f), 0f);
            // reader housing
            Deco("black_matte", new Vector3(67.2f, 1.3f, 0.08f), new Vector3(0.35f, 0.5f, 0.12f), 0, true);
            // turnstile line
            for (float x = 50f; x < 74f; x += 2.4f)
            {
                if (x > 59f && x < 65f) continue;
                Solid("chrome", new Vector3(x, 0.5f, 7f), new Vector3(0.25f, 1f, 1.2f), 0, false);
                Deco("chrome", new Vector3(x + 0.6f, 0.9f, 7f), new Vector3(1f, 0.05f, 0.05f));
                AddColliderRot(new Vector3(x + 0.6f, 0.55f, 7f), new Vector3(1f, 1.0f, 0.1f), 0); // the arm was walk-through
            }
            // ticket booths
            TicketBooth(r.P(7f, 4f), 0f, "eh_booth_a");
            TicketBooth(r.P(29f, 4f), 0f, "eh_booth_b");
            // info desk + start point
            Solid("wood_painted", r.P(18f, 17.5f, 0.55f), new Vector3(4.5f, 1.1f, 1f), 0);
            // v1.7: the thin cyan neon bar floating in front of the counter was removed (read as a stray stick)
            AddSpot("misc", r.P(16.1f, 17.5f, 1.15f), 0); // clear of the guaranteed water/oil at 17.2 / 18.8
            Anchor("player_start", r.P(18f, 13.2f), 180f);
            Anchor("respawn", r.P(18f, 13.2f), 180f);
            Bench(r.P(14f, 12f), 90f);
            Bench(r.P(22f, 12f), -90f);
            // planters with dead plants
            foreach (var p in new[] { r.P(3f, 18f), r.P(33f, 18f), r.P(3f, 8f), r.P(33f, 8f) })
            {
                Solid("wall_plaster", p + Vector3.up * 0.4f, new Vector3(1.4f, 0.8f, 1.4f), 0, true);
                for (int i = 0; i < 5; i++)
                    wm.Tube("wood", p + Vector3.up * 0.8f, p + new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(1.4f, 2.4f), Random.Range(-0.6f, 0.6f)), 0.025f, 4, false);
            }
            // giant broken neon logo above the arch
            wm.For("neon_logo", r.P(18f, 21.7f), false).QuadUV(r.P(18f, 21.75f, 6.6f), Vector3.back, Vector3.up, 12f, 3f, new Rect(0, 0, 1, 1));
            lights.Add(r.P(18f, 20.5f, 6.6f), staticRoot, new Color(1f, 0.3f, 0.15f), 2.2f, 10f, LightGroup.Main, true);
            Poster(r.P(0.02f, 5f, 2.2f), Vector3.right, 1, 1.2f);
            Poster(r.P(35.98f, 10f, 2.2f), Vector3.left, 2, 1.2f); // z=5 was on the security door (world z 4..6)
            Poster(r.P(10f, 0.02f, 2.6f), Vector3.forward, 1, 1f);
            Mannequin(r.P(32f, 14f), -120f);
            // lost & found shelf
            Shelf(r.P(33.5f, 2.2f), 90f, 3.2f);
            AddSpot("misc", r.P(33.28f, 2.2f, 0.86f), 90f); // 2nd shelf board, front lip (outside the shelf collider)
            ScatterDecals(r, 2, 6);
            Debris(r, 18);
        }

        void TicketBooth(Vector3 p, float yaw, string key)
        {
            // a small booth with a window and a drawer under the counter
            var q = Yaw(yaw);
            Solid("wood_painted", p + q * new Vector3(0, 1.3f, -1f), new Vector3(2.4f, 2.6f, 0.1f), yaw, false);
            Solid("wood_painted", p + q * new Vector3(-1.2f, 1.3f, 0), new Vector3(0.1f, 2.6f, 2f), yaw, false);
            Solid("wood_painted", p + q * new Vector3(1.2f, 1.3f, 0), new Vector3(0.1f, 2.6f, 2f), yaw, false);
            Solid("wood_painted", p + q * new Vector3(0, 0.55f, 1f), new Vector3(2.4f, 1.1f, 0.1f), yaw, false);
            Deco("glass", p + q * new Vector3(0, 1.75f, 1f), new Vector3(2.3f, 1.3f, 0.02f), yaw);
            AddColliderRot(p + q * new Vector3(0, 1.75f, 1f), new Vector3(2.3f, 1.3f, 0.05f), yaw);
            Deco("wood_painted", p + q * new Vector3(0, 2.65f, 0), new Vector3(2.5f, 0.1f, 2.1f), yaw, true);
            Deco("neon_red", p + q * new Vector3(0, 2.5f, 1.06f), new Vector3(1.8f, 0.1f, 0.04f), yaw);
            map.BlockWorld(p, new Vector3(2.4f, 1, 2f), yaw);
            // counter + drawer facing the hall (outside the glass)
            Solid("wood", p + q * new Vector3(0, 1.1f, 1.25f), new Vector3(2.4f, 0.06f, 0.45f), yaw, false);
            var drawerRoot = new GameObject("Drawer_" + key).transform;
            drawerRoot.SetParent(dynRoot, false);
            drawerRoot.position = p + q * new Vector3(0.5f, 0.85f, 1.12f);
            drawerRoot.rotation = q;
            var slide = new GameObject("Slide").transform;
            slide.SetParent(drawerRoot, false);
            RF.Prim(PrimitiveType.Cube, slide, "wood", new Vector3(0, 0, 0.01f), new Vector3(0.7f, 0.2f, 0.42f));
            RF.Prim(PrimitiveType.Cube, slide, "chrome", new Vector3(0, 0, 0.23f), new Vector3(0.18f, 0.04f, 0.04f), default, false);
            var drawer = drawerRoot.gameObject.AddComponent<Drawer>();
            drawer.slide = slide;
            drawer.openOffset = new Vector3(0, 0, 0.32f);
            drawer.AddHitBox(new Vector3(0, 0, 0.1f), new Vector3(0.75f, 0.3f, 0.5f));
            anchors["drawer_" + key] = slide;
            drawers[key] = drawer;
        }

        public readonly Dictionary<string, Drawer> drawers = new Dictionary<string, Drawer>();

        // ------------------------------------------------------------------ Security office (16 x 14)
        void SecurityOffice()
        {
            var r = R(RoomId.Security);
            // desk with CCTV monitors facing the north wall
            Solid("wood", r.P(8f, 11.5f, 0.75f), new Vector3(5f, 0.06f, 1.1f), 0, false);
            Solid("metal_rust", r.P(8f, 11.5f, 0.37f), new Vector3(4.8f, 0.74f, 1f), 0, true);
            for (int i = 0; i < 4; i++)
            {
                var mp = r.P(6.2f + i * 1.2f, 12.1f, 1.15f);
                Deco("black_matte", mp, new Vector3(1f, 0.75f, 0.55f), 0, true);
                wm.For(i % 2 == 0 ? "cctv" : "crt_static", mp, false).QuadUV(mp + Vector3.back * 0.28f, Vector3.back, Vector3.up, 0.86f, 0.6f, new Rect(0, 0, 1, 1));
            }
            for (int i = 0; i < 3; i++)
            {
                var mp = r.P(6.8f + i * 1.2f, 12.3f, 1.95f);
                Deco("black_matte", mp, new Vector3(1f, 0.75f, 0.55f), 0, true);
                wm.For("cctv", mp, false).QuadUV(mp + Vector3.back * 0.28f, Vector3.back, Vector3.up, 0.86f, 0.6f, new Rect(0, 0, 1, 1));
            }
            lights.Add(r.P(8f, 11f, 1.6f), staticRoot, new Color(0.5f, 0.9f, 0.6f), 0.8f, 4f, LightGroup.Main, true);
            Chair(r.P(8f, 10.2f), 0f);
            // safe in the north-east corner
            safe = SafeBox.Create(dynRoot, r.P(14.6f, 12.4f), -90f);
            // filing cabinets, gun cabinet, lockers
            for (int i = 0; i < 3; i++) Solid("wall_metal", r.P(0.5f, 9f + i * 0.85f, 0.7f), new Vector3(0.6f, 1.4f, 0.8f), 0, true);
            Lockers(r.P(10f, 0.45f), Vector3.right, 0f, 4, 2);
            Poster(r.P(4f, 13.98f, 1.8f), Vector3.back, 2, 0.8f);
            AddSpot("misc", r.P(2.5f, 11f, 0.05f), 0); // nothing to stand on here: floor
            Table(r.P(4.5f, 2.5f), false, true);
            AddSpot("misc", r.P(4.5f, 2.5f, 0.8f), 0);
            ScatterDecals(r, 1, 2);
            Debris(r, 8);
        }

        // ------------------------------------------------------------------ Broadcast booth (16 x 8)
        void BroadcastBooth()
        {
            var r = R(RoomId.Broadcast);
            Solid("wood", r.P(12.5f, 6.6f, 0.4f), new Vector3(5f, 0.8f, 1.2f), 0, true);
            Deco("black_matte", r.P(12.5f, 6.6f, 0.85f), new Vector3(2.2f, 0.1f, 0.8f), 0, true);
            for (int i = 0; i < 10; i++) Deco(i % 3 == 0 ? "neon_red" : "chrome", r.P(11.6f + i * 0.18f, 6.4f, 0.92f), new Vector3(0.05f, 0.05f, 0.05f));
            Anchor("pa_deck", r.P(14f, 6.3f, 0.95f), 180f);
            Deco("black_matte", r.P(14f, 6.6f, 1.0f), new Vector3(0.6f, 0.2f, 0.4f), 0, true);
            // microphone
            wm.Tube("chrome", r.P(12f, 6.0f, 0.9f), r.P(12f, 5.8f, 1.35f), 0.015f, 6, false);
            wm.Cyl("black_matte", r.P(12f, 5.75f, 1.35f), 0.05f, 0.12f, 8, false);
            // speakers + acoustic foam
            for (int i = 0; i < 2; i++) Solid("black_matte", r.P(9.5f + i * 6f, 7.4f, 1f), new Vector3(0.7f, 2f, 0.6f), 0, true);
            for (float x = 1f; x < 15f; x += 1.1f)
                for (float y = 1.4f; y < 3.2f; y += 0.6f)
                    Deco("black_matte", r.P(x, 7.96f, y), new Vector3(1f, 0.5f, 0.08f));
            // drawer that will hold note 5
            var drawerRoot = new GameObject("PA_Drawer").transform;
            drawerRoot.SetParent(dynRoot, false);
            drawerRoot.position = r.P(10.6f, 6.0f, 0.55f);
            drawerRoot.rotation = Yaw(180f);
            var slide = new GameObject("Slide").transform;
            slide.SetParent(drawerRoot, false);
            RF.Prim(PrimitiveType.Cube, slide, "wood", Vector3.zero, new Vector3(0.7f, 0.22f, 0.5f));
            RF.Prim(PrimitiveType.Cube, slide, "chrome", new Vector3(0, 0, 0.27f), new Vector3(0.18f, 0.04f, 0.04f), default, false);
            anchors["pa_drawer"] = drawerRoot;
            Lockers(r.P(1f, 7.5f), Vector3.right, 180f, 2, 1);
            Poster(r.P(0.02f, 6f, 1.8f), Vector3.right, 3, 0.8f); // z=3.5 was on the broadcast door (rel z 2..4)
            Debris(r, 6);
        }

        // ------------------------------------------------------------------ Food court (44 x 32, h7)
        void FoodCourt()
        {
            var r = R(RoomId.FoodCourt);
            Stall(r.P(7f, 2f), 0f, "neon_yellow", 6f, "fc_stall_burger");
            Stall(r.P(17f, 2f), 0f, "neon_red", 6f, "fc_stall_pizza");
            Stall(r.P(27f, 2f), 0f, "neon_pink", 6f, "fc_stall_candy");
            Stall(r.P(37f, 2f), 0f, "neon_cyan", 6f, "fc_stall_ice");
            Stall(r.P(42.5f, 20f), -90f, "neon_yellow", 5f, "fc_stall_drinks");
            // dining tables
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 3; j++)
                {
                    var p = r.P(8f + i * 8f + Random.Range(-0.6f, 0.6f), 11f + j * 6.5f + Random.Range(-0.6f, 0.6f));
                    Table(p, true, true, Random.value < 0.35f);
                    if (Random.value < 0.4f) AddSpot("misc", p + Vector3.up * 0.8f, Random.Range(0, 360f));
                    if (i == 1 && j == 2) AddSpot("fc_table", p + Vector3.up * 0.8f, 0);
                }
            // a coin-operated kiddie ride (cracked smiling rocket)
            var ride = r.P(38f, 28f);
            Solid("metal_rust", ride + Vector3.up * 0.2f, new Vector3(1.8f, 0.4f, 1.8f), 0, true);
            wm.Cyl("red_paint", ride + Vector3.up * 0.4f, 0.45f, 1.6f, 12, true, true, 0.1f);
            Deco("white_paint", ride + new Vector3(0, 1.1f, 0.42f), new Vector3(0.5f, 0.3f, 0.1f));
            AddColliderRot(ride + Vector3.up * 1.2f, new Vector3(0.9f, 1.6f, 0.9f), 0); // the rocket itself had no collider
            Mannequin(r.P(3f, 19f), 135f, "fur");
            Barrel(r.P(1f, 14f));
            Barrel(r.P(43f, 10f));
            foreach (var p in new[] { r.P(14f, 8f), r.P(30f, 8f), r.P(22f, 29f) })
            {
                wm.Cyl("green_paint", p, 0.3f, 1f, 10);
                AddColliderRot(p + Vector3.up * 0.5f, new Vector3(0.6f, 1f, 0.6f), 0);
            }
            // hide: storage cabinet behind the candy stall
            var cab = BuildCabinet(r.P(31.2f, 4.3f), 90f, "wood_painted");
            Poster(r.P(0.02f, 6f, 2.5f), Vector3.right, 1, 1.3f);
            Poster(r.P(22f, 31.98f, 2.5f), Vector3.back, 4, 1.3f);
            StringLights(r.P(2f, 30f, 5.5f), r.P(42f, 30f, 5.5f), 26, 1.2f);
            StringLights(r.P(2f, 9f, 5.5f), r.P(42f, 9f, 5.5f), 26, 1.2f);
            for (int i = 0; i < 6; i++) Decal("decal_water", map.RandomPointInRoom(r), Random.Range(0.8f, 1.8f), Random.Range(0, 360f));
            ScatterDecals(r, 3, 8);
            Debris(r, 30);
        }

        HideSpot BuildCabinet(Vector3 p, float yaw, string mat)
        {
            var q = Yaw(yaw);
            var root = new GameObject("CabinetVis").transform;
            root.SetParent(dynRoot, false);
            root.position = p; root.rotation = q;
            float w = 1.2f, h = 2.1f, d = 0.7f;
            HideSpot.AddSolid(root, mat, new Vector3(0, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.05f));
            HideSpot.AddSolid(root, mat, new Vector3(-w * 0.5f, h * 0.5f, 0), new Vector3(0.05f, h, d));
            HideSpot.AddSolid(root, mat, new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(0.05f, h, d));
            HideSpot.AddSolid(root, mat, new Vector3(0, h, 0), new Vector3(w, 0.05f, d));
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(-w * 0.5f, 0, d * 0.5f);
            var door = RF.Prim(PrimitiveType.Cube, hinge, mat, new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(w, h - 0.05f, 0.04f));
            RF.Prim(PrimitiveType.Cube, door.transform, "chrome", new Vector3(0.4f, 0, 0.8f), new Vector3(0.04f, 0.05f, 1f), default, false);
            var hs = HideSpot.Custom(dynRoot, "Cabinet", p, yaw, HideKind.Cabinet, new Vector3(0, 1.55f, 0), new Vector3(0, 0, 1f), new Vector3(w, h, d), hinge);
            root.SetParent(hs.transform, true);
            hs.occluders.AddRange(door.GetComponentsInChildren<Renderer>());
            map.BlockWorld(p, new Vector3(w, 1, d), yaw);
            return hs;
        }

        // ------------------------------------------------------------------ Kitchen (24 x 16)
        void Kitchen()
        {
            var r = R(RoomId.Kitchen);
            // counters along west and north walls
            Solid("wall_metal", r.P(0.45f, 9f, 0.45f), new Vector3(0.9f, 0.9f, 14f), 0, true);
            Solid("wall_metal", r.P(13f, 15.55f, 0.45f), new Vector3(20f, 0.9f, 0.9f), 0, true);
            Deco("chrome", r.P(0.45f, 9f, 0.92f), new Vector3(0.92f, 0.04f, 14f), 0, false);
            Deco("chrome", r.P(13f, 15.55f, 0.92f), new Vector3(20f, 0.04f, 0.92f), 0, false);
            AddSpot("ki_counter", r.P(0.5f, 6f, 0.98f), 90f);
            AddSpot("misc", r.P(10f, 15.5f, 0.98f), 180f);
            // stoves + hood
            for (int i = 0; i < 3; i++)
            {
                var sp = r.P(6f + i * 2.2f, 15.55f, 0.95f);
                for (int b = 0; b < 4; b++) Deco("black_matte", sp + new Vector3((b % 2) * 0.5f - 0.25f, 0.01f, (b / 2) * 0.4f - 0.2f), new Vector3(0.3f, 0.02f, 0.3f));
            }
            Deco("chrome", r.P(8.2f, 15.4f, 2.6f), new Vector3(7f, 0.5f, 1.2f), 0, true);
            // island
            Solid("chrome", r.P(12f, 7f, 0.45f), new Vector3(6f, 0.9f, 1.8f), 0, true);
            AddSpot("ki_island", r.P(12f, 7f, 0.95f), 0);
            for (int i = 0; i < 5; i++)
                wm.Cyl("chrome", r.P(10f + i, 7f, 2.3f), 0.18f, 0.2f, 10, false);
            for (int i = 0; i < 5; i++) wm.Tube("black_matte", r.P(10f + i, 7f, 2.5f), r.P(10f + i, 7f, 3.4f), 0.01f, 3, false);
            // shelves on east wall + fridge
            Shelf(r.P(22.9f, 9.5f), 90f, 4f, 2.2f);
            AddSpot("ki_shelf", r.P(22.68f, 9f, 1.56f), -90f); // front lip of the board, outside the shelf collider
            Solid("chrome", r.P(22.6f, 13f, 1.1f), new Vector3(1.2f, 2.2f, 1.4f), 0, true);
            BuildCabinet(r.P(18f, 0.45f), 0f, "wall_metal");
            for (int i = 0; i < 4; i++) Decal("decal_blood", map.RandomPointInRoom(r), Random.Range(0.4f, 1.1f), Random.Range(0, 360f));
            Debris(r, 12);
        }

        // ------------------------------------------------------------------ Cold room (12 x 12)
        void ColdRoom()
        {
            var r = R(RoomId.ColdRoom);
            Anchor("ice_block", r.P(6f, 5.5f), 0f);
            Shelf(r.P(6f, 11.5f), 0f, 8f, 2.4f);
            Shelf(r.P(11.5f, 6f), -90f, 8f, 2.4f);
            // hanging wrapped "sacks" swaying from hooks (very unsettling)
            for (int i = 0; i < 5; i++)
            {
                var hp = r.P(3f + i * 1.5f, 8.6f + (i % 2) * 0.6f, 3.1f);
                wm.Tube("chrome", hp, hp + Vector3.down * 0.5f, 0.012f, 3, false);
                RF.Prim(PrimitiveType.Capsule, staticRoot, "cloth_white", hp + Vector3.down * 1.35f, new Vector3(0.5f, 0.75f, 0.4f), new Vector3(Random.Range(-6, 6), Random.Range(0, 360), Random.Range(-6, 6)));
                AddColliderRot(new Vector3(hp.x, 1.75f, hp.z), new Vector3(0.48f, 1.7f, 0.4f), 0); // v1.2: y 0.9..2.6, was walk-through
            }
            for (int i = 0; i < 6; i++) Decal("decal_water", map.RandomPointInRoom(r), Random.Range(0.6f, 1.2f), Random.Range(0, 360f));
            lights.Add(r.P(6f, 6f, 2.8f), staticRoot, new Color(0.55f, 0.75f, 1f), 1.2f, 8f, LightGroup.Always, true);
        }

        // ------------------------------------------------------------------ Mirror maze
        void MirrorMaze()
        {
            var r = R(RoomId.MirrorMaze);
            // dead-end spots: the first gets a token candidate, then gear/misc
            for (int i = 0; i < mazeDeadEnds.Count; i++)
            {
                var p = FairMap.CellCenter(mazeDeadEnds[i].x, mazeDeadEnds[i].y);
                if (i == 0) AddSpot("mm_deadend_far", p + Vector3.up * 0.05f, 0);
                else if (i < 3) AddSpot("mm_deadend", p + Vector3.up * 0.05f, 0);
                else if (i < 5) AddSpot("misc", p + Vector3.up * 0.05f, 0);
            }
            // a purple carnival sign over the entrance
            Deco("neon_pink", new Vector3(43.8f, 2.9f, 9f), new Vector3(0.05f, 0.15f, 2.4f));
            for (int i = 0; i < 6; i++) Decal("decal_grime", map.RandomPointInRoom(r), Random.Range(1f, 2f), Random.Range(0, 360f));
        }

        // ------------------------------------------------------------------ Carousel hall (56 x 46, h12)
        void CarouselHall()
        {
            var r = R(RoomId.CarouselHall);
            Vector3 c = r.Center;
            carousel = Carousel.Create(dynRoot, c, 6.2f);
            map.BlockWorld(c, new Vector3(12.8f, 1, 12.8f));
            AddColliderRot(c + Vector3.up * 0.3f, new Vector3(12.4f, 0.6f, 12.4f), 0);
            AddCollider(c + Vector3.up * 2f, new Vector3(9f, 4f, 9f), RF.LayerProps);
            // control booth south of the carousel
            var bp = c + new Vector3(0, 0, -9.2f);
            Solid("wood_painted", bp + Vector3.up * 0.55f, new Vector3(2.2f, 1.1f, 1.2f), 0, true);
            Deco("tent", bp + Vector3.up * 2.4f, new Vector3(2.6f, 0.08f, 1.8f), 0, true);
            foreach (var o in new[] { new Vector3(-1.1f, 0, -0.6f), new Vector3(1.1f, 0, -0.6f), new Vector3(-1.1f, 0, 0.6f), new Vector3(1.1f, 0, 0.6f) })
                Deco("gold_trim", bp + o + Vector3.up * 1.2f, new Vector3(0.08f, 2.4f, 0.08f));
            Anchor("carousel_booth", bp + new Vector3(0, 1.15f, -0.5f), 180f);
            Anchor("carousel_reward", c + new Vector3(0, 0.55f, -6.6f), 180f);
            // string lights from the canopy to the walls
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var wallPt = c + dir * 20f;
                StringLights(c + dir * 6.4f + Vector3.up * 8.2f, new Vector3(Mathf.Clamp(wallPt.x, r.Min.x + 0.5f, r.Max.x - 0.5f), 9.5f, Mathf.Clamp(wallPt.z, r.Min.z + 0.5f, r.Max.z - 0.5f)), 18, 1.6f);
            }
            // benches ring
            for (int i = 0; i < 10; i++)
            {
                float a = (i * 36f + 18f);
                var p = c + Yaw(a) * Vector3.forward * 11.5f;
                if (Mathf.Abs(Mathf.DeltaAngle(a, 180f)) < 20f) continue; // keep the booth approach clear
                Bench(p, a + 180f);
                if (Random.value < 0.4f)
                {
                    // a balloon tied to the bench
                    var bpos = p + Vector3.up * 2.4f + new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.3f, 0.3f));
                    wm.Tube("black_matte", p + Vector3.up * 0.5f, bpos, 0.004f, 3, false);
                    RF.Prim(PrimitiveType.Sphere, staticRoot, "red_paint", bpos + Vector3.up * 0.2f, new Vector3(0.38f, 0.45f, 0.38f), default, false);
                }
            }
            // stage in the north-west corner
            var sp = r.P(5.2f, 40f);
            Solid("wood", sp + Vector3.up * 0.45f, new Vector3(8f, 0.9f, 8f), 0, true);
            Deco("tent", sp + new Vector3(0, 3.6f, 3.9f), new Vector3(8f, 6f, 0.1f), 0, true);
            Deco("red_paint", sp + new Vector3(-3.6f, 3.3f, 3.6f), new Vector3(0.8f, 5.4f, 0.3f), 0, true);
            Deco("red_paint", sp + new Vector3(3.6f, 3.3f, 3.6f), new Vector3(0.8f, 5.4f, 0.3f), 0, true);
            // v1.2: the backdrop and curtains were walk-through (you could step off the back of the stage through them)
            AddColliderRot(sp + new Vector3(0, 3.6f, 3.9f), new Vector3(8f, 6f, 0.24f), 0); // y 0.6..6.6: can't walk off the back
            AddColliderRot(sp + new Vector3(-3.6f, 3.3f, 3.6f), new Vector3(0.8f, 5.4f, 0.3f), 0);
            AddColliderRot(sp + new Vector3(3.6f, 3.3f, 3.6f), new Vector3(0.8f, 5.4f, 0.3f), 0);
            Deco("neon_yellow", sp + new Vector3(0, 6.2f, 3.7f), new Vector3(7f, 0.15f, 0.08f));
            AddSpot("ch_stage", sp + new Vector3(Random.Range(-2f, 2f), 0.95f, 0.8f), 180f); // keep clear of the mascot statues
            Mannequin(sp + new Vector3(-1.6f, 0.9f, 2f), 160f, "fur_dark");
            Mannequin(sp + new Vector3(1.8f, 0.9f, 2.2f), 200f, "plastic");
            lights.Add(sp + new Vector3(0, 5f, -3f), staticRoot, new Color(1f, 0.25f, 0.2f), 2f, 12f, LightGroup.Main, true);
            // stage steps
            Deco("wood", sp + new Vector3(0, 0.22f, -4.4f), new Vector3(3f, 0.44f, 0.8f), 0, true);
            AddColliderRot(sp + new Vector3(0, 0.22f, -4.4f), new Vector3(3f, 0.44f, 0.8f), 0);
            // prize booth (hide) east of the stage
            var pb = r.P(18.5f, 42.5f);
            var hs = BuildBooth(pb, 180f);
            // popcorn + balloon carts
            Cart(r.P(40f, 10f), 30f, "neon_yellow");
            Cart(r.P(14f, 14f), -20f, "neon_pink");
            Cart(r.P(46f, 36f), 120f, "neon_cyan");
            AddSpot("misc", r.P(40f, 10f, 1.22f), 0); // cart body top is 1.2
            AddSpot("misc", r.P(46f, 36f, 1.22f), 0);
            AddSpot("misc", r.P(30f, 40f, 0.05f), 0);
            Poster(r.P(0.02f, 30f, 2.8f), Vector3.right, 2, 1.5f);
            Poster(r.P(55.98f, 20f, 2.8f), Vector3.left, 1, 1.5f);
            Poster(r.P(30f, 45.98f, 2.8f), Vector3.back, 3, 1.5f);
            ScatterDecals(r, 4, 12);
            Debris(r, 40);
        }

        void Cart(Vector3 p, float yaw, string neon)
        {
            var q = Yaw(yaw);
            Solid("red_paint", p + Vector3.up * 0.6f, new Vector3(1.6f, 1.2f, 0.9f), yaw, true);
            Deco("glass", p + q * new Vector3(0, 1.55f, 0), new Vector3(1.4f, 0.7f, 0.8f), yaw, false);
            Deco("tent", p + q * new Vector3(0, 2.3f, 0), new Vector3(1.9f, 0.06f, 1.3f), yaw, true);
            Deco(neon, p + q * new Vector3(0, 1.22f, 0.46f), new Vector3(1.3f, 0.08f, 0.03f), yaw);
            wm.Cyl("rubber", p + q * new Vector3(-0.6f, 0, 0.5f), 0.22f, 0.08f, 10, false);
            for (int i = 0; i < 2; i++)
                Deco("gold_trim", p + q * new Vector3(i == 0 ? -0.75f : 0.75f, 1.75f, 0), new Vector3(0.04f, 1.1f, 0.04f), yaw);
        }

        HideSpot BuildBooth(Vector3 p, float yaw)
        {
            // a curtained booth (photo booth / prize booth)
            var q = Yaw(yaw);
            float w = 1.3f, d = 1.3f, h = 2.3f;
            Solid("blue_paint", p + q * new Vector3(0, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.08f), yaw, false);
            Solid("blue_paint", p + q * new Vector3(-w * 0.5f, h * 0.5f, 0), new Vector3(0.08f, h, d), yaw, false);
            Solid("blue_paint", p + q * new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(0.08f, h, d), yaw, false);
            Deco("blue_paint", p + q * new Vector3(0, h, 0), new Vector3(w + 0.1f, 0.08f, d + 0.1f), yaw, true);
            Deco("neon_pink", p + q * new Vector3(0, h - 0.15f, d * 0.5f + 0.02f), new Vector3(w * 0.8f, 0.1f, 0.03f), yaw);
            map.BlockWorld(p, new Vector3(w, 1, d), yaw);
            var hinge = new GameObject("Curtain").transform;
            hinge.position = p + q * new Vector3(-w * 0.5f, 0, d * 0.5f);
            hinge.rotation = q;
            var curtain = RF.Prim(PrimitiveType.Cube, hinge, "tent", new Vector3(w * 0.5f, h * 0.48f, 0), new Vector3(w - 0.1f, h * 0.88f, 0.03f));
            var hs = HideSpot.Custom(dynRoot, "Booth", p, yaw, HideKind.Booth, new Vector3(0, 1.5f, 0), new Vector3(0, 0, 1.2f), new Vector3(w, h, d), hinge);
            hs.doorOpenAngle = -80f;
            hs.occluders.Add(curtain.GetComponent<Renderer>());
            return hs;
        }

        // ------------------------------------------------------------------ Arcade (40 x 28, h5)
        void Arcade()
        {
            var r = R(RoomId.Arcade);
            int s = 0;
            for (int row = 0; row < 3; row++)
            {
                float z = 7f + row * 6f;
                for (float x = 6f; x < 28f; x += 1.0f)
                {
                    if (Random.value < 0.12f) { if (Random.value < 0.5f) AddSpot("misc", r.P(x, z + 0.8f, 0.05f), 0); continue; }
                    ArcadeCab(r.P(x, z - 0.45f), 0f, s++);
                    ArcadeCab(r.P(x, z + 0.45f), 180f, s++);
                }
                map.BlockWorld(r.P(17f, z), new Vector3(22f, 1, 1.8f));
            }
            // claw machines along the west part of the north wall
            for (int i = 0; i < 3; i++)
            {
                var cp = r.P(4f + i * 2f, 26.8f);
                Solid("red_paint", cp + Vector3.up * 0.5f, new Vector3(1.4f, 1f, 1.4f), 0, true);
                Deco("glass", cp + Vector3.up * 1.6f, new Vector3(1.3f, 1.2f, 1.3f));
                AddColliderRot(cp + Vector3.up * 1.6f, new Vector3(1.3f, 1.2f, 1.3f), 0);
                Deco("neon_cyan", cp + Vector3.up * 2.25f, new Vector3(1.4f, 0.1f, 1.4f));
                for (int k = 0; k < 5; k++)
                    RF.Prim(PrimitiveType.Sphere, staticRoot, k % 2 == 0 ? "fur" : "fur_dark", cp + new Vector3(Random.Range(-0.4f, 0.4f), 1.15f, Random.Range(-0.4f, 0.4f)), Vector3.one * 0.3f, default, false);
            }
            // prize counter (east side)
            Solid("wood_painted", r.P(36f, 12f, 0.55f), new Vector3(1f, 1.1f, 12f), 0, true);
            Deco("neon_pink", r.P(35.45f, 12f, 1.05f), new Vector3(0.04f, 0.08f, 11.8f));
            Shelf(r.P(39.4f, 12f), -90f, 10f, 2.6f, true);
            AddSpot("ar_counter", r.P(36f, Random.Range(8f, 16f), 1.15f), -90f);
            // drawer under the prize counter
            var drawerRoot = new GameObject("Drawer_ar").transform;
            drawerRoot.SetParent(dynRoot, false);
            drawerRoot.position = r.P(35.35f, 9f, 0.75f);
            drawerRoot.rotation = Yaw(-90f);
            var slide = new GameObject("Slide").transform;
            slide.SetParent(drawerRoot, false);
            RF.Prim(PrimitiveType.Cube, slide, "wood", Vector3.zero, new Vector3(0.8f, 0.22f, 0.5f));
            RF.Prim(PrimitiveType.Cube, slide, "chrome", new Vector3(0, 0, 0.27f), new Vector3(0.2f, 0.04f, 0.04f), default, false);
            var dr = drawerRoot.gameObject.AddComponent<Drawer>();
            dr.slide = slide;
            dr.openOffset = new Vector3(0, 0, 0.35f);
            dr.AddHitBox(new Vector3(0, 0, 0.1f), new Vector3(0.85f, 0.3f, 0.55f));
            anchors["drawer_ar"] = slide;
            drawers["ar"] = dr;
            // fortune teller (north-east)
            fortuneTeller = FortuneTeller.Create(dynRoot, r.P(34f, 25.6f), 180f);
            map.BlockWorld(r.P(34f, 25.6f), new Vector3(2.2f, 1, 2f));
            AddColliderRot(r.P(34f, 25.6f, 1.3f), new Vector3(2.1f, 2.6f, 1.6f), 180f);
            Anchor("fortune_slot", r.P(34.75f, 24.6f, 1.05f), 180f);
            Anchor("fortune_reward", r.P(33.4f, 24.55f, 0.85f), 180f);
            // photo booth (hide)
            BuildBooth(r.P(1.2f, 20f), 90f);
            // wall neon
            Deco("neon_pink", r.P(20f, 27.95f, 4.8f), new Vector3(30f, 0.08f, 0.05f)); // above the 4.6 m bumper-cars arch (was floating in the opening)
            Deco("neon_cyan", r.P(20f, 0.05f, 4.2f), new Vector3(30f, 0.08f, 0.05f));
            for (int i = 0; i < 3; i++)
                lights.Add(r.P(8f + i * 11f, 14f, 3.8f), staticRoot, i % 2 == 0 ? new Color(1f, 0.3f, 0.8f) : new Color(0.3f, 0.8f, 1f), 1.1f, 9f, LightGroup.Main, i == 1);
            Poster(r.P(0.02f, 16f, 2.2f), Vector3.right, 2, 1f); // z=6 hung inside the carousel arch (rel z 4..12)
            ScatterDecals(r, 2, 6);
            Debris(r, 25);
        }

        // ------------------------------------------------------------------ Bumper cars (36 x 32, h7)
        void BumperCars()
        {
            var r = R(RoomId.BumperCars);
            // rink fence with two gaps
            float x0 = 6f, x1 = 30f, z0 = 7f, z1 = 25f;
            for (float x = x0; x < x1; x += 2f)
            {
                if (Mathf.Abs(x - 18f) > 1.5f) FenceSeg(r.P(x + 1f, z0), 0f);
                FenceSeg(r.P(x + 1f, z1), 0f);
            }
            for (float z = z0; z < z1; z += 2f)
            {
                FenceSeg(r.P(x0, z + 1f), 90f);
                if (Mathf.Abs(z - 16f) > 1.5f) FenceSeg(r.P(x1, z + 1f), 90f);
            }
            // cars
            string[] carMats = { "red_paint", "blue_paint", "yellow_paint", "green_paint" };
            for (int i = 0; i < 9; i++)
            {
                var p = r.P(Random.Range(x0 + 2f, x1 - 2f), Random.Range(z0 + 2f, z1 - 2f));
                float yaw = Random.Range(0, 360f);
                var q = Yaw(yaw);
                Solid(carMats[i % 4], p + Vector3.up * 0.35f, new Vector3(1.4f, 0.55f, 2f), yaw, true);
                wm.Box("rubber", p + Vector3.up * 0.2f, new Vector3(1.6f, 0.25f, 2.2f), yaw, false);
                wm.Box("black_matte", p + q * new Vector3(0, 0.75f, -0.3f), new Vector3(1.1f, 0.4f, 0.12f), yaw, false);
                wm.Tube("metal_rust", p + q * new Vector3(0, 0.6f, -0.75f), p + q * new Vector3(0, 6.9f, -0.75f), 0.025f, 4, false);
                if (i == 0) AddSpot("bc_rink", p + q * new Vector3(0, 0.7f, 0.3f), yaw);
            }
            // ceiling electrical grid
            for (float x = x0; x <= x1; x += 3f)
                Deco("metal_rust", r.P(x, (z0 + z1) * 0.5f, 6.8f), new Vector3(0.05f, 0.05f, z1 - z0));
            // operator booth (south-east)
            var op = r.P(33f, 4f);
            Solid("wood_painted", op + new Vector3(0, 0.55f, 1.1f), new Vector3(2.4f, 1.1f, 0.1f), 0, false);
            Solid("wood_painted", op + new Vector3(-1.2f, 1.3f, 0), new Vector3(0.1f, 2.6f, 2.2f), 0, false);
            Solid("wood_painted", op + new Vector3(1.2f, 1.3f, 0), new Vector3(0.1f, 2.6f, 2.2f), 0, false);
            Deco("wood_painted", op + new Vector3(0, 2.65f, 0), new Vector3(2.5f, 0.1f, 2.3f), 0, true);
            Deco("neon_yellow", op + new Vector3(0, 2.55f, 1.17f), new Vector3(2f, 0.1f, 0.04f));
            Solid("wood", op + new Vector3(0, 0.85f, 0.2f), new Vector3(2f, 0.06f, 0.7f), 0, false);
            AddSpot("bc_booth", op + new Vector3(0.4f, 0.92f, 0.2f), 180f);
            Poster(r.P(0.02f, 20f, 2.5f), Vector3.right, 4, 1.2f);
            Poster(r.P(35.98f, 10f, 2.5f), Vector3.left, 1, 1.2f);
            StringLights(r.P(1f, 31f, 6f), r.P(35f, 31f, 6f), 24, 1f);
            AddSpot("misc", r.P(3f, 3f, 0.05f), 0);
            AddSpot("misc", r.P(18f, 29f, 0.05f), 0);
            lights.Add(r.P(18f, 16f, 6.2f), staticRoot, new Color(0.4f, 0.6f, 1f), 1.5f, 14f, LightGroup.Main, true);
            ScatterDecals(r, 2, 10);
            Debris(r, 22);
        }

        void FenceSeg(Vector3 p, float yaw)
        {
            var q = Yaw(yaw);
            wm.Box("yellow_paint", p + Vector3.up * 0.95f, new Vector3(2f, 0.1f, 0.1f), yaw, false);
            wm.Box("rubber", p + Vector3.up * 0.35f, new Vector3(2f, 0.5f, 0.2f), yaw, false);
            wm.Box("metal_rust", p + q * new Vector3(-0.95f, 0.5f, 0), new Vector3(0.08f, 1f, 0.08f), yaw, false);
            AddColliderRot(p + Vector3.up * 0.55f, new Vector3(2f, 1.1f, 0.2f), yaw);
        }

        // ------------------------------------------------------------------ Bumper office (12 x 16)
        void BumperOffice()
        {
            var r = R(RoomId.BumperOffice);
            Solid("wood", r.P(8f, 12f, 0.75f), new Vector3(2.4f, 0.06f, 1f), 0, false);
            Solid("wood", r.P(8f, 12f, 0.37f), new Vector3(2.3f, 0.74f, 0.9f), 0, true);
            Chair(r.P(8f, 11f), 0f);
            AddSpot("bo_desk", r.P(7.6f, 12f, 0.82f), 0);
            var tv = r.P(10.5f, 15.4f, 1.6f);
            Deco("black_matte", tv, new Vector3(0.9f, 0.7f, 0.5f), 0, true);
            // v1.2: the TV floated at chest height with no collider; it now stands on a metal cabinet
            Solid("metal_rust", new Vector3(tv.x, 0.62f, tv.z + 0.05f), new Vector3(0.8f, 1.24f, 0.5f), 0, false);
            AddColliderRot(tv, new Vector3(0.9f, 0.7f, 0.5f), 0);
            wm.For("crt_static", tv, false).QuadUV(tv + Vector3.back * 0.26f, Vector3.back, Vector3.up, 0.75f, 0.55f, new Rect(0, 0, 1, 1));
            for (int i = 0; i < 2; i++) Solid("wall_metal", r.P(0.5f, 12f + i * 0.85f, 0.7f), new Vector3(0.6f, 1.4f, 0.8f), 0, true);
            BuildCabinet(r.P(5f, 15.4f), 180f, "wood");
            AddSpot("misc", r.P(1.5f, 3f, 0.05f), 0);
            Poster(r.P(11.98f, 6f, 1.6f), Vector3.left, 3, 0.8f);
            Debris(r, 8);
        }

        // ------------------------------------------------------------------ Ghost train (40 x 50, h6)
        void GhostTrain()
        {
            var r = R(RoomId.GhostTrain);
            // station facade (on the z=19 cell edge, world z=40) facing south
            float fz = 40f - 0.2f;
            wm.For("poster_4", new Vector3(20, 3.6f, fz), false).QuadUV(new Vector3(20f, 3.8f, fz), Vector3.back, Vector3.up, 4f, 3.2f, new Rect(0, 0, 1, 0.6f));
            Deco("neon_red", new Vector3(35f, 3.2f, fz), new Vector3(3.2f, 0.12f, 0.05f));
            // giant skull arch over the tunnel gate (x=34..36)
            var sk = new Vector3(35f, 3.9f, fz - 0.3f);
            RF.Prim(PrimitiveType.Sphere, staticRoot, "bone", sk, new Vector3(2.4f, 2f, 1.2f));
            RF.Prim(PrimitiveType.Sphere, staticRoot, "eye_red", sk + new Vector3(-0.5f, 0.2f, -0.5f), Vector3.one * 0.45f, default, false);
            RF.Prim(PrimitiveType.Sphere, staticRoot, "eye_red", sk + new Vector3(0.5f, 0.2f, -0.5f), Vector3.one * 0.45f, default, false);
            for (int i = 0; i < 6; i++) RF.Prim(PrimitiveType.Cube, staticRoot, "teeth", sk + new Vector3(-0.6f + i * 0.24f, -0.75f, -0.48f), new Vector3(0.18f, 0.3f, 0.1f), default, false);
            // rails: station loop + through the four tunnel bands
            Rail(r.P(35f, 2f), r.P(35f, 16f));
            Rail(r.P(35f, 2f), r.P(5f, 2f));
            Rail(r.P(35f, 16f), r.P(35f, 22f));
            Rail(r.P(35f, 22f), r.P(3f, 22f)); Rail(r.P(3f, 22f), r.P(3f, 30f));
            Rail(r.P(3f, 30f), r.P(37f, 30f)); Rail(r.P(37f, 30f), r.P(37f, 38f));
            Rail(r.P(37f, 38f), r.P(3f, 38f)); Rail(r.P(3f, 38f), r.P(3f, 46f));
            Rail(r.P(3f, 46f), r.P(32f, 46f));
            // carts at the station
            for (int i = 0; i < 2; i++) GhostCart(r.P(8f + i * 3.2f, 2f), 90f);
            // control booth with the ride panel (near the gate)
            var cb = r.P(30f, 14f);
            Solid("black_matte", cb + new Vector3(0, 1.3f, 1.2f), new Vector3(3f, 2.6f, 0.1f), 0, false);
            Solid("black_matte", cb + new Vector3(-1.5f, 1.3f, 0), new Vector3(0.1f, 2.6f, 2.4f), 0, false);
            Solid("black_matte", cb + new Vector3(0, 0.5f, -0.4f), new Vector3(2.6f, 1f, 0.6f), 0, true);
            Deco("green_paint", cb + new Vector3(0, 1.02f, -0.4f), new Vector3(2.6f, 0.04f, 0.6f));
            for (int i = 0; i < 6; i++) Deco(i % 2 == 0 ? "neon_red" : "neon_yellow", cb + new Vector3(-0.8f + i * 0.3f, 1.06f, -0.5f), new Vector3(0.08f, 0.04f, 0.08f));
            Anchor("ride_panel", cb + new Vector3(0.6f, 1.08f, -0.55f), 180f);
            AddSpot("gt_booth", cb + new Vector3(-0.7f, 1.08f, -0.35f), 180f);
            AddSpot("gt_station", r.P(6f, 6f, 0.05f), 0);
            // standing coffins (one is a hiding spot)
            Coffin(r.P(2f, 10f), 90f, true);
            Coffin(r.P(2f, 13f), 90f, false);
            Coffin(r.P(38.5f, 30f), -90f, true);
            // tunnel dressing
            for (int band = 0; band < 4; band++)
            {
                float z = 21f + band * 8f + 1f;
                for (int i = 0; i < 4; i++)
                {
                    var p = r.P(6f + i * 9f + Random.Range(-1f, 1f), z + Random.Range(-0.5f, 2.5f));
                    if (Random.value < 0.5f) Skeleton(p + Vector3.up * 3.6f, Random.Range(0, 360f));
                    else Ghost(p + Vector3.up * 2.78f); // v1.2: raised, the player's head passed through them
                }
                Cobweb(r.P(0, z - 1f, 5.9f), Vector3.right, Vector3.forward);
            }
            AddSpot("gt_tunnel", r.P(20f, 30f, 0.05f), 0);
            AddSpot("gt_tunnel", r.P(10f, 37f, 0.05f), 0);
            // scare triggers inside the tunnel
            GhostScare.Create(dynRoot, r.P(18f, 22f), new Vector3(4f, 3f, 3.5f), GhostScare.Kind.Skeleton);
            GhostScare.Create(dynRoot, r.P(26f, 30f), new Vector3(4f, 3f, 3.5f), GhostScare.Kind.Shriek);
            GhostScare.Create(dynRoot, r.P(14f, 38f), new Vector3(4f, 3f, 3.5f), GhostScare.Kind.Skeleton);
            // crypt at the end of the last band
            var crypt = r.P(35f, 46f);
            Solid("wall_brick", crypt + new Vector3(0, 0.45f, 1.6f), new Vector3(3f, 0.9f, 0.6f), 0, true);
            Deco("gold_trim", crypt + new Vector3(0, 0.92f, 1.6f), new Vector3(3f, 0.04f, 0.6f));
            Anchor("crypt", crypt, 180f);
            for (int i = 0; i < 6; i++)
                wm.Cyl("white_paint", crypt + new Vector3(-1.3f + i * 0.5f, 0.9f, 1.75f), 0.05f, 0.25f, 6, false);
            lights.Add(crypt + new Vector3(0, 1.5f, 1.2f), staticRoot, new Color(1f, 0.55f, 0.2f), 0.9f, 5f, LightGroup.Always, true);
            ScatterDecals(r, 5, 8);
            Debris(r, 20);
        }

        void Rail(Vector3 a, Vector3 b)
        {
            Vector3 dir = (b - a).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.45f;
            wm.Tube("chrome", a + side + Vector3.up * 0.06f, b + side + Vector3.up * 0.06f, 0.035f, 5, false);
            wm.Tube("chrome", a - side + Vector3.up * 0.06f, b - side + Vector3.up * 0.06f, 0.035f, 5, false);
            float len = (b - a).magnitude;
            for (float t = 0; t < len; t += 0.8f)
                wm.Box("wood", a + dir * t + Vector3.up * 0.02f, new Vector3(1.2f, 0.04f, 0.18f), Quaternion.LookRotation(dir).eulerAngles.y, false);
        }

        void GhostCart(Vector3 p, float yaw)
        {
            var q = Yaw(yaw);
            Solid("black_matte", p + Vector3.up * 0.55f, new Vector3(1.2f, 0.9f, 2f), yaw, true);
            Deco("red_paint", p + q * new Vector3(0, 0.75f, 0.2f), new Vector3(1.0f, 0.1f, 1.2f), yaw);
            var head = RF.Prim(PrimitiveType.Sphere, staticRoot, "bone", p + q * new Vector3(0, 1.1f, 1.05f), new Vector3(0.8f, 0.7f, 0.6f), new Vector3(0, yaw, 0));
            RF.Prim(PrimitiveType.Sphere, head.transform, "black_matte", new Vector3(-0.22f, 0.1f, 0.38f), Vector3.one * 0.3f, default, false);
            RF.Prim(PrimitiveType.Sphere, head.transform, "black_matte", new Vector3(0.22f, 0.1f, 0.38f), Vector3.one * 0.3f, default, false);
        }

        void Coffin(Vector3 p, float yaw, bool hideable)
        {
            var q = Yaw(yaw);
            float w = 0.9f, h = 2.1f, d = 0.6f;
            if (!hideable)
            {
                Solid("wood", p + Vector3.up * h * 0.5f, new Vector3(w, h, d), yaw, true);
                Deco("gold_trim", p + q * new Vector3(0, 1.4f, d * 0.5f + 0.01f), new Vector3(0.1f, 0.5f, 0.02f), yaw);
                Deco("gold_trim", p + q * new Vector3(0, 1.5f, d * 0.5f + 0.01f), new Vector3(0.35f, 0.08f, 0.02f), yaw);
                return;
            }
            var root = new GameObject("CoffinVis").transform;
            root.SetParent(dynRoot, false);
            root.position = p; root.rotation = q;
            HideSpot.AddSolid(root, "wood", new Vector3(0, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.05f));
            HideSpot.AddSolid(root, "wood", new Vector3(-w * 0.5f, h * 0.5f, 0), new Vector3(0.05f, h, d));
            HideSpot.AddSolid(root, "wood", new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(0.05f, h, d));
            HideSpot.AddSolid(root, "wood", new Vector3(0, h, 0), new Vector3(w, 0.05f, d));
            var hinge = new GameObject("Lid").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(-w * 0.5f, 0, d * 0.5f);
            var lid = RF.Prim(PrimitiveType.Cube, hinge, "wood", new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(w, h, 0.05f));
            RF.Prim(PrimitiveType.Cube, lid.transform, "gold_trim", new Vector3(0, 0.15f, 0.6f), new Vector3(0.1f, 0.25f, 0.4f), default, false);
            RF.Prim(PrimitiveType.Cube, lid.transform, "gold_trim", new Vector3(0, 0.2f, 0.6f), new Vector3(0.4f, 0.04f, 0.4f), default, false);
            var hs = HideSpot.Custom(dynRoot, "Coffin", p, yaw, HideKind.Coffin, new Vector3(0, 1.55f, 0), new Vector3(0, 0, 1f), new Vector3(w, h, d), hinge);
            root.SetParent(hs.transform, true);
            hs.occluders.AddRange(lid.GetComponentsInChildren<Renderer>());
            map.BlockWorld(p, new Vector3(w, 1, d), yaw);
        }

        void Skeleton(Vector3 top, float yaw)
        {
            var root = new GameObject("Skeleton").transform;
            root.SetParent(staticRoot, false);
            root.position = top;
            root.rotation = Yaw(yaw);
            wm.Tube("black_matte", top, top + Vector3.up * 2.4f, 0.006f, 3, false);
            RF.Prim(PrimitiveType.Sphere, root, "bone", new Vector3(0, -0.15f, 0), new Vector3(0.24f, 0.28f, 0.26f), new Vector3(25, 0, 10));
            RF.Prim(PrimitiveType.Capsule, root, "bone", new Vector3(0, -0.6f, 0), new Vector3(0.22f, 0.32f, 0.12f), default, false);
            for (int i = 0; i < 4; i++) RF.Prim(PrimitiveType.Cube, root, "bone", new Vector3(0, -0.45f - i * 0.08f, 0.02f), new Vector3(0.34f, 0.025f, 0.15f), default, false);
            RF.Prim(PrimitiveType.Capsule, root, "bone", new Vector3(-0.22f, -0.7f, 0), new Vector3(0.05f, 0.35f, 0.05f), new Vector3(0, 0, 12), false);
            RF.Prim(PrimitiveType.Capsule, root, "bone", new Vector3(0.22f, -0.7f, 0), new Vector3(0.05f, 0.35f, 0.05f), new Vector3(0, 0, -12), false);
            RF.Prim(PrimitiveType.Capsule, root, "bone", new Vector3(-0.09f, -1.25f, 0), new Vector3(0.06f, 0.4f, 0.06f), default, false);
            RF.Prim(PrimitiveType.Capsule, root, "bone", new Vector3(0.09f, -1.25f, 0), new Vector3(0.06f, 0.4f, 0.06f), default, false);
        }

        void Ghost(Vector3 p)
        {
            // cloth ghost hanging from a wire: centre y 2.78, 0.62 x 1.6 x 0.45 -> bottom hem at ~1.98 m, above the player's head
            var g = RF.Prim(PrimitiveType.Sphere, staticRoot, "cloth_white", p, new Vector3(0.62f, 1.6f, 0.45f), new Vector3(0, Random.Range(0, 360), Random.Range(-6, 6)), false);
            RF.Prim(PrimitiveType.Sphere, g.transform, "black_matte", new Vector3(-0.18f, 0.2f, 0.42f), new Vector3(0.18f, 0.07f, 0.1f), default, false);
            RF.Prim(PrimitiveType.Sphere, g.transform, "black_matte", new Vector3(0.18f, 0.2f, 0.42f), new Vector3(0.18f, 0.07f, 0.1f), default, false);
            RF.Prim(PrimitiveType.Sphere, g.transform, "black_matte", new Vector3(0f, 0.08f, 0.46f), new Vector3(0.12f, 0.09f, 0.08f), default, false);
            wm.Tube("black_matte", p + Vector3.up * 0.77f, new Vector3(p.x, 6f, p.z), 0.006f, 3, false);
        }

        // ------------------------------------------------------------------ Warehouse (44 x 40, h9)
        void Warehouse()
        {
            var r = R(RoomId.Warehouse);
            // tall shelving rows
            for (int row = 0; row < 3; row++)
            {
                float z = 8f + row * 8f;
                Shelf(r.P(12f, z), 0f, 14f, 4.6f, true);
                Shelf(r.P(31f, z), 0f, 10f, 4.6f, true);
                // v1.4: on the front lip of a board (either aisle side), not inside the shelf collider
                if (row == 1) AddSpot("wh_shelf", r.P(12f + Random.Range(-5f, 5f), z + (Random.value < 0.5f ? -0.22f : 0.22f), 1.6f), 0);
                if (row == 2) AddSpot("wh_shelf2", r.P(31f + Random.Range(-3f, 3f), z + (Random.value < 0.5f ? -0.22f : 0.22f), 0.15f), 0);
            }
            // charging pods for the robots (north wall)
            for (int i = 0; i < 3; i++)
            {
                var pp = r.P(9f + i * 9f, 37.4f);
                Solid("metal_rust", pp + new Vector3(0, 2f, 1.6f), new Vector3(3.6f, 4f, 0.3f), 0, false);
                Solid("metal_rust", pp + new Vector3(-1.8f, 2f, 0.4f), new Vector3(0.25f, 4f, 2.6f), 0, false);
                Solid("metal_rust", pp + new Vector3(1.8f, 2f, 0.4f), new Vector3(0.25f, 4f, 2.6f), 0, false);
                Deco("metal_rust", pp + new Vector3(0, 4.1f, 0.4f), new Vector3(3.9f, 0.25f, 2.7f), 0, true);
                Deco("emergency_red", pp + new Vector3(0, 3.9f, -0.85f), new Vector3(3f, 0.06f, 0.06f));
                for (int k = 0; k < 4; k++)
                    wm.Tube("rubber", pp + new Vector3(-1.2f + k * 0.8f, 3.9f, 1.3f), pp + new Vector3(-0.6f + k * 0.4f, 1.6f + Random.Range(-0.2f, 0.3f), 0.5f), 0.04f, 5, false);
                var pod = Anchor("pod_" + i, pp + new Vector3(0, 0, 0.2f), 180f);
                pods.Add(pod);
                lights.Add(pp + new Vector3(0, 3.2f, -0.6f), staticRoot, new Color(1f, 0.1f, 0.05f), 1.4f, 6f, LightGroup.Always, true);
            }
            // forklift
            var fl = r.P(38f, 30f);
            Solid("yellow_paint", fl + Vector3.up * 0.8f, new Vector3(1.4f, 1.3f, 2.4f), 15f, true);
            Deco("black_matte", fl + Vector3.up * 2.1f, new Vector3(1.3f, 0.1f, 1.4f), 15f, true);
            for (int i = 0; i < 2; i++) wm.Tube("metal_rust", fl + Yaw(15f) * new Vector3(-0.55f + i * 1.1f, 0, 1.25f), fl + Yaw(15f) * new Vector3(-0.55f + i * 1.1f, 3f, 1.25f), 0.06f, 6, false);
            Deco("metal_rust", fl + Yaw(15f) * new Vector3(0, 0.15f, 1.9f), new Vector3(1f, 0.06f, 1.1f), 15f);
            AddSpot("wh_forklift", fl + Yaw(15f) * new Vector3(0, 0.25f, 1.9f), 15f);
            // pallets and crates
            // keep crate stacks off the doors (wh_cg / wo_wh on the east wall) and off item spots
            var keepClear = new[] { r.P(44f, 9f), r.P(44f, 29f), r.P(20f, 4f), r.P(22f, 30f), r.P(40f, 3f), r.P(38f, 30f), r.P(2.5f, 12f), r.P(1.4f, 30f) };
            for (int i = 0; i < 10; i++)
            {
                var p = r.P(Random.Range(2f, 38f), Random.Range(2f, 32f));
                var cell = FairMap.CellOf(p);
                if (!map.Walkable(cell.x, cell.y)) continue;
                bool clear = true;
                foreach (var k in keepClear) if ((k - p).sqrMagnitude < 3.5f * 3.5f) { clear = false; break; }
                if (!clear) continue;
                CrateStack(p, Random.Range(-20f, 20f));
            }
            AddSpot("wh_crate", r.P(40f, 3f, 1.15f), 0);
            Crate(r.P(40f, 3f), 1.1f, 0f, true);
            for (int i = 0; i < 6; i++) Barrel(r.P(1f + (i % 2) * 0.7f, 28f + i * 0.7f));
            // hiding crate (big open crate with a lid)
            BuildCrateHide(r.P(2.5f, 12f), 90f);
            // loading shutters on the west wall
            for (int i = 0; i < 2; i++)
            {
                var sp = r.P(0.05f, 20f + i * 8f, 2.2f);
                Deco("wall_metal", sp, new Vector3(0.1f, 4.4f, 5f), 0, false);
                for (int k = 0; k < 10; k++) Deco("metal_rust", sp + new Vector3(0.06f, -2f + k * 0.44f, 0), new Vector3(0.04f, 0.05f, 5f));
            }
            AddSpot("misc", r.P(20f, 4f, 0.05f), 0);
            AddSpot("misc", r.P(22f, 30f, 0.05f), 0);
            ScatterDecals(r, 6, 14);
            for (int i = 0; i < 5; i++) Decal("decal_oil", map.RandomPointInRoom(r), Random.Range(0.8f, 2f), Random.Range(0, 360f));
            Debris(r, 35);
        }

        void BuildCrateHide(Vector3 p, float yaw)
        {
            var q = Yaw(yaw);
            float w = 1.5f, h = 1.4f, d = 1.5f;
            var root = new GameObject("CrateVis").transform;
            root.SetParent(dynRoot, false);
            root.position = p; root.rotation = q;
            HideSpot.AddSolid(root, "wood", new Vector3(0, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.06f));
            HideSpot.AddSolid(root, "wood", new Vector3(-w * 0.5f, h * 0.5f, 0), new Vector3(0.06f, h, d));
            HideSpot.AddSolid(root, "wood", new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(0.06f, h, d));
            HideSpot.AddSolid(root, "wood", new Vector3(0, h * 0.5f, d * 0.5f), new Vector3(w, h, 0.06f));
            var hinge = new GameObject("Lid").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(-w * 0.5f, h, 0);
            hinge.localRotation = Quaternion.identity;
            var lidPivot = new GameObject("LidPivot").transform;
            lidPivot.SetParent(hinge, false);
            lidPivot.localRotation = Quaternion.Euler(0, 0, 0);
            var lid = RF.Prim(PrimitiveType.Cube, lidPivot, "wood", new Vector3(w * 0.5f, 0.03f, 0), new Vector3(w, 0.06f, d));
            var hs = HideSpot.Custom(dynRoot, "Crate", p, yaw, HideKind.Crate, new Vector3(0, 1.05f, 0.1f), new Vector3(0, 0, 1.5f), new Vector3(w, h, d), null);
            root.SetParent(hs.transform, true);
            hs.occluders.Add(lid.GetComponent<Renderer>());
            map.BlockWorld(p, new Vector3(w, 1, d), yaw);
        }

        // ------------------------------------------------------------------ Warehouse office (12 x 16)
        void WarehouseOffice()
        {
            var r = R(RoomId.WarehouseOffice);
            Solid("wood", r.P(6f, 12.5f, 0.75f), new Vector3(2.6f, 0.06f, 1.1f), 0, false);
            Solid("wood", r.P(5.2f, 12.5f, 0.37f), new Vector3(0.8f, 0.74f, 1f), 0, true);
            Solid("wood", r.P(7.3f, 12.5f, 0.37f), new Vector3(0.4f, 0.74f, 1f), 0, true);
            AddColliderRot(r.P(6f, 12.5f, 0.4f), new Vector3(2.6f, 0.8f, 1.1f), 0); // v1.2: whole desk incl. the knee gap
            Chair(r.P(6f, 11.4f), 0f);
            var crt = r.P(6.6f, 12.8f, 1.05f);
            Deco("white_paint", crt, new Vector3(0.5f, 0.42f, 0.45f), 0, true);
            wm.For("crt_static", crt, false).QuadUV(crt + Vector3.back * 0.23f, Vector3.back, Vector3.up, 0.38f, 0.3f, new Rect(0, 0, 1, 1));
            // drawer for the ride key
            var drawerRoot = new GameObject("Drawer_wo").transform;
            drawerRoot.SetParent(dynRoot, false);
            drawerRoot.position = r.P(5.2f, 11.95f, 0.6f);
            drawerRoot.rotation = Yaw(180f);
            var slide = new GameObject("Slide").transform;
            slide.SetParent(drawerRoot, false);
            RF.Prim(PrimitiveType.Cube, slide, "wood", Vector3.zero, new Vector3(0.7f, 0.2f, 0.5f));
            RF.Prim(PrimitiveType.Cube, slide, "chrome", new Vector3(0, 0, 0.27f), new Vector3(0.18f, 0.04f, 0.04f), default, false);
            var dr = drawerRoot.gameObject.AddComponent<Drawer>();
            dr.slide = slide;
            dr.openOffset = new Vector3(0, 0, 0.35f);
            dr.AddHitBox(new Vector3(0, 0, 0.1f), new Vector3(0.75f, 0.3f, 0.55f));
            anchors["drawer_wo"] = slide;
            drawers["wo"] = dr;
            for (int i = 0; i < 3; i++) Solid("wall_metal", r.P(11.5f, 3f + i * 0.85f, 0.7f), new Vector3(0.6f, 1.4f, 0.8f), 0, true);
            BuildCabinet(r.P(0.45f, 11f), 90f, "wood");
            Poster(r.P(6f, 15.98f, 1.9f), Vector3.back, 2, 0.8f);
            AddSpot("misc", r.P(10f, 13f, 0.05f), 0);
            Debris(r, 10);
        }

        // ------------------------------------------------------------------ Clock gallery (24 x 24, h6.5)
        void ClockGallery()
        {
            var r = R(RoomId.ClockGallery);
            clock = ClockAutomaton.Create(dynRoot, r.P(12f, 22.2f), 180f);
            map.BlockWorld(r.P(12f, 22.4f), new Vector3(4.4f, 1, 3f));
            AddColliderRot(r.P(12f, 22.4f, 2.5f), new Vector3(4f, 5f, 2.6f), 0);
            Anchor("clock_socket", r.P(11f, 20.75f, 1.7f), 180f); // matches the gear slots (local x 1.0, y 1.25..2.15 at yaw 180)
            Anchor("clock_reward", r.P(12f, 20.5f, 1.0f), 180f);
            // display cases on pedestals
            for (int i = 0; i < 6; i++)
            {
                var dp = r.P(5f + (i % 3) * 7f, 6f + (i / 3) * 7f);
                Solid("wood_painted", dp + Vector3.up * 0.5f, new Vector3(1f, 1f, 1f), 0, true);
                Deco("glass", dp + Vector3.up * 1.35f, new Vector3(0.9f, 0.7f, 0.9f));
                AddColliderRot(dp + Vector3.up * 1.35f, new Vector3(0.9f, 0.7f, 0.9f), 0);
                // a porcelain doll inside
                RF.Prim(PrimitiveType.Capsule, staticRoot, "white_paint", dp + Vector3.up * 1.2f, new Vector3(0.18f, 0.16f, 0.18f));
                var hh = RF.Prim(PrimitiveType.Sphere, staticRoot, "plastic", dp + Vector3.up * 1.48f, Vector3.one * 0.16f, new Vector3(0, Random.Range(0, 360), Random.Range(-25, 25)), false);
                RF.Prim(PrimitiveType.Sphere, hh.transform, "black_matte", new Vector3(-0.2f, 0.1f, 0.4f), Vector3.one * 0.22f, default, false);
                RF.Prim(PrimitiveType.Sphere, hh.transform, "black_matte", new Vector3(0.2f, 0.1f, 0.4f), Vector3.one * 0.22f, default, false);
                if (i == 4) AddSpot("cg_display", dp + new Vector3(0, 0.05f, -0.8f), 180f); // was floating 0.1 m off the pedestal edge
            }
            // grandfather clocks along the walls (one is a hiding spot)
            float[] gz = { 3f, 7f, 18f, 22f };
            for (int i = 0; i < 4; i++)
            {
                var gp = r.P(0.5f, gz[i]);
                if (i == 2) { GrandfatherHide(gp, 90f); continue; }
                Solid("wood", gp + Vector3.up * 1.2f, new Vector3(0.6f, 2.4f, 0.8f), 0, true);
                Deco("white_paint", gp + new Vector3(0.31f, 1.9f, 0), new Vector3(0.02f, 0.45f, 0.45f));
                Deco("gold_trim", gp + new Vector3(0.31f, 1.1f, 0), new Vector3(0.02f, 0.6f, 0.08f));
            }
            // velvet ropes
            for (int i = 0; i < 4; i++)
            {
                var a = r.P(8f + i * 2.5f, 18.5f);
                wm.Cyl("gold_trim", a, 0.05f, 0.95f, 8, false);
                if (i < 3) StringRope(a + Vector3.up * 0.85f, r.P(10.5f + i * 2.5f, 18.5f, 0.85f));
            }
            AddColliderRot(r.P(11.75f, 18.5f, 0.5f), new Vector3(7.6f, 1f, 0.12f), 0); // the velvet rope line was walk-through
            Poster(r.P(23.98f, 6f, 2.2f), Vector3.left, 3, 1f);
            AddSpot("misc", r.P(20f, 3f, 0.05f), 0);
            ScatterDecals(r, 1, 4);
            Debris(r, 12);
        }

        void StringRope(Vector3 a, Vector3 b)
        {
            Vector3 prev = a;
            for (int i = 1; i <= 6; i++)
            {
                float t = i / 6f;
                var p = Vector3.Lerp(a, b, t) - Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.18f;
                wm.Tube("red_paint", prev, p, 0.025f, 5, false);
                prev = p;
            }
        }

        void GrandfatherHide(Vector3 p, float yaw)
        {
            var q = Yaw(yaw);
            float w = 0.9f, h = 2.5f, d = 0.75f;
            var root = new GameObject("ClockCaseVis").transform;
            root.SetParent(dynRoot, false);
            root.position = p; root.rotation = q;
            HideSpot.AddSolid(root, "wood", new Vector3(0, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.05f));
            HideSpot.AddSolid(root, "wood", new Vector3(-w * 0.5f, h * 0.5f, 0), new Vector3(0.05f, h, d));
            HideSpot.AddSolid(root, "wood", new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(0.05f, h, d));
            HideSpot.AddSolid(root, "wood", new Vector3(0, h, 0), new Vector3(w + 0.1f, 0.1f, d + 0.1f));
            var hinge = new GameObject("CaseDoor").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(-w * 0.5f, 0, d * 0.5f);
            var door = RF.Prim(PrimitiveType.Cube, hinge, "wood", new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(w, h, 0.04f));
            RF.Prim(PrimitiveType.Cube, door.transform, "white_paint", new Vector3(0, 0.3f, 0.8f), new Vector3(0.5f, 0.18f, 0.4f), default, false);
            RF.Prim(PrimitiveType.Cube, door.transform, "glass", new Vector3(0, -0.05f, 0.8f), new Vector3(0.5f, 0.3f, 0.4f), default, false);
            var hs = HideSpot.Custom(dynRoot, "ClockCase", p, yaw, HideKind.Wardrobe, new Vector3(0, 1.6f, 0), new Vector3(0, 0, 1f), new Vector3(w, h, d), hinge);
            root.SetParent(hs.transform, true);
            hs.occluders.AddRange(door.GetComponentsInChildren<Renderer>());
            map.BlockWorld(p, new Vector3(w, 1, d), yaw);
        }

        // ------------------------------------------------------------------ Maintenance (24 x 28, h5)
        void MaintenanceRoom()
        {
            var r = R(RoomId.Maintenance);
            generator = Generator.Create(dynRoot, r.P(15f, 17f), 0f);
            map.BlockWorld(r.P(15f, 17f), new Vector3(5f, 1, 3f));
            AddColliderRot(r.P(15f, 17f, 1.2f), new Vector3(4.6f, 2.4f, 2.6f), 0);
            Anchor("generator_lever", r.P(15f, 15.4f, 1.1f), 180f);
            // fuse box on the north wall
            var fb = r.P(6f, 27.85f, 1.5f);
            Deco("wall_metal", fb, new Vector3(1.2f, 1.4f, 0.25f), 0, true);
            Anchor("fusebox", fb + Vector3.back * 0.14f, 180f);
            for (int i = 0; i < 4; i++) Pipe(r.P(0.3f, 27.6f - i * 0.25f, 4.4f - i * 0.2f), r.P(23.7f, 27.6f - i * 0.25f, 4.4f - i * 0.2f), 0.07f + i * 0.02f);
            Pipe(r.P(0.3f, 2f, 4.3f), r.P(0.3f, 26f, 4.3f), 0.12f);
            Pipe(r.P(23.6f, 2f, 3.9f), r.P(23.6f, 26f, 3.9f), 0.1f, "chrome");
            // workbench (west wall)
            Solid("wood", r.P(1f, 7f, 0.9f), new Vector3(1.2f, 0.08f, 3.5f), 0, false);
            Solid("metal_rust", r.P(1f, 7f, 0.44f), new Vector3(1.1f, 0.86f, 3.4f), 0, true);
            AddSpot("mt_workbench", r.P(1f, 7.5f, 0.98f), 90f);
            AddSpot("mt_bench", r.P(1f, 6f, 0.98f), 90f);
            Deco("wall_metal", r.P(0.05f, 7f, 1.8f), new Vector3(0.05f, 1.2f, 3f));
            for (int i = 0; i < 6; i++) Deco("chrome", r.P(0.12f, 5.8f + i * 0.45f, 1.8f + (i % 2) * 0.2f), new Vector3(0.04f, 0.3f, 0.06f));
            // electrical cabinets + barrels
            for (int i = 0; i < 4; i++) Solid("wall_metal", r.P(18f + i * 1.3f, 0.5f, 1.1f), new Vector3(1.2f, 2.2f, 0.6f), 0, true);
            for (int i = 0; i < 5; i++) Barrel(r.P(21f + (i % 2) * 0.7f, 3f + i * 0.7f), i % 2 == 0 ? "red_paint" : "metal_rust");
            // lockers on the east wall
            Lockers(r.P(23.5f, 21f), Vector3.forward, -90f, 3, 1);
            ScatterDecals(r, 2, 8);
            for (int i = 0; i < 4; i++) Decal("decal_oil", map.RandomPointInRoom(r), Random.Range(0.6f, 1.6f), Random.Range(0, 360f));
            AddSpot("misc", r.P(12f, 3f, 0.05f), 0);
            Debris(r, 18);
        }

        // ------------------------------------------------------------------ Service hall (12 x 20)
        void ServiceHall()
        {
            var r = R(RoomId.ServiceHall);
            Pipe(r.P(0.3f, 0.5f, 3f), r.P(0.3f, 19.5f, 3f), 0.09f);
            Pipe(r.P(11.7f, 0.5f, 2.8f), r.P(11.7f, 19.5f, 2.8f), 0.07f, "chrome");
            foreach (float cz in new[] { 2f, 6f, 17f }) CrateStack(r.P(10.2f, cz), Random.Range(-10, 10));
            Barrel(r.P(1.2f, 16f));
            wm.Cyl("yellow_paint", r.P(1.2f, 3f), 0.25f, 0.4f, 10);
            AddColliderRot(r.P(1.2f, 3f, 0.2f), new Vector3(0.5f, 0.4f, 0.5f), 0);
            BuildCabinet(r.P(0.45f, 8f), 90f, "wall_metal");
            AddSpot("misc", r.P(6f, 18f, 0.05f), 0);
            ScatterDecals(r, 2, 4);
            Debris(r, 10);
        }

        // ------------------------------------------------------------------ Staff area (24 x 16)
        void StaffArea()
        {
            var r = R(RoomId.StaffArea);
            // row of lockers on the north wall; #13 is the locked one with the crank
            Lockers(r.P(2f, 15.5f), Vector3.right, 180f, 6, 2);
            var ll = r.P(8f, 15.5f);
            var lockedRoot = new GameObject("Locker13").transform;
            lockedRoot.SetParent(dynRoot, false);
            lockedRoot.position = ll;
            lockedRoot.rotation = Yaw(180f);
            HideSpot.AddSolid(lockedRoot, "red_paint", new Vector3(0, 1.02f, -0.3f), new Vector3(0.8f, 2.05f, 0.05f));
            HideSpot.AddSolid(lockedRoot, "red_paint", new Vector3(-0.4f, 1.02f, 0), new Vector3(0.04f, 2.05f, 0.65f));
            HideSpot.AddSolid(lockedRoot, "red_paint", new Vector3(0.4f, 1.02f, 0), new Vector3(0.04f, 2.05f, 0.65f));
            HideSpot.AddSolid(lockedRoot, "red_paint", new Vector3(0, 2.04f, 0), new Vector3(0.8f, 0.04f, 0.65f));
            HideSpot.AddSolid(lockedRoot, "red_paint", new Vector3(0, 1.0f, 0), new Vector3(0.78f, 0.03f, 0.62f));
            Anchor("locker13_inside", ll + Vector3.up * 1.1f, 180f);
            var door = Door.Create(lockedRoot, ll + Yaw(180f) * new Vector3(0, 0, 0.33f), Yaw(180f) * Vector3.forward, 0.78f, 2.0f, "red_paint", "Locker13Door");
            door.transform.SetParent(lockedRoot, true);
            door.locked = true;
            door.keyItem = ItemId.LockerKey;
            door.lockedKey = "int_locked_locker";
            door.robotsCanUse = false;
            door.openAngle = -110f; // locker door swings out towards the player (Door opens away by default)
            doors["locker13"] = door;
            Deco("white_paint", ll + Yaw(180f) * new Vector3(0, 1.75f, 0.36f), new Vector3(0.22f, 0.12f, 0.01f), 180f);
            Lockers(r.P(9f, 15.5f), Vector3.right, 180f, 6, 2);
            map.BlockWorld(ll, new Vector3(0.8f, 1, 0.65f));
            // break area
            Table(r.P(12f, 6f), false, true, true);
            AddSpot("st_table", r.P(12f, 6f, 0.8f), 0);
            Solid("blue_paint", r.P(20f, 1.2f, 0.4f), new Vector3(3f, 0.8f, 1f), 0, true);
            Deco("blue_paint", r.P(20f, 0.8f, 0.9f), new Vector3(3f, 0.9f, 0.25f), 0, true);
            // vending machine
            var vm = r.P(23.4f, 12f, 1f);
            Solid("red_paint", vm, new Vector3(0.9f, 2f, 1.2f), 0, true);
            wm.For("neon_cyan", vm, false).QuadUV(vm + Vector3.left * 0.46f + Vector3.up * 0.2f, Vector3.left, Vector3.up, 0.8f, 1.1f, new Rect(0, 0, 1, 1));
            lights.Add(vm + Vector3.left * 0.9f, staticRoot, new Color(0.4f, 0.8f, 1f), 0.6f, 4f, LightGroup.Main, true);
            Poster(r.P(0.02f, 6f, 1.7f), Vector3.right, 1, 0.8f);
            Poster(r.P(15f, 0.02f, 1.7f), Vector3.forward, 3, 0.8f);
            AddSpot("misc", r.P(4f, 4f, 0.05f), 0);
            ScatterDecals(r, 1, 4);
            Debris(r, 12);
        }

        // ------------------------------------------------------------------ outside the main gate
        void Outside()
        {
            // a small rainy plaza beyond the gate, visible once it opens
            float x0 = 50f, x1 = 74f, z0 = -16f, z1 = 0f;
            wm.Quad("ground_wet", new Vector3((x0 + x1) * 0.5f, -0.02f, (z0 + z1) * 0.5f), Vector3.up, Vector3.forward, x1 - x0, z1 - z0, false);
            AddCollider(new Vector3((x0 + x1) * 0.5f, -0.27f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.5f, z1 - z0));
            AddCollider(new Vector3(x0, 2f, (z0 + z1) * 0.5f), new Vector3(0.3f, 4f, z1 - z0));
            AddCollider(new Vector3(x1, 2f, (z0 + z1) * 0.5f), new Vector3(0.3f, 4f, z1 - z0));
            AddCollider(new Vector3((x0 + x1) * 0.5f, 2f, z0), new Vector3(x1 - x0, 4f, 0.3f));
            for (float x = x0; x < x1; x += 2f)
                wm.For("fence", new Vector3(x + 1f, 1.5f, z0 + 0.1f), false).Quad(new Vector3(x + 1f, 1.5f, z0 + 0.1f), Vector3.forward, Vector3.up, 2f, 3f, 2f);
            // an exterior wall face so the building has a facade from outside
            wm.Quad("wall_brick", new Vector3(54f, 6f, -0.05f), Vector3.back, Vector3.up, 8f, 12f, false);
            wm.Quad("wall_brick", new Vector3(70f, 6f, -0.05f), Vector3.back, Vector3.up, 8f, 12f, false);
            wm.Quad("wall_brick", new Vector3(62f, 8.3f, -0.05f), Vector3.back, Vector3.up, 8f, 7.4f, false);
            // street lamp
            var lp = new Vector3(56f, 0, -8f);
            wm.Cyl("black_matte", lp, 0.08f, 4.2f, 8);
            var bulb = RF.Prim(PrimitiveType.Sphere, staticRoot, "bulb_warm", lp + Vector3.up * 4.3f, Vector3.one * 0.3f, default, false);
            lights.Add(lp + Vector3.up * 4.1f, staticRoot, new Color(0.6f, 0.75f, 1f), 2.5f, 14f, LightGroup.Always, false, bulb.GetComponent<Renderer>());
            Anchor("escape_point", new Vector3(62f, 0f, -7f), 180f);
            Anchor("outside_rain", new Vector3(62f, 0f, -8f), 0f); // 16 m deep rain box now spans z -16..0 (was leaking 2 m into the hall)
        }
    }
}
