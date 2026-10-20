using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RustedFair
{
    /// <summary>Hint text in the bottom-right corner (no box since v1.1); occasional general tips plus contextual ones.</summary>
    public class Tips : MonoBehaviour
    {
        RectTransform box;
        Vector2 basePos;
        Text text;
        CanvasGroup cg;
        readonly Queue<string> queue = new Queue<string>();
        readonly HashSet<string> shown = new HashSet<string>();
        bool running;
        float idleTimer = 45f;

        static readonly string[] General =
        {
            "tip_crouch", "tip_hide", "tip_sprint", "tip_flashlight", "tip_oil", "tip_water", "tip_toy",
            "tip_listen", "tip_doors", "tip_notes", "tip_brutus", "tip_rattle", "tip_grin", "tip_battery", "tip_journal"
        };

        public static Tips Create(RectTransform parent)
        {
            // v1.1: plain right-aligned text with a soft shadow (no box), still in the bottom-right corner,
            // in the free strip under the action buttons (sprint/interact start at y≈135)
            var rt = UIKit.Rect(parent, "Tips");
            UIKit.Place(rt, 1f, 0f, new Vector2(-28, 16), new Vector2(660, 108), 1f, 0f);
            var t = rt.gameObject.AddComponent<Tips>();
            t.box = rt;
            t.basePos = rt.anchoredPosition;
            t.cg = rt.gameObject.AddComponent<CanvasGroup>();
            t.cg.alpha = 0f;
            t.cg.blocksRaycasts = false;
            t.text = UIKit.Txt(rt, "Text", "", 27, UIKit.Bone, TextAnchor.LowerRight, FontRole.UI, false);
            UIKit.Stretch(t.text.rectTransform);
            UIKit.SoftShadow(t.text, 0.7f);
            var ol = t.text.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0, 0, 0, 0.35f);
            ol.effectDistance = new Vector2(1f, -1f);
            // long translations (de/ru) would spill out of the area
            t.text.resizeTextForBestFit = true;
            t.text.resizeTextMinSize = 18;
            t.text.resizeTextMaxSize = 27;
            t.text.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        /// <summary>Queues a tip (shown once per run unless forced).</summary>
        public void Show(string key, bool force = false)
        {
            if (!force && !SaveSystem.Data.showTips) return;
            if (shown.Contains(key) && !force) return;
            shown.Add(key);
            if (queue.Contains(key)) return;
            queue.Enqueue(key);
            if (!running) StartCoroutine(Run());
        }

        const char FreeText = '\u0001'; // queue entries starting with this are literal text, not loc keys

        /// <summary>v1.2 hints: free text that jumps the queue (replacing an older hint still waiting) and is
        /// shown even if the same text appeared before.</summary>
        public void ShowText(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            var rest = queue.ToArray();
            queue.Clear();
            queue.Enqueue(FreeText + message);
            foreach (var k in rest) if (k.Length == 0 || k[0] != FreeText) queue.Enqueue(k);
            if (!running && isActiveAndEnabled) StartCoroutine(Run());
        }

        void Update()
        {
            if (!SaveSystem.Data.showTips) return;
            idleTimer -= Time.deltaTime;
            if (idleTimer <= 0f)
            {
                idleTimer = Random.Range(70f, 110f);
                for (int i = 0; i < 6; i++)
                {
                    var k = General[Random.Range(0, General.Length)];
                    if (!shown.Contains(k)) { Show(k); break; }
                }
            }
        }

        IEnumerator Run()
        {
            running = true;
            while (queue.Count > 0)
            {
                var k = queue.Dequeue();
                text.font = Fonts.Get(FontRole.UI);
                bool free = k.Length > 0 && k[0] == FreeText;
                text.text = free ? k.Substring(1) : Loc.T(k);
                float t = 0;
                while (t < 0.35f) { t += Time.unscaledDeltaTime; cg.alpha = t / 0.35f; box.anchoredPosition = basePos + new Vector2((1 - cg.alpha) * 40f, 0f); yield return null; }
                box.anchoredPosition = basePos;
                cg.alpha = 1f;
                float hold = free ? 8f : Mathf.Clamp(text.text.Length * 0.055f, 3.5f, 7f); // hints stay 8 s
                yield return new WaitForSecondsRealtime(hold);
                t = 0;
                while (t < 0.4f) { t += Time.unscaledDeltaTime; cg.alpha = 1f - t / 0.4f; yield return null; }
                cg.alpha = 0f;
                yield return new WaitForSecondsRealtime(0.6f);
            }
            running = false;
        }
    }
}
