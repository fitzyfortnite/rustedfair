using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RustedFair
{
    /// <summary>
    /// Builds and runs one night at the fair: world, player, robots, quest, HUD, music/ambience director,
    /// jumpscares, death/respawn with ad gating, and the final escape.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        public static GameController I { get; private set; }

        public FairMap Map => builder.map;

#if UNITY_EDITOR
        // ---- editor-only hooks for "Rusted Fair ▸ Capture Store Screenshots" (not compiled into any build) ----
        public FairBuilder EditorBuilder => builder;
        public RobotAI EditorGrin => grin;
        public void EditorCaptureMode(bool on)
        {
            PlayerSafe = on;
            foreach (var r in robots) r.Freeze(on);
        }
        /// <summary>True once the wake-up (eyelids, blur, exposure) and the briefing are over.</summary>
        public bool EditorIntroDone => IntroDone;
        public RobotAI EditorRobot(RobotKind k) => robots.Find(r => r != null && r.Kind == k);
        public bool EditorCatching => catching;
        /// <summary>"Rusted Fair ▸ Test Jumpscares": runs a robot's catch even while the player is safe.</summary>
        public void EditorForceCatch(RobotAI robot)
        {
            if (catching || robot == null) return;
            PlayerSafe = false;
            catching = true;
            StartCoroutine(JumpscareSequence(robot));
        }
        /// <summary>Closes the death panel and respawns without costing a life.</summary>
        public void EditorRecover()
        {
            Hud.CloseAll();
            Lives = MaxLives;
            StartCoroutine(Respawn());
        }
#endif
        /// <summary>v1.0.2: the wake-up and the briefing are over (the player has control).</summary>
        public bool IntroDone { get; private set; }
        public Quest Quest { get; private set; }
        public HUD Hud { get; private set; }
        public Tips Tips => Hud.tips;
        /// <summary>v1.2 hint director (Tasks screen button, HUD arrow).</summary>
        public HintDirector Hints => hints;
        public PlayerController Player { get; private set; }
        public int Lives { get; private set; }
        public int MaxLives { get; private set; }
        public bool PlayerSafe { get; private set; } = true;
        // v1.8: +0.08 once the player owns the mask, cap 0.48 (v1.6: min(0.4, 0.12 + 0.04n))
        public float DirectorPressure => Mathf.Min(0.48f, 0.12f + Quest.TasksDone * 0.04f + Anger * 0.08f);
        /// <summary>v1.8: 1 once the player owns the mask - robots get a little angrier (speed x1.07, vision and awareness gain x1.12).</summary>
        public float Anger => Player != null && Player.HasMask ? 1f : 0f;

        FairBuilder builder;
        LightSystem lightSystem;
        PostFX postFx;
        readonly List<RobotAI> robots = new List<RobotAI>();
        RobotAI grin, rattle, brutus;
        float runTime;
        int deathsThisRun;
        bool catching, paused, ended, gateOpen;
        float lastSting = -99f;
        float chaseCooldown;
        float ambientTimer = 12f;
        float zoneTimer;
        bool inDarkZone;
        bool firstChaseTip;
        Cubemap reflection;
        AudioSource gateAlarm;
        HintDirector hints;
        float lastWarn = -99f, darkCheck, lastFlashTip = -999f;
        int darkSeconds, flashTips;
        ParticleSystem[] rainSystems;
        bool rainRunning = true;

        float restockT = 75f;

        /// <summary>
        /// v1.6: every 70 s, if fewer than 12 consumables are left on the map, up to 3 new ones (battery x2 weight, oil,
        /// water, toy) appear at free misc spots more than 18 m from the player.
        /// </summary>
        void Restock()
        {
            if (ended || catching) return;
            restockT -= Time.deltaTime;
            if (restockT > 0f) return;
            restockT = 70f;
            int left = 0;
            foreach (var p in Pickup.All) if (p != null && ItemGlint.IsConsumable(p.item)) left++;
            if (left >= 12 || !builder.spots.TryGetValue("misc", out var misc)) return;
            var free = new List<Spot>();
            Vector3 pp = Player.transform.position;
            foreach (var s in misc)
            {
                if ((s.pos - pp).magnitude <= 18f) continue;
                bool taken = false;
                foreach (var p in Pickup.All) if (p != null && (p.transform.position - s.pos).magnitude < 1f) { taken = true; break; }
                if (!taken) free.Add(s);
            }
            ItemId[] pool = { ItemId.Battery, ItemId.Battery, ItemId.Oil, ItemId.Water, ItemId.Toy };
            for (int i = 0; i < 3 && free.Count > 0; i++)
            {
                int k = UnityEngine.Random.Range(0, free.Count);
                var s = free[k];
                free.RemoveAt(k);
                ItemVisuals.SpawnPickup(transform, pool[UnityEngine.Random.Range(0, pool.Length)], s.pos, s.yaw);
            }
        }

        /// <summary>v1.5: the outside rain only simulates while the player is near the main gate.
        /// v1.0.2: anywhere in the entrance hall (z &lt; 22), the only room the open gate is seen from - a paused particle
        /// system keeps drawing its frozen streaks, which looked broken from the start point (z 13.2, facing the gate).</summary>
        void UpdateRain()
        {
            if (rainSystems == null) return;
            bool near = Player.transform.position.z < 22f;
            if (near == rainRunning) return;
            rainRunning = near;
            foreach (var ps in rainSystems) { if (ps == null) continue; if (near) ps.Play(false); else ps.Pause(false); }
        }

        /// <summary>
        /// v1.5 flashlight reminder: once a second; in control, not hidden, flashlight off and the real lights at the
        /// camera add up to &lt; 0.3 for 4 s in a row -> queue-jumping tip (touch / keyboard text). At most 5 times, 70 s apart.
        /// </summary>
        void DarkTick()
        {
            darkCheck -= Time.deltaTime;
            if (darkCheck > 0f) return;
            darkCheck = 1f;
            if (PlayerSafe || paused || ended || catching || Player.ControlLocked || Player.Dead || Player.Hidden != null || Player.Flashlight == null || Player.Flashlight.On)
            {
                darkSeconds = 0;
                return;
            }
            float lux = lightSystem != null ? lightSystem.LuxAt(Player.Cam.transform.position) : 1f;
            if (lux < 0.3f) darkSeconds++; else darkSeconds = 0;
            if (darkSeconds >= 4 && flashTips < 5 && Time.time - lastFlashTip > 70f)
            {
                lastFlashTip = Time.time;
                flashTips++;
                darkSeconds = 0;
                Tips.ShowText(Loc.T(Application.isMobilePlatform ? "tip_flash_touch" : "tip_flash_key"));
            }
        }

        // ================================================================== build
        void Awake()
        {
            I = this;
            Power.Reset();
            Noise.Clear();
            GameEvents.Clear();
            HideSpot.All.Clear();
            OilSlick.All.Clear();
            InputHub.ResetAll();
            Time.timeScale = 1f;
            // robots shove past small furniture instead of getting stuck on it (big props are blocked in the AI grid)
            Physics.IgnoreLayerCollision(RF.LayerRobot, RF.LayerProps, true);
            // v1.4: 3 lives on every difficulty (shown only on the death screen); v1.9: Custom Night = the chosen chances
            MaxLives = GameMode.Custom ? GameMode.Settings.lives : 3;
            Lives = MaxLives;
            BuildEnvironment();

            RobotAI.GlowScale = 1f; // static: never carry a jumpscare value into a new night
            lightSystem = gameObject.AddComponent<LightSystem>();
            lightSystem.webFalloff = true; // v1.0.2: web light values -> URP inverse-square intensities
            builder = new FairBuilder(transform, lightSystem);
            builder.Build(false); // baked after the task items are placed (ClearAroundItems)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // v1.2 self-check: the extra dressing must never cut a quest spot, anchor or room off the A* grid
            var reachLog = new System.Text.StringBuilder();
            int reachProblems = builder.VerifyReachability(reachLog);
            if (reachProblems > 0) Debug.LogWarning("[RF] Reachability self-check: " + reachProblems + " problem(s)\n" + reachLog);
            else RF.Log("[RF] Reachability self-check OK. Dressing: " + builder.DressSummary() + "\n" + reachLog);
#endif

            var start = builder.anchors["player_start"];
            Player = PlayerController.Spawn(transform, start.position, start.eulerAngles.y);
            Player.ControlLocked = true;
            lightSystem.SetViewer(Player.Cam.transform);
            postFx = PostFX.Create(transform, false);

            Quest = new Quest();
            Quest.Setup(builder, Player, transform);
            Quest.OnTaskDone += OnTaskDone;
            Quest.OnNote += OnNote;
            PrepareTaskItems();

            SpawnRobots();
            Hud = HUD.Create(transform, Player, Quest);
            Hud.SetEyelids(0f);
            Hud.SetControlsVisible(false);
            hints = new HintDirector(builder, Quest, Player, Tips);
            OutsideRain();
            RF.Log("[RF] Safe code this night: " + Quest.CodeString); // editor / development builds only
        }

        /// <summary>
        /// v1.4: task items must never be hidden or stuck: lift any that start inside a collider, drop small static
        /// clutter touching them, then bake the static geometry and build the fake light glow.
        /// </summary>
        void PrepareTaskItems()
        {
            var pts = new List<Vector3>();
            var items = new List<Pickup>();
            foreach (var p in Pickup.All)
            {
                if (p == null || !ItemGlint.IsQuestItem(p.item)) continue;
                items.Add(p);
                if (p.InDrawer == null && p.item != ItemId.Crank) builder.LiftOutOfColliders(p.transform);
                pts.Add(p.transform.position);
            }
            int cleared = builder.ClearAroundItems(pts);
            builder.Bake();
            lightSystem.BuildFx(transform, GraphicsQuality.Level > 0);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var log = new System.Text.StringBuilder();
            int bad = builder.VerifyItemVisibility(items, log);
            string msg = "[RF] Task items: " + items.Count + ", clutter removed around them: " + cleared + " loose + " + builder.ClearedStatic + " static";
            if (bad > 0) Debug.LogWarning(msg + ", " + bad + " poorly visible:\n" + log); else RF.Log(msg + ", all visible from >= 6 places.");
#endif
        }

        // ------------------------------------------------------------------ dynamic resolution (v1.4, faster in v1.5)
        float perfAcc;
        int perfN;

        /// <summary>Measures the frame rate over ~0.8 s: below 50 fps the render scale drops by 0.1 (0.2 under 35 fps,
        /// floor 0.5), above 57.5 fps it climbs back by 0.05 up to the quality preset's scale.</summary>
        void AdaptResolution()
        {
            float raw = Time.unscaledDeltaTime;
            if (paused || raw <= 0f || raw > 0.5f) return;
            perfAcc += raw;
            perfN++;
            if (perfAcc < 0.8f) return;
            float fps = perfN / perfAcc;
            perfAcc = 0f;
            perfN = 0;
            float s = GraphicsQuality.DynamicScale;
            if (fps < 50f) s = Mathf.Max(0.5f, s - (fps < 35f ? 0.2f : 0.1f));
            else if (fps > 57.5f && s < 1f) s = Mathf.Min(1f, s + 0.05f);
            if (Mathf.Abs(s - GraphicsQuality.DynamicScale) > 0.001f) GraphicsQuality.SetDynamicScale(s);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            GraphicsQuality.SetDynamicScale(1f); // menu / next run start at the preset's render scale
            if (reflection != null) Destroy(reflection);
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void BuildEnvironment()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // v1.5: darker so the flashlight matters - fog 0.03 (#030307), hemisphere ambient #3A4260 / #120D0A at 0.5.
            // v1.0.2: Three.js r128 uses those hex values as LINEAR colours, while Unity (linear colour space) treats
            // RenderSettings colours as sRGB and linearises them - the old values (x0.184, then ^2.2) ended up ~35x darker
            // than the web. The targets below are linear, converted with .gamma. The ambient is scaled by 0.5 because
            // Neutral tonemapping + post exposure lifts the darks about twice as much as the web's ACES (exposure 1.15).
            // The equator (walls) is the hemisphere light's value for a horizontal normal: the sky/ground average.
            RenderSettings.fogDensity = 0.03f;
            RenderSettings.fogColor = new Color(3f / 255f, 3f / 255f, 7f / 255f).gamma;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            const float amb = 0.5f * 0.5f; // web hemisphere intensity 0.5 x tonemap match 0.5
            var sky = new Color(58f / 255f, 66f / 255f, 96f / 255f) * amb;
            var ground = new Color(18f / 255f, 13f / 255f, 10f / 255f) * amb;
            RenderSettings.ambientSkyColor = sky.gamma;
            RenderSettings.ambientEquatorColor = ((sky + ground) * 0.5f).gamma;
            RenderSettings.ambientGroundColor = ground.gamma;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            reflection = new Cubemap(8, TextureFormat.RGBA32, false);
            var px = new Color[64];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.02f, 0.02f, 0.03f, 1f);
            for (int f = 0; f < 6; f++) reflection.SetPixels(px, (CubemapFace)f);
            reflection.Apply();
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = reflection;
            RenderSettings.reflectionIntensity = 0.6f;
        }

        void SpawnRobots()
        {
            var pods = builder.pods;
            grin = RobotAI.Spawn(transform, RobotKind.Grin, pods[0].position, 180f, Map);
            rattle = RobotAI.Spawn(transform, RobotKind.Rattle, pods[1].position, 180f, Map);
            brutus = RobotAI.Spawn(transform, RobotKind.Brutus, pods[2].position, 180f, Map);
            robots.Add(grin); robots.Add(rattle); robots.Add(brutus);
        }

        void OutsideRain()
        {
            var a = builder.anchors["outside_rain"];
            var go = new GameObject("OutsideRain");
            go.transform.SetParent(transform, false);
            go.transform.position = a.position + Vector3.up * 9f;
            RainFX.Make(go.transform, new Vector3(24f, 1f, 16f), 900);
            rainSystems = go.GetComponentsInChildren<ParticleSystem>(true);
        }

        void Start()
        {
            StartCoroutine(Intro());
            if (AudioManager.I != null)
            {
                AudioManager.I.SetLayer("amb", "amb_hall", 0.6f, 4f, false);
                AudioManager.I.SetLayer("rain", "amb_rain_roof", 0.32f, 4f, false);
            }
            Achievements.Report("wake_up");
        }

        // ================================================================== intro
        IEnumerator Intro()
        {
            PlayerSafe = true;
            postFx.blur = 1f;
            postFx.exposure = -1.5f;
            if (RFApp.I != null) RFApp.I.SetFaderInstant(0f);
            Hud.SetEyelids(0f);
            if (AudioManager.I != null) AudioManager.I.Play2D("wake_up", 0.8f);
            yield return new WaitForSeconds(1.8f);
            if (AudioManager.I != null) AudioManager.I.Play2D("breath_wake", 0.46f);
            yield return Lids(0f, 0.35f, 0.7f);
            yield return Lids(0.35f, 0f, 0.35f);
            yield return new WaitForSeconds(0.7f);
            postFx.blur = 0.7f;
            yield return Lids(0f, 0.75f, 0.9f);
            yield return Lids(0.75f, 0.12f, 0.25f);
            postFx.blur = 0.35f;
            postFx.exposure = -0.5f;
            yield return Lids(0.12f, 1f, 1.3f);
            postFx.blur = 0f;
            postFx.exposure = 0f;
            // v1.1: detailed briefing (goal, the 3 steps, first task) before the player gets control; the game is paused
            bool briefed = false;
            Pause(true, false);
            Hud.ShowBriefing(true, -1, () => briefed = true);
            yield return new WaitUntil(() => briefed); // WaitUntil is polled every frame, also at timeScale 0
            Pause(false, false);
            IntroDone = true;
            Player.ControlLocked = false;
            Hud.SetControlsVisible(true);
            PlayerSafe = false;
            Hud.ShowObjectiveNow();
            Tips.Show("tip_welcome", true);
            yield return new WaitForSeconds(6f);
            yield return PowerSurge();
            yield return new WaitForSeconds(10f);
            // v1.9 wake schedule: story / Custom "normal" = one by one (Rattle after task 1, Brutus after task 3),
            // Custom "task1" = the other two after the first task, Custom "start" = Rattle 6 s and Brutus 8 s later
            grin.Wake();
            if (grin.Level > 0) Tips.Show("tip_footsteps", true);
            if (WakeMode == "start")
            {
                yield return new WaitForSeconds(6f);
                rattle.Wake();
                yield return new WaitForSeconds(8f);
                brutus.Wake();
                yield break;
            }
            if (!GameMode.Custom && SaveSystem.Data.difficulty == 2)
            {
                yield return new WaitForSeconds(100f);
                if (Quest.TasksDone == 0) rattle.Wake();
            }
        }

        IEnumerator Lids(float from, float to, float seconds)
        {
            float t = 0;
            while (t < seconds)
            {
                t += Time.deltaTime;
                Hud.SetEyelids(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / seconds)));
                yield return null;
            }
            Hud.SetEyelids(to);
        }

        IEnumerator PowerSurge()
        {
            var wh = Map.Room(RoomId.Warehouse).Center;
            if (AudioManager.I != null)
            {
                AudioManager.I.PlayAt("power_surge", wh + Vector3.up * 4f, 1f, 30f, 200f);
                AudioManager.I.Play2D("power_surge", 0.35f);
            }
            lightSystem.Surge(3f);
            yield return new WaitForSeconds(1.6f);
            lightSystem.Blackout(1.4f);
            Player.Shake(0.25f, 0.6f);
            if (AudioManager.I != null) AudioManager.I.PlayAt("distant_bang_1", wh, 1f, 30f, 200f);
            yield return new WaitForSeconds(1.6f);
            Tips.Show("tip_surge", true);
        }

        // ================================================================== loop
        void Update()
        {
            if (Player == null) return;
            if (!ended && !paused) runTime += Time.deltaTime;

            // never open the journal/pause on top of an already open panel (would unpause the game and leave AudioListener paused)
            if (!ended && !catching && !paused && !Player.Dead)
            {
                if (InputHub.ConsumePause()) RequestPause();
                if (InputHub.ConsumeNotes()) OpenJournal();
                if (InputHub.ConsumeInventory() && !paused) OpenInventory();
            }
            else { InputHub.ConsumePause(); InputHub.ConsumeNotes(); InputHub.ConsumeInventory(); }
            DeliverPendingNote();

            UpdateFearAndMusic();
            UpdateZones();
            RandomAmbience();
            UpdateFlashlightInterference();
            RobotAI.UpdateSharedGlow(transform, Player.transform.position);
            AdaptResolution();
            DarkTick();
            Restock();
            UpdateRain();
            if (hints != null)
                hints.Tick(ended || catching, SaveSystem.Data.showTips && !PlayerSafe && !paused && !Player.ControlLocked && !Player.Dead);

            if (gateOpen && !ended && !Player.Dead && Player.transform.position.z < -2.8f) StartCoroutine(Escape());
        }

        void UpdateFearAndMusic()
        {
            float fear = 0f;
            bool chasing = false;
            foreach (var r in robots)
            {
                if (!r.Active && r.State != RobotState.Jumpscare) continue;
                float d = (r.transform.position - Player.transform.position).magnitude;
                float f = Mathf.Clamp01(1f - d / 20f) * (r.IsChasing ? 1f : 0.55f);
                fear = Mathf.Max(fear, f);
                if (r.IsChasing) chasing = true;
            }
            Player.Fear = fear;
            postFx.fear = catching ? 1f : fear;
            if (AudioManager.I == null) return;

            if (chasing) chaseCooldown = 4f;
            else chaseCooldown -= Time.deltaTime;
            bool chaseMusic = chaseCooldown > 0f && !catching && !ended;
            if (chaseMusic && !AudioManager.I.LayerPlaying("chase"))
                AudioManager.I.SetLayer("chase", "mus_chase", 0.9f, 0.4f, true);
            else if (!chaseMusic && AudioManager.I.LayerPlaying("chase"))
                AudioManager.I.StopLayer("chase", 3f);

            if (fear > 0.25f && !catching)
            {
                if (!AudioManager.I.LayerPlaying("heart")) AudioManager.I.SetLayer("heart", "heartbeat_loop", 0.0f, 0.5f, false);
                AudioManager.I.SetLayer("heart", "heartbeat_loop", 0.25f + fear * 0.6f, 0.5f, false);
            }
            else if (AudioManager.I.LayerPlaying("heart")) AudioManager.I.StopLayer("heart", 2f);
        }

        void UpdateZones()
        {
            zoneTimer -= Time.deltaTime;
            if (zoneTimer > 0f) return;
            zoneTimer = 0.5f;
            var room = Map.RoomAtPos(Player.transform.position);
            bool dark = room != null && (room.id == RoomId.MirrorMaze || (room.id == RoomId.GhostTrain && Player.transform.position.z > 40f) || room.id == RoomId.ColdRoom);
            if (dark == inDarkZone || AudioManager.I == null) return;
            inDarkZone = dark;
            if (dark)
            {
                AudioManager.I.SetLayer("dark", "amb_dark", 0.7f, 3f, false);
                AudioManager.I.FadeLayer("amb", 0.35f, 3f);
                if (room.id == RoomId.MirrorMaze) Tips.Show("tip_maze");
            }
            else
            {
                AudioManager.I.StopLayer("dark", 3f);
                AudioManager.I.FadeLayer("amb", 1f, 3f);
            }
        }

        void RandomAmbience()
        {
            ambientTimer -= Time.deltaTime;
            if (ambientTimer > 0f || AudioManager.I == null) return;
            ambientTimer = UnityEngine.Random.Range(10f, 24f);
            string[] pool = { "metal_creak_1", "metal_creak_2", "metal_creak_3", "distant_bang_1", "distant_bang_2", "whisper_1", "whisper_2", "servo_2" };
            string clip = pool[UnityEngine.Random.Range(0, pool.Length)];
            var dir = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360f), 0) * Vector3.forward;
            float dist = clip.StartsWith("whisper") ? UnityEngine.Random.Range(3f, 7f) : UnityEngine.Random.Range(14f, 30f);
            AudioManager.I.PlayAt(clip, Player.transform.position + dir * dist + Vector3.up * UnityEngine.Random.Range(1f, 5f), clip.StartsWith("whisper") ? 0.35f : 0.8f, 4f, 60f, 0.1f);
        }

        void UpdateFlashlightInterference()
        {
            float k = 0f;
            foreach (var r in robots)
            {
                if (!r.Active) continue;
                float d = (r.transform.position - Player.transform.position).magnitude;
                k = Mathf.Max(k, Mathf.Clamp01(1f - d / 9f) * (r.IsChasing ? 1f : 0.6f));
            }
            if (!Player.Dead) Player.Flashlight.interference = k;
        }

        /// <summary>v1.9: "normal" (story), "task1" or "start" - only a Custom Night changes it.</summary>
        static string WakeMode => GameMode.Custom ? GameMode.Settings.wake : "normal";

        // ================================================================== events
        void OnTaskDone(TaskId id)
        {
            int n = Quest.TasksDone;
            foreach (var r in robots) r.SetEscalation(n);
            string cw = WakeMode;
            if (n == 1 && cw == "task1")
            {
                rattle.Wake(4f);
                brutus.Wake(4f);
                if (rattle.Level > 0 || brutus.Level > 0) StartCoroutine(DelayedTip("tip_all_awake", 7f));
            }
            if (n == 1 && cw == "normal")
            {
                rattle.Wake(4f);
                if (rattle.Level > 0) StartCoroutine(DelayedTip("tip_rattle_awake", 7f));
            }
            if (n == 3 && cw == "normal")
            {
                brutus.Wake(4f);
                if (brutus.Level > 0) StartCoroutine(DelayedTip("tip_brutus_awake", 7f));
            }
            if (AudioManager.I != null) AudioManager.I.Play2D("ui_note", 0.5f);
            Hud.ShowObjectiveNow();
        }

        IEnumerator DelayedTip(string key, float delay)
        {
            yield return new WaitForSeconds(delay);
            var wh = Map.Room(RoomId.Warehouse).Center;
            if (AudioManager.I != null) AudioManager.I.PlayAt("power_surge", wh + Vector3.up * 3f, 0.8f, 30f, 200f);
            lightSystem.Surge(1.5f);
            Tips.Show(key, true);
        }

        void OnNote(int idx)
        {
            Pause(true, false);
            int found = Quest.NotesFound;
            // note closed -> briefing for the next task (note i belongs to task i) -> unpause -> pickup tips
            Hud.ShowNote(idx, () => Hud.ShowBriefing(false, idx, () =>
            {
                Pause(false, false);
                if (found == 7) Tips.Show("tip_all_notes", true);
                else if (found == 1) Tips.Show("tip_journal", true);
            }));
        }

        /// <summary>
        /// v1.8: a finished task's note is collected automatically 1.4 s after completion (note panel, then the next
        /// briefing) - but not while being caught / dead / climbing into a hide spot / paused / after the end, and not
        /// while a robot is chasing unless the note has been waiting for more than 15 s.
        /// </summary>
        void DeliverPendingNote()
        {
            if (Quest == null || Quest.PendingNotes.Count == 0) return;
            var pn = Quest.PendingNotes[0];
            if (Time.time < pn.at || catching || ended || paused || Player.Dead || Player.ControlLocked || Player.HideAnimating) return;
            if (Time.time - pn.at <= 15f)
                foreach (var r in robots) if (r.IsChasing) return;
            Quest.PendingNotes.RemoveAt(0);
            Quest.NoteFound(pn.index);
        }

        /// <summary>v1.8: the Bag (inventory) screen from the HUD button / I key; pauses the game.</summary>
        public void OpenInventory()
        {
            if (paused || ended || catching || Player == null || Player.Dead) return;
            Pause(true, false);
            Hud.ShowInventory(() => Pause(false, false));
        }

        /// <summary>Opens the Tasks screen (TASKS button / J key).</summary>
        public void OpenJournal()
        {
            Pause(true, false);
            Hud.ShowTasks(() => Pause(false, false));
        }

        public void OpenKeypad(Func<string, bool> tryCode)
        {
            Pause(true, false);
            Hud.OpenKeypad(tryCode, () => Pause(false, false));
        }

        public void OnRobotDetected(RobotAI r)
        {
            // v1.5: big red "Find a place to hide!" (6 s cooldown, not while hidden)
            if (Time.time - lastWarn > 6f && Player.Hidden == null)
            {
                lastWarn = Time.time;
                Hud.Warn(Loc.T("warn_hide"));
            }
            if (Time.time - lastSting > 8f && AudioManager.I != null)
            {
                lastSting = Time.time;
                AudioManager.I.Play2D("detect_sting", 0.9f);
                Player.Shake(0.2f, 0.35f);
            }
            if (!firstChaseTip)
            {
                firstChaseTip = true;
                Tips.Show("tip_run_hide", true);
            }
        }

        public void OnPlayerHid(HideSpot spot)
        {
            foreach (var r in robots) r.WitnessHide(spot);
        }

        public void OnPlayerLeftHide(HideSpot spot) { }

        // ================================================================== pause
        public void RequestPause()
        {
            if (paused || ended || catching || Player == null || Player.Dead) return;
            Pause(true, true);
            Hud.ShowPause(() => Pause(false, true), QuitToMenu);
        }

        void Pause(bool on, bool muteWorld)
        {
            paused = on;
            Time.timeScale = on ? 0f : 1f;
            if (muteWorld) AudioListener.pause = on;
            if (AudioManager.I != null) AudioManager.I.Muffle(on ? 0.7f : (Player.Hidden != null ? 0.55f : 0f));
            InputHub.ResetAll();
            if (on) ReleaseCursor();
        }

        /// <summary>Desktop/editor: a locked mouse-look cursor can't click UI panels (PollDesktop doesn't run at timeScale 0).</summary>
        static void ReleaseCursor()
        {
            InputHub.MouseLook = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void QuitToMenu()
        {
            // the world keeps running during the 0.8 s fade-out: make sure nothing can catch the player meanwhile
            ended = true;
            PlayerSafe = true;
            if (Player != null) Player.ControlLocked = true;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (RFApp.I != null) RFApp.I.GoToMenu();
        }

        // ================================================================== death
        public bool TryCatch(RobotAI robot)
        {
            // robots still tick (dt = 0) while a panel has time frozen; a catch then would leave `paused` stuck true
            if (catching || ended || paused || PlayerSafe || Player.Dead) return false;
            catching = true;
            StartCoroutine(JumpscareSequence(robot));
            return true;
        }

        Light faceLight;

        /// <summary>v1.0.3: warm light at the camera so the face reads even with the flashlight off or flickering.</summary>
        void FaceLight(bool on)
        {
            if (faceLight == null && Player != null && Player.Cam != null)
            {
                var go = new GameObject("JumpscareFaceLight");
                go.transform.SetParent(Player.Cam.transform, false);
                go.transform.localPosition = new Vector3(0f, 0.05f, 0.12f);
                faceLight = go.AddComponent<Light>();
                faceLight.type = LightType.Point;
                faceLight.range = 3.2f;
                faceLight.color = new Color(1f, 0.82f, 0.66f);
                faceLight.shadows = LightShadows.None;
                faceLight.intensity = LightSystem.WebToURP(1.4f, 3.2f, 2f, 0.6f);
            }
            if (faceLight != null) faceLight.enabled = on;
        }

        IEnumerator JumpscareSequence(RobotAI robot)
        {
            Hud.CloseAll();
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Player.Kill();
            Player.Flashlight.interference = 0.2f; // a little flicker, but the face stays lit
            Hud.SetControlsVisible(false);
            foreach (var r in robots) if (r != robot) r.Freeze(true);
            if (AudioManager.I != null)
            {
                AudioManager.I.StopLayer("chase", 0.1f);
                AudioManager.I.StopLayer("heart", 0.1f);
                AudioManager.I.Muffle(0f);
            }
            FaceLight(true);
            RobotAI.GlowScale = 0.15f;
            Player.Flashlight.dim = robot.Kind == RobotKind.Brutus ? 0.2f : 0.5f;
            if (robot.Kind == RobotKind.Brutus) { yield return BrutusGrab(robot); yield break; }

            robot.PoseForJumpscare(Player.Cam.transform);
            if (AudioManager.I != null)
                AudioManager.I.Play2D(robot.Kind == RobotKind.Grin ? "jumpscare_grin" : "jumpscare_rattle", 1f);
            RF.Vibrate();
            Hud.Jumpscare(true);
            postFx.red = 1f;
            Player.Shake(0.35f, 1.7f); // v1.0.3: was 1.4 - shook the face out of the frame
            float baseFov = Player.Cam.fieldOfView;
            float t = 0;
            while (t < 1.7f)
            {
                t += Time.deltaTime;
                // keep the face filling the view
                Vector3 to = robot.Rig.FacePosition - Player.Cam.transform.position;
                var look = Quaternion.LookRotation(to, Vector3.up).eulerAngles;
                float pitch = look.x > 180f ? look.x - 360f : look.x;
                Player.SetLook(look.y, pitch);
                Player.Cam.fieldOfView = Mathf.Lerp(baseFov, 48f, Mathf.Clamp01(t * 4f));
                yield return null;
            }
            if (RFApp.I != null) yield return RFApp.I.Fade(1f, 0.12f);
            yield return AfterCatch(robot);
        }

        /// <summary>v1.0.3: Brutus grabs you, lifts you to his mouth and bites (synced with jumpscare_brutus: bite at 1.85 s).</summary>
        IEnumerator BrutusGrab(RobotAI robot)
        {
            var camT = Player.Cam.transform;
            robot.PoseForGrab(camT);
            var rig = robot.Rig;
            Vector3 start = camT.position;
            Vector3 F = (camT.position - robot.transform.position).Flat().normalized; // robot -> player
            Vector3 right = Vector3.Cross(Vector3.up, F).normalized;
            if (AudioManager.I != null) AudioManager.I.Play2D("jumpscare_brutus", 1f);
            RF.Vibrate();
            Hud.Jumpscare(true);
            postFx.red = 0.6f;
            Player.Shake(0.8f, 0.35f);
            float baseFov = Player.Cam.fieldOfView;
            var paw = new Vector3(0f, -0.62f, 0.03f);
            float t = 0f;
            bool bit = false;
            while (t < 2.2f)
            {
                t += Time.deltaTime;
                Vector3 mouth = rig.MouthPosition;
                Vector3 H0 = robot.transform.position + Vector3.up * 1.95f + F * 0.95f;
                Vector3 H1 = mouth + F * 0.6f, H2 = mouth + F * 0.07f;
                Vector3 c;
                if (t < 0.3f) c = Vector3.Lerp(start, H0, Mathf.SmoothStep(0f, 1f, t / 0.3f));
                else if (t < 1.55f) c = Vector3.Lerp(H0, H1, Mathf.SmoothStep(0f, 1f, (t - 0.3f) / 1.25f));
                else { float k2 = Mathf.Clamp01((t - 1.55f) / 0.3f); c = Vector3.Lerp(H1, H2, k2 * k2); }
                c += new Vector3((Mathf.PerlinNoise(t * 7f, 0f) - 0.5f) * 0.05f, (Mathf.PerlinNoise(0f, t * 6f) - 0.5f) * 0.05f, 0f);
                Player.CamOverride = c;
                Vector3 to = mouth + Vector3.up * 0.05f - c;
                var look = Quaternion.LookRotation(to, Vector3.up).eulerAngles;
                Player.SetLook(look.y, look.x > 180f ? look.x - 360f : look.x);
                Player.Cam.fieldOfView = Mathf.Lerp(baseFov, baseFov - 10f, Mathf.Clamp01(t));
                // paws hold the camera at the lower corners of the view
                Vector3 cf = to.normalized;
                float k = Mathf.SmoothStep(0f, 1f, t / 0.18f);
                RobotRig.IK2(rig.armL, rig.foreL, paw, c + cf * 0.32f - right * 0.31f + Vector3.down * 0.21f, -right * 0.8f + Vector3.down * 0.6f, k);
                RobotRig.IK2(rig.armR, rig.foreR, paw, c + cf * 0.32f + right * 0.31f + Vector3.down * 0.21f, right * 0.8f + Vector3.down * 0.6f, k);
                rig.jaw.localRotation = Quaternion.Euler(bit ? 0f : Mathf.Lerp(7f, 60f, Mathf.SmoothStep(0f, 1f, (t - 0.45f) / 1.1f)), 0f, 0f);
                rig.spine.localRotation *= Quaternion.Euler(-7f * Mathf.SmoothStep(0f, 1f, t / 1.5f), 0f, 0f);
                if (!bit && t >= 1.85f)
                {
                    bit = true;
                    Player.Shake(2f, 0.3f);
                    RF.Vibrate();
                    if (RFApp.I != null) RFApp.I.SetFaderInstant(1f); // the jaws close: black
                }
                yield return null;
            }
            yield return AfterCatch(robot);
        }

        IEnumerator AfterCatch(RobotAI robot)
        {
            FaceLight(false);
            RobotAI.GlowScale = 1f;
            Player.Flashlight.dim = 1f;
            if (AudioManager.I != null) AudioManager.I.Play2D("death_sting", 0.9f);
            Hud.Jumpscare(false);
            postFx.red = 0f;
            Player.CamOverride = null;
            Player.Cam.fieldOfView = SaveSystem.Data.fov;
            robot.EndJumpscare();

            Lives--;
            deathsThisRun++;
            SaveSystem.Data.totalDeaths++;
            Achievements.SetProgress("persistent", SaveSystem.Data.totalDeaths);
            Achievements.Report(robot.Kind == RobotKind.Grin ? "met_grin" : (robot.Kind == RobotKind.Rattle ? "met_rattle" : "met_brutus"));
            SaveSystem.Save();
            if (AdsManager.I != null) AdsManager.I.OnPlayerDied();

            yield return new WaitForSeconds(0.6f);
            if (RFApp.I != null) RFApp.I.SetFaderInstant(0f);
            ReleaseCursor();
            if (Lives <= 0)
            {
                ended = true;
                Hud.ShowEnd(false, StatsText(false), () => AfterAd(QuitToMenu), () => AfterAd(Retry));
            }
            else
            {
                Hud.ShowDeath(robot.DisplayNameKey, Lives, MaxLives, () => AfterAd(() => StartCoroutine(Respawn())), () => AfterAd(QuitToMenu));
            }
        }

        void AfterAd(Action next)
        {
            if (AdsManager.I != null) AdsManager.I.ShowInterstitialIfDue(next);
            else next();
        }

        IEnumerator Respawn()
        {
            if (RFApp.I != null) RFApp.I.SetFaderInstant(1f);
            var sp = builder.anchors["respawn"];
            Player.Respawn(sp.position, sp.eulerAngles.y);
            Player.ControlLocked = true;
            PlayerSafe = true;
            // send robots far away from the respawn point
            foreach (var r in robots)
            {
                Vector3 far = FarPointFrom(sp.position);
                r.ResetTo(far);
                r.Freeze(false);
            }
            Hud.SetEyelids(0f);
            postFx.blur = 0.8f;
            yield return null;
            if (RFApp.I != null) RFApp.I.SetFaderInstant(0f);
            if (AudioManager.I != null) AudioManager.I.Play2D("breath_wake", 0.4f);
            yield return Lids(0f, 0.5f, 0.6f);
            yield return Lids(0.5f, 0.1f, 0.25f);
            postFx.blur = 0.3f;
            yield return Lids(0.1f, 1f, 0.9f);
            postFx.blur = 0f;
            Player.ControlLocked = false;
            Hud.SetControlsVisible(true);
            catching = false;
            yield return new WaitForSeconds(3f);
            PlayerSafe = false;
        }

        Vector3 FarPointFrom(Vector3 p)
        {
            // only rooms a robot can always walk out of (no locked doors, no ride tunnel)
            RoomId[] ok = { RoomId.Warehouse, RoomId.WarehouseOffice, RoomId.ClockGallery, RoomId.Maintenance, RoomId.ServiceHall,
                            RoomId.StaffArea, RoomId.BumperCars, RoomId.BumperOffice, RoomId.Arcade, RoomId.FoodCourt, RoomId.Kitchen };
            for (int i = 0; i < 20; i++)
            {
                var room = Map.Room(ok[UnityEngine.Random.Range(0, ok.Length)]);
                var pt = Map.RandomPointInRoom(room);
                if ((pt - p).magnitude > 55f) return pt;
            }
            return Map.Room(RoomId.Warehouse).Center;
        }

        void Retry()
        {
            if (RFApp.I != null) RFApp.I.StartGame();
        }

        // ================================================================== escape
        public void BeginGateSequence()
        {
            StartCoroutine(GateSequence());
        }

        IEnumerator GateSequence()
        {
            Tips.Show("tip_gate_alarm", true);
            gateAlarm = AudioManager.Attach(builder.exitGate.gameObject, "gate_alarm", 1f, true, 8f, 120f);
            if (lightSystem != null) lightSystem.Surge(10f);
            var gatePos = builder.exitGate.transform.position + Vector3.forward * 2f;
            foreach (var r in robots)
            {
                if (r.State == RobotState.Dormant) r.Wake(1f); // level-0 robots (Custom Night) stay off
            }
            float t = 0;
            bool started = false;
            while (t < 10f)
            {
                t += Time.deltaTime;
                if (Mathf.Repeat(t, 2f) < Time.deltaTime) Noise.Emit(gatePos, 400f, NoiseKind.Alarm);
                if (t > 2f && !started)
                {
                    started = true;
                    builder.exitGate.Open(8f, () => { gateOpen = true; Tips.Show("tip_gate_open", true); });
                }
                yield return null;
            }
        }

        IEnumerator Escape()
        {
            ended = true;
            PlayerSafe = true;
            Player.ControlLocked = true;
            Hud.SetControlsVisible(false);
            foreach (var r in robots) r.Freeze(true);
            if (gateAlarm != null) Destroy(gateAlarm);
            if (AudioManager.I != null)
            {
                AudioManager.I.StopAllLayers(2f);
                AudioManager.I.Play2D("mus_escape", 0.9f);
                AudioManager.I.SetLayer("rainOut", "amb_rain_ext", 0.6f, 2f, false);
            }
            // look up into the rain
            float t = 0;
            while (t < 2.5f)
            {
                t += Time.deltaTime;
                Player.SetLook(Player.transform.eulerAngles.y, Mathf.Lerp(0f, -25f, t / 2.5f));
                Player.transform.position += Player.transform.forward * Time.deltaTime * 1.2f;
                yield return null;
            }
            // results
            var d = SaveSystem.Data;
            d.escapes++;
            int secs = Mathf.RoundToInt(runTime);
            if (d.bestTimeSeconds == 0 || secs < d.bestTimeSeconds) d.bestTimeSeconds = secs;
            bool custom = GameMode.Custom;
            if (!custom && d.difficulty == 2) d.escapesHard++;
            SaveSystem.Save();
            if (RFApp.I != null) yield return RFApp.I.Fade(1f, 1.5f);
            if (RFApp.I != null) RFApp.I.SetFaderInstant(0f);
            ReleaseCursor();
            Hud.ShowEnd(true, StatsText(true), QuitToMenu, Retry);
            // v1.9 (web awardEscape, after the end screen is up): every unlock pops (Toasts); the first escape also unlocks the Custom Night mode
            bool firstEscape = !Achievements.Has("escape");
            Achievements.Report("escape");
            if (deathsThisRun == 0) Achievements.Report("untouchable");
            if (secs < 25 * 60) Achievements.Report("speedrun");
            if (!custom && d.difficulty == 2) Achievements.Report("escape_hard");
            if (custom)
            {
                Achievements.Report("custom");
                var c = GameMode.Settings;
                if (c.grin >= 20 && c.rattle >= 20 && c.brutus >= 20) Achievements.Report("nightmare");
            }
            if (firstEscape) Toasts.ModeUnlocked(0.9f);
        }

        string StatsText(bool escaped)
        {
            int secs = Mathf.RoundToInt(runTime);
            string time = (secs / 60).ToString("00") + ":" + (secs % 60).ToString("00");
            string diff = Loc.T(SaveSystem.Data.difficulty == 0 ? "diff_easy" : SaveSystem.Data.difficulty == 2 ? "diff_hard" : "diff_normal");
            string cn = "";
            if (GameMode.Custom)
            {
                var c = GameMode.Settings;
                cn = Loc.F("stats_custom", c.grin, c.rattle, c.brutus) + "\n"; // v1.9: first line in a Custom Night
            }
            return cn + Loc.F("stats_time", time) + "\n" +
                   Loc.F("stats_deaths", deathsThisRun) + "\n" +
                   Loc.F("stats_notes", Quest.NotesFound) + "\n" +
                   Loc.F("stats_tasks", Quest.TasksDone) + "\n" +
                   Loc.F("stats_difficulty", diff);
        }
    }
}
