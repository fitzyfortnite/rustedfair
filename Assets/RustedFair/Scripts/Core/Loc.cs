using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RustedFair
{
    /// <summary>
    /// Lightweight localisation. String tables live in Resources/RF/Localization/{code}.txt
    /// as "key = value" lines (# comments, \n for newlines). English is the fallback.
    /// </summary>
    public static class Loc
    {
        public struct Lang
        {
            public string code, native;
            public Lang(string c, string n) { code = c; native = n; }
        }

        // v1.10: 14 languages, in the web build's order, each named in its own language
        public static readonly Lang[] Languages =
        {
            new Lang("en", "English"),
            new Lang("tr", "Türkçe"),
            new Lang("de", "Deutsch"),
            new Lang("fr", "Français"),
            new Lang("es", "Español"),
            new Lang("it", "Italiano"),
            new Lang("pt", "Português"),
            new Lang("ru", "Русский"),
            new Lang("pl", "Polski"),
            new Lang("id", "Bahasa Indonesia"),
            new Lang("vi", "Tiếng Việt"),
            new Lang("ja", "日本語"),
            new Lang("ko", "한국어"),
            new Lang("zh", "简体中文"),
        };

        static Dictionary<string, string> current = new Dictionary<string, string>();
        static Dictionary<string, string> fallback;
        public static string Code { get; private set; } = "en";
        public static event Action OnChanged;

        public static bool IsCJK => Code == "ja" || Code == "ko" || Code == "zh";

        public static void Init()
        {
            string code = SaveSystem.Data.language;
            if (string.IsNullOrEmpty(code)) code = FromSystem(Application.systemLanguage);
            SetLanguage(code, false);
        }

        static string FromSystem(SystemLanguage l)
        {
            switch (l)
            {
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.German: return "de";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Indonesian: return "id";
                case SystemLanguage.Vietnamese: return "vi";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return "zh";
                default: return "en";
            }
        }

        public static void SetLanguage(string code, bool save = true)
        {
            if (fallback == null) fallback = Parse("en");
            var table = Parse(code);
            if (table.Count == 0) { code = "en"; table = fallback; }
            current = table;
            Code = code;
            Fonts.Refresh();
            if (save)
            {
                SaveSystem.Data.language = code;
                SaveSystem.Save();
            }
            OnChanged?.Invoke();
        }

        public static int CurrentIndex()
        {
            for (int i = 0; i < Languages.Length; i++) if (Languages[i].code == Code) return i;
            return 0;
        }

        static Dictionary<string, string> Parse(string code)
        {
            var dict = new Dictionary<string, string>();
            var ta = Resources.Load<TextAsset>(RF.LocPath + code);
            if (ta == null) return dict;
            string[] lines = ta.text.Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
                dict[k] = StripUnsupported(v);
            }
            return dict;
        }

        // v1.0.0: symbols no bundled font has (they would render as empty boxes in the legacy Text). The shared
        // web tables keep them; Unity removes them when loading.
        static readonly string[] Unsupported = { "🔦", "↻", "🔈" };

        static string StripUnsupported(string v)
        {
            bool hit = false;
            foreach (var u in Unsupported) if (v.IndexOf(u, StringComparison.Ordinal) >= 0) { v = v.Replace(u, ""); hit = true; }
            if (!hit) return v;
            v = v.Replace("()", "").Replace("( )", "");
            while (v.Contains("  ")) v = v.Replace("  ", " ");
            return v.Replace(" .", ".").Replace(" ,", ",").Trim();
        }

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (current != null && current.TryGetValue(key, out var v)) return v;
            if (fallback != null && fallback.TryGetValue(key, out v)) return v;
            return key;
        }

        /// <summary>Uppercase for button labels; Turkish dotted/dotless i handled without relying on CultureInfo (IL2CPP-safe).</summary>
        public static string Upper(string s)
        {
            if (string.IsNullOrEmpty(s) || IsCJK) return s ?? "";
            if (Code == "tr") s = s.Replace('i', 'İ').Replace('ı', 'I');
            return s.ToUpperInvariant();
        }

        public static string F(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch (FormatException) { return T(key); }
        }
    }

    /// <summary>Attach to a UI Text to keep it translated and using the right font.</summary>
    [RequireComponent(typeof(Text))]
    public class LocText : MonoBehaviour
    {
        public string key;
        public object[] args;
        public FontRole role = FontRole.UI;
        public bool upper;
        Text text;

        public void Set(string newKey, params object[] newArgs)
        {
            key = newKey;
            args = newArgs;
            Refresh();
        }

        void OnEnable()
        {
            Loc.OnChanged += Refresh;
            Refresh();
        }

        void OnDisable() => Loc.OnChanged -= Refresh;

        public void Refresh()
        {
            if (text == null) text = GetComponent<Text>();
            if (text == null) return;
            text.font = Fonts.Get(role);
            if (string.IsNullOrEmpty(key)) return;
            string v = (args != null && args.Length > 0) ? Loc.F(key, args) : Loc.T(key);
            text.text = upper ? Loc.Upper(v) : v;
        }
    }
}
