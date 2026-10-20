using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RustedFair
{
    /// <summary>Floating virtual joystick (left side).</summary>
    public class TouchJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform bg, knob;
        public float radius = 120f;
        const float IdleAlpha = 0.3f; // v1.1: lighter, more transparent touch controls
        int pointer = int.MinValue;
        Vector2 home;
        CanvasGroup cg;

        void Start()
        {
            home = bg.anchoredPosition;
            cg = bg.GetComponent<CanvasGroup>();
            if (cg == null) cg = bg.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = IdleAlpha;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != int.MinValue) return;
            pointer = e.pointerId;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var local);
            // joystick origin follows the thumb (within the area)
            bg.localPosition = local;
            knob.anchoredPosition = Vector2.zero;
            cg.alpha = 0.75f;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var local);
            Vector2 d = local - (Vector2)bg.localPosition;
            // drag the base along if the thumb goes far beyond the radius (comfortable for long sprints)
            if (d.magnitude > radius * 1.6f)
            {
                bg.localPosition = (Vector2)bg.localPosition + d.normalized * (d.magnitude - radius * 1.6f);
                d = local - (Vector2)bg.localPosition;
            }
            var c = Vector2.ClampMagnitude(d, radius);
            knob.anchoredPosition = c;
            Vector2 v = c / radius;
            // small dead zone
            InputHub.TouchMove = v.magnitude < 0.12f ? Vector2.zero : v;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            pointer = int.MinValue;
            InputHub.TouchMove = Vector2.zero;
            knob.anchoredPosition = Vector2.zero;
            bg.anchoredPosition = home;
            cg.alpha = IdleAlpha;
        }

        void OnDisable()
        {
            pointer = int.MinValue;
            InputHub.TouchMove = Vector2.zero;
            // controls hidden mid-drag (death, cutscene): put the stick back home
            if (cg != null)
            {
                knob.anchoredPosition = Vector2.zero;
                bg.anchoredPosition = home;
                cg.alpha = IdleAlpha;
            }
        }
    }

    /// <summary>Shared look-steering for touch: same sensitivity on the look pad and on held action buttons.</summary>
    public static class TouchLook
    {
        public static void Steer(Vector2 screenDelta)
        {
            float k = 150f / Mathf.Max(400f, Screen.height) * SaveSystem.Data.sensitivity;
            InputHub.LookDelta += screenDelta * k;
        }
    }

    /// <summary>
    /// Right-side drag area for looking around. v1.2: every finger is tracked by its own pointer id (a second finger can
    /// look while another holds a button, and two fingers on the pad both steer); no drag threshold, so looking starts at once.
    /// </summary>
    public class TouchLookPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        readonly HashSet<int> pointers = new HashSet<int>();

        public void OnPointerDown(PointerEventData e) => pointers.Add(e.pointerId);

        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;

        public void OnDrag(PointerEventData e)
        {
            if (!pointers.Contains(e.pointerId)) return;
            TouchLook.Steer(e.delta);
        }

        public void OnPointerUp(PointerEventData e) => pointers.Remove(e.pointerId);

        void OnDisable() => pointers.Clear();
    }

    /// <summary>
    /// A round action button with hold support. v1.2 multi-touch: each finger is tracked by its own pointer id; the
    /// button is held from the first finger down until its LAST finger lifts. A finger that pressed it can slide to look
    /// around while the button stays held (lookWhileHeld; off for pause/tasks).
    /// </summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public Action onDown, onUp;
        public bool lookWhileHeld = true;
        public Image icon, bg;
        readonly HashSet<int> pointers = new HashSet<int>();
        float pressedScale = 1f;

        public bool Held => pointers.Count > 0;

        public void OnPointerDown(PointerEventData e)
        {
            if (!pointers.Add(e.pointerId)) return;
            if (pointers.Count > 1) return; // already held by another finger
            pressedScale = 0.9f;
            onDown?.Invoke();
        }

        public void OnInitializePotentialDrag(PointerEventData e) { if (lookWhileHeld) e.useDragThreshold = false; }

        public void OnDrag(PointerEventData e)
        {
            if (!lookWhileHeld || !pointers.Contains(e.pointerId)) return;
            TouchLook.Steer(e.delta);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!pointers.Remove(e.pointerId) || pointers.Count > 0) return;
            pressedScale = 1f;
            onUp?.Invoke();
        }

        void OnDisable()
        {
            if (pointers.Count > 0) onUp?.Invoke();
            pointers.Clear();
            pressedScale = 1f;
            transform.localScale = Vector3.one;
        }

        void Update()
        {
            float s = Mathf.Lerp(transform.localScale.x, pressedScale, 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime));
            transform.localScale = new Vector3(s, s, 1);
        }

        public static TouchButton Make(Transform parent, string name, string iconName, float size, Vector2 anchor, Vector2 pos, Action down, Action up = null)
        {
            // v1.1: lighter / more transparent round buttons
            var bg = UIKit.Img(parent, name, "btn_circle", new Color(1f, 1f, 1f, 0.42f));
            if (bg.sprite == null) bg.color = new Color(0, 0, 0, 0.25f);
            bg.raycastTarget = true;
            UIKit.Place(bg.rectTransform, anchor.x, anchor.y, pos, new Vector2(size, size));
            var ic = UIKit.Img(bg.transform, "Icon", iconName, new Color(UIKit.Bone.r, UIKit.Bone.g, UIKit.Bone.b, 0.9f));
            UIKit.Stretch(ic.rectTransform, size * 0.24f);
            ic.preserveAspect = true;
            var tb = bg.gameObject.AddComponent<TouchButton>();
            tb.onDown = down;
            tb.onUp = up;
            tb.icon = ic;
            tb.bg = bg;
            return tb;
        }

        /// <summary>Box-less button with an icon and a text label (the top-left TASKS button). pos is its left-centre.</summary>
        public static TouchButton MakeLabeled(Transform parent, string name, string iconName, string labelKey, Vector2 size, Vector2 anchor, Vector2 pos, Action down, Action up = null)
        {
            var bg = UIKit.Img(parent, name, null, new Color(0, 0, 0, 0f));
            bg.raycastTarget = true;
            UIKit.Place(bg.rectTransform, anchor.x, anchor.y, pos, size, 0f, 0.5f);
            float ics = size.y * 0.62f;
            var ic = UIKit.Img(bg.transform, "Icon", iconName, UIKit.Bone);
            UIKit.Place(ic.rectTransform, 0f, 0.5f, new Vector2(6f, 0f), new Vector2(ics, ics), 0f, 0.5f);
            ic.preserveAspect = true;
            var ish = ic.gameObject.AddComponent<Shadow>();
            ish.effectColor = new Color(0, 0, 0, 0.75f);
            ish.effectDistance = new Vector2(2f, -2f);
            var t = UIKit.Txt(bg.transform, "Label", labelKey, 34, UIKit.Bone, TextAnchor.MiddleLeft, FontRole.UIBold);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(ics + 18f, 0f);
            t.rectTransform.offsetMax = Vector2.zero;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 18;
            t.resizeTextMaxSize = 34;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            UIKit.Upper(t);
            UIKit.SoftShadow(t);
            var tb = bg.gameObject.AddComponent<TouchButton>();
            tb.onDown = down;
            tb.onUp = up;
            tb.icon = ic;
            tb.bg = bg;
            return tb;
        }
    }
}
