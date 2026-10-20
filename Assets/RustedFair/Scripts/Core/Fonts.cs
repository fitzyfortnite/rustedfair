using UnityEngine;

namespace RustedFair
{
    /// <summary>UI = Oswald, UIBold = Oswald SemiBold, Title = Cinzel Bold, Heavy = Cinzel Black, Note = Special Elite.</summary>
    public enum FontRole { UI, UIBold, Title, Note, Heavy }

    /// <summary>
    /// Font selection per language. Latin uses the bundled Oswald / Special Elite / Cinzel fonts (v1.6: Cinzel replaced
    /// Creepster for titles, primary buttons and the logo). Cinzel has no Cyrillic, so Russian titles use Oswald SemiBold.
    /// v1.10: Polish and Indonesian are fully covered by all bundled fonts. Vietnamese (ơ ư ạ ả ấ ...) is only covered by
    /// Oswald (checked with fontTools: Cinzel lacks 95 and Special Elite 91 of the glyphs the vi table uses), so
    /// Vietnamese titles use Oswald SemiBold and notes Oswald Regular, like Russian.
    /// Japanese, Korean and Chinese fall back to the device's CJK system fonts.
    /// </summary>
    public static class Fonts
    {
        static Font oswald, oswaldBold, cinzelBold, cinzelBlack, typewriter, cjk;

        static Font Load(ref Font f, string name)
        {
            if (f == null) f = Resources.Load<Font>(RF.FontPath + name);
            if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return f;
        }

        static Font CJK()
        {
            if (cjk != null) return cjk;
            string[] names =
            {
                // iOS
                "PingFang SC", "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Apple SD Gothic Neo",
                // Android
                "Noto Sans CJK SC", "Noto Sans CJK JP", "Noto Sans CJK KR", "NotoSansCJK-Regular", "Droid Sans Fallback",
                // Desktop editors
                "Microsoft YaHei", "Yu Gothic", "Malgun Gothic", "Arial Unicode MS", "Arial"
            };
            try { cjk = Font.CreateDynamicFontFromOSFont(names, 32); } catch { cjk = null; }
            if (cjk == null) cjk = Load(ref oswald, "Oswald-Regular");
            return cjk;
        }

        public static void Refresh() { /* fonts are resolved lazily per call; kept for symmetry */ }

        public static Font Get(FontRole role)
        {
            bool cjkLang = Loc.IsCJK;
            bool cyr = Loc.Code == "ru" || Loc.Code == "vi"; // no Cinzel / Special Elite glyphs for these scripts
            switch (role)
            {
                case FontRole.Title:
                case FontRole.Heavy:
                    if (cjkLang) return CJK();
                    if (cyr) return Load(ref oswaldBold, "Oswald-SemiBold"); // Cinzel has no Cyrillic
                    return role == FontRole.Heavy ? Load(ref cinzelBlack, "Cinzel-Black") : Load(ref cinzelBold, "Cinzel-Bold");
                case FontRole.Note:
                    if (cjkLang) return CJK();
                    if (cyr) return Load(ref oswald, "Oswald-Regular");
                    return Load(ref typewriter, "SpecialElite-Regular");
                case FontRole.UIBold:
                    return cjkLang ? CJK() : Load(ref oswaldBold, "Oswald-SemiBold");
                default:
                    return cjkLang ? CJK() : Load(ref oswald, "Oswald-Regular");
            }
        }

        /// <summary>v1.10 language picker: each language's own name in a font that has its glyphs, whatever the current language.</summary>
        public static Font ForLanguageName(string code)
        {
            if (code == "ja" || code == "ko" || code == "zh") return CJK();
            return Load(ref oswaldBold, "Oswald-SemiBold"); // Latin incl. Polish / Vietnamese, and Cyrillic
        }

        /// <summary>The logo ("RUSTED FAIR", Latin in every language) is always Cinzel Black.</summary>
        public static Font Logo => Load(ref cinzelBlack, "Cinzel-Black");
    }
}
