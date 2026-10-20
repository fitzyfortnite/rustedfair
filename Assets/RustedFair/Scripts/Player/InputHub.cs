using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RustedFair
{
    /// <summary>
    /// Collects input from the on-screen touch controls and (for editor/desktop testing) keyboard + mouse.
    /// One-shot presses are latched until consumed, so script execution order never drops a tap.
    /// </summary>
    public static class InputHub
    {
        public static Vector2 TouchMove;
        public static Vector2 LookDelta;          // degrees accumulated since last consume
        public static bool SprintHeld;
        public static bool InteractHeld;

        static bool crouch, interact, flash, mask, throwIt, cycle, pause, notes, inventory;

        public static void PressCrouch() => crouch = true;
        public static void PressInteract() => interact = true;
        public static void PressFlash() => flash = true;
        public static void PressMask() => mask = true;
        public static void PressThrow() => throwIt = true;
        public static void PressCycle() => cycle = true;
        public static void PressPause() => pause = true;
        public static void PressNotes() => notes = true;
        public static void PressInventory() => inventory = true;

        public static bool ConsumeCrouch() { bool b = crouch; crouch = false; return b; }
        public static bool ConsumeInteract() { bool b = interact; interact = false; return b; }
        public static bool ConsumeFlash() { bool b = flash; flash = false; return b; }
        public static bool ConsumeMask() { bool b = mask; mask = false; return b; }
        public static bool ConsumeThrow() { bool b = throwIt; throwIt = false; return b; }
        public static bool ConsumeCycle() { bool b = cycle; cycle = false; return b; }
        public static bool ConsumePause() { bool b = pause; pause = false; return b; }
        public static bool ConsumeNotes() { bool b = notes; notes = false; return b; }
        public static bool ConsumeInventory() { bool b = inventory; inventory = false; return b; }

        public static Vector2 ConsumeLook()
        {
            var d = LookDelta;
            LookDelta = Vector2.zero;
            return d;
        }

        public static void ResetAll()
        {
            TouchMove = Vector2.zero; LookDelta = Vector2.zero;
            SprintHeld = InteractHeld = false;
            crouch = interact = flash = mask = throwIt = cycle = pause = notes = inventory = false;
        }

        // ------------------------------------------------------------------ desktop / editor
        public static bool MouseLook;
        static bool kbSprint, kbInteractHeld;

        public static Vector2 KeyboardMove()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return Vector2.zero;
            float x = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
            float y = (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
#else
            return Vector2.zero;
#endif
        }

        /// <summary>Polls keyboard/mouse once per frame (called by the player).</summary>
        public static void PollDesktop(float sensitivity)
        {
            if (Application.isMobilePlatform) return;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            var m = Mouse.current;
            if (k != null)
            {
                kbSprint = k.leftShiftKey.isPressed;
                kbInteractHeld = k.eKey.isPressed;
                if (k.eKey.wasPressedThisFrame) interact = true;
                if (k.cKey.wasPressedThisFrame || k.leftCtrlKey.wasPressedThisFrame) crouch = true;
                if (k.fKey.wasPressedThisFrame) flash = true;
                if (k.qKey.wasPressedThisFrame) mask = true;
                if (k.gKey.wasPressedThisFrame) throwIt = true;
                if (k.tabKey.wasPressedThisFrame) cycle = true;
                if (k.jKey.wasPressedThisFrame) notes = true;
                if (k.iKey.wasPressedThisFrame) inventory = true;
                if (k.escapeKey.wasPressedThisFrame) pause = true;
            }
            if (m != null)
            {
                if (m.rightButton.wasPressedThisFrame) MouseLook = !MouseLook;
                if (MouseLook) LookDelta += m.delta.ReadValue() * 0.09f * sensitivity;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            kbSprint = Input.GetKey(KeyCode.LeftShift);
            kbInteractHeld = Input.GetKey(KeyCode.E);
            if (Input.GetKeyDown(KeyCode.E)) interact = true;
            if (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.LeftControl)) crouch = true;
            if (Input.GetKeyDown(KeyCode.F)) flash = true;
            if (Input.GetKeyDown(KeyCode.Q)) mask = true;
            if (Input.GetKeyDown(KeyCode.G)) throwIt = true;
            if (Input.GetKeyDown(KeyCode.Tab)) cycle = true;
            if (Input.GetKeyDown(KeyCode.J)) notes = true;
            if (Input.GetKeyDown(KeyCode.I)) inventory = true;
            if (Input.GetKeyDown(KeyCode.Escape)) pause = true;
            if (Input.GetMouseButtonDown(1)) MouseLook = !MouseLook;
            if (MouseLook) LookDelta += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 2.2f * sensitivity;
#endif
            if (Time.timeScale <= 0f) MouseLook = false; // paused / menus: free the cursor
            Cursor.lockState = MouseLook ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !MouseLook;
        }

        public static bool Sprint => SprintHeld || kbSprint;
        public static bool InteractHold => InteractHeld || kbInteractHeld;

        public static Vector2 Move
        {
            get
            {
                var k = Application.isMobilePlatform ? Vector2.zero : KeyboardMove();
                return k.sqrMagnitude > 0.01f ? k : TouchMove;
            }
        }
    }
}
