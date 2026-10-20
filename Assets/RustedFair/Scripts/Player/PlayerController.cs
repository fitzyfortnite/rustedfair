using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RustedFair
{
    /// <summary>
    /// First-person survivor: walk / sprint (with exhaustion) / crouch, flashlight, interaction (tap + hold),
    /// hiding, the repelling mask, and throwables to slow the robots.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController I { get; private set; }

        // ---- tuning ----
        public const float WalkSpeed = 3.1f;
        public const float SprintSpeed = 5.9f;
        public const float CrouchSpeed = 1.6f;
        public const float StandHeight = 1.75f;
        public const float CrouchHeight = 1.05f;
        public const float SprintDuration = 10f;    // v1.6: seconds of full stamina (was 6.5)
        public const float StaminaRegen = 1f / 5f;  // v1.6: full in 5 s (was 5.5), x1.5 when standing still
        public const float MaskDuration = 3.5f;
        public const float MaskCooldown = 15f;
        public const float MaskRadius = 18f;

        public CharacterController cc;
        public Camera Cam { get; private set; }
        public Transform head, camOffset;
        public Inventory Inventory { get; } = new Inventory();
        public FlashlightController Flashlight { get; private set; }

        public float Stamina { get; private set; } = 1f;
        public bool Exhausted { get; private set; }
        public bool Sprinting { get; private set; }
        public bool Crouching { get; private set; }
        public HideSpot Hidden { get; private set; }
        /// <summary>Climbing into a hide spot (the camera is still moving in).</summary>
        public bool HideAnimating => hideRoutine != null;
        public bool Dead { get; private set; }
        public bool ControlLocked;
        public bool HasMask;
        public float MaskCooldownLeft { get; private set; }
        public bool MaskActive => Time.time < maskActiveUntil;
        public float Fear;                      // 0..1 (set by GameController)
        public ItemId SelectedThrowable = ItemId.None;

        public Interactable Focus { get; private set; }
        public InteractInfo FocusInfo { get; private set; }
        public float HoldProgress { get; private set; }

        float yaw, pitch;
        float vy;
        Vector3 velocity;
        float stepDist;
        float bobT;
        float regenDelay;
        float shakeAmp, shakeTime;
        float maskActiveUntil;
        float currentHeight = StandHeight;
        AudioSource breath;
        AudioClip breathClip;
        float breathCD;
        bool holding;
        Interactable holdTarget;
        float hideYawCenter;
        Coroutine hideRoutine;
        float noiseTimer;

        public static PlayerController Spawn(Transform parent, Vector3 pos, float yawDeg)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(parent, false);
            go.transform.position = pos + Vector3.up * 0.05f;
            go.layer = RF.LayerPlayer;
            var cc = go.AddComponent<CharacterController>();
            cc.height = StandHeight;
            cc.radius = 0.32f;
            cc.center = new Vector3(0, StandHeight * 0.5f, 0);
            cc.stepOffset = 0.5f; // v1.7: can step onto low surfaces (< 0.5 m), e.g. the stage via its 0.44 m step
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.04f;
            var p = go.AddComponent<PlayerController>();
            p.cc = cc;
            p.head = RF.Child(go.transform, "Head", new Vector3(0, StandHeight - 0.13f, 0)).transform;
            p.camOffset = RF.Child(p.head, "CamOffset").transform;
            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(p.camOffset, false);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 62f; // v1.4: fog (0.026) hides it; less to draw
            cam.fieldOfView = SaveSystem.Data.fov;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.005f, 0.006f, 0.01f);
            var urp = cam.GetUniversalAdditionalCameraData();
            urp.renderPostProcessing = true;
            urp.antialiasing = GraphicsQuality.Level >= 2 ? AntialiasingMode.None : AntialiasingMode.FastApproximateAntialiasing;
            urp.renderShadows = true;
            var listener = camGo.AddComponent<AudioListener>();
            if (AudioManager.I != null) AudioManager.I.RegisterListener(listener);
            p.Cam = cam;
            p.Flashlight = FlashlightController.Create(camGo.transform);
            p.yaw = yawDeg;
            go.transform.rotation = Quaternion.Euler(0, yawDeg, 0);
            return p;
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        // ================================================================== update
        void Update()
        {
            // poll first: while paused (dt == 0) the cursor must still unlock / Esc must still register
            InputHub.PollDesktop(SaveSystem.Data.sensitivity);
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (MaskCooldownLeft > 0f) MaskCooldownLeft = Mathf.Max(0f, MaskCooldownLeft - dt);
            Flashlight.TickBattery(Inventory);

            if (Dead || ControlLocked)
            {
                InputHub.ConsumeLook();
                UpdateCamera(dt, false);
                return;
            }

            if (InputHub.ConsumeFlash()) Flashlight.Toggle();
            if (InputHub.ConsumeMask()) UseMask();
            if (InputHub.ConsumeCycle()) CycleThrowable();
            if (InputHub.ConsumeThrow()) ThrowSelected();

            Look(dt);
            if (Hidden == null)
            {
                if (InputHub.ConsumeCrouch()) ToggleCrouch();
                Move(dt);
            }
            else
            {
                InputHub.ConsumeCrouch();
            }
            UpdateInteraction(dt);
            UpdateStaminaAudio();
            UpdateCamera(dt, Hidden == null);
        }

        void Look(float dt)
        {
            var d = InputHub.ConsumeLook();
            yaw += d.x;
            pitch -= d.y * (SaveSystem.Data.invertY ? -1f : 1f);
            if (Hidden != null)
            {
                yaw = hideYawCenter + Mathf.Clamp(Mathf.DeltaAngle(hideYawCenter, yaw), -55f, 55f);
                pitch = Mathf.Clamp(pitch, -35f, 35f);
            }
            else pitch = Mathf.Clamp(pitch, -82f, 82f);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            head.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        void Move(float dt)
        {
            Vector2 mv = InputHub.Move;
            float mag = Mathf.Clamp01(mv.magnitude);
            bool wantSprint = InputHub.Sprint && mv.y > 0.25f && mag > 0.4f && !Crouching && !Exhausted;
            if (wantSprint && Crouching) ToggleCrouch();
            Sprinting = wantSprint && Stamina > 0f;

            // stamina
            if (Sprinting)
            {
                Stamina -= dt / SprintDuration;
                regenDelay = 0.9f;
                if (Stamina <= 0f)
                {
                    Stamina = 0f;
                    Exhausted = true;
                    Sprinting = false;
                    if (GameController.I != null) GameController.I.Tips.Show("tip_exhausted");
                }
            }
            else
            {
                regenDelay -= dt;
                if (regenDelay <= 0f) Stamina = Mathf.Min(1f, Stamina + dt * StaminaRegen * (mag < 0.1f ? 1.5f : 1f));
                if (Exhausted && Stamina >= 0.4f) Exhausted = false;
            }

            float speed = Crouching ? CrouchSpeed : (Sprinting ? SprintSpeed : WalkSpeed);
            if (Exhausted) speed *= 0.82f;
            Vector3 wish = (transform.right * mv.x + transform.forward * mv.y);
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            Vector3 target = wish * speed;
            velocity = Vector3.Lerp(velocity, target, 1f - Mathf.Exp(-(target.sqrMagnitude > velocity.sqrMagnitude ? 12f : 9f) * dt));

            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy -= 18f * dt; // v1.7: 18 m/s² when walking off an edge (stage, steps)
            cc.Move((velocity + Vector3.up * vy) * dt);

            // crouch height
            float targetH = Crouching ? CrouchHeight : StandHeight;
            if (!Mathf.Approximately(currentHeight, targetH))
            {
                currentHeight = Mathf.MoveTowards(currentHeight, targetH, dt * 4f);
                cc.height = currentHeight;
                cc.center = new Vector3(0, currentHeight * 0.5f, 0);
            }

            // footsteps
            float horiz = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
            if (cc.isGrounded && horiz > 0.3f)
            {
                stepDist += horiz * dt;
                float stride = Sprinting ? 2.1f : (Crouching ? 1.1f : 1.55f);
                if (stepDist >= stride)
                {
                    stepDist = 0f;
                    Footstep();
                }
                bobT += dt * horiz * (Sprinting ? 1.25f : 1.45f);
            }
        }

        void Footstep()
        {
            var room = GameController.I != null ? GameController.I.Map.RoomAtPos(transform.position) : null;
            bool metal = room != null && room.floor == "floor_grate";
            string clip = metal ? AudioManager.Var("fs_player_metal", 2) : AudioManager.Var("fs_player_concrete", 4);
            float vol = Crouching ? 0.25f : (Sprinting ? 0.9f : 0.55f);
            if (AudioManager.I != null) AudioManager.I.PlayAt(clip, transform.position, vol, 1f, 12f, 0.08f);
            float radius = Crouching ? 1.5f : (Sprinting ? 11f : 4.5f); // v1.6: quieter (was 1.8 / 15 / 6)
            Noise.Emit(transform.position, radius, NoiseKind.Footstep);
        }

        void ToggleCrouch()
        {
            if (Crouching)
            {
                // check headroom before standing
                var top = transform.position + Vector3.up * (CrouchHeight - 0.1f);
                if (Physics.SphereCast(top, 0.28f, Vector3.up, out _, StandHeight - CrouchHeight + 0.05f, RF.MaskWorld, QueryTriggerInteraction.Ignore)) return;
                Crouching = false;
            }
            else Crouching = true;
        }

        /// <summary>
        /// v1.8.1: calmer recovery breathing - only while actually exhausted (not when stamina merely dips under 25 %),
        /// quiet (0.10, was 0.22), slightly slowed (pitch 0.9), and a 2.2 s pause after each breath clip.
        /// </summary>
        void UpdateStaminaAudio()
        {
            breathCD -= Time.deltaTime;
            // pooled source: finished, or already reused for another clip -> the breath is over
            if (breath != null && (!breath.isPlaying || breath.clip != breathClip)) { breath = null; breathCD = 2.2f; }
            if (Exhausted && breath == null && breathCD <= 0f && AudioManager.I != null)
            {
                breath = AudioManager.I.Play2D("breath_heavy", 0.10f, 0.9f);
                breathClip = breath != null ? breath.clip : null;
            }
        }

        // ================================================================== camera
        void UpdateCamera(float dt, bool bob)
        {
            Vector3 offset = Vector3.zero;
            if (bob && Hidden == null)
            {
                float amp = Sprinting ? 0.055f : (Crouching ? 0.015f : 0.03f);
                float horiz = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
                float k = Mathf.Clamp01(horiz / WalkSpeed);
                offset += new Vector3(Mathf.Cos(bobT * Mathf.PI) * amp * 0.6f, Mathf.Abs(Mathf.Sin(bobT * Mathf.PI)) * amp, 0) * k;
                if (Sprinting)
                {
                    // running shake
                    offset += new Vector3(Mathf.PerlinNoise(Time.time * 9f, 0) - 0.5f, Mathf.PerlinNoise(0, Time.time * 9f) - 0.5f, 0) * 0.035f;
                }
            }
            if (Exhausted) offset += new Vector3(0, Mathf.Sin(Time.time * 4.5f) * 0.012f, 0);
            if (shakeTime > 0f)
            {
                shakeTime -= dt;
                float a = shakeAmp * Mathf.Clamp01(shakeTime * 3f);
                offset += new Vector3(Random.Range(-a, a), Random.Range(-a, a), 0) * 0.25f;
                camOffset.localRotation = Quaternion.Euler(Random.Range(-a, a) * 6f, Random.Range(-a, a) * 6f, Random.Range(-a, a) * 4f);
            }
            else camOffset.localRotation = Quaternion.Slerp(camOffset.localRotation, Quaternion.identity, 1f - Mathf.Exp(-10f * dt));
            camOffset.localPosition = Vector3.Lerp(camOffset.localPosition, offset, 1f - Mathf.Exp(-18f * dt));

            float headY = Hidden != null ? head.localPosition.y : currentHeight - 0.13f;
            if (CamOverride.HasValue) head.position = CamOverride.Value; // v1.0.3: Brutus carries you
            else if (Hidden == null) head.localPosition = new Vector3(0, Mathf.Lerp(head.localPosition.y, headY, 1f - Mathf.Exp(-12f * dt)), 0);

            float fov = SaveSystem.Data.fov + (Sprinting ? 6f : 0f) - (Hidden != null ? 8f : 0f);
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, fov, 1f - Mathf.Exp(-6f * dt));
        }

        public void Shake(float amplitude, float seconds)
        {
            shakeAmp = Mathf.Max(shakeAmp * Mathf.Clamp01(shakeTime), amplitude);
            shakeTime = Mathf.Max(shakeTime, seconds);
        }

        public void SetLook(float yawDeg, float pitchDeg)
        {
            yaw = yawDeg; pitch = pitchDeg;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            head.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        // ================================================================== interaction
        void UpdateInteraction(float dt)
        {
            Interactable found = null;
            var ray = new Ray(Cam.transform.position, Cam.transform.forward);
            if (Hidden != null) found = Hidden;
            else found = CastInteract(ray, 0.1f);
            // fall back to a thin ray (sphere cast can hit a nearby wall edge first, or an open door's edge)
            if (Hidden == null && (found == null || IsOpenDoor(found)))
            {
                var thin = CastInteract(ray, 0f);
                if (thin != null && (found == null || !IsOpenDoor(thin))) found = thin;
            }

            Focus = found;
            FocusInfo = found != null ? found.Query(this) : default;

            bool pressed = InputHub.ConsumeInteract();
            bool held = InputHub.InteractHold;

            if (holding)
            {
                if (holdTarget != Focus || !held || !FocusInfo.enabled)
                {
                    if (holdTarget != null) holdTarget.OnHoldCancel(this);
                    holding = false;
                    holdTarget = null;
                    HoldProgress = 0f;
                }
                else
                {
                    HoldProgress += dt / Mathf.Max(0.1f, FocusInfo.hold);
                    holdTarget.OnHoldTick(this, HoldProgress);
                    if (HoldProgress >= 1f)
                    {
                        var t = holdTarget;
                        holding = false;
                        holdTarget = null;
                        HoldProgress = 0f;
                        t.Interact(this);
                    }
                }
                return;
            }

            if (!pressed || Focus == null) return;
            if (!FocusInfo.enabled)
            {
                Focus.OnDenied(this);
                if (GameController.I != null) GameController.I.Hud.Flash(FocusInfo.text);
                return;
            }
            if (FocusInfo.hold > 0f)
            {
                holding = true;
                holdTarget = Focus;
                HoldProgress = 0f;
            }
            else Focus.Interact(this);
        }

        Collider lastCol;
        Interactable lastColInteractable;
        static readonly RaycastHit[] interactHits = new RaycastHit[24];

        static bool IsOpenDoor(Interactable it) => it is Door d && d.IsOpen;

        /// <summary>
        /// Nearest interactable along the look ray (radius 0 = thin ray). v1.7.1: when the first hit is an
        /// OPEN door, another interactable up to 0.9 m behind it wins (e.g. the crank inside locker 13 seen
        /// past the swung-out door); a solid non-interactable collider in between still blocks, and with
        /// nothing behind it the door itself is returned so it can be closed.
        /// </summary>
        Interactable CastInteract(Ray ray, float radius)
        {
            int n = radius > 0f
                ? Physics.SphereCastNonAlloc(ray, radius, interactHits, 2.4f, RF.MaskInteract, QueryTriggerInteraction.Collide)
                : Physics.RaycastNonAlloc(ray, interactHits, 2.4f, RF.MaskInteract, QueryTriggerInteraction.Collide);
            if (radius > 0f)
            {
                // SphereCast (single) skipped colliders already overlapping the start sphere; *NonAlloc reports
                // them at distance 0, so drop those to keep the old behaviour
                int k = 0;
                for (int i = 0; i < n; i++) if (interactHits[i].distance > 0f) interactHits[k++] = interactHits[i];
                n = k;
            }
            if (n <= 0) return null;
            // NonAlloc results are unordered: insertion sort by distance (n is tiny)
            for (int i = 1; i < n; i++)
            {
                var h = interactHits[i];
                int j = i - 1;
                while (j >= 0 && interactHits[j].distance > h.distance) { interactHits[j + 1] = interactHits[j]; j--; }
                interactHits[j + 1] = h;
            }
            var first = InteractableOf(interactHits[0].collider);
            if (!IsOpenDoor(first)) return first;
            float limit = interactHits[0].distance + 0.9f;
            for (int i = 1; i < n; i++)
            {
                var h = interactHits[i];
                if (h.distance > limit) break;
                var it = h.collider.GetComponentInParent<Interactable>();
                if (it == first) continue;           // another part of the same door
                if (it != null) return it;
                if (!h.collider.isTrigger) break;    // wall / solid prop behind the door blocks
            }
            return first;
        }

        Interactable InteractableOf(Collider c)
        {
            if (c != lastCol || (lastColInteractable == null && !ReferenceEquals(lastColInteractable, null)))
            {
                lastCol = c;
                lastColInteractable = c.GetComponentInParent<Interactable>();
            }
            return lastColInteractable;
        }

        // ================================================================== hiding
        public void EnterHide(HideSpot spot)
        {
            if (Hidden != null || Dead) return;
            Hidden = spot;
            spot.SetOccupant(this);
            cc.enabled = false;
            Crouching = false;
            Sprinting = false;
            velocity = Vector3.zero;
            hideYawCenter = spot.viewPoint.eulerAngles.y;
            if (hideRoutine != null) StopCoroutine(hideRoutine);
            hideRoutine = StartCoroutine(HideMove(spot));
            SaveSystem.Data.hides++;
            Achievements.Report("hide_seek");
            if (GameController.I != null) GameController.I.OnPlayerHid(spot);
        }

        IEnumerator HideMove(HideSpot spot)
        {
            spot.AnimateDoor(true);
            Vector3 startPos = transform.position;
            Vector3 endPos = spot.viewPoint.position - Vector3.up * (StandHeight - 0.13f);
            float startYaw = yaw;
            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime * 2.4f;
                float e = Mathf.SmoothStep(0, 1, t);
                transform.position = Vector3.Lerp(startPos, endPos, e);
                yaw = Mathf.LerpAngle(startYaw, hideYawCenter, e);
                pitch = Mathf.Lerp(pitch, 0f, e);
                head.localPosition = new Vector3(0, StandHeight - 0.13f, 0);
                yield return null;
            }
            foreach (var r in spot.occluders) if (r != null) r.enabled = false;
            spot.AnimateDoor(false);
            if (AudioManager.I != null)
            {
                AudioManager.I.Muffle(0.55f);
                AudioManager.I.Play2D("breath_hide", 0.29f);
            }
            hideRoutine = null;
        }

        public void ExitHide()
        {
            if (Hidden == null) return;
            var spot = Hidden;
            foreach (var r in spot.occluders) if (r != null) r.enabled = true;
            if (hideRoutine != null) { StopCoroutine(hideRoutine); hideRoutine = null; }
            spot.AnimateDoor(true);
            spot.Invoke("CloseAfterCheck", 0.9f);
            transform.position = spot.exitPoint.position + Vector3.up * 0.05f;
            spot.SetOccupant(null);
            Hidden = null;
            cc.enabled = true;
            if (AudioManager.I != null) AudioManager.I.Muffle(0f);
            if (GameController.I != null) GameController.I.OnPlayerLeftHide(spot);
        }

        // ================================================================== mask
        public void UseMask()
        {
            if (!HasMask)
            {
                if (GameController.I != null) GameController.I.Hud.Flash(Loc.T("hud_no_mask"));
                return;
            }
            if (MaskCooldownLeft > 0f || Dead) return;
            maskActiveUntil = Time.time + MaskDuration;
            MaskCooldownLeft = MaskCooldown;
            if (AudioManager.I != null)
            {
                AudioManager.I.Play2D("mask_on", 0.9f);
                AudioManager.I.Play2D("mask_repel", 0.9f);
            }
            RobotAI.RepelAll(transform.position, MaskRadius);
            SaveSystem.Data.maskUses++;
            Achievements.Report("masked");
            Shake(0.15f, 0.3f);
            if (GameController.I != null) GameController.I.Hud.MaskOverlay(MaskDuration);
            StartCoroutine(MaskOff());
        }

        IEnumerator MaskOff()
        {
            yield return new WaitForSeconds(MaskDuration);
            if (AudioManager.I != null) AudioManager.I.Play2D("mask_off", 0.7f);
        }

        // ================================================================== throwables
        public void CycleThrowable()
        {
            ItemId[] order = { ItemId.Oil, ItemId.Water, ItemId.Toy };
            int start = System.Array.IndexOf(order, SelectedThrowable);
            for (int i = 1; i <= 3; i++)
            {
                var c = order[(start + i + 3) % 3];
                if (Inventory.Has(c)) { SelectedThrowable = c; return; }
            }
            SelectedThrowable = ItemId.None;
        }

        public void EnsureThrowableSelected()
        {
            if (SelectedThrowable != ItemId.None && Inventory.Has(SelectedThrowable)) return;
            SelectedThrowable = ItemId.None;
            CycleThrowable();
        }

        void ThrowSelected()
        {
            EnsureThrowableSelected();
            if (SelectedThrowable == ItemId.None || Hidden != null) return;
            var item = SelectedThrowable;
            if (!Inventory.Remove(item)) return;
            Throwables.Use(item, this);
            EnsureThrowableSelected();
        }

        // ================================================================== death / respawn
        public void Kill()
        {
            if (Dead) return;
            if (hideRoutine != null) { StopCoroutine(hideRoutine); hideRoutine = null; } // would re-muffle audio / hide occluders mid-jumpscare
            if (holding && holdTarget != null) holdTarget.OnHoldCancel(this);            // stops HoldAction loop sounds
            holdTarget = null;
            HoldProgress = 0f;
            if (Hidden != null)
            {
                foreach (var r in Hidden.occluders) if (r != null) r.enabled = true;
                Hidden.SetOccupant(null);
                Hidden = null;
            }
            Dead = true;
            holding = false;
            Sprinting = false;
            velocity = Vector3.zero;
            if (Flashlight.On) Flashlight.interference = 1f;
        }

        public void Respawn(Vector3 pos, float yawDeg)
        {
            CamOverride = null;
            Dead = false;
            ControlLocked = false;
            cc.enabled = false;
            transform.position = pos + Vector3.up * 0.05f;
            cc.enabled = true;
            Stamina = 1f;
            Exhausted = false;
            Crouching = false;
            currentHeight = StandHeight;
            cc.height = StandHeight;
            cc.center = new Vector3(0, StandHeight * 0.5f, 0);
            SetLook(yawDeg, 0f);
            Flashlight.interference = 0f;
            if (AudioManager.I != null) AudioManager.I.Muffle(0f);
        }

        /// <summary>0..1 how easy the player is to see right now (used by robot vision).</summary>
        public float Visibility
        {
            get
            {
                if (Hidden != null || MaskActive) return 0f;
                float v = 1f;
                if (Crouching) v *= 0.45f; // v1.7: crouching hides you much better (was 0.6)
                if (Sprinting) v *= 1.25f;
                if (Flashlight.On) v *= 1.3f;
                return v;
            }
        }

        /// <summary>v1.0.3: world position the head is pinned to (grab jumpscare); null = normal.</summary>
        public Vector3? CamOverride;

        public Vector3 EyePosition => Cam != null ? Cam.transform.position : transform.position + Vector3.up * 1.6f;
    }
}
