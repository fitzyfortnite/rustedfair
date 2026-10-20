using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RustedFair
{
    /// <summary>In-game HUD, touch controls, overlays and in-game panels.</summary>
    public class HUD : MonoBehaviour
    {
        public Canvas canvas;
        RectTransform safe, controls, overlays, panels;
        PlayerController player;
        Quest quest;
        public Tips tips;

        Text prompt, flashMsg, objective, notesCount, throwCount;
        Image holdRing, crosshair, staminaFill, staminaBg, batteryFill, maskCooldown, interactIcon, throwIcon;
        CanvasGroup staminaCg, objectiveCg, promptCg, flashCg, hideCg, maskCg, jumpCg;
        TouchButton interactBtn, maskBtn, throwBtn, flashBtn, sprintBtn, crouchBtn;
        GameObject cycleBtn;
        Image redOverlay, eyelidTop, eyelidBottom, tasksDot, hintArrow;
        Text hintDist;
        CanvasGroup hintCg;
        Text warnText;
        CanvasGroup warnCg;
        Outline warnGlow;
        float warnTimer;
        const float WarnTime = 2.6f;
        static Sprite arrowSprite;
        /// <summary>A task briefing was shown and the Tasks screen hasn't been opened since (red dot on the button).</summary>
        public bool TasksUnseen;
        readonly List<Image> blood = new List<Image>();
        readonly List<Image> hearts = new List<Image>();
        float flashTimer, objectiveTimer;
        string lastObjective;
        GameObject openPanel;
        public bool PanelOpen => openPanel != null;

        public static HUD Create(Transform parent, PlayerController p, Quest q)
        {
            var c = UIKit.MakeCanvas("HUD", 100, parent);
            var h = c.gameObject.AddComponent<HUD>();
            h.canvas = c;
            h.player = p;
            h.quest = q;
            h.Build();
            return h;
        }

        // ================================================================== build
        void Build()
        {
            overlays = UIKit.Rect(canvas.transform, "Overlays");
            UIKit.Stretch(overlays);
            safe = UIKit.SafeRoot(canvas.transform);
            controls = UIKit.Rect(safe, "Controls");
            UIKit.Stretch(controls);
            panels = UIKit.Rect(canvas.transform, "Panels");
            UIKit.Stretch(panels);

            BuildOverlays();

            // ---- look pad (right) and joystick (left) ----
            var look = UIKit.Img(controls, "LookPad", null, new Color(0, 0, 0, 0.001f));
            look.raycastTarget = true;
            look.rectTransform.anchorMin = new Vector2(0.38f, 0f);
            look.rectTransform.anchorMax = new Vector2(1f, 1f);
            look.rectTransform.offsetMin = look.rectTransform.offsetMax = Vector2.zero;
            look.gameObject.AddComponent<TouchLookPad>();

            var joyArea = UIKit.Img(controls, "JoystickArea", null, new Color(0, 0, 0, 0.001f));
            joyArea.raycastTarget = true;
            joyArea.rectTransform.anchorMin = new Vector2(0f, 0f);
            joyArea.rectTransform.anchorMax = new Vector2(0.38f, 0.78f);
            joyArea.rectTransform.offsetMin = joyArea.rectTransform.offsetMax = Vector2.zero;
            var joyBg = UIKit.Img(joyArea.transform, "Base", "joystick_bg", Color.white);
            if (joyBg.sprite == null) joyBg.color = new Color(1, 1, 1, 0.15f);
            UIKit.Place(joyBg.rectTransform, 0f, 0f, new Vector2(270, 260), new Vector2(280, 280));
            var knob = UIKit.Img(joyBg.transform, "Knob", "joystick_knob", Color.white);
            if (knob.sprite == null) knob.color = new Color(1, 1, 1, 0.35f);
            UIKit.Place(knob.rectTransform, 0.5f, 0.5f, Vector2.zero, new Vector2(130, 130));
            var joy = joyArea.gameObject.AddComponent<TouchJoystick>();
            joy.bg = joyBg.rectTransform;
            joy.knob = knob.rectTransform;
            joy.radius = 115f;

            // ---- action buttons (bottom-right) ----
            var br = new Vector2(1f, 0f);
            interactBtn = TouchButton.Make(controls, "Interact", "icon_hand", 200f, br, new Vector2(-270, 280),
                () => { InputHub.PressInteract(); InputHub.InteractHeld = true; }, () => InputHub.InteractHeld = false);
            interactIcon = interactBtn.icon;
            sprintBtn = TouchButton.Make(controls, "Sprint", "icon_sprint", 150f, br, new Vector2(-500, 210),
                () => InputHub.SprintHeld = true, () => InputHub.SprintHeld = false);
            // v1.2: every action button steers the camera while held (only pause/tasks don't)
            crouchBtn = TouchButton.Make(controls, "Crouch", "icon_crouch", 130f, br, new Vector2(-100, 300), InputHub.PressCrouch); // -110 overlapped the interact button's rect by 5px
            maskBtn = TouchButton.Make(controls, "Mask", "icon_mask", 130f, br, new Vector2(-120, 470), InputHub.PressMask);
            maskCooldown = UIKit.Img(maskBtn.transform, "Cooldown", "btn_circle", new Color(0, 0, 0, 0.7f));
            if (maskCooldown.sprite == null) maskCooldown.color = new Color(0, 0, 0, 0.6f);
            UIKit.Stretch(maskCooldown.rectTransform);
            maskCooldown.type = Image.Type.Filled;
            maskCooldown.fillMethod = Image.FillMethod.Radial360;
            maskCooldown.fillOrigin = (int)Image.Origin360.Top;
            maskCooldown.fillAmount = 0f;
            throwBtn = TouchButton.Make(controls, "Throw", "icon_throw", 130f, br, new Vector2(-300, 520), InputHub.PressThrow);
            throwIcon = throwBtn.icon;
            throwCount = UIKit.Txt(throwBtn.transform, "Count", "", 30, UIKit.Bone, TextAnchor.LowerRight, FontRole.UIBold, false);
            UIKit.Stretch(throwCount.rectTransform, 6);
            // item cycle: a TouchButton (multi-touch, steers while held) instead of a UI Button
            var cyc = TouchButton.Make(controls, "Cycle", "icon_back", 68f, br, new Vector2(-190, 600), InputHub.PressCycle);
            cyc.transform.localRotation = Quaternion.Euler(0, 0, 180);
            cycleBtn = cyc.gameObject;

            // ---- top bar ----
            // v1.8.1: pause / TASKS / BAG / notes count as one compact row centred at the very top edge (safe area,
            // ~2 px web = 6 units from the top), smaller, whole row at 85 % opacity - it took too much of the view
            const float rowH = 72f, gap = 18f, tasksW = 228f, bagW = 196f, notesW = 90f;
            float rowW = rowH + gap + tasksW + gap + bagW + 10f + notesW;
            var row = UIKit.Rect(controls, "TopRow");
            UIKit.Place(row, 0.5f, 1f, new Vector2(0f, -6f), new Vector2(rowW, rowH), 0.5f, 1f);
            var rowCg = row.gameObject.AddComponent<CanvasGroup>();
            rowCg.alpha = 0.85f;
            var tl = new Vector2(0f, 0.5f); // anchors inside the row
            var pause = TouchButton.Make(row, "Pause", "icon_pause", rowH, tl, new Vector2(rowH * 0.5f, 0f), InputHub.PressPause);
            pause.lookWhileHeld = false;
            if (pause.bg != null && pause.bg.sprite != null) pause.bg.color = new Color(1f, 1f, 1f, 0.22f); // thinner / fainter ring
            // clearly labelled TASKS button (icon + text), opens the Tasks screen; red dot = unread briefing
            float x = rowH + gap;
            var tasks = TouchButton.MakeLabeled(row, "Tasks", "icon_notes", "tasks_btn", new Vector2(tasksW, rowH), tl, new Vector2(x, 0f), InputHub.PressNotes);
            tasks.lookWhileHeld = false;
            CompactLabel(tasks);
            tasksDot = UIKit.Img(tasks.icon.transform, "Dot", "joystick_knob", UIKit.BloodBright);
            if (tasksDot.sprite == null) tasksDot.color = UIKit.BloodBright;
            UIKit.Place(tasksDot.rectTransform, 1f, 1f, new Vector2(4, 4), new Vector2(18, 18));
            tasksDot.gameObject.SetActive(false);
            // v1.8: BAG button (bag icon + label) next to TASKS: opens the inventory screen (also I key / pause menu)
            x += tasksW + gap;
            var bag = TouchButton.MakeLabeled(row, "Bag", "icon_bag", "inv_btn", new Vector2(bagW, rowH), tl, new Vector2(x, 0f), InputHub.PressInventory);
            bag.lookWhileHeld = false;
            CompactLabel(bag);
            x += bagW + 10f;
            notesCount = UIKit.Txt(row, "NotesCount", "0/7", 26, UIKit.Dim, TextAnchor.MiddleLeft, FontRole.UIBold, false);
            UIKit.Place(notesCount.rectTransform, 0f, 0.5f, new Vector2(x, 0f), new Vector2(notesW, 50), 0f, 0.5f);
            // v1.4: no lives bar on the HUD any more - the remaining lives (x / 3) are only shown on the death screen
            flashBtn = TouchButton.Make(controls, "Flashlight", "icon_flashlight", 110f, new Vector2(1f, 1f), new Vector2(-90, -90), InputHub.PressFlash);
            var batBg = UIKit.Img(controls, "BatteryBg", null, new Color(0, 0, 0, 0.4f));
            UIKit.Place(batBg.rectTransform, 1f, 1f, new Vector2(-90, -164), new Vector2(84, 8));
            batteryFill = UIKit.Img(batBg.transform, "Fill", null, new Color(0.9f, 0.85f, 0.6f));
            batteryFill.rectTransform.anchorMin = Vector2.zero;
            batteryFill.rectTransform.anchorMax = Vector2.one;
            batteryFill.rectTransform.offsetMin = batteryFill.rectTransform.offsetMax = Vector2.zero;

            objective = UIKit.Txt(controls, "Objective", "", 30, UIKit.Bone, TextAnchor.MiddleCenter, FontRole.UI, false);
            // v1.8.1: just under the compact top row (web: top + clamp(30px, 9vh, 42px))
            UIKit.Place(objective.rectTransform, 0.5f, 1f, new Vector2(0, -112), new Vector2(1000, 60));
            UIKit.SoftShadow(objective);
            objectiveCg = objective.gameObject.AddComponent<CanvasGroup>();
            objectiveCg.alpha = 0f;
            objectiveCg.blocksRaycasts = false;

            // v1.2 hint arrow (top centre, under the objective line): points at the hinted item/place, with the distance
            var hintRoot = UIKit.Rect(controls, "HintArrow");
            UIKit.Place(hintRoot, 0.5f, 1f, new Vector2(0, -200), new Vector2(200, 110)); // below the objective line
            hintCg = hintRoot.gameObject.AddComponent<CanvasGroup>();
            hintCg.alpha = 0f;
            hintCg.blocksRaycasts = false;
            hintCg.interactable = false;
            var gold = new Color(1f, 0.824f, 0.478f);
            hintArrow = UIKit.Img(hintRoot, "Arrow", null, gold);
            hintArrow.sprite = ArrowSprite();
            hintArrow.raycastTarget = false;
            UIKit.Place(hintArrow.rectTransform, 0.5f, 1f, new Vector2(0, -32), new Vector2(58, 58));
            var ash = hintArrow.gameObject.AddComponent<Shadow>();
            ash.effectColor = new Color(0, 0, 0, 0.7f);
            ash.effectDistance = new Vector2(2f, -2f);
            hintDist = UIKit.Txt(hintRoot, "Dist", "", 26, gold, TextAnchor.MiddleCenter, FontRole.UIBold, false);
            hintDist.raycastTarget = false;
            UIKit.Place(hintDist.rectTransform, 0.5f, 0f, new Vector2(0, 18), new Vector2(200, 36));
            UIKit.SoftShadow(hintDist);

            // ---- centre ----
            crosshair = UIKit.Img(controls, "Crosshair", "joystick_knob", new Color(1, 1, 1, 0.55f));
            UIKit.Place(crosshair.rectTransform, 0.5f, 0.5f, Vector2.zero, new Vector2(10, 10));
            holdRing = UIKit.Img(controls, "HoldRing", "joystick_bg", UIKit.BloodBright);
            if (holdRing.sprite == null) holdRing.color = UIKit.BloodBright;
            UIKit.Place(holdRing.rectTransform, 0.5f, 0.5f, Vector2.zero, new Vector2(90, 90));
            holdRing.type = Image.Type.Filled;
            holdRing.fillMethod = Image.FillMethod.Radial360;
            holdRing.fillOrigin = (int)Image.Origin360.Top;
            holdRing.fillAmount = 0f;
            prompt = UIKit.Txt(controls, "Prompt", "", 32, UIKit.Bone, TextAnchor.MiddleCenter, FontRole.UIBold, false);
            UIKit.Place(prompt.rectTransform, 0.5f, 0.5f, new Vector2(0, -95), new Vector2(900, 60));
            UIKit.SoftShadow(prompt);
            promptCg = prompt.gameObject.AddComponent<CanvasGroup>();
            promptCg.blocksRaycasts = false;
            flashMsg = UIKit.Txt(controls, "Flash", "", 28, new Color(1f, 0.6f, 0.5f), TextAnchor.MiddleCenter, FontRole.UI, false);
            UIKit.Place(flashMsg.rectTransform, 0.5f, 0.5f, new Vector2(0, -150), new Vector2(1000, 50));
            UIKit.SoftShadow(flashMsg);
            flashCg = flashMsg.gameObject.AddComponent<CanvasGroup>();
            flashCg.alpha = 0f;
            flashCg.blocksRaycasts = false;

            // ---- stamina (thin, bottom centre, hides when full) ----
            staminaBg = UIKit.Img(controls, "Stamina", null, new Color(0, 0, 0, 0.5f));
            UIKit.Place(staminaBg.rectTransform, 0.5f, 0f, new Vector2(0, 36), new Vector2(340, 9));
            staminaFill = UIKit.Img(staminaBg.transform, "Fill", null, UIKit.Bone);
            staminaFill.rectTransform.anchorMin = Vector2.zero;
            staminaFill.rectTransform.anchorMax = Vector2.one;
            staminaFill.rectTransform.offsetMin = staminaFill.rectTransform.offsetMax = Vector2.zero;
            staminaCg = staminaBg.gameObject.AddComponent<CanvasGroup>();
            staminaCg.blocksRaycasts = false;

            tips = Tips.Create(safe);
            tips.transform.SetAsLastSibling();

            // red warning when a robot spots the player ("Find a place to hide!") - v1.6: Oswald SemiBold uppercase,
            // much smaller, wide tracking, #FF3B2A with a soft glow, at 24 % height
            warnText = UIKit.Txt(safe, "Warn", "", 44, new Color(1f, 0.231f, 0.165f), TextAnchor.MiddleCenter, FontRole.UIBold, false);
            UIKit.Place(warnText.rectTransform, 0.5f, 0.76f, Vector2.zero, new Vector2(1700, 90));
            warnText.raycastTarget = false;
            warnText.resizeTextForBestFit = true;
            warnText.resizeTextMinSize = 22;
            warnText.resizeTextMaxSize = 44;
            LetterSpacing.Add(warnText, 0.24f);
            warnText.horizontalOverflow = HorizontalWrapMode.Wrap;
            warnText.verticalOverflow = VerticalWrapMode.Truncate;
            warnGlow = warnText.gameObject.AddComponent<Outline>();
            warnGlow.effectColor = new Color(1f, 0.08f, 0.04f, 0.4f);
            warnGlow.effectDistance = new Vector2(2f, -2f);
            var wsh = warnText.gameObject.AddComponent<Shadow>();
            wsh.effectColor = new Color(0f, 0f, 0f, 0.9f);
            wsh.effectDistance = new Vector2(0f, -2f);
            warnCg = warnText.gameObject.AddComponent<CanvasGroup>();
            warnCg.alpha = 0f;
            warnCg.blocksRaycasts = false;
            warnCg.interactable = false;
        }

        /// <summary>v1.8.1: smaller label (web 10-13 px, wide tracking) on the compact top-row buttons.</summary>
        static void CompactLabel(TouchButton b)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t == null) return;
            t.resizeTextMaxSize = 28;
            t.resizeTextMinSize = 16;
            LetterSpacing.Add(t, 0.16f);
        }

        void BuildOverlays()
        {
            // hiding: dark slats
            var hide = UIKit.Rect(overlays, "Hide");
            UIKit.Stretch(hide);
            hideCg = hide.gameObject.AddComponent<CanvasGroup>();
            hideCg.alpha = 0f;
            hideCg.blocksRaycasts = false;
            for (int i = 0; i < 9; i++)
            {
                var slat = UIKit.Img(hide, "Slat" + i, null, new Color(0.01f, 0.01f, 0.01f, 0.97f));
                slat.rectTransform.anchorMin = new Vector2(0, i / 9f + 0.045f);
                slat.rectTransform.anchorMax = new Vector2(1, (i + 1) / 9f);
                slat.rectTransform.offsetMin = slat.rectTransform.offsetMax = Vector2.zero;
            }
            var hv = UIKit.Img(hide, "Vignette", "vignette", Color.black);
            UIKit.Stretch(hv.rectTransform, -40);

            // mask on face
            var mask = UIKit.Rect(overlays, "Mask");
            UIKit.Stretch(mask);
            maskCg = mask.gameObject.AddComponent<CanvasGroup>();
            maskCg.alpha = 0f;
            maskCg.blocksRaycasts = false;
            var mv = UIKit.Img(mask, "V", "vignette", Color.black);
            UIKit.Stretch(mv.rectTransform, -200);
            var top = UIKit.Img(mask, "Top", null, new Color(0.02f, 0.015f, 0.01f, 1f));
            top.rectTransform.anchorMin = new Vector2(0, 0.72f); top.rectTransform.anchorMax = Vector2.one;
            top.rectTransform.offsetMin = top.rectTransform.offsetMax = Vector2.zero;
            var bottom = UIKit.Img(mask, "Bottom", null, new Color(0.02f, 0.015f, 0.01f, 1f));
            bottom.rectTransform.anchorMin = Vector2.zero; bottom.rectTransform.anchorMax = new Vector2(1, 0.24f);
            bottom.rectTransform.offsetMin = bottom.rectTransform.offsetMax = Vector2.zero;
            var nose = UIKit.Img(mask, "Nose", null, new Color(0.02f, 0.015f, 0.01f, 1f));
            nose.rectTransform.anchorMin = new Vector2(0.46f, 0.2f); nose.rectTransform.anchorMax = new Vector2(0.54f, 0.75f);
            nose.rectTransform.offsetMin = nose.rectTransform.offsetMax = Vector2.zero;

            // jumpscare red + blood
            var jump = UIKit.Rect(overlays, "Jumpscare");
            UIKit.Stretch(jump);
            jumpCg = jump.gameObject.AddComponent<CanvasGroup>();
            jumpCg.alpha = 0f;
            jumpCg.blocksRaycasts = false;
            redOverlay = UIKit.Img(jump, "Red", "vignette", new Color(0.6f, 0f, 0f, 0.35f)); // v1.0.3: edges only
            UIKit.Stretch(redOverlay.rectTransform);
            for (int i = 1; i <= 3; i++)
            {
                var b = UIKit.Img(jump, "Blood" + i, "blood_overlay_" + i, Color.white);
                UIKit.Stretch(b.rectTransform);
                b.color = new Color(1, 1, 1, 0);
                blood.Add(b);
            }
            // v1.0.2: the full-screen "scratch_overlay" (soft dust blobs + thin diagonal scratches) was removed: the web
            // build never draws it, and with linear-space UI blending it read as grey blobs/streaks all over the view.

            // eyelids (waking up / blinking)
            eyelidTop = UIKit.Img(overlays, "LidTop", null, Color.black);
            eyelidTop.rectTransform.anchorMin = new Vector2(0, 0.5f); eyelidTop.rectTransform.anchorMax = new Vector2(1, 1.1f);
            eyelidTop.rectTransform.offsetMin = eyelidTop.rectTransform.offsetMax = Vector2.zero;
            eyelidBottom = UIKit.Img(overlays, "LidBottom", null, Color.black);
            eyelidBottom.rectTransform.anchorMin = new Vector2(0, -0.1f); eyelidBottom.rectTransform.anchorMax = new Vector2(1, 0.5f);
            eyelidBottom.rectTransform.offsetMin = eyelidBottom.rectTransform.offsetMax = Vector2.zero;
            SetEyelids(0f);
        }

        // ================================================================== per-frame
        void Update()
        {
            if (player == null) return;
            float dt = Time.unscaledDeltaTime;

            // prompt + hold ring
            var info = player.FocusInfo;
            bool hasFocus = player.Focus != null && !string.IsNullOrEmpty(info.text);
            if (hasFocus)
            {
                prompt.font = Fonts.Get(FontRole.UIBold);
                prompt.text = info.text;
                prompt.color = info.enabled ? UIKit.Bone : new Color(0.7f, 0.55f, 0.5f);
            }
            promptCg.alpha = Mathf.MoveTowards(promptCg.alpha, hasFocus ? 1f : 0f, dt * 8f);
            holdRing.fillAmount = player.HoldProgress;
            crosshair.color = new Color(1, 1, 1, hasFocus && info.enabled ? 0.95f : 0.45f);
            crosshair.rectTransform.sizeDelta = Vector2.one * (hasFocus && info.enabled ? 16f : 10f);
            if (interactIcon != null)
            {
                string want = hasFocus ? (string.IsNullOrEmpty(info.icon) ? "icon_hand" : info.icon) : "icon_hand";
                var s = RF.Sprite(want);
                if (s != null && interactIcon.sprite != s) interactIcon.sprite = s;
                interactIcon.color = hasFocus && info.enabled ? UIKit.Bone : new Color(1, 1, 1, 0.35f);
            }

            // stamina
            staminaFill.rectTransform.anchorMax = new Vector2(player.Stamina, 1f);
            staminaFill.color = player.Exhausted ? UIKit.BloodBright : UIKit.Bone;
            staminaCg.alpha = Mathf.MoveTowards(staminaCg.alpha, player.Stamina < 0.995f ? 1f : 0f, dt * 2f);
            sprintBtn.icon.color = player.Exhausted ? new Color(0.8f, 0.3f, 0.25f, 0.6f) : UIKit.Bone;

            // battery
            var fl = player.Flashlight;
            batteryFill.rectTransform.anchorMax = new Vector2(fl.Charge, 1f);
            batteryFill.color = fl.Charge < 0.2f ? UIKit.BloodBright : new Color(0.9f, 0.85f, 0.6f);
            flashBtn.icon.color = fl.On ? new Color(1f, 0.95f, 0.7f) : new Color(0.6f, 0.6f, 0.6f);

            // mask
            maskBtn.gameObject.SetActive(player.HasMask);
            maskCooldown.fillAmount = player.MaskCooldownLeft / PlayerController.MaskCooldown;
            maskCg.alpha = Mathf.MoveTowards(maskCg.alpha, player.MaskActive ? 1f : 0f, dt * 5f);
            if (PostFX.I != null) PostFX.I.mask = player.MaskActive ? 1f : 0f;

            // throwables
            player.EnsureThrowableSelected();
            var sel = player.SelectedThrowable;
            bool anyThrow = sel != ItemId.None;
            throwBtn.gameObject.SetActive(anyThrow);
            cycleBtn.SetActive(anyThrow && CountThrowableTypes() > 1);
            if (anyThrow)
            {
                var sp = RF.Sprite(Items.Icon(sel));
                if (sp != null && throwIcon.sprite != sp) throwIcon.sprite = sp;
                throwIcon.color = Items.Tint(sel);
                throwCount.text = player.Inventory.Count(sel).ToString();
            }

            // hiding
            hideCg.alpha = Mathf.MoveTowards(hideCg.alpha, player.Hidden != null ? 1f : 0f, dt * 3f);
            sprintBtn.gameObject.SetActive(player.Hidden == null);
            crouchBtn.gameObject.SetActive(player.Hidden == null);

            // flash message
            if (flashTimer > 0f) { flashTimer -= dt; flashCg.alpha = Mathf.Clamp01(flashTimer * 2f); }

            // objective
            if (quest != null)
            {
                string key = quest.ObjectiveKey();
                if (key != lastObjective)
                {
                    lastObjective = key;
                    objective.font = Fonts.Get(FontRole.UI);
                    objective.text = Loc.T(key);
                    objectiveTimer = 7f;
                }
                notesCount.text = quest.NotesFound + "/7";
            }
            if (objectiveTimer > 0f)
            {
                objectiveTimer -= dt;
                objectiveCg.alpha = Mathf.Clamp01(Mathf.Min(objectiveTimer, 7f - objectiveTimer) * 1.5f);
            }

            // red warning (fade in/out 0.25 s, pulsing glow)
            if (warnTimer > 0f)
            {
                warnTimer -= dt;
                float fade = Mathf.Clamp01(Mathf.Min(warnTimer, WarnTime - warnTimer) / 0.25f);
                warnCg.alpha = fade;
                warnText.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, fade);
                float pulse = Mathf.PingPong(Time.unscaledTime * 2f, 1f);
                warnGlow.effectColor = new Color(1f, 0.08f, 0.04f, 0.3f + 0.35f * pulse);
                warnGlow.effectDistance = new Vector2(1.5f + 1.5f * pulse, -1.5f - 1.5f * pulse);
            }
            else if (warnCg.alpha > 0f) warnCg.alpha = 0f;

            // hint arrow: bearing to the hinted target relative to the camera heading, hidden within 1.6 m
            {
                var hd = GameController.I != null ? GameController.I.Hints : null;
                bool show = hd != null && hd.ArrowActive && player.Cam != null && !PanelOpen && player.Hidden == null;
                if (show)
                {
                    Vector3 to = hd.MarkPos - player.transform.position;
                    to.y = 0f;
                    float d = to.magnitude;
                    if (d < 1.6f) show = false;
                    else
                    {
                        float rel = Mathf.DeltaAngle(player.Cam.transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                        hintArrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, -rel);
                        hintDist.font = Fonts.Get(FontRole.UIBold);
                        hintDist.text = d < 2.5f ? "" : Mathf.RoundToInt(d) + " m";
                    }
                }
                hintCg.alpha = Mathf.MoveTowards(hintCg.alpha, show ? 1f : 0f, dt * 4f);
            }

            // unread task briefing: pulsing red dot on the TASKS button
            if (tasksDot != null)
            {
                tasksDot.gameObject.SetActive(TasksUnseen);
                if (TasksUnseen)
                {
                    float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                    tasksDot.color = new Color(UIKit.BloodBright.r, UIKit.BloodBright.g, UIKit.BloodBright.b, 0.55f + 0.45f * k);
                    tasksDot.rectTransform.localScale = Vector3.one * (0.85f + 0.3f * k);
                }
            }

            // lives
            var gc = GameController.I;
            if (gc != null)
                for (int i = 0; i < hearts.Count; i++)
                {
                    hearts[i].gameObject.SetActive(i < gc.MaxLives);
                    hearts[i].color = i < gc.Lives ? UIKit.BloodBright : new Color(0.2f, 0.18f, 0.18f, 0.8f);
                }
        }

        int CountThrowableTypes()
        {
            int n = 0;
            if (player.Inventory.Has(ItemId.Oil)) n++;
            if (player.Inventory.Has(ItemId.Water)) n++;
            if (player.Inventory.Has(ItemId.Toy)) n++;
            return n;
        }

        // ================================================================== public api
        public void Flash(string text)
        {
            flashMsg.font = Fonts.Get(FontRole.UI);
            flashMsg.text = text;
            flashTimer = 1.8f;
            flashCg.alpha = 1f;
        }

        public void ShowObjectiveNow() { lastObjective = null; }

        public void SetControlsVisible(bool on)
        {
            controls.gameObject.SetActive(on);
            if (!on) InputHub.ResetAll();
        }

        public void MaskOverlay(float seconds) { /* driven by player.MaskActive in Update */ }

        public void SetEyelids(float open01)
        {
            float o = Mathf.Clamp01(open01);
            // closed: both lids meet in the middle; open: slide out of screen
            eyelidTop.rectTransform.anchorMin = new Vector2(0, Mathf.Lerp(0.5f, 1.05f, o));
            eyelidTop.rectTransform.anchorMax = new Vector2(1, Mathf.Lerp(1.1f, 1.65f, o));
            eyelidBottom.rectTransform.anchorMin = new Vector2(0, Mathf.Lerp(-0.1f, -0.65f, o));
            eyelidBottom.rectTransform.anchorMax = new Vector2(1, Mathf.Lerp(0.5f, -0.05f, o));
            eyelidTop.enabled = eyelidBottom.enabled = o < 0.999f;
        }

        public void Jumpscare(bool on)
        {
            jumpCg.alpha = on ? 1f : 0f;
            if (on)
            {
                foreach (var b in blood) b.color = new Color(1, 1, 1, 0);
                StartCoroutine(BloodIn());
            }
        }

        IEnumerator BloodIn()
        {
            // v1.0.3: a short red flash, then only red edges and faint blood - the robot's face must stay readable
            float t = 0;
            int pick = UnityEngine.Random.Range(0, blood.Count);
            while (t < 1.6f && jumpCg.alpha > 0f)
            {
                t += Time.unscaledDeltaTime;
                for (int i = 0; i < blood.Count; i++)
                    blood[i].color = new Color(1, 1, 1, i == pick ? Mathf.Clamp01((t - 0.25f) * 2f) * 0.3f : 0f);
                float a = t < 0.12f ? 0.95f : 0.45f + Mathf.PerlinNoise(t * 30f, 0f) * 0.3f;
                redOverlay.color = new Color(0.7f, 0f, 0f, a);
                yield return null;
            }
        }

        // ================================================================== panels
        GameObject Open(GameObject panel)
        {
            if (openPanel != null) Destroy(openPanel);
            openPanel = panel;
            return panel;
        }

        public void CloseAll()
        {
            if (openPanel != null) Destroy(openPanel);
            openPanel = null;
        }

        /// <summary>Full-screen dark scrim with a centred, box-less content area that scales to fit the screen.</summary>
        RectTransform Box(string name, Vector2 size, out RectTransform root)
        {
            root = UIKit.Rect(panels, name);
            UIKit.Stretch(root);
            UIKit.Blocker(root, UIKit.ScrimAlpha);
            var box = UIKit.PanelBox(root, "Box", size);
            Open(root.gameObject);
            return box.rectTransform;
        }

        Text PanelTitle(RectTransform box, string key, float width, bool localized = true)
        {
            var t = UIKit.Txt(box, "Title", key, 66, UIKit.BloodBright, TextAnchor.MiddleCenter, FontRole.Title, localized);
            LetterSpacing.Add(t, 0.1f);
            UIKit.Place(t.rectTransform, 0.5f, 1f, new Vector2(0, -46), new Vector2(width, 86));
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 30;
            t.resizeTextMaxSize = 66;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            UIKit.SoftShadow(t);
            return t;
        }

        void CloseX(RectTransform box, Action onClose)
        {
            var close = UIKit.IconBtn(box, "Close", "icon_close", () => { CloseAll(); onClose?.Invoke(); }, 80f, UIKit.Dim, false);
            UIKit.Place((RectTransform)close.transform, 1f, 1f, Vector2.zero, new Vector2(80, 80), 1f, 1f);
        }

        public void ShowPause(Action resume, Action menu)
        {
            var box = Box("Pause", new Vector2(760, 780), out var root);
            PanelTitle(box, "paused", 700);
            var list = UIKit.Rect(box, "List");
            UIKit.Place(list, 0.5f, 1f, new Vector2(0, -110), new Vector2(600, 660), 0.5f, 1f);
            UIKit.VList(list, 4f, TextAnchor.UpperCenter);
            var sz = new Vector2(580, 84);
            UIKit.Btn(list, "Resume", "resume", () => { CloseAll(); resume?.Invoke(); }, new Vector2(580, 104), true, 52);
            UIKit.Btn(list, "Tasks", "journal", () => ShowTasks(() => ShowPause(resume, menu)), sz, false, 40);
            UIKit.Btn(list, "Bag", "inv_btn", () => ShowInventory(() => ShowPause(resume, menu)), sz, false, 40);
            UIKit.Btn(list, "Help", "how_to_play", () => { var p = Panels.HowToPlay(panels, () => ShowPause(resume, menu)); Open(p); }, sz, false, 40);
            UIKit.Btn(list, "Settings", "settings", () => { var p = Panels.Settings(panels, () => ShowPause(resume, menu)); Open(p); }, sz, false, 40);
            if (!SaveSystem.Data.AdsRemoved)
                UIKit.AltBtn(list, "Store", "remove_ads", () => { var p = Panels.Store(panels, () => ShowPause(resume, menu)); Open(p); }, sz, 36);
            UIKit.AltBtn(list, "Menu", "quit_to_menu", () => { CloseAll(); menu?.Invoke(); }, sz, 36);
        }

        /// <summary>v1.5: shows the big red warning for 2.6 s (pulsing glow).</summary>
        public void Warn(string text)
        {
            if (warnText == null || string.IsNullOrEmpty(text)) return;
            warnText.font = Fonts.Get(FontRole.UIBold);
            warnText.text = Loc.Upper(text);
            warnTimer = WarnTime;
        }

        /// <summary>Procedural up-pointing arrow (no sprite asset needed).</summary>
        static Sprite ArrowSprite()
        {
            if (arrowSprite != null) return arrowSprite;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "HintArrow" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n;
                    bool head = v >= 0.42f && Mathf.Abs(u) <= 0.48f * (1f - v) / 0.58f;
                    bool stem = v >= 0.06f && v < 0.45f && Mathf.Abs(u) <= 0.13f;
                    px[y * n + x] = head || stem ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            arrowSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return arrowSprite;
        }

        // ================================================================== inventory (v1.8)
        static readonly ItemId[] QuestItemOrder =
        {
            ItemId.Fuse, ItemId.LockerKey, ItemId.Crank, ItemId.Token, ItemId.RideKey, ItemId.Tape,
            ItemId.KitchenKey, ItemId.Hammer, ItemId.Gear, ItemId.Keycard
        };
        static readonly ItemId[] ThrowableOrder = { ItemId.Oil, ItemId.Water, ItemId.Toy };
        static readonly Color QuestGold = new Color(1f, 0.824f, 0.478f);   // #FFD27A
        static readonly Color SelBlue = new Color(0.624f, 0.847f, 1f);     // #9FD8FF

        /// <summary>
        /// Bag screen (game paused by the caller): "Task items" (gold, incl. the mask with its recharge state) and
        /// "Usable" (throwables: tap = select for the throw button, selected one highlighted; battery: flashlight
        /// charge, tap = put in a spare battery when below 98 %). onClose = back to the game or the pause menu.
        /// </summary>
        public void ShowInventory(Action onClose)
        {
            var box = Box("Inventory", new Vector2(1500, 880), out var root);
            PanelTitle(box, "inv_btn", 1100);
            CloseX(box, onClose);
            var sr = UIKit.Scroll(box, "List", Vector2.zero, out var content);
            var srt = (RectTransform)sr.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(0f, 10f); srt.offsetMax = new Vector2(0f, -112f);
            UIKit.FlowList(content, 10f, new RectOffset(20, 20, 4, 24));
            const float w = 1420f;
            var inv = player.Inventory;
            player.EnsureThrowableSelected();
            Action refresh = () => ShowInventory(onClose);

            InvSection(content, "inv_task_items", w);
            RectTransform grid = null;
            foreach (var id in QuestItemOrder)
            {
                if (!inv.Has(id)) continue;
                if (grid == null) grid = InvGrid(content, w);
                InvCell(grid, id, inv.Count(id), null, true, false, null);
            }
            if (player.HasMask)
            {
                if (grid == null) grid = InvGrid(content, w);
                float cd = player.MaskCooldownLeft;
                InvCell(grid, ItemId.Mask, 1, cd > 0f ? Loc.F("inv_mask_cd", Mathf.CeilToInt(cd)) : Loc.T("inv_mask_ready"), true, false, null);
            }
            if (grid == null) UIKit.Para(content, "None", Loc.T("inv_none"), 26, UIKit.Dim, w, TextAnchor.UpperLeft);

            InvSection(content, "inv_usable", w);
            var g2 = InvGrid(content, w);
            foreach (var id in ThrowableOrder)
            {
                if (!inv.Has(id)) continue;
                var item = id;
                bool sel = player.SelectedThrowable == id;
                InvCell(g2, id, inv.Count(id), Loc.T(sel ? "inv_selected" : "inv_tap_select"), false, sel, () =>
                {
                    player.SelectedThrowable = item;
                    refresh();
                });
            }
            var fl = player.Flashlight;
            string bat = Loc.F("inv_battery", Mathf.RoundToInt((fl != null ? fl.Charge : 0f) * 100f)) +
                         (inv.Has(ItemId.Battery) ? " · " + Loc.T("inv_tap_battery") : "");
            InvCell(g2, ItemId.Battery, inv.Count(ItemId.Battery), bat, false, false, () =>
            {
                if (fl == null || !inv.Has(ItemId.Battery) || fl.Charge >= 0.98f) return;
                inv.Remove(ItemId.Battery);
                fl.AddCharge(1f);
                if (AudioManager.I != null) AudioManager.I.Play2D("item_insert", 0.6f, 1f, true);
                refresh();
            });
        }

        void InvSection(RectTransform content, string key, float w)
        {
            var t = UIKit.Para(content, "Sec_" + key, Loc.Upper(Loc.T(key)), 24, UIKit.Rust, w, TextAnchor.UpperLeft, FontRole.UIBold);
            LetterSpacing.Add(t, 0.18f);
            var gap = UIKit.Rect(content, "Gap_" + key);
            gap.gameObject.AddComponent<LayoutElement>().preferredHeight = 2f;
        }

        static RectTransform InvGrid(RectTransform content, float w)
        {
            var g = UIKit.Rect(content, "Grid");
            var gl = g.gameObject.AddComponent<GridLayoutGroup>();
            const float gap = 14f;
            gl.cellSize = new Vector2((w - 2f * gap) / 3f, 104f);
            gl.spacing = new Vector2(gap, gap);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 3;
            gl.childAlignment = TextAnchor.UpperLeft;
            var le = g.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.minWidth = w;
            return g;
        }

        /// <summary>One inventory cell: icon, name (+ a small status line) and the count on the right.</summary>
        static void InvCell(RectTransform grid, ItemId id, int count, string extra, bool questItem, bool selected, Action onTap)
        {
            Image bg;
            if (onTap != null) bg = (Image)UIKit.Btn(grid, "Cell_" + id, null, onTap, Vector2.zero, false).GetComponent<Image>();
            else bg = UIKit.Img(grid, "Cell_" + id, null, Color.white);
            bg.color = selected ? new Color(SelBlue.r, SelBlue.g, SelBlue.b, 0.1f) : new Color(1f, 1f, 1f, 0.05f);
            bg.raycastTarget = onTap != null;
            if (selected)
            {
                var c = new Color(SelBlue.r, SelBlue.g, SelBlue.b, 0.6f);
                Edge(bg.transform, c, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), Vector2.zero);
                Edge(bg.transform, c, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 2f));
                Edge(bg.transform, c, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(2f, 0f));
                Edge(bg.transform, c, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-2f, 0f), Vector2.zero);
            }
            var ic = UIKit.Img(bg.transform, "Icon", Items.Icon(id), Items.IsThrowable(id) ? Items.Tint(id) : (questItem ? QuestGold : UIKit.Bone));
            ic.preserveAspect = true;
            ic.raycastTarget = false;
            UIKit.Place(ic.rectTransform, 0f, 0.5f, new Vector2(18f, 0f), new Vector2(60f, 60f), 0f, 0.5f);

            bool showCount = count > 1 || Items.IsThrowable(id) || id == ItemId.Battery;
            var name = UIKit.Txt(bg.transform, "Name", Loc.T(Items.NameKey(id)), 28, questItem ? QuestGold : UIKit.Bone, TextAnchor.MiddleLeft, FontRole.UIBold, false);
            var nr = name.rectTransform;
            nr.anchorMin = new Vector2(0f, string.IsNullOrEmpty(extra) ? 0f : 0.48f);
            nr.anchorMax = new Vector2(1f, 1f);
            nr.offsetMin = new Vector2(92f, 0f);
            nr.offsetMax = new Vector2(showCount ? -84f : -12f, string.IsNullOrEmpty(extra) ? 0f : -6f);
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 18;
            name.resizeTextMaxSize = 28;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            if (!string.IsNullOrEmpty(extra))
            {
                name.alignment = TextAnchor.LowerLeft;
                var ex = UIKit.Txt(bg.transform, "Extra", extra, 21, UIKit.Dim, TextAnchor.UpperLeft, FontRole.UI, false);
                var er = ex.rectTransform;
                er.anchorMin = new Vector2(0f, 0f);
                er.anchorMax = new Vector2(1f, 0.48f);
                er.offsetMin = new Vector2(92f, 6f);
                er.offsetMax = new Vector2(showCount ? -84f : -12f, -2f);
                ex.resizeTextForBestFit = true;
                ex.resizeTextMinSize = 14;
                ex.resizeTextMaxSize = 21;
                ex.verticalOverflow = VerticalWrapMode.Truncate;
            }
            if (showCount)
            {
                var n = UIKit.Txt(bg.transform, "Count", "×" + count, 30, questItem ? QuestGold : UIKit.Bone, TextAnchor.MiddleRight, FontRole.UIBold, false);
                UIKit.Place(n.rectTransform, 1f, 0.5f, new Vector2(-16f, 0f), new Vector2(76f, 60f), 1f, 0.5f);
            }
        }

        static void Edge(Transform parent, Color c, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var e = UIKit.Img(parent, "Edge", null, c);
            e.raycastTarget = false;
            e.rectTransform.anchorMin = aMin;
            e.rectTransform.anchorMax = aMax;
            e.rectTransform.offsetMin = oMin;
            e.rectTransform.offsetMax = oMax;
        }

        /// <summary>Kept for older call sites: the journal is the Tasks screen since v1.1.</summary>
        public void ShowJournal(Action onClose) => ShowTasks(onClose);

        /// <summary>
        /// Tasks screen: all 7 tasks plus the final escape step, each with status (✓ done / ▸ current / ○ open),
        /// progress and the detailed description, and the safe-code digits found so far. Scrolls if it doesn't fit.
        /// </summary>
        public void ShowTasks(Action onClose)
        {
            TasksUnseen = false;
            var box = Box("Tasks", new Vector2(1760, 900), out var root);
            PanelTitle(box, "journal", 1200);
            CloseX(box, onClose);

            var obj = UIKit.Txt(box, "Objective", Loc.T(quest.ObjectiveKey()), 30, UIKit.Bone, TextAnchor.MiddleCenter, FontRole.UIBold, false);
            UIKit.Place(obj.rectTransform, 0.5f, 1f, new Vector2(0, -118), new Vector2(1500, 46));

            // ---- task list (left, scrolls) ----
            var sr = UIKit.Scroll(box, "List", Vector2.zero, out var content);
            var srt = (RectTransform)sr.transform;
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.offsetMin = new Vector2(0f, 0f); srt.offsetMax = new Vector2(1140f, -160f);
            UIKit.FlowList(content, 6f, new RectOffset(10, 10, 4, 30));
            int cur = quest.Current();
            float w = 1100f;
            const string dimHex = "#9A907F";
            for (int i = 0; i <= 7; i++)
            {
                bool isEscape = i == 7;
                bool isDone = !isEscape && quest.done[i];
                bool isCur = i == cur;
                string name = isEscape ? Loc.T("task_safe") : Quest.TaskName((TaskId)i);
                string prog = isEscape ? quest.EscapeProgress() : quest.TaskProgress((TaskId)i);
                string glyph = isDone ? "✓" : (isCur ? "▸" : "○");
                string status = isDone ? Loc.T("task_status_done") : (isCur ? Loc.T("now") : Loc.T("task_status_open"));
                Color col = isDone ? new Color(0.5f, 0.56f, 0.42f) : (isCur ? UIKit.BloodBright : UIKit.Bone);
                string head = glyph + "  " + Loc.Upper(name) + "   <color=" + dimHex + ">" + status + (isDone ? "" : " · " + prog) + "</color>";
                var h = UIKit.Para(content, "Task" + i, head, isCur ? 34 : 30, col, w, TextAnchor.UpperCenter, FontRole.UIBold);
                if (isCur) UIKit.SoftShadow(h);
                if (!isDone)
                {
                    string desc = isEscape ? Loc.T("task_safe_d") : Quest.TaskDesc((TaskId)i);
                    UIKit.Para(content, "Desc" + i, desc, isCur ? 26 : 23, isCur ? UIKit.Bone : UIKit.Dim, w - 40f, TextAnchor.UpperCenter, FontRole.UI, 1.05f);
                }
                var gap = UIKit.Rect(content, "Gap" + i);
                gap.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
            }

            // ---- safe code (right) ----
            var right = UIKit.Rect(box, "Code");
            UIKit.Place(right, 1f, 1f, new Vector2(0, -170), new Vector2(580, 700), 1f, 1f);
            var codeTitle = UIKit.Txt(right, "CodeTitle", "safe_code", 32, UIKit.Rust, TextAnchor.MiddleCenter, FontRole.UIBold);
            UIKit.Place(codeTitle.rectTransform, 0.5f, 1f, new Vector2(0, -30), new Vector2(560, 50));
            for (int i = 0; i < 7; i++)
            {
                bool found = quest.notes[i];
                int idx = i;
                var btn = UIKit.Btn(right, "Digit" + i, null, found ? (Action)(() => ShowNote(idx, () => ShowTasks(onClose))) : null, new Vector2(72, 100), false);
                UIKit.Place((RectTransform)btn.transform, 0.5f, 1f, new Vector2(-240 + i * 80, -130), new Vector2(72, 100));
                var d = UIKit.Txt(btn.transform, "D", found ? quest.code[i].ToString() : "?", 64, found ? UIKit.Bone : new Color(1, 1, 1, 0.22f), TextAnchor.MiddleCenter, FontRole.Note, false);
                UIKit.Stretch(d.rectTransform);
                var line = UIKit.Img(btn.transform, "Line", null, found ? UIKit.BloodBright : new Color(1, 1, 1, 0.18f));
                UIKit.Place(line.rectTransform, 0.5f, 0f, new Vector2(0, 4), new Vector2(52, 3));
                btn.interactable = found;
            }
            var hint = UIKit.Txt(box, "Hint", "journal_hint", 24, UIKit.Dim);
            UIKit.Place(hint.rectTransform, 1f, 1f, new Vector2(-290, -415), new Vector2(560, 90));
            var notesLine = UIKit.Txt(box, "Notes", Loc.F("prog_notes", quest.NotesFound, 7), 28, UIKit.Bone, TextAnchor.MiddleCenter, FontRole.UIBold, false);
            UIKit.Place(notesLine.rectTransform, 1f, 1f, new Vector2(-290, -500), new Vector2(560, 50));
            // v1.2: "Give me a hint" (12 s cooldown); on success the screen closes so the tip and arrow are visible
            var hintMsg = UIKit.Txt(box, "HintMsg", "", 24, UIKit.Dim, TextAnchor.MiddleCenter, FontRole.UI, false);
            UIKit.Place(hintMsg.rectTransform, 1f, 1f, new Vector2(-290, -662), new Vector2(560, 50));
            var hintBtn = UIKit.Btn(box, "HintBtn", "hint_btn", () =>
            {
                var hd = GameController.I != null ? GameController.I.Hints : null;
                string msg = hd != null ? hd.Request() : Loc.T("hint_none");
                if (msg != null) { hintMsg.font = Fonts.Get(FontRole.UI); hintMsg.text = msg; return; }
                CloseAll();
                onClose?.Invoke();
            }, new Vector2(460, 76), false, 32);
            UIKit.Place((RectTransform)hintBtn.transform, 1f, 1f, new Vector2(-290, -588), new Vector2(460, 76));
            var help = UIKit.Btn(box, "Help", "how_to_play", () => { var p = Panels.HowToPlay(panels, () => ShowTasks(onClose)); Open(p); }, new Vector2(460, 80), false, 34);
            UIKit.Place((RectTransform)help.transform, 1f, 0f, new Vector2(-290, 30), new Vector2(460, 80), 0.5f, 0f);
        }

        /// <summary>
        /// Task briefing (game is paused by the caller). start = the night's goal, the 3 steps and the first task;
        /// otherwise = "✓ task complete" + the next task in detail and the other open tasks (or the escape when all 7 are done).
        /// </summary>
        public void ShowBriefing(bool start, int completedTask, Action onClose)
        {
            TasksUnseen = true;
            var box = Box(start ? "BriefStart" : "BriefNext", new Vector2(1400, 900), out var root);
            var sr = UIKit.Scroll(box, "Scroll", Vector2.zero, out var content);
            var srt = (RectTransform)sr.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(0, 130); srt.offsetMax = Vector2.zero;
            UIKit.FlowList(content, 12f, new RectOffset(0, 0, 4, 24));
            float w = 1300f;
            int cur = quest.Current();
            bool escape = cur >= 7;

            if (start)
            {
                var t = UIKit.Para(content, "Title", Loc.T("brief_title"), 76, UIKit.BloodBright, w, TextAnchor.UpperCenter, FontRole.Title, 1f);
                UIKit.SoftShadow(t);
                UIKit.Para(content, "Body", Loc.T("brief_body"), 29, UIKit.Bone, w - 60f);
                UIKit.Para(content, "Step1", Loc.T("brief_step1"), 28, UIKit.Bone, w, TextAnchor.UpperCenter, FontRole.UIBold);
                UIKit.Para(content, "Step2", Loc.T("brief_step2"), 28, UIKit.Bone, w, TextAnchor.UpperCenter, FontRole.UIBold);
                UIKit.Para(content, "Step3", Loc.T("brief_step3"), 28, UIKit.Bone, w, TextAnchor.UpperCenter, FontRole.UIBold);
            }
            else
            {
                string done = completedTask >= 0 && completedTask < 7 ? Quest.TaskName((TaskId)completedTask) : "";
                var t = UIKit.Para(content, "Title", "✓ " + done + " · " + Loc.T("task_done_title"), 58, UIKit.BloodBright, w, TextAnchor.UpperCenter, FontRole.Title, 1f);
                UIKit.SoftShadow(t);
            }

            var gap = UIKit.Rect(content, "Gap");
            gap.gameObject.AddComponent<LayoutElement>().preferredHeight = 6f;
            UIKit.Para(content, "NowLabel", Loc.Upper(Loc.T(start ? "now" : (escape ? "now" : "task_new"))), 24, UIKit.Dim, w, TextAnchor.UpperCenter, FontRole.UIBold);
            string nextName = escape ? Loc.T("task_safe") : Quest.TaskName((TaskId)cur);
            string nextDesc = escape ? Loc.T("task_safe_d") : Quest.TaskDesc((TaskId)cur);
            var n = UIKit.Para(content, "Next", Loc.Upper(nextName), 44, UIKit.BloodBright, w, TextAnchor.UpperCenter, FontRole.UIBold, 1f);
            UIKit.SoftShadow(n);
            UIKit.Para(content, "NextDesc", nextDesc, 28, UIKit.Bone, w - 60f);

            if (!start && !escape)
            {
                var others = quest.OpenTasks(cur);
                if (others.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var o in others) names.Add(Quest.TaskName(o));
                    UIKit.Para(content, "OthersLabel", Loc.T("brief_others"), 24, UIKit.Dim, w, TextAnchor.UpperCenter, FontRole.UIBold);
                    UIKit.Para(content, "Others", string.Join("  ·  ", names.ToArray()), 27, UIKit.Bone, w - 60f);
                }
            }
            UIKit.Para(content, "Hint", Loc.T("brief_hint"), 24, UIKit.Dim, w - 60f);

            var ok = UIKit.Btn(box, "OK", "brief_ok", () => { CloseAll(); onClose?.Invoke(); }, new Vector2(520, 110), true, 56);
            UIKit.Place((RectTransform)ok.transform, 0.5f, 0f, new Vector2(0, 8), new Vector2(520, 110), 0.5f, 0f);
        }

        public void ShowNote(int index, Action onClose)
        {
            var root = UIKit.Rect(panels, "Note");
            UIKit.Stretch(root);
            UIKit.Blocker(root, UIKit.ScrimAlpha);
            Open(root.gameObject);
            var frame = UIKit.Rect(root, "Frame");
            UIKit.Place(frame, 0.5f, 0.5f, Vector2.zero, new Vector2(800, 940));
            frame.gameObject.AddComponent<UIFit>();
            // the note itself stays a sheet of paper (diegetic prop, not a UI box)
            var paper = UIKit.Img(frame, "Paper", "note_paper", Color.white);
            if (paper.sprite == null) paper.color = new Color(0.82f, 0.77f, 0.64f);
            UIKit.Place(paper.rectTransform, 0.5f, 1f, Vector2.zero, new Vector2(760, 820), 0.5f, 1f);
            paper.rectTransform.localRotation = Quaternion.Euler(0, 0, UnityEngine.Random.Range(-2f, 2f));
            var ink = new Color(0.14f, 0.09f, 0.07f);
            var title = UIKit.Txt(paper.transform, "Title", "note_title_" + (index + 1), 40, ink, TextAnchor.UpperCenter, FontRole.Note);
            title.GetComponent<Shadow>().enabled = false;
            UIKit.Place(title.rectTransform, 0.5f, 1f, new Vector2(0, -80), new Vector2(640, 60), 0.5f, 1f);
            var body = UIKit.Txt(paper.transform, "Body", "note_body_" + (index + 1), 26, ink, TextAnchor.UpperLeft, FontRole.Note);
            body.GetComponent<Shadow>().enabled = false;
            body.lineSpacing = 1.1f;
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = 17;
            body.resizeTextMaxSize = 26;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            UIKit.Place(body.rectTransform, 0.5f, 1f, new Vector2(0, -150), new Vector2(620, 450), 0.5f, 1f);
            var digit = UIKit.Txt(paper.transform, "Digit", Loc.F("note_digit", index + 1, quest.code[index]), 44, new Color(0.55f, 0.04f, 0.03f), TextAnchor.MiddleCenter, FontRole.Note, false);
            digit.GetComponent<Shadow>().enabled = false;
            UIKit.Place(digit.rectTransform, 0.5f, 0f, new Vector2(0, 120), new Vector2(640, 80), 0.5f, 0f);
            var close = UIKit.Btn(frame, "Close", "close", () => { CloseAll(); onClose?.Invoke(); }, new Vector2(400, 96), false, 44);
            UIKit.Place((RectTransform)close.transform, 0.5f, 0f, Vector2.zero, new Vector2(400, 96), 0.5f, 0f);
        }

        public void OpenKeypad(Func<string, bool> tryCode, Action onClose)
        {
            var box = Box("Keypad", new Vector2(640, 720), out var root);
            CloseX(box, onClose);
            // green LCD digits straight on the scrim (no display box)
            var txt = UIKit.Txt(box, "Display", "_ _ _ _ _ _ _", 64, new Color(0.4f, 1f, 0.5f), TextAnchor.MiddleCenter, FontRole.UIBold, false);
            UIKit.Place(txt.rectTransform, 0.5f, 1f, new Vector2(0, -70), new Vector2(520, 100));
            UIKit.SoftShadow(txt);
            var underline = UIKit.Img(box, "Underline", null, new Color(0.4f, 1f, 0.5f, 0.35f));
            UIKit.Place(underline.rectTransform, 0.5f, 1f, new Vector2(0, -124), new Vector2(480, 2));
            string entered = "";
            bool solved = false;
            Action refresh = () =>
            {
                var s = "";
                for (int i = 0; i < 7; i++) s += (i < entered.Length ? entered[i].ToString() : "_") + (i < 6 ? " " : "");
                txt.text = s;
                txt.color = new Color(0.4f, 1f, 0.5f);
            };
            var grid = UIKit.Rect(box, "Grid");
            UIKit.Place(grid, 0.5f, 1f, new Vector2(0, -150), new Vector2(480, 540), 0.5f, 1f);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.childAlignment = TextAnchor.UpperCenter; // 3*150+2*15 = 480 wide, 4*125+3*10 = 530 tall
            g.cellSize = new Vector2(150, 125);
            g.spacing = new Vector2(15, 10);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 3;
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "C", "0", "OK" };
            foreach (var k in keys)
            {
                string key = k;
                var b = UIKit.Btn(grid, "K" + k, null, () =>
                {
                    if (AudioManager.I != null) AudioManager.I.Play2D("keypad_beep", 0.6f, 1f, true);
                    if (solved) return;
                    if (key == "C") { entered = ""; refresh(); return; }
                    if (key == "OK")
                    {
                        if (entered.Length < 7 || solved) return;
                        if (tryCode(entered))
                        {
                            solved = true; // a second OK tap would open the safe again and spawn a second keycard
                            txt.text = Loc.T("keypad_open");
                            txt.color = new Color(0.5f, 1f, 0.5f);
                            StartCoroutine(CloseLater(0.8f, root.gameObject, onClose));
                        }
                        else
                        {
                            txt.text = Loc.T("keypad_wrong");
                            txt.color = new Color(1f, 0.3f, 0.25f);
                            entered = "";
                        }
                        return;
                    }
                    if (entered.Length >= 7) return;
                    entered += key;
                    refresh();
                }, new Vector2(150, 125), key == "OK", 50);
                Color kc = key == "OK" ? UIKit.BloodBright : (key == "C" ? UIKit.Dim : UIKit.Bone);
                var lbl = UIKit.Txt(b.transform, "L", key == "OK" ? Loc.Upper(Loc.T("ok")) : key, key == "OK" ? 50 : 64, kc, TextAnchor.MiddleCenter, FontRole.UIBold, false);
                UIKit.Stretch(lbl.rectTransform);
                UIKit.SoftShadow(lbl);
                b.targetGraphic = lbl;
            }
        }

        IEnumerator CloseLater(float s, GameObject panel, Action onClose)
        {
            yield return new WaitForSecondsRealtime(s);
            // the player may already have closed it (X button) — don't run onClose (unpause) a second time
            if (panel == null || openPanel != panel) yield break;
            CloseAll();
            onClose?.Invoke();
        }

        public void ShowDeath(string robotNameKey, int lives, int maxLives, Action onContinue, Action onMenu)
        {
            var root = UIKit.Rect(panels, "Death");
            UIKit.Stretch(root);
            var bg = UIKit.Blocker(root, 1f);
            bg.color = new Color(0.02f, 0, 0, 1f);
            Open(root.gameObject);
            var c = UIKit.Rect(root, "Content");
            UIKit.Place(c, 0.5f, 0.5f, Vector2.zero, new Vector2(1600, 760));
            c.gameObject.AddComponent<UIFit>();
            var t = UIKit.Txt(c, "Title", "caught", 110, UIKit.BloodBright, TextAnchor.MiddleCenter, FontRole.Heavy);
            LetterSpacing.Add(t, 0.04f);
            UIKit.Place(t.rectTransform, 0.5f, 0.5f, new Vector2(0, 250), new Vector2(1600, 160));
            UIKit.SoftShadow(t);
            var who = UIKit.Txt(c, "Who", Loc.F("caught_by", Loc.T(robotNameKey)), 38, UIKit.Bone, TextAnchor.MiddleCenter, FontRole.UI, false);
            UIKit.Place(who.rectTransform, 0.5f, 0.5f, new Vector2(0, 140), new Vector2(1400, 60));
            var lv = UIKit.Txt(c, "Lives", Loc.F("lives_left", lives, maxLives), 34, UIKit.Dim, TextAnchor.MiddleCenter, FontRole.UI, false);
            UIKit.Place(lv.rectTransform, 0.5f, 0.5f, new Vector2(0, 76), new Vector2(1000, 60));
            var cont = UIKit.Btn(c, "Continue", "continue", () => { CloseAll(); onContinue?.Invoke(); }, new Vector2(560, 116), true, 56);
            UIKit.Place((RectTransform)cont.transform, 0.5f, 0.5f, new Vector2(0, -70), new Vector2(560, 116));
            var menu = UIKit.AltBtn(c, "Menu", "quit_to_menu", () => { CloseAll(); onMenu?.Invoke(); }, new Vector2(520, 84), 38);
            UIKit.Place((RectTransform)menu.transform, 0.5f, 0.5f, new Vector2(0, -190), new Vector2(520, 84));
            var cg = root.gameObject.AddComponent<CanvasGroup>();
            StartCoroutine(FadeIn(cg, 0.8f));
        }

        public void ShowEnd(bool escaped, string statsText, Action onMenu, Action onRetry)
        {
            var root = UIKit.Rect(panels, "End");
            UIKit.Stretch(root);
            var bg = UIKit.Blocker(root, 1f);
            bg.color = escaped ? new Color(0.02f, 0.025f, 0.035f, 1f) : new Color(0.03f, 0f, 0f, 1f);
            Open(root.gameObject);
            var c = UIKit.Rect(root, "Content");
            UIKit.Place(c, 0.5f, 0.5f, Vector2.zero, new Vector2(1700, 820));
            c.gameObject.AddComponent<UIFit>();
            var t = UIKit.Txt(c, "Title", escaped ? "escaped" : "game_over", 112, escaped ? UIKit.Bone : UIKit.BloodBright, TextAnchor.MiddleCenter, FontRole.Heavy);
            LetterSpacing.Add(t, 0.04f);
            UIKit.Place(t.rectTransform, 0.5f, 0.5f, new Vector2(0, 310), new Vector2(1700, 160));
            UIKit.SoftShadow(t);
            var sub = UIKit.Txt(c, "Sub", escaped ? "escaped_sub" : "game_over_sub", 34, UIKit.Dim);
            UIKit.Place(sub.rectTransform, 0.5f, 0.5f, new Vector2(0, 210), new Vector2(1400, 60));
            var st = UIKit.Txt(c, "Stats", statsText, 32, UIKit.Bone, TextAnchor.MiddleCenter, FontRole.UI, false);
            st.lineSpacing = 1.2f;
            UIKit.Place(st.rectTransform, 0.5f, 0.5f, new Vector2(0, 25), new Vector2(1200, 290)); // v1.9: room for the Custom Night line
            var retry = UIKit.Btn(c, "Retry", escaped ? "play_again" : "try_again", () => { CloseAll(); onRetry?.Invoke(); }, new Vector2(520, 110), true, 52);
            UIKit.Place((RectTransform)retry.transform, 0.5f, 0.5f, new Vector2(-280, -250), new Vector2(520, 110));
            var menu = UIKit.AltBtn(c, "Menu", "quit_to_menu", () => { CloseAll(); onMenu?.Invoke(); }, new Vector2(520, 110), 40);
            UIKit.Place((RectTransform)menu.transform, 0.5f, 0.5f, new Vector2(280, -250), new Vector2(520, 110));
            var cg = root.gameObject.AddComponent<CanvasGroup>();
            StartCoroutine(FadeIn(cg, 1.6f));
        }

        IEnumerator FadeIn(CanvasGroup cg, float s)
        {
            float t = 0;
            cg.alpha = 0;
            while (t < s && cg != null) { t += Time.unscaledDeltaTime; cg.alpha = t / s; yield return null; }
            if (cg != null) cg.alpha = 1;
        }
    }
}
