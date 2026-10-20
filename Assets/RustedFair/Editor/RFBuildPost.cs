#if UNITY_IOS
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace RustedFair.EditorTools
{
    /// <summary>
    /// iOS (v1.0.0 store release): Info.plist for App Store review and AdMob, the ATT prompt text in 14 languages
    /// (InfoPlist.strings), ProMotion 120 Hz and the AppTrackingTransparency framework for Plugins/iOS/RFTracking.mm.
    /// The app-level privacy manifest is Assets/Plugins/iOS/PrivacyInfo.xcprivacy (Unity copies it automatically).
    /// </summary>
    public static class RFBuildPost
    {
        /// <summary>Codes of the 14 game languages as iOS localizations (zh = Simplified Chinese).</summary>
        static readonly string[] Lprojs = { "en", "tr", "de", "fr", "es", "it", "pt", "ru", "pl", "id", "vi", "ja", "ko", "zh-Hans" };

        /// <summary>NSUserTrackingUsageDescription per language (the system ATT dialog shows it).</summary>
        static readonly Dictionary<string, string> TrackingText = new Dictionary<string, string>
        {
            { "en", "Your data will be used to show you more relevant ads. The game works the same either way." },
            { "tr", "Verilerin sana daha alakalı reklamlar göstermek için kullanılır. Oyun her iki durumda da aynı şekilde çalışır." },
            { "de", "Deine Daten werden verwendet, um dir relevantere Werbung zu zeigen. Das Spiel funktioniert in beiden Fällen gleich." },
            { "fr", "Vos données seront utilisées pour vous montrer des publicités plus pertinentes. Le jeu fonctionne de la même façon dans les deux cas." },
            { "es", "Tus datos se usarán para mostrarte anuncios más relevantes. El juego funciona igual en ambos casos." },
            { "it", "I tuoi dati verranno usati per mostrarti annunci più pertinenti. Il gioco funziona allo stesso modo in entrambi i casi." },
            { "pt", "Seus dados serão usados para mostrar anúncios mais relevantes. O jogo funciona da mesma forma em ambos os casos." },
            { "ru", "Ваши данные будут использоваться, чтобы показывать более подходящую рекламу. Игра работает одинаково в любом случае." },
            { "pl", "Twoje dane będą używane do wyświetlania trafniejszych reklam. Gra działa tak samo w obu przypadkach." },
            { "id", "Datamu akan digunakan untuk menampilkan iklan yang lebih relevan. Game tetap berjalan sama dalam kedua pilihan." },
            { "vi", "Dữ liệu của bạn sẽ được dùng để hiển thị quảng cáo phù hợp hơn. Trò chơi vẫn hoạt động như nhau dù bạn chọn thế nào." },
            { "ja", "データは、より関連性の高い広告を表示するために使用されます。どちらを選んでもゲームの内容は変わりません。" },
            { "ko", "데이터는 더 관련성 높은 광고를 보여 주는 데 사용됩니다. 어느 쪽을 선택해도 게임은 똑같이 작동합니다." },
            { "zh-Hans", "你的数据将用于向你展示更相关的广告。无论如何选择，游戏的运行都完全相同。" },
        };

        /// <summary>
        /// SKAdNetwork IDs: Google's own (cstr6suwn9) plus Google's recommended third-party buyer list.
        /// Refresh from https://developers.google.com/admob/ios/3p-skadnetworks before each release.
        /// </summary>
        static readonly string[] SkAdNetworks =
        {
            "cstr6suwn9", "4fzdc2evr5", "2fnua5tdw4", "ydx93a7ass", "p78axxw29g", "v72qych5uu", "ludvb6z3bs", "cp8zw746q7",
            "3sh42y64q3", "c6k4g5qg8m", "s39g8k73mm", "3qy4746246", "hs6bdukanm", "mlmmfzh3r3", "v4nxqhlyqp", "wzmmz9fp6w",
            "su67r6k2v3", "yclnxrl5pm", "7ug5zh24hu", "gta9lk7p23", "vutu7akeur", "y5ghdn5j9k", "v9wttpbfk9", "n38lu8286q",
            "47vhws6wlr", "kbd757ywx3", "9t245vhmpl", "a2p9lx4jpn", "22mmun2rn5", "44jx6755aq", "k674qkevps", "4468km3ulz",
            "2u9pt9hc89", "8s468mfl3y", "ppxm28t8ap", "uw77j35x4d", "pwa73g5rt2", "578prtvx9j", "4dzt52r2t5", "tl55sbb4fm",
            "e5fvkxwrpn", "8c4e2ghe7u", "3rd42ekr43", "3qcr597p9d"
        };

        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            // ---------------- Info.plist ----------------
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;
            root.SetBoolean("CADisableMinimumFrameDurationOnPhone", true);   // ProMotion 120 Hz
            root.SetString("NSUserTrackingUsageDescription", TrackingText["en"]);
            root.SetString("GADApplicationIdentifier", RFStoreBuild.IosAppId(EditorUserBuildSettings.development));
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);         // HTTPS only -> exempt
            root.SetBoolean("UIRequiresFullScreen", true);
            root.SetBoolean("UIStatusBarHidden", true);
            root.SetBoolean("UIViewControllerBasedStatusBarAppearance", false);
            root.SetString("CFBundleDevelopmentRegion", "en");
            var locs = root.CreateArray("CFBundleLocalizations");
            foreach (var l in Lprojs) locs.AddString(l);
            var sk = root.CreateArray("SKAdNetworkItems");
            foreach (var id in SkAdNetworks) sk.AddDict().SetString("SKAdNetworkIdentifier", id + ".skadnetwork");
            plist.WriteToFile(plistPath);

            // ---------------- Xcode project ----------------
            string projPath = PBXProject.GetPBXProjectPath(path);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);
            string mainTarget = proj.GetUnityMainTargetGuid();
            string frameworkTarget = proj.GetUnityFrameworkTargetGuid();
            proj.AddFrameworkToProject(frameworkTarget, "AppTrackingTransparency.framework", true); // weak: RFTracking.mm

            // localized ATT text: <lang>.lproj/InfoPlist.strings as folder references in the app bundle
            foreach (var l in Lprojs)
            {
                string dir = Path.Combine(path, "RFLocalization", l + ".lproj");
                Directory.CreateDirectory(dir);
                string text = TrackingText.TryGetValue(l, out var t) ? t : TrackingText["en"];
                File.WriteAllText(Path.Combine(dir, "InfoPlist.strings"),
                    "\"NSUserTrackingUsageDescription\" = \"" + Escape(text) + "\";\n", new UTF8Encoding(false));
                string rel = "RFLocalization/" + l + ".lproj";
                string guid = proj.FindFileGuidByProjectPath(rel);
                if (guid == null) guid = proj.AddFolderReference(rel, rel, PBXSourceTree.Source);
                proj.AddFileToBuild(mainTarget, guid);
            }
            proj.WriteToFile(projPath);
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
