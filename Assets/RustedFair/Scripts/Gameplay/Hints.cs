using System.Text;
using UnityEngine;

namespace RustedFair
{
    /// <summary>
    /// Hint director (v1.2, timings v1.4). When the player makes no progress for a while (40 / 55 / 80 s by difficulty,
    /// ×1.3 each time the same hint repeats, capped ×2.5; never in the first 60 s), a bottom-right tip names the room of
    /// the next thing they need for the current task (same "suggested task" as the Tasks screen) and that item flares
    /// for 45 s (or a pulsing glint marks the place); the HUD arrow points at it for the first 10 s. Any progress clears the mark and restarts the timer.
    /// The Tasks screen's "Give me a hint" button calls Request() (12 s cooldown).
    /// </summary>
    public class HintDirector
    {
        readonly FairBuilder fb;
        readonly Quest quest;
        readonly PlayerController player;
        readonly Tips tips;

        float repeat = 1f, lastProgress, lastHint = -999f, lastAsk = -999f, checkT, startTime;
        string lastText = "", sig = "";
        int count;
        HintGlow mark;        // place marker (items use their own glint)
        Pickup markPickup;    // hinted pickup
        Vector3 markPos;
        bool marked;
        float markUntil;

        /// <summary>Is a hint target active (for the HUD arrow), and where it is.</summary>
        public bool MarkActive => marked;
        float arrowUntil;
        /// <summary>HUD arrow: only for 10 s after a hint (the item's flare lasts 45 s).</summary>
        public bool ArrowActive => marked && Time.time < arrowUntil;
        public Vector3 MarkPos => markPos;
        readonly StringBuilder sb = new StringBuilder(96);

        static readonly ItemId[] Tracked =
        {
            ItemId.Fuse, ItemId.LockerKey, ItemId.Crank, ItemId.Token, ItemId.RideKey, ItemId.Tape,
            ItemId.KitchenKey, ItemId.Hammer, ItemId.Gear, ItemId.Keycard
        };

        public HintDirector(FairBuilder builder, Quest q, PlayerController p, Tips t)
        {
            fb = builder; quest = q; player = p; tips = t;
            lastProgress = startTime = Time.time;
        }

        float Wait()
        {
            float[] w = { 40f, 55f, 80f }; // v1.4: "don't overdo the hints"
            int d = Mathf.Clamp(SaveSystem.Data.difficulty, 0, 2);
            return w[d] * Mathf.Min(2.5f, repeat);
        }

        string Signature()
        {
            var q = quest;
            var inv = player.Inventory;
            sb.Length = 0;
            sb.Append(q.TasksDone).Append(',').Append(q.NotesFound).Append(',').Append(q.fusesIn).Append(',').Append(q.tokensIn).Append(',')
              .Append(q.gearsIn).Append(',').Append(q.crankUsed).Append(',').Append(q.rideKeyUsed).Append(',').Append(q.tapePlayed).Append(',')
              .Append(q.HasKeycard).Append(',').Append(q.SafeOpened).Append(',').Append(Power.On);
            foreach (var id in Tracked) sb.Append(',').Append(inv.Count(id));
            return sb.ToString();
        }

        /// <summary>Called every frame by the GameController. blocked = run ended / jumpscare; canShow = player in control.</summary>
        public void Tick(bool blocked, bool canShow)
        {
            if (blocked || player == null) return;
            checkT -= Time.deltaTime;
            if (checkT <= 0f)
            {
                checkT = 1f;
                var s = Signature();
                if (s != sig)
                {
                    if (sig.Length > 0) ClearMark();
                    sig = s;
                    lastProgress = Time.time;
                }
            }
            if (marked)
            {
                if (Time.time > markUntil || (markPickup == null && mark == null)) ClearMark();
                else if (markPickup != null) markPos = markPickup.transform.position;
            }
            if (!canShow || tips == null) return;
            float w = Wait();
            if (Time.time - startTime > 60f && Time.time - lastProgress > w && Time.time - lastHint > w)
            {
                lastHint = Time.time;
                Give();
            }
        }

        // ------------------------------------------------------------------ what is needed next
        struct Need
        {
            public bool valid, isItem, isNote;
            public ItemId item;
            public Vector3 pos;
            public string act;
        }

        static Need Item(ItemId id) => new Need { valid = true, isItem = true, item = id };

        Need Place(string anchor, string act)
        {
            if (fb.anchors.TryGetValue(anchor, out var a) && a != null) return new Need { valid = true, pos = a.position, act = act };
            return default;
        }

        bool DoorLocked(string key) => fb.doors.TryGetValue(key, out var d) && d != null && d.locked;

        Need Next()
        {
            var q = quest;
            var inv = player.Inventory;
            switch (q.Current())
            {
                case 0:
                    if (q.fusesIn + inv.Count(ItemId.Fuse) < 3) return Item(ItemId.Fuse);
                    if (q.fusesIn < 3) return Place("fusebox", "hint_act_fusebox");
                    return Place("generator_lever", "hint_act_lever");
                case 1:
                    if (!q.crankUsed && !inv.Has(ItemId.Crank))
                        return DoorLocked("locker13") && !inv.Has(ItemId.LockerKey) ? Item(ItemId.LockerKey) : Item(ItemId.Crank);
                    return Place("carousel_booth", "hint_act_carousel");
                case 2:
                    if (q.tokensIn + inv.Count(ItemId.Token) < 3) return Item(ItemId.Token);
                    return Place("fortune_slot", "hint_act_fortune");
                case 3:
                    if (!q.rideKeyUsed && !inv.Has(ItemId.RideKey)) return Item(ItemId.RideKey);
                    if (!q.rideKeyUsed) return Place("ride_panel", "hint_act_ride");
                    return Place("crypt", "hint_act_crypt");
                case 4:
                    if (!q.tapePlayed && !inv.Has(ItemId.Tape)) return Item(ItemId.Tape);
                    return Place("pa_deck", "hint_act_tape");
                case 5:
                    if (DoorLocked("cold") && !inv.Has(ItemId.KitchenKey)) return Item(ItemId.KitchenKey);
                    if (!inv.Has(ItemId.Hammer)) return Item(ItemId.Hammer);
                    return Place("ice_block", "hint_act_ice");
                case 6:
                    if (q.gearsIn + inv.Count(ItemId.Gear) < 3) return Item(ItemId.Gear);
                    return Place("clock_socket", "hint_act_clock");
                default:
                    if (q.NotesFound < 7)
                        foreach (var n in NotePickup.All)
                            if (n != null) return new Need { valid = true, isNote = true, pos = n.transform.position };
                    if (!q.HasKeycard)
                    {
                        if (fb.safe != null) return new Need { valid = true, pos = fb.safe.transform.position, act = "hint_act_safe" };
                        return default;
                    }
                    return Place("gate_reader", "hint_act_gate");
            }
        }

        Pickup NearestPickup(ItemId id)
        {
            Pickup best = null;
            float bd = float.MaxValue;
            Vector3 pp = player.transform.position;
            foreach (var p in Pickup.All)
            {
                if (p == null || p.item != id) continue;
                float d = (p.transform.position - pp).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        string RoomName(Vector3 p)
        {
            var r = fb.map.RoomAtPos(p);
            return r != null ? Loc.T("room_" + r.id) : "?";
        }

        /// <summary>Lower-case an item name for the middle of a sentence with the language's casing rules
        /// (Turkish dotted/dotless i handled by hand like Loc.Upper, so it is IL2CPP / invariant-globalization safe).</summary>
        static string Lower(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            switch (Loc.Code)
            {
                case "de": return s; // German nouns keep their capital letter
                case "ja": case "ko": case "zh": return s;
                case "tr": return s.Replace('I', 'ı').Replace('İ', 'i').ToLowerInvariant();
                default: return s.ToLowerInvariant(); // identical to the culture rules for en/es/fr/it/pt/ru/pl/id/vi (Vietnamese diacritics survive)
            }
        }

        bool Give()
        {
            var n = Next();
            if (!n.valid) return false;
            string text;
            Vector3 pos = n.pos;
            Pickup glow = null;
            if (n.isItem)
            {
                var f = NearestPickup(n.item);
                if (f == null) return false;
                pos = f.transform.position;
                string name = Loc.T(Items.NameKey(n.item)), room = RoomName(pos);
                var drawer = f.InDrawer;
                if (n.item == ItemId.Crank) text = Loc.F("hint_item_locker", name, room);
                else if (drawer != null && !drawer.IsOpen) text = Loc.F("hint_item_drawer", Lower(name), room);
                else { text = Loc.F("hint_item", Lower(name), room); glow = f; }
            }
            else if (n.isNote) text = Loc.F("hint_note", RoomName(pos));
            else text = Loc.F("hint_place", RoomName(pos), Loc.T(n.act));

            repeat = text == lastText ? repeat * 1.3f : 1f;
            lastText = text;
            tips.ShowText(text);
            ClearMark();
            markUntil = Time.time + 45f;
            arrowUntil = Time.time + 10f; // the HUD arrow only shows for the first 10 s
            if (glow != null) { markPickup = glow; glow.hintUntil = markUntil; markPos = glow.transform.position; }
            else { mark = HintGlow.Create(GameController.I != null ? GameController.I.transform : null, pos + Vector3.up * 0.35f, 1f); markPos = pos; }
            marked = true;
            count++;
            return true;
        }

        /// <summary>"Give me a hint" (Tasks screen). Returns null when a hint was given, otherwise the message to show.</summary>
        public string Request()
        {
            int left = Mathf.CeilToInt(12f - (Time.time - lastAsk));
            if (left > 0) return Loc.F("hint_wait", left);
            lastAsk = lastHint = Time.time;
            return Give() ? null : Loc.T("hint_none");
        }

        void ClearMark()
        {
            if (mark != null) Object.Destroy(mark.gameObject);
            if (markPickup != null) markPickup.hintUntil = -1f;
            mark = null;
            markPickup = null;
            marked = false;
        }
    }

    /// <summary>
    /// Warm-gold glint of task items (fuse, keys, crank, tokens, tape, hammer, gears, keycard, mask): pulses harder than
    /// the faint sparkle of consumables (opacity 0.5–0.9, scale 0.5–0.7) and flares while hinted (0.75–1, 0.9–1.7, faster).
    /// </summary>
    public class ItemGlint : MonoBehaviour
    {
        Pickup pickup;
        Renderer quad, beam;
        Light glowLight;
        MaterialPropertyBlock mpb, beamMpb;
        float t;
        bool strong;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color Gold = new Color(1f, 0.824f, 0.478f); // #FFD27A
        static readonly Color BeamGold = new Color(1f, 0.812f, 0.451f); // #FFCF73
        static Material beamMat;

        /// <summary>Fuses, tokens and gears (the ones hidden around the fair) get the big beam + glint.</summary>
        public static bool IsStrong(ItemId id) => id == ItemId.Fuse || id == ItemId.Token || id == ItemId.Gear;

        /// <summary>Additive beam texture: soft horizontally, bright at the bottom fading upward.</summary>
        static Material BeamMaterial()
        {
            if (beamMat != null) return beamMat;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "fx_beam" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                float a = Mathf.Pow(1f - y / (float)(n - 1), 1.6f);
                for (int x = 0; x < n; x++)
                {
                    float h = 1f - Mathf.Abs((x + 0.5f) / n - 0.5f) * 2f;
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(a * h * 255f), 0, 255);
                    px[y * n + x] = new Color32(b, b, b, b);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            beamMat = MatLib.Create(new MatSpec { name = "fx_beam", tex = "fx", shader = MatShader.ParticleAdd, color = Color.white }, _ => tex);
            if (beamMat.HasProperty("_Cull")) beamMat.SetFloat("_Cull", 0f);
            return beamMat;
        }

        public static bool IsQuestItem(ItemId id) =>
            id == ItemId.Fuse || id == ItemId.LockerKey || id == ItemId.Crank || id == ItemId.Token || id == ItemId.RideKey || id == ItemId.Tape ||
            id == ItemId.KitchenKey || id == ItemId.Hammer || id == ItemId.Gear || id == ItemId.Keycard || id == ItemId.Mask;

        /// <summary>v1.6: batteries, oil, water balloons and toys get a cool white-blue glint and a small blue beam.</summary>
        public static bool IsConsumable(ItemId id) => id == ItemId.Battery || id == ItemId.Oil || id == ItemId.Water || id == ItemId.Toy;

        // consumables share two materials pulsed once per frame (no property blocks -> they still batch)
        static Material consGlintMat, consBeamMat;
        static int consPulseFrame = -1;
        static readonly Color ConsGlint = new Color(0.749f, 0.902f, 1f); // #BFE6FF
        static readonly Color ConsBeam = new Color(0.624f, 0.847f, 1f);  // #9FD8FF
        bool consumable;

        public static ItemGlint Add(Pickup p)
        {
            bool cons = IsConsumable(p.item);
            var go = new GameObject(cons ? "ItemGlint" : "QuestGlint");
            go.transform.SetParent(p.transform, false);
            go.transform.localPosition = new Vector3(0f, cons ? 0.14f : 0.16f, 0f);
            var g = go.AddComponent<ItemGlint>();
            g.pickup = p;
            g.consumable = cons;
            g.t = Random.value * 9f;
            var q = RF.Prim(PrimitiveType.Quad, go.transform, "fx_glow", Vector3.zero, Vector3.one * 0.5f, default, false);
            g.quad = q.GetComponent<Renderer>();
            g.quad.receiveShadows = false;
            g.mpb = new MaterialPropertyBlock();
            // v1.4: a tall vertical light beam (turns only around Y towards the camera) so task items read from afar
            g.strong = !cons && IsStrong(p.item);
            var bgo = new GameObject("Beam");
            bgo.transform.SetParent(p.transform, false);
            bgo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            var bq = RF.Prim(PrimitiveType.Quad, bgo.transform, "fx_glow", Vector3.zero, Vector3.one, default, false);
            float bw = cons ? 0.26f : (g.strong ? 0.42f : 0.3f), bh = cons ? 1.0f : (g.strong ? 1.9f : 1.3f);
            bq.transform.localPosition = new Vector3(0f, bh * 0.5f, 0f);
            bq.transform.localScale = new Vector3(bw, bh, 1f);
            g.beam = bq.GetComponent<Renderer>();
            g.beam.sharedMaterial = BeamMaterial();
            if (cons)
            {
                if (consGlintMat == null) consGlintMat = new Material(MatLib.Get("fx_glow")) { name = "fx_glow_consumable" };
                if (consBeamMat == null) consBeamMat = new Material(BeamMaterial()) { name = "fx_beam_consumable" };
                g.quad.sharedMaterial = consGlintMat;
                g.beam.sharedMaterial = consBeamMat;
            }
            g.beam.receiveShadows = false;
            g.beamMpb = new MaterialPropertyBlock();
            var lgo = new GameObject("FlareLight");
            lgo.transform.SetParent(go.transform, false);
            g.glowLight = lgo.AddComponent<Light>();
            g.glowLight.type = LightType.Point;
            g.glowLight.color = Gold;
            g.glowLight.range = 2.2f;
            g.glowLight.intensity = 0f;
            g.glowLight.shadows = LightShadows.None;
            g.glowLight.enabled = false; // only while hinted
            return g;
        }

        void LateUpdate()
        {
            if (consumable) { ConsumableUpdate(); return; }
            t += Time.deltaTime;
            bool hinted = pickup != null && Time.time < pickup.hintUntil;
            float f = 0.5f + 0.5f * Mathf.Sin(t * (hinted ? 6f : 3f));
            float alpha = hinted ? 0.75f + 0.25f * f : 0.6f + 0.4f * f;
            float scale = hinted ? 1.0f + 0.8f * f : (strong ? 0.7f + 0.3f * f : 0.5f + 0.2f * f);
            var cam = Camera.main;
            if (cam != null)
            {
                quad.transform.rotation = cam.transform.rotation;
                if (beam != null)
                {
                    Vector3 to = cam.transform.position - beam.transform.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 1e-4f) beam.transform.parent.rotation = Quaternion.LookRotation(-to, Vector3.up);
                }
            }
            if (beam != null)
            {
                float ba = (strong ? 0.45f : 0.25f) + (hinted ? 0.4f : 0.15f) * f;
                beam.GetPropertyBlock(beamMpb);
                beamMpb.SetColor(BaseColor, new Color(BeamGold.r, BeamGold.g, BeamGold.b, Mathf.Clamp01(ba)));
                beam.SetPropertyBlock(beamMpb);
            }
            quad.transform.localScale = Vector3.one * scale;
            quad.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColor, new Color(Gold.r, Gold.g, Gold.b, alpha));
            quad.SetPropertyBlock(mpb);
            if (glowLight.enabled != hinted) glowLight.enabled = hinted;
            if (hinted) glowLight.intensity = 0.35f + 0.75f * f;
        }

        /// <summary>Consumables: opacity 0.55–0.95 / scale 0.45–0.65 glint, beam 0.2–0.35 (shared materials).</summary>
        void ConsumableUpdate()
        {
            float f = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f);
            if (consPulseFrame != Time.frameCount)
            {
                consPulseFrame = Time.frameCount;
                consGlintMat.SetColor(BaseColor, new Color(ConsGlint.r, ConsGlint.g, ConsGlint.b, 0.55f + 0.4f * f));
                consBeamMat.SetColor(BaseColor, new Color(ConsBeam.r, ConsBeam.g, ConsBeam.b, 0.2f + 0.15f * f));
            }
            var cam = Camera.main;
            if (cam != null)
            {
                quad.transform.rotation = cam.transform.rotation;
                Vector3 to = cam.transform.position - beam.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-4f) beam.transform.parent.rotation = Quaternion.LookRotation(-to, Vector3.up);
            }
            quad.transform.localScale = Vector3.one * (0.45f + 0.2f * f);
            if (glowLight.enabled) glowLight.enabled = false;
        }
    }

    /// <summary>Pulsing additive glint (camera-facing) with a faint light: marks a hinted place.</summary>
    public class HintGlow : MonoBehaviour
    {
        Renderer quad;
        Light glowLight;
        MaterialPropertyBlock mpb;
        float baseSize = 1f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public static HintGlow Create(Transform parent, Vector3 worldPos, float size)
        {
            var go = new GameObject("HintGlow");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            var g = go.AddComponent<HintGlow>();
            g.baseSize = size;
            var q = RF.Prim(PrimitiveType.Quad, go.transform, "fx_glow", Vector3.zero, Vector3.one * 0.6f * size, default, false);
            q.name = "Glint";
            g.quad = q.GetComponent<Renderer>();
            g.quad.receiveShadows = false;
            var lgo = new GameObject("GlintLight");
            lgo.transform.SetParent(go.transform, false);
            g.glowLight = lgo.AddComponent<Light>();
            g.glowLight.type = LightType.Point;
            g.glowLight.color = new Color(1f, 0.85f, 0.6f);
            g.glowLight.range = 2.2f;
            g.glowLight.intensity = 0f;
            g.glowLight.shadows = LightShadows.None;
            g.mpb = new MaterialPropertyBlock();
            return g;
        }

        void LateUpdate()
        {
            float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            var cam = Camera.main;
            if (cam != null) quad.transform.rotation = cam.transform.rotation; // billboard
            quad.transform.localScale = Vector3.one * baseSize * (0.6f + k * 0.6f);
            quad.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColor, new Color(1f, 0.824f, 0.478f, 0.55f + 0.45f * k));
            quad.SetPropertyBlock(mpb);
            glowLight.intensity = 0.35f + 0.75f * k;
        }
    }
}
