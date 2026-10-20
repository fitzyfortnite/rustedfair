using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RustedFair
{
    /// <summary>Persistent overlay for achievement pop-ups and short messages.</summary>
    public class Toasts : MonoBehaviour
    {
        static Toasts I;
        RectTransform root;
        struct Pop { public string title, desc; public bool mode; }
        readonly Queue<Pop> queue = new Queue<Pop>();
        bool running;

        public static void Create()
        {
            if (I != null) return;
            var c = UIKit.MakeCanvas("[RF Toasts]", 4000);
            DontDestroyOnLoad(c.gameObject);
            I = c.gameObject.AddComponent<Toasts>();
            I.root = UIKit.SafeRoot(c.transform);
        }

        /// <summary>Badge pop-up (queued). Every unlock goes through here: tasks, catches, notes, escape...</summary>
        public static void Achievement(AchievementDef def)
        {
            if (def == null) return;
            Enqueue(new Pop { title = Loc.T(def.NameKey), desc = Loc.T(def.DescKey), mode = false });
        }

        /// <summary>v1.9: red-accented "NEW MODE - Custom Night unlocked" pop-up (first escape), after a short delay.</summary>
        public static void ModeUnlocked(float delay = 0.9f)
        {
            if (I == null) return;
            I.StartCoroutine(I.DelayedMode(delay));
        }

        IEnumerator DelayedMode(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            Enqueue(new Pop { title = Loc.T("mode_unlocked"), desc = Loc.T("mode_unlocked_d"), mode = true });
        }

        static void Enqueue(Pop p)
        {
            if (I == null) return;
            I.queue.Enqueue(p);
            if (!I.running) I.StartCoroutine(I.Run());
        }

        /// <summary>
        /// v1.9 (web .achp): dark strip at the top centre with a thin gold top line (red for a new mode), trophy (or skull)
        /// icon, small gold "BADGE UNLOCKED" / red "NEW MODE", the title in Cinzel Bold and a one-line description.
        /// Slides + fades in (0.35 s), stays ~4.8 s, then fades out; pop-ups are shown one after another.
        /// </summary>
        IEnumerator Run()
        {
            running = true;
            var gold = new Color(1f, 0.824f, 0.478f);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                var accent = p.mode ? UIKit.BloodBright : gold;
                var box = UIKit.Img(root, "AchPop", null, new Color(12f / 255f, 9f / 255f, 8f / 255f, 0.92f));
                box.raycastTarget = false;
                UIKit.Place(box.rectTransform, 0.5f, 1f, new Vector2(0, -24), new Vector2(1000, 150), 0.5f, 1f);
                var line = UIKit.Img(box.transform, "Top", null, new Color(accent.r, accent.g, accent.b, 0.55f));
                line.rectTransform.anchorMin = new Vector2(0f, 1f); line.rectTransform.anchorMax = new Vector2(1f, 1f);
                line.rectTransform.offsetMin = new Vector2(0f, -3f); line.rectTransform.offsetMax = Vector2.zero;
                var line2 = UIKit.Img(box.transform, "Bottom", null, new Color(gold.r, gold.g, gold.b, 0.15f));
                line2.rectTransform.anchorMin = Vector2.zero; line2.rectTransform.anchorMax = new Vector2(1f, 0f);
                line2.rectTransform.offsetMin = Vector2.zero; line2.rectTransform.offsetMax = new Vector2(0f, 3f);
                var ic = UIKit.Img(box.transform, "Icon", p.mode ? "icon_skull" : "icon_trophy", p.mode ? UIKit.BloodBright : gold);
                ic.preserveAspect = true;
                UIKit.Place(ic.rectTransform, 0f, 0.5f, new Vector2(30, 0), new Vector2(96, 96), 0f, 0.5f);
                var head = UIKit.Txt(box.transform, "Head", Loc.Upper(Loc.T(p.mode ? "mode_new" : "ach_unlocked")), 24, accent, TextAnchor.LowerLeft, FontRole.UIBold, false);
                LetterSpacing.Add(head, 0.2f);
                UIKit.Place(head.rectTransform, 0f, 1f, new Vector2(150, -12), new Vector2(820, 34), 0f, 1f);
                var title = UIKit.Txt(box.transform, "Title", p.title, 40, UIKit.Bone, TextAnchor.MiddleLeft, FontRole.Title, false);
                LetterSpacing.Add(title, 0.06f);
                title.resizeTextForBestFit = true; title.resizeTextMinSize = 24; title.resizeTextMaxSize = 40;
                title.verticalOverflow = VerticalWrapMode.Truncate;
                UIKit.Place(title.rectTransform, 0f, 1f, new Vector2(150, -46), new Vector2(820, 50), 0f, 1f);
                var desc = UIKit.Txt(box.transform, "Desc", p.desc, 24, UIKit.Dim, TextAnchor.UpperLeft, FontRole.UI, false);
                desc.resizeTextForBestFit = true; desc.resizeTextMinSize = 16; desc.resizeTextMaxSize = 24;
                desc.verticalOverflow = VerticalWrapMode.Truncate;
                UIKit.Place(desc.rectTransform, 0f, 1f, new Vector2(150, -96), new Vector2(820, 48), 0f, 1f);
                var cg = box.gameObject.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false;
                cg.interactable = false;
                if (AudioManager.I != null) AudioManager.I.Play2D("ui_achievement", 0.8f, p.mode ? 0.8f : 1f, true);

                var rt = box.rectTransform;
                float t = 0;
                while (t < 0.35f)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Ease(t / 0.35f);
                    cg.alpha = k;
                    rt.anchoredPosition = new Vector2(0, Mathf.Lerp(12f, -24f, k));
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(4.8f - 0.35f);
                t = 0;
                while (t < 0.4f)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / 0.4f);
                    cg.alpha = 1f - k;
                    rt.anchoredPosition = new Vector2(0, Mathf.Lerp(-24f, 12f, k));
                    yield return null;
                }
                Destroy(box.gameObject);
            }
            running = false;
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - Mathf.Pow(1f - x, 3f); }

        /// <summary>Short centred message (store results etc.).</summary>
        public static void Message(string text, float seconds = 2.5f)
        {
            if (I == null) return;
            I.StartCoroutine(I.MessageRoutine(text, seconds));
        }

        IEnumerator MessageRoutine(string text, float seconds)
        {
            var box = UIKit.Img(root, "Msg", null, new Color(0, 0, 0, 0f)); // no box since v1.1
            UIKit.Place(box.rectTransform, 0.5f, 0.5f, new Vector2(0, -300), new Vector2(1000, 120));
            var t = UIKit.Txt(box.transform, "T", text, 36, UIKit.Bone, TextAnchor.MiddleCenter, FontRole.UIBold, false);
            UIKit.Stretch(t.rectTransform, 10);
            UIKit.SoftShadow(t, 0.75f);
            var cg = box.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            float k = 0;
            while (k < 0.25f) { k += Time.unscaledDeltaTime; cg.alpha = k / 0.25f; yield return null; }
            yield return new WaitForSecondsRealtime(seconds);
            k = 0;
            while (k < 0.4f) { k += Time.unscaledDeltaTime; cg.alpha = 1f - k / 0.4f; yield return null; }
            Destroy(box.gameObject);
        }

        /// <summary>Editor-only stand-in for an interstitial ad.</summary>
        public static void ShowFakeAd(Action done)
        {
            if (I == null) { done(); return; }
            I.StartCoroutine(I.FakeAd(done));
        }

        IEnumerator FakeAd(Action done)
        {
            var block = UIKit.Blocker(root, 0.97f);
            var t = UIKit.Txt(block.transform, "T", "[EDITOR] TEST INTERSTITIAL AD", 60, Color.white, TextAnchor.MiddleCenter, FontRole.UIBold, false);
            UIKit.Stretch(t.rectTransform);
            yield return new WaitForSecondsRealtime(2f);
            Destroy(block.gameObject);
            done();
        }
    }
}
