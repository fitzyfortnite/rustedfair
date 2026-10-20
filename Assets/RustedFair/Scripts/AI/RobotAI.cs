using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    public enum RobotState { Dormant, Waking, Patrol, Investigate, Chase, Search, Stunned, Slipped, Repelled, CheckHide, Grab, Jumpscare, Frozen }

    /// <summary>
    /// Animatronic hunter AI: sight + hearing, A* pathfinding over the fair grid, door opening/smashing,
    /// hide-spot checks, and reactions to the mask, oil, water and toys.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class RobotAI : MonoBehaviour
    {
        public static readonly List<RobotAI> All = new List<RobotAI>();

        public RobotKind Kind { get; private set; }
        public RobotState State { get; private set; } = RobotState.Dormant;
        public RobotRig Rig { get; private set; }
        public bool IsChasing => State == RobotState.Chase || State == RobotState.Grab;
        public bool Active => State != RobotState.Dormant && State != RobotState.Waking && State != RobotState.Frozen;
        public float LastSawPlayerTime { get; private set; } = -999f;
        public string DisplayNameKey => "robot_" + Kind.ToString().ToLowerInvariant();

        // stats
        float walkSpeed, chaseSpeed, visionRange, fovDeg, hearing, loseSightTime, catchDist;
        bool checksHides, slipImmune;

        CharacterController cc;
        FairMap map;
        PlayerController player;
        readonly List<Vector3> path = new List<Vector3>();
        int pathIndex;
        Vector3 target;
        float stateTimer, repathTimer, senseTimer, voiceTimer, stuckTimer;
        Vector3 lastStuckPos;
        float awareness;
        Vector3 lastSeenPos;
        float vy;
        float speedNow;
        HideSpot hideTarget;
        RobotState resumeState;
        float slowUntil;
        float speedScale = 1f;
        /// <summary>v1.9 Custom Night level 0-20 (story = 10). 0 = this robot never wakes up.</summary>
        public int Level { get; private set; } = 10;
        float gainK = 1f;
        float lungeT;
        Vector3 home;
        float wakeTimer;
        int stuckN;          // consecutive stuck checks (v1.4)
        float ghostT;        // while > 0 the body probes are skipped (only walls / doors / the gate block it)

        // ================================================================== shared eye glow (v1.4)
        static Light sharedGlow;

        /// <summary>One red PointLight follows the eyes of the nearest awake robot within 14 m (saves 2 per-pixel lights).</summary>
        /// <summary>v1.0.3: the glow is a point light at the face; in a jumpscare (camera ~0.5 m away, I/d²) it flooded the
        /// whole view red and hid the face. GameController drops it while the catch plays.</summary>
        public static float GlowScale = 1f;

        public static void UpdateSharedGlow(Transform parent, Vector3 playerPos)
        {
            if (sharedGlow == null)
            {
                var go = new GameObject("RobotEyeGlow");
                go.transform.SetParent(parent, false);
                sharedGlow = go.AddComponent<Light>();
                sharedGlow.type = LightType.Point;
                sharedGlow.color = new Color(1f, 0.08f, 0.04f);
                sharedGlow.shadows = LightShadows.None;
                sharedGlow.intensity = 0f;
            }
            RobotAI best = null;
            float bd = 14f * 14f;
            foreach (var r in All)
            {
                if (r == null || r.Rig == null || r.Rig.GlowIntensity <= 0f) continue;
                float d = (r.transform.position - playerPos).sqrMagnitude;
                if (d < bd) { bd = d; best = r; }
            }
            if (best == null) { if (sharedGlow.enabled) sharedGlow.enabled = false; return; }
            if (!sharedGlow.enabled) sharedGlow.enabled = true;
            sharedGlow.transform.position = best.Rig.glowAnchor.position;
            sharedGlow.intensity = best.Rig.GlowIntensity * GlowScale;
            sharedGlow.range = best.Rig.GlowRange;
        }

        // ================================================================== creation
        public static RobotAI Spawn(Transform parent, RobotKind kind, Vector3 pos, float yaw, FairMap map)
        {
            var go = new GameObject("Robot_" + kind);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.layer = RF.LayerRobot;
            var cc = go.AddComponent<CharacterController>();
            var ai = go.AddComponent<RobotAI>();
            ai.cc = cc;
            ai.map = map;
            ai.Kind = kind;
            ai.home = pos;
            ai.Rig = RobotRig.Build(go.transform, kind);
            ai.Rig.OnFootstep += ai.OnFootstep;
            ai.Configure();
            cc.height = Mathf.Max(1.0f, ai.Rig.Height);
            cc.radius = kind == RobotKind.Brutus ? 0.55f : 0.38f;
            cc.center = new Vector3(0, cc.height * 0.5f, 0);
            cc.stepOffset = 0.25f; // v1.7: robots keep a low step (the player now steps 0.5 m)
            cc.slopeLimit = 50f;
            return ai;
        }

        void Configure()
        {
            // v1.9: in a Custom Night the story difficulty is ignored; the robot's own level scales it instead
            bool custom = GameMode.Custom;
            float diff = custom ? 1f : (SaveSystem.Data.difficulty == 0 ? 0.86f : (SaveSystem.Data.difficulty == 2 ? 1.1f : 1f));
            switch (Kind)
            {
                // v1.7: a bit sharper than v1.6 again (crouching now lowers visibility to 0.45)
                case RobotKind.Grin:
                    walkSpeed = 1.9f; chaseSpeed = 4.3f; visionRange = 18f; fovDeg = 105f; hearing = 0.85f; loseSightTime = 3.6f; catchDist = 1.35f;
                    break;
                case RobotKind.Rattle:
                    walkSpeed = 1.7f; chaseSpeed = 4.6f; visionRange = 11f; fovDeg = 140f; hearing = 1.4f; loseSightTime = 2.6f; catchDist = 1.25f;
                    break;
                default:
                    walkSpeed = 1.5f; chaseSpeed = 3.7f; visionRange = 15f; fovDeg = 90f; hearing = 0.7f; loseSightTime = 6f; catchDist = 1.7f;
                    checksHides = true; slipImmune = true;
                    break;
            }
            walkSpeed *= diff;
            chaseSpeed *= diff;
            visionRange *= Mathf.Lerp(0.85f, 1f, diff);
            Level = custom ? GameMode.Settings.Level(Kind) : 10;
            gainK = 1f;
            if (custom)
            {
                int L = Level;
                float sp = 0.82f + 0.018f * L, sense = 0.6f + 0.04f * L;
                walkSpeed *= sp;
                chaseSpeed *= sp;
                visionRange *= sense;
                hearing *= sense;
                loseSightTime *= 0.7f + 0.03f * L;
                gainK = 0.6f + 0.04f * L;
            }
        }

        void OnEnable()
        {
            All.Add(this);
            Noise.OnNoise += Hear;
        }

        void OnDisable()
        {
            All.Remove(this);
            Noise.OnNoise -= Hear;
            if (LightSystem.I != null) LightSystem.I.interferers.Remove(transform);
        }

        static float Anger => GameController.I != null ? GameController.I.Anger : 0f;

        /// <summary>Escalation: robots get a little faster as the night goes on.</summary>
        public void SetEscalation(int tasksDone) => speedScale = 1f + tasksDone * 0.015f;

        // ================================================================== activation
        public void Wake(float delay = 0f)
        {
            if (Level <= 0) return; // v1.9: switched off in the Custom Night (also ignores the gate alarm)
            if (State != RobotState.Dormant) return;
            wakeTimer = delay;
            SetState(RobotState.Waking);
            stateTimer = 3.2f + delay;
        }

        public void Freeze(bool on)
        {
            if (on)
            {
                if (State == RobotState.Frozen) return;
                resumeState = State == RobotState.Jumpscare ? RobotState.Patrol : State;
                SetState(RobotState.Frozen);
            }
            else if (State == RobotState.Frozen)
            {
                SetState(resumeState == RobotState.Dormant || resumeState == RobotState.Waking ? resumeState : RobotState.Patrol);
            }
        }

        /// <summary>Teleport back home (after the player respawns).</summary>
        public void ResetTo(Vector3 pos)
        {
            // GameController freezes everyone before respawn: look at the state we will resume to
            RobotState eff = State == RobotState.Frozen ? resumeState : State;
            bool asleep = eff == RobotState.Dormant || eff == RobotState.Waking;
            if (eff == RobotState.Dormant) pos = home; // stay slumped in the charging pod
            hideTarget = null;
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
            path.Clear();
            awareness = 0f;
            Rig.jumpscare = 0f;
            Rig.stunned = 0f;
            Rig.slipped = 0f;
            if (State == RobotState.Frozen) resumeState = asleep ? eff : RobotState.Patrol;
            else if (!asleep) SetState(RobotState.Patrol);
        }

        void SetState(RobotState s)
        {
            State = s;
            path.Clear();
            pathIndex = 0;
            repathTimer = 0f;
            if (s == RobotState.Chase && LightSystem.I != null && !LightSystem.I.interferers.Contains(transform))
                LightSystem.I.interferers.Add(transform);
        }

        // ================================================================== update
        void Update()
        {
            if (map == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return; // paused (timeScale 0): no sensing, no catching
            if (player == null) player = PlayerController.I;
            bool running = false;
            float desiredSpeed = 0f;

            switch (State)
            {
                case RobotState.Dormant:
                    Rig.dormant = 1f;
                    Rig.Animate(0f, false, dt);
                    return;

                case RobotState.Waking:
                    stateTimer -= dt;
                    wakeTimer -= dt;
                    if (wakeTimer <= 0f)
                    {
                        Rig.dormant = Mathf.Clamp01(stateTimer / 3.2f);
                        if (Random.value < dt * 3f && AudioManager.I != null)
                            AudioManager.I.PlayAt(AudioManager.Var("servo", 2), transform.position + Vector3.up, 0.8f, 2f, 22f);
                        if (stateTimer < 2.4f && Rig.eyes.Count > 0 && Rig.GlowIntensity <= 0f)
                        {
                            Rig.SetEyes(true);
                            if (AudioManager.I != null) AudioManager.I.PlayAt(VoiceClip, transform.position + Vector3.up * 1.5f, 1f, 3f, 35f);
                        }
                    }
                    Rig.Animate(0f, false, dt);
                    if (stateTimer <= 0f)
                    {
                        Rig.dormant = 0f;
                        Rig.SetEyes(true);
                        SetState(RobotState.Patrol);
                    }
                    return;

                case RobotState.Frozen:
                    Rig.Animate(0f, false, dt);
                    return;

                case RobotState.Jumpscare:
                    Rig.Animate(0f, false, dt);
                    return;
            }

            Sense(dt);
            Voice(dt);

            switch (State)
            {
                case RobotState.Patrol:
                    desiredSpeed = walkSpeed;
                    if (path.Count == 0 || pathIndex >= path.Count)
                    {
                        stateTimer -= dt;
                        if (stateTimer <= 0f) PickPatrolTarget();
                    }
                    break;

                case RobotState.Investigate:
                    desiredSpeed = walkSpeed * 1.35f;
                    if (pathIndex >= path.Count)
                    {
                        stateTimer -= dt;
                        if (stateTimer <= 0f) SetState(RobotState.Patrol);
                    }
                    break;

                case RobotState.Chase:
                    running = true;
                    desiredSpeed = chaseSpeed;
                    if (Kind == RobotKind.Rattle)
                    {
                        lungeT += dt;
                        desiredSpeed *= 0.8f + 0.35f * Mathf.Max(0f, Mathf.Sin(lungeT * 2.3f));
                    }
                    ChaseUpdate(dt);
                    break;

                case RobotState.Search:
                    desiredSpeed = walkSpeed * 1.5f;
                    stateTimer -= dt;
                    repathTimer -= dt;
                    if (pathIndex >= path.Count && repathTimer <= 0f)
                    {
                        repathTimer = 0.6f;
                        // poke around the last known position
                        var p = lastSeenPos + new Vector3(Random.Range(-6f, 6f), 0, Random.Range(-6f, 6f));
                        GoTo(p);
                    }
                    if (stateTimer <= 0f) SetState(RobotState.Patrol);
                    break;

                case RobotState.Stunned:
                case RobotState.Slipped:
                    stateTimer -= dt;
                    float k = Mathf.Clamp01(stateTimer * 2f);
                    if (State == RobotState.Stunned) Rig.stunned = k; else Rig.slipped = k;
                    if (State == RobotState.Stunned && Random.value < dt * 4f && AudioManager.I != null)
                        AudioManager.I.PlayAt(AudioManager.Var("spark", 2), transform.position + Vector3.up * Rig.Height * 0.7f, 0.8f, 1.5f, 16f);
                    if (stateTimer <= 0f)
                    {
                        Rig.stunned = 0f; Rig.slipped = 0f;
                        lastSeenPos = player != null ? player.transform.position : transform.position;
                        SetState(RobotState.Search);
                        stateTimer = 6f;
                    }
                    Rig.Animate(0f, false, dt);
                    Gravity(dt);
                    return;

                case RobotState.Repelled:
                    desiredSpeed = walkSpeed * 1.6f;
                    stateTimer -= dt;
                    if (stateTimer <= 0f) { SetState(RobotState.Patrol); stateTimer = 1f; }
                    break;

                case RobotState.CheckHide:
                    desiredSpeed = walkSpeed;
                    if (hideTarget == null) { SetState(RobotState.Patrol); break; }
                    float hd = (transform.position - hideTarget.exitPoint.position).Flat().sqrMagnitude;
                    if (pathIndex >= path.Count && hd >= 1.1f && hd > 3f * 3f) { hideTarget = null; SetState(RobotState.Patrol); stateTimer = 1f; break; } // unreachable
                    if (hd < 1.1f || pathIndex >= path.Count)
                    {
                        FaceTowards(hideTarget.transform.position, dt);
                        stateTimer -= dt;
                        if (stateTimer <= 0.9f && stateTimer + dt > 0.9f) hideTarget.RobotCheck();
                        if (stateTimer <= 0.5f)
                        {
                            if (hideTarget.Occupant != null && player != null && !player.MaskActive) { Catch(); return; }
                            hideTarget = null;
                            SetState(RobotState.Patrol);
                            stateTimer = 1.5f;
                        }
                        Rig.Animate(0f, false, dt);
                        Gravity(dt);
                        return;
                    }
                    break;

                case RobotState.Grab:
                    running = true;
                    desiredSpeed = chaseSpeed;
                    if (hideTarget == null || hideTarget.Occupant == null) { lastSeenPos = transform.position; SetState(RobotState.Search); stateTimer = 6f; break; }
                    float gd = (transform.position - hideTarget.exitPoint.position).Flat().sqrMagnitude;
                    if (gd < 1.4f || (pathIndex >= path.Count && path.Count > 0 && gd < 3f * 3f))
                    {
                        hideTarget.RobotCheck();
                        if (player != null && !player.MaskActive) { Catch(); return; }
                    }
                    else if (pathIndex >= path.Count)
                    {
                        // path ended (or failed) too far away: retry a few times, then give up
                        repathTimer -= dt;
                        if (repathTimer <= 0f) { repathTimer = 0.5f; GoTo(hideTarget.exitPoint.position); }
                        if (path.Count == 0) { lastSeenPos = hideTarget.exitPoint.position; hideTarget = null; SetState(RobotState.Search); stateTimer = 6f; break; }
                    }
                    break;
            }

            if (Time.time < slowUntil) desiredSpeed *= 0.5f;
            desiredSpeed *= speedScale * (1f + 0.07f * Anger); // v1.8: mask owned -> a little faster
            FollowPath(desiredSpeed, dt);
            OilCheck();
            Rig.Animate(speedNow, running, dt);
        }

        // ================================================================== senses
        string VoiceClip => Kind == RobotKind.Grin ? "robot_voice_grin" : (Kind == RobotKind.Rattle ? "robot_voice_rattle" : "robot_voice_brutus");

        void Voice(float dt)
        {
            voiceTimer -= dt;
            if (voiceTimer > 0f) return;
            voiceTimer = Random.Range(9f, 22f) * (IsChasing ? 0.4f : 1f);
            if (AudioManager.I != null) AudioManager.I.PlayAt(VoiceClip, transform.position + Vector3.up * Rig.Height * 0.85f, 0.9f, 3f, 32f, 0.1f);
        }

        void Sense(float dt)
        {
            senseTimer -= dt;
            if (senseTimer > 0f) return;
            float step = 0.12f;
            senseTimer = step;
            if (player == null || player.Dead) return;
            if (State == RobotState.Repelled || State == RobotState.Stunned || State == RobotState.Slipped) return;
            if (GameController.I != null && GameController.I.PlayerSafe) return;

            Vector3 eye = transform.position + Vector3.up * (Rig.Height * 0.88f);
            Vector3 tgt = player.EyePosition - Vector3.up * 0.35f;
            Vector3 to = tgt - eye;
            float dist = to.magnitude;
            float vis = player.Visibility;

            bool sees = false;
            float ang0 = 1f + 0.12f * Anger; // v1.8: mask owned -> sees a bit further and notices faster
            if (vis > 0f && dist < visionRange * ang0 * (0.55f + 0.45f * Mathf.Min(1.2f, vis)))
            {
                float ang = Vector3.Angle(transform.forward, to.Flat());
                bool inCone = ang < fovDeg * 0.5f || dist < 2.2f;
                if (inCone && !Physics.Linecast(eye, tgt, RF.MaskSight, QueryTriggerInteraction.Ignore)) sees = true;
            }

            if (sees)
            {
                LastSawPlayerTime = Time.time;
                lastSeenPos = player.transform.position;
                if (IsChasing) return;
                float rate = (dist < 2.5f ? 5.5f : 1.0f + 2.9f * Mathf.Max(0f, 1f - dist / visionRange)) * vis * ang0 * gainK; // v1.7, v1.8 anger, v1.9 custom level
                awareness += rate * step;
                // turn towards what we think we saw
                if (awareness > 0.35f && State != RobotState.Chase) FaceTowards(player.transform.position, step * 4f);
                if (awareness >= 1f) StartChase();
            }
            else
            {
                awareness = Mathf.Max(0f, awareness - step * 0.6f); // v1.6: forgets faster (was 0.35/s)
            }
        }

        void Hear(Vector3 pos, float radius, NoiseKind kind)
        {
            if (!Active || State == RobotState.Repelled || State == RobotState.Stunned || State == RobotState.Slipped || State == RobotState.Jumpscare) return;
            if (IsChasing || State == RobotState.CheckHide) return;
            float d = (pos - transform.position).magnitude;
            if (d > radius * hearing) return;
            if (kind == NoiseKind.Footstep && d > 4f && Random.value < 0.35f) return;
            Investigate(pos, kind == NoiseKind.Distraction ? 6f : 3.5f);
        }

        public void Investigate(Vector3 pos, float lookAround)
        {
            if (!Active || IsChasing) return;
            if (State != RobotState.Investigate || (pos - target).sqrMagnitude > 4f)
            {
                SetState(RobotState.Investigate);
                GoTo(pos);
            }
            stateTimer = lookAround;
        }

        void StartChase()
        {
            awareness = 1f;
            bool first = State != RobotState.Chase;
            SetState(RobotState.Chase);
            lungeT = 0f;
            if (first)
            {
                if (AudioManager.I != null) AudioManager.I.PlayAt(VoiceClip, transform.position + Vector3.up * Rig.Height, 1f, 4f, 40f);
                if (GameController.I != null) GameController.I.OnRobotDetected(this);
            }
        }

        void ChaseUpdate(float dt)
        {
            if (player == null || player.Dead) { SetState(RobotState.Patrol); return; }
            if (player.Hidden != null)
            {
                // didn't see them get in? then they're gone.
                if (Time.time - LastSawPlayerTime > 1.2f || player.Hidden.LastEnterTime > LastSawPlayerTime + 0.3f)
                {
                    lastSeenPos = player.Hidden.exitPoint.position;
                    SetState(RobotState.Search);
                    stateTimer = 9f;
                    return;
                }
            }
            if (Time.time - LastSawPlayerTime > loseSightTime)
            {
                SetState(RobotState.Search);
                stateTimer = 7f; // v1.6: was 10
                GoTo(lastSeenPos);
                return;
            }
            Vector3 ppos = player.transform.position;
            float flat = (ppos - transform.position).Flat().magnitude;
            if (flat < catchDist && Mathf.Abs(ppos.y - transform.position.y) < 1.5f && !player.MaskActive)
            {
                Catch();
                return;
            }
            repathTimer -= dt;
            bool direct = Time.time - LastSawPlayerTime < 0.3f && flat < 9f && map.LineClear(transform.position, ppos);
            if (direct)
            {
                path.Clear();
                path.Add(ppos);
                pathIndex = 0;
            }
            else if (repathTimer <= 0f)
            {
                repathTimer = 0.35f;
                GoTo(Time.time - LastSawPlayerTime < 1.5f ? ppos : lastSeenPos);
            }
        }

        // ================================================================== reactions
        public static void RepelAll(Vector3 from, float radius)
        {
            foreach (var r in All)
            {
                if (!r.Active || r.State == RobotState.Jumpscare) continue;
                float d = (r.transform.position - from).magnitude;
                if (d > radius && !(r.IsChasing && d < radius * 1.6f)) continue;
                r.Repel(from);
            }
        }

        void Repel(Vector3 from)
        {
            SetState(RobotState.Repelled);
            stateTimer = 5.5f;
            awareness = 0f;
            LastSawPlayerTime = -999f;
            Vector3 away = (transform.position - from).Flat().normalized;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            GoTo(transform.position + away * 16f);
            if (AudioManager.I != null) AudioManager.I.PlayAt(VoiceClip, transform.position + Vector3.up * Rig.Height, 0.9f, 3f, 30f, 0.15f, 0.8f);
        }

        public static int StunInRadius(Vector3 at, float radius)
        {
            int n = 0;
            foreach (var r in All)
            {
                if (!r.Active || r.State == RobotState.Jumpscare) continue;
                var c = r.transform.position + Vector3.up * 1f;
                if ((c - at).sqrMagnitude > radius * radius && (r.transform.position - at).Flat().sqrMagnitude > radius * radius) continue;
                r.Stun(r.Kind == RobotKind.Brutus ? 6.5f : (r.Kind == RobotKind.Rattle ? 4f : 5f));
                n++;
            }
            return n;
        }

        public void Stun(float seconds)
        {
            SetState(RobotState.Stunned);
            stateTimer = seconds;
            if (AudioManager.I != null) AudioManager.I.PlayAt("stun_zap", transform.position + Vector3.up * Rig.Height * 0.6f, 1f, 3f, 30f);
            SparkBurst();
        }

        void SparkBurst()
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * Rig.Height * 0.7f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // plays on Awake: duration can't be set while playing
            var main = ps.main;
            main.duration = 4f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.gravityModifier = 1.5f;
            main.startColor = new Color(1f, 0.8f, 0.4f);
            main.maxParticles = 120;
            var em = ps.emission;
            em.rateOverTime = 30f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.3f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MatLib.Get("fx_spark");
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            ps.Play();
            Destroy(go, 5f);
        }

        public static int DistractTo(Vector3 pos, float radius)
        {
            int n = 0;
            foreach (var r in All)
            {
                if (!r.Active || r.IsChasing || r.State == RobotState.Repelled || r.State == RobotState.Stunned || r.State == RobotState.Slipped) continue;
                if ((r.transform.position - pos).magnitude > radius) continue;
                r.Investigate(pos, 7f);
                n++;
            }
            return n;
        }

        void OilCheck()
        {
            if (speedNow < 0.5f) return;
            var slick = OilSlick.At(transform.position);
            if (slick == null) return;
            if (slipImmune)
            {
                slowUntil = Time.time + 4f;
                slick.Consume();
                return;
            }
            slick.Consume();
            SetState(RobotState.Slipped);
            stateTimer = 4.5f;
            if (AudioManager.I != null) AudioManager.I.PlayAt("oil_slip", transform.position, 1f, 3f, 30f);
            Noise.Emit(transform.position, 10f, NoiseKind.Loud);
            SaveSystem.Data.robotsSlipped++;
            Achievements.Report("slippery");
        }

        /// <summary>Called when the player climbs into a hiding spot.</summary>
        public void WitnessHide(HideSpot spot)
        {
            if (!Active || player == null) return;
            if (Time.time - LastSawPlayerTime > 1.2f) return;
            hideTarget = spot;
            SetState(RobotState.Grab);
            GoTo(spot.exitPoint.position);
        }

        void Catch()
        {
            if (GameController.I == null) return;
            if (GameController.I.TryCatch(this)) SetState(RobotState.Jumpscare);
        }

        public void EndJumpscare()
        {
            Rig.jumpscare = 0f;
            resumeState = RobotState.Patrol;
            SetState(RobotState.Frozen);
        }

        // ================================================================== patrol logic
        void PickPatrolTarget()
        {
            stateTimer = Random.Range(1.2f, 3.5f);
            // Brutus: sometimes check a nearby hiding place
            if (checksHides && Random.value < 0.4f)
            {
                HideSpot best = null;
                float bestD = 9f * 9f;
                foreach (var h in HideSpot.All)
                {
                    float d = (h.transform.position - transform.position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = h; }
                }
                if (best != null)
                {
                    hideTarget = best;
                    SetState(RobotState.CheckHide);
                    stateTimer = 2.2f;
                    GoTo(best.exitPoint.position);
                    return;
                }
            }
            Vector3 dest;
            var gc = GameController.I;
            if (gc != null && player != null && Random.value < gc.DirectorPressure)
            {
                // drift towards the player's general area (not exactly)
                var room = map.RoomAtPos(player.transform.position);
                dest = room != null ? map.RandomPointInRoom(room) : player.transform.position;
            }
            else
            {
                var room = map.rooms[Random.Range(0, map.rooms.Count)];
                dest = map.RandomPointInRoom(room);
            }
            GoTo(dest);
        }

        // ================================================================== movement
        void GoTo(Vector3 dest)
        {
            target = dest;
            if (!map.FindPath(transform.position, dest, path)) { path.Clear(); }
            pathIndex = path.Count > 1 ? 1 : 0;
        }

        void FollowPath(float speed, float dt)
        {
            Vector3 vel = Vector3.zero;
            if (pathIndex < path.Count)
            {
                Vector3 wp = path[pathIndex];
                Vector3 to = (wp - transform.position).Flat();
                float d = to.magnitude;
                if (d < 0.45f)
                {
                    pathIndex++;
                }
                else
                {
                    Vector3 dir = to / d;
                    vel = dir * speed;
                    FaceTowards(transform.position + dir, dt * 1.5f);
                    OpenDoorsAhead(dir);
                }
            }
            speedNow = Mathf.Lerp(speedNow, vel.magnitude, 1f - Mathf.Exp(-6f * dt));
            Vector3 move = vel.sqrMagnitude > 0.001f ? vel.normalized * speedNow : Vector3.zero;
            if (cc.isGrounded && vy < 0) vy = -2f;
            vy -= 20f * dt;
            cc.Move((move + Vector3.up * vy) * dt);
            BodyResolve();

            // stuck detection (v1.4): every 0.9 s; wanting to move but moved < ~0.35 m -> Unstick()
            stuckTimer += dt;
            if (ghostT > 0f) ghostT -= dt;
            if (stuckTimer > 0.9f)
            {
                if (vel.magnitude > 0.5f && (transform.position - lastStuckPos).Flat().sqrMagnitude < 0.12f) Unstick();
                else stuckN = 0;
                stuckTimer = 0f;
                lastStuckPos = transform.position;
            }
        }

        /// <summary>
        /// Wedged: for 2.2 s skip the body probes (loose props never block robots - they're on an ignored layer - so only
        /// walls, the gate and doors remain) and repath to the chase target / path end. On the 3rd stuck in a row, while
        /// the player can't see it, hop to the next waypoint (if within 3 m) or the centre of its cell.
        /// </summary>
        void Unstick()
        {
            stuckN++;
            ghostT = 2.2f;
            Vector3? tgt = IsChasing && player != null ? player.transform.position : (path.Count > 0 ? path[path.Count - 1] : (Vector3?)null);
            if (stuckN >= 3)
            {
                bool seen = player != null && (player.transform.position - transform.position).magnitude < 14f && map.LineClear(player.transform.position, transform.position);
                if (!seen)
                {
                    var cell = FairMap.CellOf(transform.position);
                    Vector3 dst = FairMap.CellCenter(cell.x, cell.y);
                    if (path.Count > 0)
                    {
                        var wp = path[Mathf.Min(pathIndex, path.Count - 1)];
                        if ((wp - transform.position).Flat().magnitude < 3f) dst = wp;
                    }
                    cc.enabled = false;
                    transform.position = new Vector3(dst.x, transform.position.y, dst.z);
                    cc.enabled = true;
                    stuckN = 0;
                }
            }
            if (tgt.HasValue) GoTo(tgt.Value);
            else GoTo(map.RandomPointInRoom(map.RoomAtPos(transform.position) ?? map.rooms[0]));
        }

        void Gravity(float dt)
        {
            if (cc.isGrounded && vy < 0) vy = -2f;
            vy -= 20f * dt;
            cc.Move(Vector3.up * vy * dt);
            speedNow = 0f;
            BodyResolve(); // turning on the spot (checking a hide spot) can swing the long body into a wall
        }

        // ================================================================== body probes (v1.2)
        // The rigs are bigger than the CharacterController: Rattle crawls ~1.3 m long, Grin's/Brutus' arms swing out
        // to ±0.7–0.9 m. Extra circles in the robot's local frame (x = right, y = forward, z = radius) are pushed out of
        // WALLS and the exit gate only (props are shoved through, doors are walked through; doorways are 2 m wide in
        // the collision, so even Brutus' 1.64 m probe span still passes), and the robot is shifted by each correction.
        static readonly Vector3[] ProbesGrin = { new Vector3(0.4f, 0.05f, 0.3f), new Vector3(-0.4f, 0.05f, 0.3f), new Vector3(0f, 0.22f, 0.3f) };
        static readonly Vector3[] ProbesRattle = { new Vector3(0f, 0.86f, 0.34f), new Vector3(0f, -0.78f, 0.32f), new Vector3(-0.24f, 0.1f, 0.32f) };
        static readonly Vector3[] ProbesBrutus = { new Vector3(0.42f, 0.1f, 0.4f), new Vector3(-0.42f, 0.1f, 0.4f), new Vector3(0f, 0.36f, 0.4f) };
        static readonly Collider[] probeHits = new Collider[16];

        void BodyResolve()
        {
            if (cc == null || !cc.enabled || ghostT > 0f) return;
            var pr = Kind == RobotKind.Grin ? ProbesGrin : (Kind == RobotKind.Rattle ? ProbesRattle : ProbesBrutus);
            Quaternion rot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Vector3 basePos = transform.position;
            Vector3 shift = Vector3.zero;
            for (int it = 0; it < 2; it++)
                for (int i = 0; i < pr.Length; i++)
                {
                    Vector3 c = basePos + shift + rot * new Vector3(pr[i].x, 0f, pr[i].y);
                    shift += WallPush(c, basePos.y, pr[i].z);
                }
            shift.y = 0f;
            if (shift.sqrMagnitude < 1e-6f) return;
            if (shift.sqrMagnitude > 0.36f) shift = shift.normalized * 0.6f; // never teleport
            cc.Move(shift);
        }

        /// <summary>XZ correction that moves a probe circle (centre c, radius rad) out of every wall it overlaps.</summary>
        static Vector3 WallPush(Vector3 c, float feetY, float rad)
        {
            float y0 = feetY + 0.3f + rad, y1 = feetY + Mathf.Max(0.3f + rad, 1.6f); // clear of the floor slab
            int n = Physics.OverlapCapsuleNonAlloc(new Vector3(c.x, y0, c.z), new Vector3(c.x, y1, c.z), rad, probeHits, 1 << RF.LayerDefault, QueryTriggerInteraction.Ignore);
            Vector3 push = Vector3.zero;
            float midY = (y0 + y1) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var col = probeHits[i];
                if (!FairBuilder.IsWallCollider(col)) continue;
                var mid = new Vector3(c.x + push.x, midY, c.z + push.z);
                Vector3 q = col.ClosestPoint(mid);
                Vector3 d = mid - q;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist >= rad) continue;
                if (dist > 1e-4f) { push += d / dist * (rad - dist); continue; }
                // centre inside the box: out along the shallowest axis (walls are axis aligned)
                var bb = col.bounds;
                float px = bb.extents.x - Mathf.Abs(mid.x - bb.center.x), pz = bb.extents.z - Mathf.Abs(mid.z - bb.center.z);
                if (px < pz) push.x += (mid.x >= bb.center.x ? 1f : -1f) * (px + rad);
                else push.z += (mid.z >= bb.center.z ? 1f : -1f) * (pz + rad);
            }
            return push;
        }

        void FaceTowards(Vector3 p, float dt)
        {
            Vector3 d = (p - transform.position).Flat();
            if (d.sqrMagnitude < 0.0001f) return;
            var q = Quaternion.LookRotation(d, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, q, 1f - Mathf.Exp(-8f * dt));
        }

        void OpenDoorsAhead(Vector3 dir)
        {
            foreach (var d in map.AllDoors)
            {
                if (d == null || d.IsOpen || d.locked) continue;
                Vector3 to = d.transform.position - transform.position;
                to.y = 0;
                if (to.sqrMagnitude > 2.6f * 2.6f) continue;
                if (Vector3.Dot(to.normalized, dir) < 0.2f) continue;
                bool violent = Kind == RobotKind.Brutus || IsChasing;
                d.OpenAwayFrom(transform.position, violent);
            }
        }

        void OnFootstep(Vector3 pos)
        {
            if (AudioManager.I == null) return;
            string clip;
            float vol;
            switch (Kind)
            {
                case RobotKind.Grin: clip = AudioManager.Var("fs_grin", 3); vol = IsChasing ? 1f : 0.85f; break;
                case RobotKind.Rattle: clip = AudioManager.Var("fs_rattle", 3); vol = IsChasing ? 0.95f : 0.7f; break;
                default: clip = AudioManager.Var("fs_brutus", 3); vol = 1f; break;
            }
            AudioManager.I.PlayAt(clip, pos, vol, Kind == RobotKind.Brutus ? 5f : 3.5f, 48f, 0.07f);
            if (player != null)
            {
                float d = (player.transform.position - pos).magnitude;
                float shake = Kind == RobotKind.Brutus ? 0.35f : 0.15f;
                if (d < 14f) player.Shake(shake * (1f - d / 14f), 0.18f);
            }
        }

        /// <summary>v1.0.3 (Brutus): stands right in front of the player for the grab - the camera is carried to the mouth.</summary>
        public void PoseForGrab(Transform cam)
        {
            SetState(RobotState.Jumpscare);
            cc.enabled = false;
            Vector3 fwd = cam.forward.Flat().normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = cam.parent != null ? cam.parent.forward : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            Rig.jumpscare = 0f;
            Rig.dormant = 0f;
            Rig.stunned = 0f;
            Rig.slipped = 0f;
            var p = cam.position + fwd * 1.15f;
            p.y = transform.position.y;
            transform.position = p;
            Rig.Animate(0f, false, 0f);
            Rig.SetEyes(true);
            cc.enabled = true;
        }

        /// <summary>Places the robot in front of the camera for the jumpscare.</summary>
        public void PoseForJumpscare(Transform cam)
        {
            SetState(RobotState.Jumpscare);
            cc.enabled = false;
            Vector3 fwd = cam.forward.Flat().normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = cam.parent != null ? cam.parent.forward : Vector3.forward;
            // apply the jumpscare pose first, then frame the FACE (not the root): the lunge pose throws the head
            // ~0.5 m forward, which previously pushed Grin's/Rattle's face through the near plane
            transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            Rig.jumpscare = 1f;
            Rig.dormant = 0f;
            Rig.stunned = 0f;
            Rig.slipped = 0f;
            Rig.Animate(0f, false, 0f);
            Vector3 faceOff = Rig.FacePosition - transform.position;
            float faceDist = Kind == RobotKind.Rattle ? 0.5f : 0.58f; // v1.0.3: measured to the face surface
            var pos = cam.position + fwd * faceDist - faceOff;
            if (Kind == RobotKind.Rattle) pos.y = Mathf.Max(cam.position.y - 1.2f, pos.y - 0.15f);
            transform.position = pos;
            Rig.SetEyes(true);
            cc.enabled = true;
        }
    }
}
