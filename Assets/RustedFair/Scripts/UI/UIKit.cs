using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RustedFair
{
    /// <summary>Builds the whole UI from code (no prefabs needed). Reference resolution 1920x1080, landscape.</summary>
    public static class UIKit
    {
        // v1.1 palette: bone text (#E4DAC7), dim alt text (#9A907F), title red (#FF4535)
        public static readonly Color Bone = new Color(0.894f, 0.855f, 0.780f);
        public static readonly Color Dim = new Color(0.604f, 0.565f, 0.498f);
        public static readonly Color Blood = new Color(0.72f, 0.06f, 0.05f);
        public static readonly Color BloodBright = new Color(1f, 0.271f, 0.208f);
        public static readonly Color Panel = new Color(0.06f, 0.05f, 0.05f, 0.94f);
        public static readonly Color Rust = new Color(0.62f, 0.33f, 0.16f);
        /// <summary>Alpha of the full-screen scrim behind every panel (no card/box backgrounds since v1.1).</summary>
        public const float ScrimAlpha = 0.86f;

        public static Canvas MakeCanvas(string name, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.6f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform SafeRoot(Transform canvas)
        {
            var rt = Rect(canvas, "Safe");
            Stretch(rt);
            rt.gameObject.AddComponent<SafeArea>();
            return rt;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        /// <summary>Anchors at a single point (ax, ay in 0..1) with a pivot and pixel offset/size.</summary>
        public static RectTransform Place(RectTransform rt, float ax, float ay, Vector2 pos, Vector2 size, float px = 0.5f, float py = 0.5f)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(px, py);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Img(Transform parent, string name, string sprite, Color color, Vector4 border = default)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            var s = string.IsNullOrEmpty(sprite) ? null : RF.Sprite(sprite, border);
            img.sprite = s;
            img.color = color;
            img.raycastTarget = false;
            if (s != null && border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }

        public static Text Txt(Transform parent, string name, string keyOrText, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontRole role = FontRole.UI, bool localized = true)
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Fonts.Get(role);
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            if (localized)
            {
                var lt = rt.gameObject.AddComponent<LocText>();
                lt.role = role;
                lt.Set(keyOrText);
            }
            else t.text = keyOrText;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.85f);
            sh.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        /// <summary>
        /// Text-only button (no background image or outline): centred uppercase label with a soft drop shadow.
        /// Primary buttons (Play / Resume / Continue / OK) use Cinzel Bold in red with wide tracking (v1.6, was Creepster);
        /// alt buttons are dimmer bone. A transparent graphic keeps the whole rect clickable.
        /// </summary>
        public static Button Btn(Transform parent, string name, string key, Action onClick, Vector2 size, bool primary = true, int fontSize = 40)
        {
            var img = Img(parent, name, null, new Color(0, 0, 0, 0f));
            img.raycastTarget = true; // fully transparent images still receive raycasts
            img.rectTransform.sizeDelta = size;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.95f, 0.9f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.75f, 0.68f, 0.66f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.45f);
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            b.targetGraphic = img;
            b.onClick.AddListener(() =>
            {
                if (AudioManager.I != null) AudioManager.I.UI();
                onClick?.Invoke();
            });
            img.gameObject.AddComponent<UIPress>();
            if (!string.IsNullOrEmpty(key))
            {
                int fs = primary ? Mathf.RoundToInt(fontSize * 1.05f) : fontSize; // v1.6: a bit smaller than the old x1.2
                var t = Txt(img.transform, "Label", key, fs, primary ? BloodBright : Bone, TextAnchor.MiddleCenter, primary ? FontRole.Title : FontRole.UIBold);
                Stretch(t.rectTransform, 4f);
                t.resizeTextForBestFit = true;
                t.verticalOverflow = VerticalWrapMode.Truncate; // best fit only shrinks text that would overflow the rect
                t.resizeTextMinSize = 16;
                t.resizeTextMaxSize = fs;
                Upper(t);
                if (primary) LetterSpacing.Add(t, 0.16f);
                SoftShadow(t);
                b.targetGraphic = t; // tint the label itself (the hit area is invisible)
            }
            return b;
        }

        /// <summary>Alt-coloured (dimmer) text button, e.g. secondary menu entries.</summary>
        public static Button AltBtn(Transform parent, string name, string key, Action onClick, Vector2 size, int fontSize = 36)
        {
            var b = Btn(parent, name, key, onClick, size, false, fontSize);
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.color = Dim;
            return b;
        }

        /// <summary>Text-only button whose label is computed (refreshes on language change and after each click).</summary>
        public static Button DynBtn(Transform parent, string name, Func<string> text, Action onClick, Vector2 size, int fontSize = 34, Color? color = null)
        {
            Text label = null;
            var b = Btn(parent, name, null, null, size, false, fontSize);
            label = Txt(b.transform, "Label", Loc.Upper(text()), fontSize, color ?? Bone, TextAnchor.MiddleCenter, FontRole.UIBold, false);
            Stretch(label.rectTransform, 4f);
            label.resizeTextForBestFit = true;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextMinSize = 16;
            label.resizeTextMaxSize = fontSize;
            SoftShadow(label);
            b.targetGraphic = label;
            Action refresh = () => { if (label != null) { label.font = Fonts.Get(FontRole.UIBold); label.text = Loc.Upper(text()); } };
            b.onClick.AddListener(() => { onClick?.Invoke(); refresh(); });
            Loc.OnChanged += refresh;
            b.gameObject.AddComponent<OnDestroyHook>().onDestroy = () => Loc.OnChanged -= refresh;
            return b;
        }

        /// <summary>Makes a text uppercase (localized texts stay uppercase after language changes).</summary>
        public static void Upper(Text t)
        {
            var lt = t.GetComponent<LocText>();
            if (lt != null) { lt.upper = true; lt.Refresh(); }
            else t.text = Loc.Upper(t.text);
        }

        /// <summary>A second, wider and fainter shadow under the standard one: readable over the 3D world without a box.</summary>
        public static void SoftShadow(Graphic g, float alpha = 0.55f)
        {
            var sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, alpha);
            sh.effectDistance = new Vector2(4f, -4f);
        }

        public static Button IconBtn(Transform parent, string name, string icon, Action onClick, float size, Color? tint = null, bool background = true)
        {
            Image bg;
            if (background)
            {
                bg = Img(parent, name, "btn_circle", Color.white);
                if (bg.sprite == null) bg.color = new Color(0, 0, 0, 0.45f);
            }
            else
            {
                bg = Img(parent, name, null, new Color(0, 0, 0, 0.001f));
            }
            bg.raycastTarget = true;
            bg.rectTransform.sizeDelta = new Vector2(size, size);
            var ic = Img(bg.transform, "Icon", icon, tint ?? Bone);
            Stretch(ic.rectTransform, background ? size * 0.22f : size * 0.14f);
            ic.preserveAspect = true;
            if (!background)
            {
                var ish = ic.gameObject.AddComponent<Shadow>();
                ish.effectColor = new Color(0, 0, 0, 0.7f);
                ish.effectDistance = new Vector2(2f, -2f);
            }
            var b = bg.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(() =>
            {
                if (AudioManager.I != null) AudioManager.I.UI();
                onClick?.Invoke();
            });
            bg.gameObject.AddComponent<UIPress>();
            return b;
        }

        /// <summary>
        /// Centred content area of a panel. Since v1.1 it has no card/box background (the full-screen scrim does
        /// the dimming) and it scales itself down to always fit the screen (short landscape phones).
        /// </summary>
        public static Image PanelBox(Transform parent, string name, Vector2 size)
        {
            var img = Img(parent, name, null, new Color(0, 0, 0, 0f));
            img.raycastTarget = true;
            Place(img.rectTransform, 0.5f, 0.5f, Vector2.zero, size);
            img.gameObject.AddComponent<UIFit>();
            return img;
        }

        /// <summary>Full-screen dim blocker that eats clicks.</summary>
        public static Image Blocker(Transform parent, float alpha = 0.75f)
        {
            var img = Img(parent, "Blocker", null, new Color(0, 0, 0, alpha));
            Stretch(img.rectTransform);
            img.raycastTarget = true;
            return img;
        }

        public static UISlider Slider(Transform parent, string labelKey, float value, Action<float> onChange, float width = 760f)
        {
            var row = Rect(parent, labelKey);
            row.sizeDelta = new Vector2(width, 70);
            var label = Txt(row, "Label", labelKey, 30, Bone, TextAnchor.MiddleLeft);
            Place(label.rectTransform, 0f, 0.5f, Vector2.zero, new Vector2(width * 0.42f, 70), 0f, 0.5f);
            var track = Img(row, "Track", null, new Color(1f, 1f, 1f, 0.16f));
            Place(track.rectTransform, 1f, 0.5f, Vector2.zero, new Vector2(width * 0.55f, 8), 1f, 0.5f);
            track.raycastTarget = true;
            var fill = Img(track.transform, "Fill", null, BloodBright);
            fill.rectTransform.anchorMin = new Vector2(0, 0);
            fill.rectTransform.anchorMax = new Vector2(value, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var knob = Img(track.transform, "Knob", "joystick_knob", Bone);
            if (knob.sprite == null) knob.color = Bone;
            knob.rectTransform.sizeDelta = new Vector2(38, 38);
            var s = track.gameObject.AddComponent<UISlider>();
            s.Init(fill.rectTransform, knob.rectTransform, value, onChange);
            // a taller invisible hit area
            var hit = Img(track.transform, "Hit", null, new Color(0, 0, 0, 0.001f));
            hit.rectTransform.anchorMin = new Vector2(0, -2.5f);
            hit.rectTransform.anchorMax = new Vector2(1, 3.5f);
            hit.rectTransform.offsetMin = hit.rectTransform.offsetMax = Vector2.zero;
            hit.raycastTarget = true;
            hit.gameObject.AddComponent<UIForwardDrag>().target = s;
            return s;
        }

        /// <summary>A row with a label and a value button that cycles options.</summary>
        public static Text Cycler(Transform parent, string labelKey, Func<string> valueText, Action onCycle, float width = 760f)
        {
            var row = Rect(parent, labelKey);
            row.sizeDelta = new Vector2(width, 70);
            var label = Txt(row, "Label", labelKey, 30, Bone, TextAnchor.MiddleLeft);
            Place(label.rectTransform, 0f, 0.5f, Vector2.zero, new Vector2(width * 0.42f, 70), 0f, 0.5f);
            Text valueLabel = null;
            var b = Btn(row, "Value", null, null, new Vector2(width * 0.55f, 62), false);
            Place((RectTransform)b.transform, 1f, 0.5f, Vector2.zero, new Vector2(width * 0.55f, 62), 1f, 0.5f);
            valueLabel = Txt(b.transform, "V", "‹  " + valueText() + "  ›", 30, BloodBright, TextAnchor.MiddleRight, FontRole.UIBold, false);
            Stretch(valueLabel.rectTransform, 6);
            valueLabel.resizeTextForBestFit = true;
            valueLabel.verticalOverflow = VerticalWrapMode.Truncate;
            valueLabel.resizeTextMinSize = 16;
            valueLabel.resizeTextMaxSize = 30;
            b.targetGraphic = valueLabel;
            b.onClick.AddListener(() =>
            {
                onCycle?.Invoke();
                valueLabel.font = Fonts.Get(FontRole.UIBold);
                valueLabel.text = "‹  " + valueText() + "  ›";
            });
            Action onLang = () => { if (valueLabel != null) { valueLabel.font = Fonts.Get(FontRole.UIBold); valueLabel.text = "‹  " + valueText() + "  ›"; } };
            Loc.OnChanged += onLang;
            // static event: unsubscribe when the row dies, otherwise every opened panel leaks a handler
            row.gameObject.AddComponent<OnDestroyHook>().onDestroy = () => Loc.OnChanged -= onLang;
            return valueLabel;
        }

        public static VerticalLayoutGroup VList(RectTransform rt, float spacing, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childAlignment = align;
            v.childControlHeight = false;
            v.childControlWidth = false;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = false;
            return v;
        }

        public static ScrollRect Scroll(Transform parent, string name, Vector2 size, out RectTransform content)
        {
            var view = Img(parent, name, null, new Color(0, 0, 0, 0.001f));
            view.raycastTarget = true;
            view.rectTransform.sizeDelta = size;
            view.gameObject.AddComponent<RectMask2D>();
            content = Rect(view.transform, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 0);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = content;
            sr.viewport = view.rectTransform;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 30f;
            return sr;
        }

        /// <summary>A wrapped text block for vertical layouts: its height follows the text (needs childControlHeight on the layout).</summary>
        public static Text Para(Transform parent, string name, string text, int size, Color color, float width,
            TextAnchor align = TextAnchor.UpperCenter, FontRole role = FontRole.UI, float lineSpacing = 1.1f)
        {
            var t = Txt(parent, name, text, size, color, align, role, false);
            t.lineSpacing = lineSpacing;
            t.rectTransform.sizeDelta = new Vector2(width, size * 1.5f);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            return t;
        }

        /// <summary>Vertical list inside a ScrollRect content whose children size themselves to their text.</summary>
        public static VerticalLayoutGroup FlowList(RectTransform content, float spacing, RectOffset padding)
        {
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = false;
            return v;
        }
    }

    /// <summary>
    /// Uniformly scales a fixed-size RectTransform down so it always fits inside its parent (minus a margin).
    /// Keeps panels, the menu column and the briefing on screen on short landscape phones (e.g. 1920x864, notches).
    /// </summary>
    public class UIFit : MonoBehaviour
    {
        public float margin = 24f;
        public float maxScale = 1f;
        RectTransform rt, parent;

        void Awake() { rt = (RectTransform)transform; }
        void OnEnable() => Apply();
        void LateUpdate() => Apply();

        public void Apply()
        {
            if (rt == null) rt = (RectTransform)transform;
            if (parent == null) parent = rt.parent as RectTransform;
            if (parent == null) return;
            var pr = parent.rect;
            var size = rt.rect.size;
            if (size.x <= 1f || size.y <= 1f || pr.width <= 1f || pr.height <= 1f) return;
            float s = Mathf.Min(maxScale, (pr.width - margin * 2f) / size.x, (pr.height - margin * 2f) / size.y);
            s = Mathf.Max(0.3f, s);
            if (Mathf.Abs(rt.localScale.x - s) > 0.0005f) rt.localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>Keeps a RectTransform inside the device safe area (notches, rounded corners).</summary>
    public class SafeArea : MonoBehaviour
    {
        Rect last;
        Vector2Int lastRes;
        RectTransform rt;

        void Awake() { rt = (RectTransform)transform; Apply(); }
        void Update()
        {
            if (Screen.safeArea != last || lastRes.x != Screen.width || lastRes.y != Screen.height) Apply();
        }

        void Apply()
        {
            last = Screen.safeArea;
            lastRes = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var min = last.position;
            var max = last.position + last.size;
            min.x /= Screen.width; min.y /= Screen.height;
            max.x /= Screen.width; max.y /= Screen.height;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Small squash when pressed.</summary>
    public class UIPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        float target = 1f;
        public void OnPointerDown(PointerEventData e) => target = 0.94f;
        public void OnPointerUp(PointerEventData e) => target = 1f;
        public void OnPointerExit(PointerEventData e) => target = 1f;
        void OnDisable() { target = 1f; transform.localScale = Vector3.one; }
        void Update()
        {
            float s = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            transform.localScale = new Vector3(s, s, 1f);
        }
    }

    public class UISlider : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        RectTransform fill, knob, track;
        Action<float> onChange;
        public float Value { get; private set; }

        public void Init(RectTransform fillRt, RectTransform knobRt, float v, Action<float> cb)
        {
            track = (RectTransform)transform;
            fill = fillRt; knob = knobRt; onChange = cb;
            SetValue(v, false);
        }

        public void SetValue(float v, bool notify)
        {
            Value = Mathf.Clamp01(v);
            fill.anchorMax = new Vector2(Value, 1f);
            knob.anchorMin = knob.anchorMax = new Vector2(Value, 0.5f);
            knob.anchoredPosition = Vector2.zero;
            if (notify) onChange?.Invoke(Value);
        }

        public void OnPointerDown(PointerEventData e) => Drag(e);
        public void OnDrag(PointerEventData e) => Drag(e);

        public void Drag(PointerEventData e)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(track, e.position, e.pressEventCamera, out var local))
            {
                var r = track.rect;
                SetValue((local.x - r.xMin) / r.width, true);
            }
        }
    }

    public class UIForwardDrag : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public UISlider target;
        public void OnPointerDown(PointerEventData e) { if (target != null) target.Drag(e); }
        public void OnDrag(PointerEventData e) { if (target != null) target.Drag(e); }
    }
}
