using System;
using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    public enum TaskId { Power, Carousel, Fortune, GhostTrain, Broadcast, ColdRoom, Clock }

    /// <summary>
    /// The seven tasks. Each one ends with a note that reveals one digit of the (random) 7-digit safe code.
    /// The safe in the Security Office holds the emergency keycard that opens the main gate.
    /// </summary>
    public class Quest
    {
        public readonly int[] code = new int[7];
        public readonly bool[] notes = new bool[7];
        public readonly bool[] done = new bool[7];
        public int TasksDone { get; private set; }
        public int NotesFound { get; private set; }
        public bool HasKeycard;
        public bool SafeOpened;
        public bool GateOpening;
        public event Action<TaskId> OnTaskDone;
        public event Action<int> OnNote;

        /// <summary>v1.8: a completed task's note is collected automatically (GameController shows it when it's safe).</summary>
        public struct PendingNote { public int index; public float at; }
        public readonly List<PendingNote> PendingNotes = new List<PendingNote>();
        public event Action OnChanged;

        // progress counters for the journal
        public int fusesIn, tokensIn, gearsIn;
        public bool crankUsed, rideKeyUsed, tapePlayed, coldOpened, iceSmashed, cryptOpened;

        FairBuilder fb;
        PlayerController player;
        Transform root;
        readonly HashSet<string> usedSpots = new HashSet<string>();

        public Quest()
        {
            for (int i = 0; i < 7; i++) code[i] = UnityEngine.Random.Range(0, 10);
        }

        public string CodeString
        {
            get
            {
                var s = "";
                foreach (var d in code) s += d.ToString();
                return s;
            }
        }

        // ================================================================== setup
        public void Setup(FairBuilder builder, PlayerController p, Transform parent)
        {
            fb = builder;
            player = p;
            root = new GameObject("Quest").transform;
            root.SetParent(parent, false);

            PlaceItems();
            SetupPower();
            SetupCarousel();
            SetupFortune();
            SetupGhostTrain();
            SetupBroadcast();
            SetupColdRoom();
            SetupClock();
            SetupSafe();
            SetupGate();

            var sec = fb.doors["security"];
            sec.locked = true;
            sec.lockedKey = "int_locked_power";
            sec.unlockCondition = () => Power.On;
            sec.robotsCanUse = true;
        }

        void Changed() => OnChanged?.Invoke();

        // ================================================================== item placement
        Spot? Take(string key)
        {
            if (!fb.spots.TryGetValue(key, out var list)) return null;
            var idx = new List<int>();
            for (int i = 0; i < list.Count; i++) if (!usedSpots.Contains(key + "#" + i)) idx.Add(i);
            if (idx.Count == 0) return null;
            int k = idx[UnityEngine.Random.Range(0, idx.Count)];
            usedSpots.Add(key + "#" + k);
            return list[k];
        }

        static string Area(string key)
        {
            int u = key.IndexOf('_');
            return u > 0 ? key.Substring(0, u) : key;
        }

        /// <summary>Picks n spots from n different areas.</summary>
        List<Spot> ChooseSpread(string[] keys, int n)
        {
            var shuffled = new List<string>(keys);
            RF.Shuffle(shuffled);
            var areas = new HashSet<string>();
            var result = new List<Spot>();
            foreach (var k in shuffled)
            {
                if (result.Count >= n) break;
                if (areas.Contains(Area(k))) continue;
                var s = Take(k);
                if (s == null) continue;
                areas.Add(Area(k));
                result.Add(s.Value);
            }
            // fallback: any misc spot
            while (result.Count < n)
            {
                var s = Take("misc");
                if (s == null) break;
                result.Add(s.Value);
            }
            return result;
        }

        void PlaceItems()
        {
            foreach (var s in ChooseSpread(new[] { "fc_stall_burger", "fc_stall_pizza", "gt_booth", "bc_booth", "ar_counter", "wh_shelf", "ki_shelf" }, 3))
                ItemVisuals.SpawnPickup(root, ItemId.Fuse, s.pos, s.yaw);

            // locker key: inside one of the two ticket booth drawers
            string booth = UnityEngine.Random.value < 0.5f ? "eh_booth_a" : "eh_booth_b";
            PutInDrawer(booth, ItemId.LockerKey);

            // crank: inside the locked staff locker #13
            var inside = fb.anchors["locker13_inside"];
            ItemVisuals.SpawnPickup(root, ItemId.Crank, inside.position, inside.eulerAngles.y, false);

            foreach (var s in ChooseSpread(new[] { "mm_deadend_far", "cg_display", "wh_crate", "bc_rink", "fc_table", "gt_station", "ch_stage" }, 3))
                ItemVisuals.SpawnPickup(root, ItemId.Token, s.pos, s.yaw);

            PutInDrawer("wo", ItemId.RideKey);

            foreach (var s in ChooseSpread(new[] { "bo_desk", "st_table", "ki_counter", "ki_island" }, 1))
                ItemVisuals.SpawnPickup(root, ItemId.Tape, s.pos, s.yaw);

            if (UnityEngine.Random.value < 0.45f) PutInDrawer("ar", ItemId.KitchenKey);
            else foreach (var s in ChooseSpread(new[] { "bc_booth", "mt_bench", "bo_desk", "st_table" }, 1))
                ItemVisuals.SpawnPickup(root, ItemId.KitchenKey, s.pos, s.yaw);

            foreach (var s in ChooseSpread(new[] { "mt_workbench", "wh_shelf2", "gt_station", "ch_stage" }, 1))
                ItemVisuals.SpawnPickup(root, ItemId.Hammer, s.pos, s.yaw);

            foreach (var s in ChooseSpread(new[] { "gt_tunnel", "wh_forklift", "ki_shelf", "ch_stage", "bo_desk", "mm_deadend", "cg_display" }, 3))
                ItemVisuals.SpawnPickup(root, ItemId.Gear, s.pos, s.yaw);

            // consumables and batteries scattered around
            // v1.6: more consumables (8 / 6 / 6 / 5), shuffled over the misc spots
            var plan = new List<ItemId>();
            for (int i = 0; i < 8; i++) plan.Add(ItemId.Battery);
            for (int i = 0; i < 6; i++) plan.Add(ItemId.Oil);
            for (int i = 0; i < 6; i++) plan.Add(ItemId.Water);
            for (int i = 0; i < 5; i++) plan.Add(ItemId.Toy);
            RF.Shuffle(plan);
            foreach (var id in plan)
            {
                var s = Take("misc");
                if (s == null) break;
                ItemVisuals.SpawnPickup(root, id, s.Value.pos, s.Value.yaw);
            }
            // one guaranteed oil + water near the start so the player learns them early
            var eh = fb.map.Room(RoomId.EntranceHall);
            ItemVisuals.SpawnPickup(root, ItemId.Water, eh.P(17.2f, 17.5f, 1.15f), 0);
            ItemVisuals.SpawnPickup(root, ItemId.Oil, eh.P(18.8f, 17.5f, 1.15f), 0);
        }

        void PutInDrawer(string drawerKey, ItemId id)
        {
            if (!fb.drawers.TryGetValue(drawerKey, out var drawer)) return;
            var slide = drawer.slide;
            // slide cube is ~0.2 tall: sit the item on its top face (SpawnPickup adds 0.06) instead of floating
            var p = ItemVisuals.SpawnPickup(slide, id, slide.position + Vector3.up * 0.05f, slide.eulerAngles.y, false);
            p.transform.SetParent(slide, true);
            drawer.contents = p;
        }

        // ================================================================== T1 power
        void SetupPower()
        {
            var a = fb.anchors["fusebox"];
            var go = new GameObject("FuseSocket");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(a.position, a.rotation);
            var socket = go.AddComponent<ItemSocket>();
            socket.item = ItemId.Fuse;
            socket.required = 3;
            socket.promptKey = "int_insert";
            for (int i = 0; i < 3; i++)
            {
                RF.Prim(PrimitiveType.Cube, go.transform, "black_matte", new Vector3(-0.3f + i * 0.3f, 0, 0.02f), new Vector3(0.18f, 0.35f, 0.04f), default, false);
                var v = ItemVisuals.Fuse(go.transform);
                v.transform.localPosition = new Vector3(-0.3f + i * 0.3f, 0, 0.06f);
                v.transform.localRotation = Quaternion.identity;
                v.SetActive(false);
                socket.fillVisuals.Add(v);
            }
            socket.AddHitBox(Vector3.zero, new Vector3(1.2f, 1.2f, 0.6f));
            socket.onInserted = n => { fusesIn = n; Changed(); };
            socket.onComplete = () =>
            {
                if (AudioManager.I != null) AudioManager.I.PlayAt("spark_1", a.position, 1f, 2f, 15f);
                Hint("tip_lever");
                Changed();
            };

            var la = fb.anchors["generator_lever"];
            var lever = new GameObject("GeneratorLever");
            lever.transform.SetParent(root, false);
            lever.transform.SetPositionAndRotation(la.position, la.rotation);
            var hold = lever.AddComponent<HoldAction>();
            hold.promptKey = "int_pull_lever";
            hold.seconds = 3.5f;
            hold.noiseRadius = 30f;
            hold.loopSound = "carousel_crank";
            hold.condition = () => socket.Complete;
            hold.deniedText = () => Loc.F("int_needs_fuses", socket.Inserted, 3);
            hold.onProgress = t => fb.generator.SetLever(t);
            hold.AddHitBox(new Vector3(0, 0, -0.1f), new Vector3(0.9f, 1.4f, 0.9f));
            hold.onDone = () =>
            {
                fb.generator.StartUp();
                Power.Set(true);
                if (LightSystem.I != null) LightSystem.I.Surge(2.5f);
                Noise.Emit(la.position, 45f, NoiseKind.Loud);
                Complete(TaskId.Power, la.position + la.forward * 0.9f - Vector3.up * 1.05f, la.eulerAngles.y);
            };
        }

        // ================================================================== T2 carousel
        void SetupCarousel()
        {
            var a = fb.anchors["carousel_booth"];
            var go = new GameObject("CarouselCrank");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(a.position, a.rotation);
            RF.Prim(PrimitiveType.Cylinder, go.transform, "black_matte", new Vector3(0, -0.02f, 0), new Vector3(0.2f, 0.03f, 0.2f), default, false);
            var crankVis = ItemVisuals.Crank(go.transform);
            crankVis.transform.localPosition = new Vector3(0, 0.1f, 0);
            crankVis.SetActive(false);
            var hold = go.AddComponent<HoldAction>();
            hold.promptKey = "int_crank";
            hold.seconds = 4.5f;
            hold.noiseRadius = 28f;
            hold.loopSound = "carousel_crank";
            hold.condition = () => Power.On && (crankUsed || player.Inventory.Has(ItemId.Crank));
            hold.deniedText = () => !Power.On ? Loc.T("int_nopower") : Loc.F("int_need_item", Loc.T(Items.NameKey(ItemId.Crank)));
            hold.onProgress = t =>
            {
                if (!crankUsed && player.Inventory.Remove(ItemId.Crank)) { crankUsed = true; crankVis.SetActive(true); Changed(); }
                crankVis.transform.localRotation = Quaternion.Euler(0, t * 1440f, 0);
            };
            hold.AddHitBox(Vector3.zero, new Vector3(1.0f, 0.8f, 1.0f));
            hold.onDone = () =>
            {
                Hint("tip_carousel_spin");
                var ra = fb.anchors["carousel_reward"];
                fb.carousel.Spin(14f, () => Complete(TaskId.Carousel, ra.position + ra.forward * 0.45f + Vector3.down * 0.53f, ra.eulerAngles.y));
            };
        }

        // ================================================================== T3 fortune teller
        void SetupFortune()
        {
            var a = fb.anchors["fortune_slot"];
            var go = new GameObject("FortuneSlot");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(a.position, a.rotation);
            var socket = go.AddComponent<ItemSocket>();
            socket.item = ItemId.Token;
            socket.required = 3;
            socket.needsPower = true;
            socket.AddHitBox(Vector3.zero, new Vector3(0.6f, 0.6f, 0.6f));
            socket.onInserted = n => { tokensIn = n; Changed(); };
            socket.onComplete = () =>
            {
                var ra = fb.anchors["fortune_reward"];
                fb.fortuneTeller.Activate(() => Complete(TaskId.Fortune, ra.position + ra.forward * 0.6f - Vector3.up * 0.82f, ra.eulerAngles.y));
            };
        }

        // ================================================================== T4 ghost train
        void SetupGhostTrain()
        {
            var gate = fb.doors["tunnel_gate"];
            gate.locked = true;
            gate.lockedKey = "int_locked_ride";
            gate.robotsCanUse = false;

            var a = fb.anchors["ride_panel"];
            var go = new GameObject("RidePanel");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(a.position, a.rotation);
            RF.Prim(PrimitiveType.Cube, go.transform, "chrome", Vector3.zero, new Vector3(0.18f, 0.05f, 0.18f), default, false);
            var keyVis = ItemVisuals.Key(go.transform, "gold_trim");
            keyVis.transform.localPosition = new Vector3(0, 0.06f, 0);
            keyVis.SetActive(false);
            var socket = go.AddComponent<ItemSocket>();
            socket.item = ItemId.RideKey;
            socket.required = 1;
            socket.needsPower = true;
            socket.promptKey = "int_insert_key";
            socket.fillVisuals.Add(keyVis);
            socket.AddHitBox(Vector3.zero, new Vector3(0.6f, 0.4f, 0.6f));
            socket.onComplete = () =>
            {
                rideKeyUsed = true;
                gate.locked = false;
                gate.OpenAwayFrom(gate.transform.position - gate.transform.forward, false);
                if (AudioManager.I != null) AudioManager.I.PlayAt("ghost_scare", gate.transform.position, 1f, 3f, 30f);
                if (LightSystem.I != null) LightSystem.I.Surge(2f);
                Hint("tip_tunnel");
                Changed();
            };

            // crypt: push the sarcophagus lid
            var ca = fb.anchors["crypt"];
            var crypt = new GameObject("Crypt");
            crypt.transform.SetParent(root, false);
            crypt.transform.SetPositionAndRotation(ca.position, Quaternion.identity);
            var lid = RF.Prim(PrimitiveType.Cube, crypt.transform, "wall_brick", new Vector3(0, 0.95f, 1.6f), new Vector3(2.9f, 0.12f, 0.58f));
            var hold = crypt.AddComponent<HoldAction>();
            hold.promptKey = "int_push_lid";
            hold.seconds = 2.8f;
            hold.noiseRadius = 16f;
            hold.loopSound = "metal_creak_1";
            hold.AddHitBox(new Vector3(0, 0.9f, 1.4f), new Vector3(3f, 0.8f, 1.2f));
            hold.onProgress = t => lid.transform.localPosition = new Vector3(t * 0.8f, 0.95f, 1.6f);
            hold.onDone = () =>
            {
                cryptOpened = true;
                lid.transform.localPosition = new Vector3(1.1f, 0.9f, 1.6f);
                lid.transform.localRotation = Quaternion.Euler(0, 0, -15f);
                if (AudioManager.I != null) AudioManager.I.PlayAt("skeleton_drop", ca.position + Vector3.up, 1f, 2f, 18f);
                player.Shake(0.35f, 0.4f);
                Complete(TaskId.GhostTrain, ca.position + new Vector3(0, 0.02f, 0.5f), 180f);
            };
        }

        // ================================================================== T5 broadcast
        void SetupBroadcast()
        {
            var a = fb.anchors["pa_deck"];
            var go = new GameObject("TapeDeck");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(a.position, a.rotation);
            var tapeVis = ItemVisuals.Tape(go.transform);
            tapeVis.transform.localPosition = new Vector3(0, 0.08f, 0);
            tapeVis.SetActive(false);
            var socket = go.AddComponent<ItemSocket>();
            socket.item = ItemId.Tape;
            socket.required = 1;
            socket.needsPower = true;
            socket.promptKey = "int_insert_tape";
            socket.fillVisuals.Add(tapeVis);
            socket.AddHitBox(Vector3.zero, new Vector3(0.7f, 0.4f, 0.6f));
            var drawerT = fb.anchors["pa_drawer"];
            socket.onComplete = () =>
            {
                tapePlayed = true;
                Changed();
                if (AudioManager.I != null)
                {
                    AudioManager.I.PlayAt("tape_insert", a.position, 1f, 1f, 8f);
                    AudioManager.I.Play2D("pa_announcement", 0.55f);
                }
                Hint("tip_pa_hide");
                if (GameController.I != null) GameController.I.StartCoroutine(Broadcast(a.position, drawerT));
            };
        }

        System.Collections.IEnumerator Broadcast(Vector3 deckPos, Transform drawer)
        {
            // every robot comes to the booth
            yield return new WaitForSeconds(1.5f);
            foreach (var r in RobotAI.All) r.Investigate(deckPos, 9f);
            Noise.Emit(deckPos, 300f, NoiseKind.Alarm);
            if (LightSystem.I != null) LightSystem.I.Surge(18f);
            yield return new WaitForSeconds(18f);
            foreach (var r in RobotAI.All) r.Investigate(deckPos, 6f);
            // the drawer pops open with note 5
            var slide = drawer.GetChild(0);
            Vector3 start = slide.localPosition;
            float t = 0;
            if (AudioManager.I != null) AudioManager.I.PlayAt("locker_open", drawer.position, 0.8f, 1f, 10f);
            while (t < 1f) { t += Time.deltaTime * 2f; slide.localPosition = start + new Vector3(0, 0, 0.35f * t); yield return null; }
            Complete(TaskId.Broadcast, slide.position + Vector3.up * 0.13f, drawer.eulerAngles.y, slide);
        }

        // ================================================================== T6 cold room
        void SetupColdRoom()
        {
            var cold = fb.doors["cold"];
            cold.locked = true;
            cold.keyItem = ItemId.KitchenKey;
            cold.lockedKey = "int_locked_key";
            cold.robotsCanUse = true;

            var a = fb.anchors["ice_block"];
            var go = new GameObject("IceBlock");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(a.position, a.rotation);
            var block = RF.Prim(PrimitiveType.Cube, go.transform, "ice", new Vector3(0, 0.55f, 0), new Vector3(1.0f, 1.1f, 0.9f));
            var inner = RF.Prim(PrimitiveType.Quad, block.transform, "paper", new Vector3(0, 0.05f, -0.51f), new Vector3(0.3f, 0.35f, 1f), default, false);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.55f, 0);
            col.size = new Vector3(1f, 1.1f, 0.9f);
            fb.map.BlockWorld(a.position, new Vector3(1f, 1f, 0.9f));
            var hold = go.AddComponent<HoldAction>();
            hold.promptKey = "int_smash_ice";
            hold.seconds = 4f;
            hold.noiseRadius = 22f;
            hold.condition = () => player.Inventory.Has(ItemId.Hammer);
            hold.deniedText = () => Loc.F("int_need_item", Loc.T(Items.NameKey(ItemId.Hammer)));
            float lastHit = 0f;
            hold.onProgress = t =>
            {
                if (t - lastHit > 0.22f || t < lastHit)
                {
                    lastHit = t;
                    if (AudioManager.I != null) AudioManager.I.PlayAt(AudioManager.Var("ice_hit", 2), a.position + Vector3.up * 0.6f, 1f, 2f, 20f);
                    player.Shake(0.12f, 0.12f);
                    block.transform.localScale = new Vector3(1.0f, 1.1f, 0.9f) * (1f - t * 0.12f);
                }
            };
            hold.onDone = () =>
            {
                iceSmashed = true;
                if (AudioManager.I != null) AudioManager.I.PlayAt("ice_break", a.position + Vector3.up * 0.5f, 1f, 2f, 22f);
                for (int i = 0; i < 8; i++)
                {
                    var chunk = RF.Prim(PrimitiveType.Cube, go.transform, "ice", new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), 0.08f, UnityEngine.Random.Range(-0.6f, 0.6f)), Vector3.one * UnityEngine.Random.Range(0.1f, 0.25f), new Vector3(UnityEngine.Random.Range(0, 90), UnityEngine.Random.Range(0, 90), 0), false);
                }
                block.SetActive(false);
                col.enabled = false;
                Complete(TaskId.ColdRoom, a.position + Vector3.up * 0.02f, 0f);
            };
            hold.AddHitBox(new Vector3(0, 0.55f, 0), new Vector3(1.3f, 1.2f, 1.2f));
        }

        // ================================================================== T7 clock
        void SetupClock()
        {
            var a = fb.anchors["clock_socket"];
            var go = new GameObject("ClockSocket");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(a.position, a.rotation);
            var socket = go.AddComponent<ItemSocket>();
            socket.item = ItemId.Gear;
            socket.required = 3;
            socket.needsPower = true;
            socket.fillVisuals.AddRange(fb.clock.gearVisuals);
            socket.AddHitBox(Vector3.zero, new Vector3(1.4f, 1.6f, 1.2f));
            socket.onInserted = n => { gearsIn = n; Changed(); };
            socket.onComplete = () =>
            {
                var ra = fb.anchors["clock_reward"];
                fb.clock.Activate(() => Complete(TaskId.Clock, ra.position + ra.forward * 1.2f - Vector3.up * 0.98f, ra.eulerAngles.y));
            };
        }

        // ================================================================== safe + gate
        void SetupSafe()
        {
            var safe = fb.safe;
            var go = new GameObject("SafeKeypad");
            go.transform.SetParent(safe.transform, false);
            go.transform.localPosition = new Vector3(-0.2f, 0.5f, 0.55f);
            keypadGo = go;
            var use = go.AddComponent<UseAction>();
            use.AddHitBox(Vector3.zero, new Vector3(1.1f, 1.2f, 0.4f));
            use.query = p =>
            {
                if (SafeOpened) return InteractInfo.Make(Loc.T("int_safe_open"), false);
                return InteractInfo.Make(Loc.T("int_keypad"), true, 0f, "icon_lock");
            };
            use.use = p =>
            {
                if (GameController.I != null) GameController.I.OpenKeypad(TryCode);
            };
        }

        GameObject keypadGo;

        bool TryCode(string entered)
        {
            if (SafeOpened) return true; // never spawn a second keycard
            if (entered != CodeString)
            {
                if (AudioManager.I != null) AudioManager.I.PlayAt("keypad_error", fb.safe.transform.position, 0.9f, 1f, 10f);
                Noise.Emit(fb.safe.transform.position, 9f, NoiseKind.Footstep);
                return false;
            }
            SafeOpened = true;
            // the keypad trigger sits in front of the safe and would swallow the interaction ray -> keycard unreachable
            if (keypadGo != null) foreach (var c in keypadGo.GetComponentsInChildren<Collider>()) c.enabled = false;
            if (AudioManager.I != null) AudioManager.I.PlayAt("keypad_ok", fb.safe.transform.position, 1f, 1f, 10f);
            fb.safe.Open();
            var card = ItemVisuals.SpawnPickup(fb.safe.inside, ItemId.Keycard, fb.safe.inside.position + Vector3.up * 0.03f, fb.safe.transform.eulerAngles.y, false);
            card.onTaken = p => { HasKeycard = true; Hint("tip_keycard"); Changed(); };
            Changed();
            return true;
        }

        void SetupGate()
        {
            var a = fb.anchors["gate_reader"];
            var go = new GameObject("GateReader");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(a.position, a.rotation);
            RF.Prim(PrimitiveType.Cube, go.transform, "screen_green", new Vector3(0, 0.12f, 0.07f), new Vector3(0.22f, 0.08f, 0.02f), default, false);
            RF.Prim(PrimitiveType.Cube, go.transform, "neon_red", new Vector3(0, -0.12f, 0.07f), new Vector3(0.05f, 0.05f, 0.02f), default, false);
            var use = go.AddComponent<UseAction>();
            use.AddHitBox(new Vector3(0, 0, 0.1f), new Vector3(0.7f, 0.8f, 0.5f));
            use.query = p =>
            {
                if (GateOpening) return InteractInfo.Make(Loc.T("int_gate_opening"), false);
                if (!p.Inventory.Has(ItemId.Keycard)) return InteractInfo.Make(Loc.T("int_need_keycard"), false, 0f, "icon_keycard");
                return InteractInfo.Make(Loc.T("int_swipe_card"), true, 0f, "icon_keycard");
            };
            use.use = p =>
            {
                GateOpening = true;
                if (AudioManager.I != null) AudioManager.I.PlayAt("card_swipe", a.position, 1f, 1f, 8f);
                if (GameController.I != null) GameController.I.BeginGateSequence();
                Changed();
            };
        }

        // ================================================================== completion
        void Complete(TaskId id, Vector3 notePos, float yaw, Transform parent = null)
        {
            int i = (int)id;
            if (done[i]) return;
            done[i] = true;
            TasksDone++;
            // v1.8: no note pickup any more - the note is taken automatically 1.4 s later
            PendingNotes.Add(new PendingNote { index = i, at = Time.time + 1.4f });
            if (TasksDone == 2 && !player.HasMask)
            {
                var maskPos = notePos + Quaternion.Euler(0, yaw, 0) * new Vector3(0.45f, 0.05f, 0);
                if (parent != null) maskPos.y = 0.02f; // note sits in a drawer: 0.45 m to the side is mid-air, drop the mask to the floor
                var mask = ItemVisuals.SpawnPickup(root, ItemId.Mask, maskPos, yaw, true);
                mask.onTaken = p => { p.HasMask = true; if (GameController.I != null) GameController.I.Tips.Show("tip_mask", true); };
                Hint("tip_mask_found");
            }
            string ach = id switch
            {
                TaskId.Power => "lights_on",
                TaskId.Carousel => "carousel",
                TaskId.Fortune => "fortune",
                TaskId.GhostTrain => "ghost_train",
                TaskId.Broadcast => "on_air",
                TaskId.ColdRoom => "ice_breaker",
                _ => "clockwork"
            };
            Achievements.Report(ach);
            OnTaskDone?.Invoke(id);
            Changed();
        }

        public void NoteFound(int idx)
        {
            if (idx < 0 || idx >= 7 || notes[idx]) return;
            notes[idx] = true;
            NotesFound++;
            SaveSystem.Data.notesFound++;
            if (NotesFound >= 7) Achievements.Report("all_notes");
            OnNote?.Invoke(idx);
            Changed();
        }

        void Hint(string key)
        {
            if (GameController.I != null) GameController.I.Tips.Show(key, true);
        }

        // ================================================================== journal text
        public string ObjectiveKey()
        {
            if (GateOpening) return "obj_run";
            if (HasKeycard) return "obj_gate";
            if (NotesFound >= 7) return "obj_safe";
            if (!Power.On) return "obj_power";
            return "obj_notes";
        }

        public string TaskLine(TaskId id)
        {
            int i = (int)id;
            string name = TaskName(id);
            if (done[i]) return "<color=#7f8a6a>✓ " + name + "</color>";
            return "• " + name + "  <color=#9a8f80>" + TaskProgress(id) + "</color>";
        }

        // ================================================================== task briefing helpers (v1.1)
        /// <summary>Index of the step the player should focus on: 0 (power) until the power is back, then the first
        /// unfinished task, 7 = all tasks done (open the safe and escape).</summary>
        public int Current()
        {
            if (!done[(int)TaskId.Power]) return 0;
            for (int i = 0; i < 7; i++) if (!done[i]) return i;
            return 7;
        }

        public bool AllTasksDone => TasksDone >= 7;

        /// <summary>Unfinished tasks in order (optionally without one, e.g. the one being briefed).</summary>
        public List<TaskId> OpenTasks(int except = -1)
        {
            var list = new List<TaskId>();
            for (int i = 0; i < 7; i++) if (!done[i] && i != except) list.Add((TaskId)i);
            return list;
        }

        public static string TaskKey(TaskId id) => "task_" + id.ToString().ToLowerInvariant();
        public static string TaskName(TaskId id) => Loc.T(TaskKey(id));
        /// <summary>Detailed "what to do" description for a task.</summary>
        public static string TaskDesc(TaskId id) => Loc.T(TaskKey(id) + "_d");

        /// <summary>Short progress text for a task ("fuses 2/3", "needs the ride key", "Done"...).</summary>
        public string TaskProgress(TaskId id)
        {
            if (done[(int)id]) return Loc.T("task_status_done");
            switch (id)
            {
                case TaskId.Power: return Loc.F("prog_fuses", fusesIn, 3);
                case TaskId.Carousel: return crankUsed ? Loc.T("prog_cranking") : Loc.T("prog_find_crank");
                case TaskId.Fortune: return Loc.F("prog_tokens", tokensIn, 3);
                case TaskId.GhostTrain: return rideKeyUsed ? Loc.T("prog_tunnel") : Loc.T("prog_find_ridekey");
                case TaskId.Broadcast: return tapePlayed ? Loc.T("prog_listen") : Loc.T("prog_find_tape");
                case TaskId.ColdRoom: return Loc.T("prog_cold");
                default: return Loc.F("prog_gears", gearsIn, 3);
            }
        }

        /// <summary>Progress of the final escape step.</summary>
        public string EscapeProgress()
        {
            if (GateOpening) return Loc.T("obj_run");
            if (HasKeycard) return Loc.T("obj_gate");
            if (NotesFound >= 7) return Loc.T("obj_safe");
            return Loc.F("prog_notes", NotesFound, 7);
        }
    }
}
