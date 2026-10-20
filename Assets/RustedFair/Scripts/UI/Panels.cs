using System;
using UnityEngine;
using UnityEngine.UI;

namespace RustedFair
{
    /// <summary>Settings, Store (Remove Ads), Achievements, How to play — shared by the main menu and the pause menu.
    /// v1.1: every panel is a full-screen dark scrim with centred content (no card/box background) that scales to fit.</summary>
    public static class Panels
    {
        // v1.0.0: edit the URLs in StoreConfig (one place for all store settings)
        public const string PrivacyUrl = StoreConfig.PrivacyUrl;
        public const string TermsUrl = StoreConfig.TermsUrl;

        static RectTransform Frame(Transform parent, string titleKey, Vector2 size, Action onClose, out RectTransform body)
        {
            var root = UIKit.Rect(parent, "Panel_" + titleKey);
            UIKit.Stretch(root);
            UIKit.Blocker(root, UIKit.ScrimAlpha);
            // v1.0.3: in linear colour space a 95 % scrim still leaves the menu text ~25 % visible behind the panel's own
            // text: hide the other UI under the same parent (menu column, HUD widgets) while the panel is open
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (Transform ch in parent)
                if (ch != root && ch.gameObject.activeSelf && ch.name != "Shade" && !ch.name.StartsWith("Panel_")) { ch.gameObject.SetActive(false); hidden.Add(ch.gameObject); }
            root.gameObject.AddComponent<OnDestroyHook>().onDestroy = () => { foreach (var g in hidden) if (g != null) g.SetActive(true); };
            var box = UIKit.PanelBox(root, "Box", size);
            var title = UIKit.Txt(box.transform, "Title", titleKey, 64, UIKit.BloodBright, TextAnchor.MiddleCenter, FontRole.Title);
            LetterSpacing.Add(title, 0.1f);
            UIKit.Place(title.rectTransform, 0.5f, 1f, new Vector2(0, -46), new Vector2(size.x - 240, 84)); // clear of the close button
            UIKit.SoftShadow(title);
            var close = UIKit.IconBtn(box.transform, "Close", "icon_close", () => { onClose?.Invoke(); UnityEngine.Object.Destroy(root.gameObject); }, 80f, UIKit.Dim, false);
            UIKit.Place((RectTransform)close.transform, 1f, 1f, Vector2.zero, new Vector2(80, 80), 1f, 1f);
            body = UIKit.Rect(box.transform, "Body");
            body.anchorMin = new Vector2(0, 0);
            body.anchorMax = new Vector2(1, 1);
            body.offsetMin = new Vector2(20, 10);
            body.offsetMax = new Vector2(-20, -100);
            return root;
        }

        // ================================================================== how to play
        public static GameObject HowToPlay(Transform parent, Action onClose)
        {
            var root = Frame(parent, "how_to_play", new Vector2(1300, 880), onClose, out var body);
            var sr = UIKit.Scroll(body, "Scroll", Vector2.zero, out var content);
            UIKit.Stretch((RectTransform)sr.transform);
            UIKit.FlowList(content, 18f, new RectOffset(0, 0, 6, 30));
            float w = 1140f;
            UIKit.Para(content, "Body", Loc.T("brief_body"), 30, UIKit.Bone, w);
            UIKit.Para(content, "Step1", Loc.T("brief_step1"), 29, UIKit.Bone, w, TextAnchor.UpperCenter, FontRole.UIBold);
            UIKit.Para(content, "Step2", Loc.T("brief_step2"), 29, UIKit.Bone, w, TextAnchor.UpperCenter, FontRole.UIBold);
            UIKit.Para(content, "Step3", Loc.T("brief_step3"), 29, UIKit.Bone, w, TextAnchor.UpperCenter, FontRole.UIBold);
            UIKit.Para(content, "Controls", Loc.T("journal_help"), 26, UIKit.Dim, w);
            if (!Application.isMobilePlatform)
                UIKit.Para(content, "Desktop", Loc.T("help_desktop"), 24, UIKit.Dim, w);
            UIKit.Para(content, "Hint", Loc.T("brief_hint"), 26, UIKit.BloodBright, w);
            return root.gameObject;
        }

        // ================================================================== settings
        public static GameObject Settings(Transform parent, Action onClose)
        {
            var root = Frame(parent, "settings", new Vector2(1000, 880), () =>
            {
                SaveSystem.SettingsChanged();
                onClose?.Invoke();
            }, out var body);
            var sr = UIKit.Scroll(body, "Scroll", Vector2.zero, out var content);
            UIKit.Stretch((RectTransform)sr.transform);
            var v = UIKit.VList(content, 14f);
            v.padding = new RectOffset(0, 0, 10, 30);
            var d = SaveSystem.Data;
            float w = 860f;

            // v1.10: 14 languages - the value button opens the language grid instead of cycling
            UIKit.Cycler(content, "set_language", () => Loc.Languages[Loc.CurrentIndex()].native, () =>
            {
                var p = LanguagePicker(parent, null);
                p.transform.SetAsLastSibling();
            }, w);
            UIKit.Slider(content, "set_master", d.masterVolume, x => { d.masterVolume = x; AudioListener.volume = x; }, w);
            UIKit.Slider(content, "set_music", d.musicVolume, x => { d.musicVolume = x; SaveSystem.PreviewSettings(); }, w);
            UIKit.Slider(content, "set_sfx", d.sfxVolume, x => { d.sfxVolume = x; SaveSystem.PreviewSettings(); }, w);
            UIKit.Slider(content, "set_sensitivity", Mathf.InverseLerp(0.25f, 3f, d.sensitivity), x => d.sensitivity = Mathf.Lerp(0.25f, 3f, x), w);
            UIKit.Slider(content, "set_fov", Mathf.InverseLerp(60f, 90f, d.fov), x => d.fov = Mathf.Round(Mathf.Lerp(60f, 90f, x)), w);
            UIKit.Cycler(content, "set_invert", () => Loc.T(d.invertY ? "on" : "off"), () => d.invertY = !d.invertY, w);
            UIKit.Cycler(content, "set_quality", () => Loc.T(d.quality == 0 ? "q_low" : d.quality == 1 ? "q_med" : "q_high"), () =>
            {
                d.quality = (Mathf.Max(0, d.quality) + 1) % 3;
                SaveSystem.SettingsChanged();
            }, w);
            UIKit.Cycler(content, "set_fps", () => Mathf.Min(d.fpsTarget, SaveSystem.MaxFps) + " FPS", () =>
            {
                int max = SaveSystem.MaxFps; // 30 -> 60 (-> 120 on high-refresh displays) -> 30
                d.fpsTarget = d.fpsTarget >= max ? 30 : (d.fpsTarget >= 60 ? 120 : 60);
                SaveSystem.SettingsChanged();
            }, w);
            UIKit.Cycler(content, "set_tips", () => Loc.T(d.showTips ? "on" : "off"), () => d.showTips = !d.showTips, w);
            UIKit.Cycler(content, "set_haptics", () => Loc.T(d.haptics ? "on" : "off"), () => d.haptics = !d.haptics, w);

            // v1.0.0 (store requirements): Restore Purchases, Privacy choices (UMP, only when required),
            // Privacy Policy and Terms of Use
            var row = UIKit.Rect(content, "Links");
            row.sizeDelta = new Vector2(w, 90);
            bool privacyChoices = AdsManager.PrivacyOptionsRequired;
            var restore = UIKit.AltBtn(row, "Restore", "store_restore", () => { if (IAPManager.I != null) IAPManager.I.Restore(); }, new Vector2(w * 0.48f, 80), 30);
            UIKit.Place((RectTransform)restore.transform, privacyChoices ? 0f : 0.5f, 0.5f, Vector2.zero, new Vector2(w * 0.48f, 80), privacyChoices ? 0f : 0.5f, 0.5f);
            if (privacyChoices)
            {
                var choices = UIKit.AltBtn(row, "PrivacyChoices", "privacy_choices", () => { if (AdsManager.I != null) AdsManager.I.ShowPrivacyOptions(); }, new Vector2(w * 0.48f, 80), 30);
                UIKit.Place((RectTransform)choices.transform, 1f, 0.5f, Vector2.zero, new Vector2(w * 0.48f, 80), 1f, 0.5f);
            }
            var row2 = UIKit.Rect(content, "Legal");
            row2.sizeDelta = new Vector2(w, 90);
            var privacy = UIKit.AltBtn(row2, "Privacy", "privacy", () => Application.OpenURL(StoreConfig.PrivacyUrlForCurrentLanguage), new Vector2(w * 0.48f, 80), 30);
            UIKit.Place((RectTransform)privacy.transform, 0f, 0.5f, Vector2.zero, new Vector2(w * 0.48f, 80), 0f, 0.5f);
            var terms = UIKit.AltBtn(row2, "Terms", "terms", () => Application.OpenURL(TermsUrl), new Vector2(w * 0.48f, 80), 30);
            UIKit.Place((RectTransform)terms.transform, 1f, 0.5f, Vector2.zero, new Vector2(w * 0.48f, 80), 1f, 0.5f);

            var credits = UIKit.Txt(content, "Credits", "credits", 22, UIKit.Dim, TextAnchor.MiddleCenter);
            credits.rectTransform.sizeDelta = new Vector2(w, 80);

            // feedback for the Restore button (otherwise only the Store panel shows purchase messages)
            Action<string> msg = m => Toasts.Message(m);
            IAPManager.OnPurchaseMessage += msg;
            root.gameObject.AddComponent<OnDestroyHook>().onDestroy = () => IAPManager.OnPurchaseMessage -= msg;
            return root.gameObject;
        }

        // ================================================================== store
        public static GameObject Store(Transform parent, Action onClose)
        {
            var root = Frame(parent, "store_title", new Vector2(1200, 840), onClose, out var body);
            var desc = UIKit.Txt(body, "Desc", "store_desc", 32, UIKit.Bone);
            UIKit.Place(desc.rectTransform, 0.5f, 1f, new Vector2(0, -10), new Vector2(1000, 90), 0.5f, 1f);

            Action<RectTransform, string, string, string, bool> card = (rt, titleKey, subKey, productId, highlight) =>
            {
                // no card background since v1.1: just centred text (the lifetime offer gets a red title)
                var c = UIKit.Rect(rt, "Card");
                UIKit.Stretch(c);
                var t = UIKit.Txt(c.transform, "T", titleKey, 44, highlight ? UIKit.BloodBright : UIKit.Bone, TextAnchor.MiddleCenter, FontRole.UIBold);
                UIKit.Place(t.rectTransform, 0.5f, 1f, new Vector2(0, -50), new Vector2(440, 70));
                var s = UIKit.Txt(c.transform, "S", subKey, 26, UIKit.Dim);
                UIKit.Place(s.rectTransform, 0.5f, 1f, new Vector2(0, -125), new Vector2(440, 80));
                string price = IAPManager.I != null ? IAPManager.I.Price(productId) : "";
                var priceT = UIKit.Txt(c.transform, "P", price, 50, UIKit.BloodBright, TextAnchor.MiddleCenter, FontRole.UIBold, false);
                UIKit.Place(priceT.rectTransform, 0.5f, 0.5f, new Vector2(0, 0), new Vector2(440, 60));
                // v1.0.0 (App Store subscription rules): the period and renewal terms next to the price
                var period = UIKit.Txt(c.transform, "Period", productId == IAPManager.MonthlyId ? "iap_sub_terms" : "iap_once", 20, UIKit.Dim);
                period.resizeTextForBestFit = true; period.resizeTextMinSize = 14; period.resizeTextMaxSize = 20;
                period.verticalOverflow = VerticalWrapMode.Truncate;
                UIKit.Place(period.rectTransform, 0.5f, 0.5f, new Vector2(0, -50), new Vector2(460, 50));
                var b = UIKit.Btn(c.transform, "Buy", productId == IAPManager.MonthlyId ? "store_subscribe" : "store_buy",
                    () => { if (IAPManager.I != null) IAPManager.I.Buy(productId); }, new Vector2(380, 90), true, 36);
                UIKit.Place((RectTransform)b.transform, 0.5f, 0f, new Vector2(0, 14), new Vector2(380, 90), 0.5f, 0f);
                b.interactable = !SaveSystem.Data.AdsRemoved;
            };

            // body is 1100x700: cards between the description (top 100) and the status line (bottom 200);
            // the old 440-tall cards ran under the status text, which covered both Buy buttons
            var left = UIKit.Rect(body, "Monthly");
            UIKit.Place(left, 0.5f, 0.5f, new Vector2(-260, 50), new Vector2(480, 380));
            card(left, "store_monthly", "store_monthly_d", IAPManager.MonthlyId, false);
            var right = UIKit.Rect(body, "Lifetime");
            UIKit.Place(right, 0.5f, 0.5f, new Vector2(260, 50), new Vector2(480, 380));
            card(right, "store_lifetime", "store_lifetime_d", IAPManager.LifetimeId, true);

            var status = UIKit.Txt(body, "Status", SaveSystem.Data.AdsRemoved ? "store_owned" : "", 30, new Color(0.6f, 0.9f, 0.6f));
            UIKit.Place(status.rectTransform, 0.5f, 0f, new Vector2(0, 150), new Vector2(1000, 50), 0.5f, 0f);
            Action refresh = () => { if (status != null) status.GetComponent<LocText>().Set(SaveSystem.Data.AdsRemoved ? "store_owned" : ""); };
            IAPManager.OnEntitlementChanged += refresh;
            Action<string> msg = m => Toasts.Message(m);
            IAPManager.OnPurchaseMessage += msg;
            var cleanup = root.gameObject.AddComponent<OnDestroyHook>();
            cleanup.onDestroy = () => { IAPManager.OnEntitlementChanged -= refresh; IAPManager.OnPurchaseMessage -= msg; };

            var legal = UIKit.Txt(body, "Legal", "store_legal", 19, UIKit.Dim);
            UIKit.Place(legal.rectTransform, 0.5f, 0f, new Vector2(0, 70), new Vector2(1060, 80), 0.5f, 0f);
            var links = UIKit.Rect(body, "Links");
            UIKit.Place(links, 0.5f, 0f, new Vector2(0, 0), new Vector2(1060, 60), 0.5f, 0f);
            var restore = UIKit.AltBtn(links, "Restore", "store_restore", () => { if (IAPManager.I != null) IAPManager.I.Restore(); }, new Vector2(320, 56), 24);
            UIKit.Place((RectTransform)restore.transform, 0.5f, 0.5f, new Vector2(-340, 0), new Vector2(320, 56));
            var terms = UIKit.AltBtn(links, "Terms", "terms", () => Application.OpenURL(TermsUrl), new Vector2(320, 56), 24);
            UIKit.Place((RectTransform)terms.transform, 0.5f, 0.5f, Vector2.zero, new Vector2(320, 56));
            var priv = UIKit.AltBtn(links, "Privacy", "privacy", () => Application.OpenURL(StoreConfig.PrivacyUrlForCurrentLanguage), new Vector2(320, 56), 24);
            UIKit.Place((RectTransform)priv.transform, 0.5f, 0.5f, new Vector2(340, 0), new Vector2(320, 56));
            return root.gameObject;
        }

        // ================================================================== language picker (v1.10)
        /// <summary>Grid of the 14 languages, each in its own name (and a font that has its glyphs); the current one is
        /// underlined. Picking one switches the language and closes the grid.</summary>
        public static GameObject LanguagePicker(Transform parent, Action onClose)
        {
            var root = Frame(parent, "set_language", new Vector2(1400, 820), onClose, out var body);
            var grid = UIKit.Rect(body, "Grid");
            UIKit.Place(grid, 0.5f, 1f, new Vector2(0, -10), new Vector2(1260, 640), 0.5f, 1f);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(404, 104);
            gl.spacing = new Vector2(24, 20);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 3;
            gl.childAlignment = TextAnchor.UpperCenter;
            foreach (var lang in Loc.Languages)
            {
                string code = lang.code;
                bool on = code == Loc.Code;
                var b = UIKit.Btn(grid, "Lang_" + code, null, () =>
                {
                    if (code != Loc.Code) Loc.SetLanguage(code);
                    onClose?.Invoke();
                    UnityEngine.Object.Destroy(root.gameObject);
                }, new Vector2(404, 104), false);
                var bg = b.GetComponent<Image>();
                bg.color = new Color(1f, 1f, 1f, 0.035f);
                var t = UIKit.Txt(b.transform, "Name", lang.native, 36, on ? UIKit.Bone : UIKit.Dim, TextAnchor.MiddleCenter, FontRole.UIBold, false);
                t.font = Fonts.ForLanguageName(code);
                UIKit.Stretch(t.rectTransform, 8f);
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 20;
                t.resizeTextMaxSize = 36;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                b.targetGraphic = t;
                if (on)
                {
                    var line = UIKit.Img(b.transform, "On", null, UIKit.BloodBright);
                    line.raycastTarget = false;
                    line.rectTransform.anchorMin = Vector2.zero;
                    line.rectTransform.anchorMax = new Vector2(1f, 0f);
                    line.rectTransform.offsetMin = Vector2.zero;
                    line.rectTransform.offsetMax = new Vector2(0f, 4f);
                }
            }
            return root.gameObject;
        }

        // ================================================================== custom night (v1.9)
        static readonly string[] CnWakes = { "normal", "task1", "start" };
        static readonly int[] CnLives = { 1, 3, 5 };

        /// <summary>
        /// Custom Night: one card per robot (name, one-line description, big level number or OFF, slider 0-20),
        /// "Wake-up:" and "Chances:" cycle buttons, presets (Easy 5 / Normal 10 / Hard 15 + wake task1 /
        /// 20/20/20 + wake start) and "Start the night". Settings are saved when the panel closes or the night starts.
        /// </summary>
        public static GameObject CustomNight(Transform parent, Action onClose, Action onStart)
        {
            var c = SaveSystem.Data.custom;
            c.Sanitize();
            var root = Frame(parent, "custom_night", new Vector2(1560, 900), () => { SaveSystem.Save(); onClose?.Invoke(); }, out var body);
            var intro = UIKit.Txt(body, "Intro", "cn_intro", 26, UIKit.Dim, TextAnchor.MiddleCenter);
            UIKit.Place(intro.rectTransform, 0.5f, 1f, Vector2.zero, new Vector2(1400, 44), 0.5f, 1f);

            RobotKind[] kinds = { RobotKind.Grin, RobotKind.Rattle, RobotKind.Brutus };
            var levels = new Text[3];
            var sliders = new UISlider[3];
            Text wakeLabel = null, livesLabel = null;
            Func<RobotKind, int> get = k => c.Level(k);
            Action<RobotKind, int> set = (k, v) => { if (k == RobotKind.Grin) c.grin = v; else if (k == RobotKind.Rattle) c.rattle = v; else c.brutus = v; };
            Action refresh = null;
            refresh = () =>
            {
                for (int i = 0; i < 3; i++)
                {
                    int L = get(kinds[i]);
                    if (levels[i] != null)
                    {
                        levels[i].font = Fonts.Get(FontRole.Title);
                        levels[i].text = L > 0 ? L.ToString() : Loc.Upper(Loc.T("cn_off"));
                        levels[i].color = L > 0 ? UIKit.BloodBright : UIKit.Dim;
                    }
                    if (sliders[i] != null) sliders[i].SetValue(L / 20f, false);
                }
                if (wakeLabel != null) { wakeLabel.font = Fonts.Get(FontRole.UIBold); wakeLabel.text = Loc.Upper(Loc.T("cn_wake_" + c.wake)); }
                if (livesLabel != null) { livesLabel.font = Fonts.Get(FontRole.UIBold); livesLabel.text = c.lives.ToString(); }
            };

            // ---- three robot cards ----
            const float cardW = 460f, cardH = 380f, gap = 30f;
            for (int i = 0; i < 3; i++)
            {
                var k = kinds[i];
                int idx = i;
                var card = UIKit.Img(body, "Card_" + k, null, new Color(1f, 1f, 1f, 0.035f));
                UIKit.Place(card.rectTransform, 0.5f, 1f, new Vector2((i - 1) * (cardW + gap), -60f), new Vector2(cardW, cardH), 0.5f, 1f);
                string kk = k.ToString().ToLowerInvariant();
                var nm = UIKit.Txt(card.transform, "Name", "robot_" + kk, 34, UIKit.Bone, TextAnchor.MiddleCenter, FontRole.Title);
                LetterSpacing.Add(nm, 0.08f);
                UIKit.Place(nm.rectTransform, 0.5f, 1f, new Vector2(0, -14), new Vector2(cardW - 20, 50), 0.5f, 1f);
                var ds = UIKit.Txt(card.transform, "Desc", "cn_desc_" + kk, 22, UIKit.Dim, TextAnchor.UpperCenter);
                ds.resizeTextForBestFit = true; ds.resizeTextMinSize = 15; ds.resizeTextMaxSize = 22;
                ds.verticalOverflow = VerticalWrapMode.Truncate;
                UIKit.Place(ds.rectTransform, 0.5f, 1f, new Vector2(0, -68), new Vector2(cardW - 40, 70), 0.5f, 1f);
                var lv = UIKit.Txt(card.transform, "Level", "", 96, UIKit.BloodBright, TextAnchor.MiddleCenter, FontRole.Title, false);
                UIKit.Place(lv.rectTransform, 0.5f, 1f, new Vector2(0, -146), new Vector2(cardW - 20, 120), 0.5f, 1f);
                UIKit.SoftShadow(lv);
                levels[i] = lv;
                sliders[i] = LevelSlider(card.transform, cardW - 80f, get(k) / 20f, v =>
                {
                    int L = Mathf.Clamp(Mathf.RoundToInt(v * 20f), 0, 20);
                    if (L == get(kinds[idx])) { sliders[idx].SetValue(L / 20f, false); return; }
                    set(kinds[idx], L);
                    refresh();
                });
            }

            // ---- wake-up / chances ----
            var opt = UIKit.Rect(body, "Options");
            UIKit.Place(opt, 0.5f, 1f, new Vector2(0, -470f), new Vector2(1400, 70), 0.5f, 1f);
            OptLabel(opt, "cn_wake", new Vector2(-560, 0), 260f);
            var wb = UIKit.Btn(opt, "Wake", null, () => { int j = Array.IndexOf(CnWakes, c.wake); c.wake = CnWakes[(j + 1) % CnWakes.Length]; refresh(); }, new Vector2(520, 66), false);
            UIKit.Place((RectTransform)wb.transform, 0.5f, 0.5f, new Vector2(-150, 0), new Vector2(520, 66));
            wakeLabel = ValueLabel(wb);
            OptLabel(opt, "cn_lives", new Vector2(250, 0), 220f);
            var lb = UIKit.Btn(opt, "Lives", null, () => { int j = Array.IndexOf(CnLives, c.lives); c.lives = CnLives[(j + 1 + CnLives.Length) % CnLives.Length]; refresh(); }, new Vector2(160, 66), false);
            UIKit.Place((RectTransform)lb.transform, 0.5f, 0.5f, new Vector2(470, 0), new Vector2(160, 66));
            livesLabel = ValueLabel(lb);

            // ---- presets ----
            var pre = UIKit.Rect(body, "Presets");
            UIKit.Place(pre, 0.5f, 1f, new Vector2(0, -556f), new Vector2(1400, 70), 0.5f, 1f);
            OptLabel(pre, "cn_presets", new Vector2(-560, 0), 260f);
            string[] pk = { "cn_p_easy", "cn_p_normal", "cn_p_hard", "cn_p_night" };
            int[] pv = { 5, 10, 15, 20 };
            for (int i = 0; i < 4; i++)
            {
                int v = pv[i];
                var b = UIKit.AltBtn(pre, "P" + v, pk[i], () =>
                {
                    c.grin = c.rattle = c.brutus = v;
                    c.wake = v >= 20 ? "start" : (v >= 15 ? "task1" : "normal");
                    refresh();
                }, new Vector2(230, 64), 30);
                UIKit.Place((RectTransform)b.transform, 0.5f, 0.5f, new Vector2(-300 + i * 245, 0), new Vector2(230, 64));
            }

            // ---- start ----
            var go = UIKit.Btn(body, "Start", "cn_start", () =>
            {
                SaveSystem.Save();
                UnityEngine.Object.Destroy(root.gameObject);
                onStart?.Invoke();
            }, new Vector2(620, 110), true, 54);
            UIKit.Place((RectTransform)go.transform, 0.5f, 0f, new Vector2(0, 6), new Vector2(620, 110), 0.5f, 0f);

            refresh();
            Action onLang = () => refresh();
            Loc.OnChanged += onLang;
            root.gameObject.AddComponent<OnDestroyHook>().onDestroy = () => Loc.OnChanged -= onLang;
            return root.gameObject;
        }

        static void OptLabel(RectTransform row, string key, Vector2 pos, float w)
        {
            var t = UIKit.Txt(row, "L_" + key, key, 28, UIKit.Dim, TextAnchor.MiddleRight, FontRole.UI);
            UIKit.Place(t.rectTransform, 0.5f, 0.5f, pos, new Vector2(w, 66));
        }

        static Text ValueLabel(Button b)
        {
            var t = UIKit.Txt(b.transform, "V", "", 30, UIKit.BloodBright, TextAnchor.MiddleCenter, FontRole.UIBold, false);
            UIKit.Stretch(t.rectTransform, 4f);
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 16;
            t.resizeTextMaxSize = 30;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            b.targetGraphic = t;
            return t;
        }

        /// <summary>Label-less slider (track, fill, knob, tall hit area) at the bottom of a Custom Night card.</summary>
        static UISlider LevelSlider(Transform card, float width, float value, Action<float> onChange)
        {
            var track = UIKit.Img(card, "Track", null, new Color(1f, 1f, 1f, 0.16f));
            UIKit.Place(track.rectTransform, 0.5f, 0f, new Vector2(0, 46), new Vector2(width, 8), 0.5f, 0.5f);
            track.raycastTarget = true;
            var fill = UIKit.Img(track.transform, "Fill", null, UIKit.BloodBright);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(value, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var knob = UIKit.Img(track.transform, "Knob", "joystick_knob", UIKit.Bone);
            if (knob.sprite == null) knob.color = UIKit.Bone;
            knob.rectTransform.sizeDelta = new Vector2(40, 40);
            var s = track.gameObject.AddComponent<UISlider>();
            s.Init(fill.rectTransform, knob.rectTransform, value, onChange);
            var hit = UIKit.Img(track.transform, "Hit", null, new Color(0, 0, 0, 0.001f));
            hit.rectTransform.anchorMin = new Vector2(0f, -4f);
            hit.rectTransform.anchorMax = new Vector2(1f, 5f);
            hit.rectTransform.offsetMin = new Vector2(-20f, 0f);
            hit.rectTransform.offsetMax = new Vector2(20f, 0f);
            hit.raycastTarget = true;
            hit.gameObject.AddComponent<UIForwardDrag>().target = s;
            return s;
        }

        // ================================================================== achievements
        public static GameObject AchievementsPanel(Transform parent, Action onClose)
        {
            var root = Frame(parent, "achievements", new Vector2(1440, 880), onClose, out var body);
            var count = UIKit.Txt(body, "Count", Loc.F("ach_count", Achievements.UnlockedCount(), Achievements.All.Count), 30, UIKit.Dim, TextAnchor.MiddleCenter, FontRole.UI, false);
            UIKit.Place(count.rectTransform, 0.5f, 1f, new Vector2(0, 0), new Vector2(800, 44), 0.5f, 1f);
            var sr = UIKit.Scroll(body, "Scroll", Vector2.zero, out var content);
            var srt = (RectTransform)sr.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = new Vector2(0, -56);
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(320, 280);
            grid.spacing = new Vector2(16, 16);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperCenter;
            foreach (var def in Achievements.All)
            {
                var st = Achievements.State(def.id);
                bool hiddenLocked = def.hidden && !st.unlocked;
                var cell = UIKit.Img(content, def.id, null, new Color(0, 0, 0, 0f)); // no cell box since v1.1
                var badge = UIKit.Img(cell.transform, "Badge", st.unlocked ? Achievements.BadgeSprite(def.tier) : "badge_locked", st.unlocked ? Color.white : new Color(1, 1, 1, 0.6f));
                UIKit.Place(badge.rectTransform, 0.5f, 1f, new Vector2(0, -6), new Vector2(140, 140), 0.5f, 1f);
                var ic = UIKit.Img(badge.transform, "Icon", hiddenLocked ? "icon_lock" : def.icon, st.unlocked ? Achievements.TierColor(def.tier) : new Color(0.45f, 0.45f, 0.45f));
                UIKit.Stretch(ic.rectTransform, 39);
                ic.preserveAspect = true;
                var n = UIKit.Txt(cell.transform, "Name", hiddenLocked ? "ach_hidden" : def.NameKey, 26, st.unlocked ? UIKit.Bone : UIKit.Dim, TextAnchor.MiddleCenter, FontRole.UIBold);
                UIKit.Place(n.rectTransform, 0.5f, 1f, new Vector2(0, -152), new Vector2(310, 40), 0.5f, 1f);
                var dsc = UIKit.Txt(cell.transform, "Desc", hiddenLocked ? "ach_hidden_d" : def.DescKey, 19, UIKit.Dim, TextAnchor.UpperCenter);
                UIKit.Place(dsc.rectTransform, 0.5f, 1f, new Vector2(0, -194), new Vector2(300, 60), 0.5f, 1f);
                if (def.target > 1 && !st.unlocked)
                {
                    var prog = UIKit.Txt(cell.transform, "Prog", st.progress + " / " + def.target, 20, UIKit.Rust, TextAnchor.MiddleCenter, FontRole.UI, false);
                    UIKit.Place(prog.rectTransform, 0.5f, 0f, new Vector2(0, 8), new Vector2(300, 28), 0.5f, 0f);
                }
            }
            return root.gameObject;
        }
    }

    public class OnDestroyHook : MonoBehaviour
    {
        public Action onDestroy;
        void OnDestroy() => onDestroy?.Invoke();
    }
}
