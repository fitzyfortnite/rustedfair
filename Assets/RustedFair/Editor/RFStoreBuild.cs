using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RustedFair.EditorTools
{
    /// <summary>
    /// v1.0.0 store builds:
    ///  • before every build: copies the AdMob app IDs from <see cref="StoreConfig"/> into the Google Mobile Ads
    ///    plugin settings (the plugin writes them into the Android manifest meta-data
    ///    com.google.android.gms.ads.APPLICATION_ID and the iOS GADApplicationIdentifier) - Google's test app IDs
    ///    for development builds, the production IDs for release builds;
    ///  • release builds: warns about every StoreConfig placeholder (ads then stay off at runtime) and about a
    ///    development / unsigned Android configuration;
    ///  • Android: adds INTERNET + AD_ID to the launcher manifest and strips dangerous permissions the game never uses.
    /// </summary>
    public class RFStoreBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100; // before the Google Mobile Ads plugin's own build checks

        public void OnPreprocessBuild(BuildReport report)
        {
            bool dev = (report.summary.options & BuildOptions.Development) != 0;
            ApplyAdMobAppIds(dev);
            if (dev) return;
            var target = report.summary.platform;
            bool ios = target == BuildTarget.iOS;
            CheckPlaceholder(ios ? StoreConfig.AdMobAppIdIOS : StoreConfig.AdMobAppIdAndroid, "AdMob app ID");
            CheckPlaceholder(ios ? StoreConfig.InterstitialIdIOS : StoreConfig.InterstitialIdAndroid, "AdMob interstitial unit ID");
            CheckPlaceholder(StoreConfig.PrivacyUrl, "Privacy Policy URL");
            if (target == BuildTarget.Android)
            {
                if (!PlayerSettings.Android.useCustomKeystore)
                    Debug.LogWarning("[RF] Release .aab without a custom upload keystore - Google Play will reject it. Rusted Fair ▸ Android Keystore Help.");
                if (!EditorUserBuildSettings.buildAppBundle)
                    Debug.LogWarning("[RF] 'Build App Bundle (Google Play)' is off - Google Play needs an .aab.");
            }
        }

        static void CheckPlaceholder(string value, string what)
        {
            if (StoreConfig.IsPlaceholder(value))
                Debug.LogWarning("[RF] RELEASE BUILD: " + what + " in Scripts/Services/StoreConfig.cs is still a placeholder (" + value + "). " +
                                 "Ads stay disabled in this build; replace it before uploading to the store.");
        }

        /// <summary>
        /// Writes the AdMob app IDs into GoogleMobileAdsSettings (Assets/GoogleMobileAds/Resources). A release build
        /// whose production ID is still a placeholder gets Google's sample app ID so the SDK doesn't crash on launch;
        /// AdsManager never requests an ad in that case.
        /// </summary>
        [MenuItem("Rusted Fair/Apply AdMob App IDs", priority = 51)]
        public static void ApplyAdMobAppIdsMenu() => ApplyAdMobAppIds(EditorUserBuildSettings.development);

        public static void ApplyAdMobAppIds(bool development)
        {
            string android = development || StoreConfig.IsPlaceholder(StoreConfig.AdMobAppIdAndroid) ? StoreConfig.TestAppIdAndroid : StoreConfig.AdMobAppIdAndroid;
            string ios = development || StoreConfig.IsPlaceholder(StoreConfig.AdMobAppIdIOS) ? StoreConfig.TestAppIdIOS : StoreConfig.AdMobAppIdIOS;
            var guids = AssetDatabase.FindAssets("t:GoogleMobileAdsSettings");
            if (guids.Length == 0) return; // plugin not installed (or settings not created yet: Assets ▸ Google Mobile Ads ▸ Settings)
            foreach (var g in guids)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(g));
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                bool any = SetString(so, "adMobAndroidAppId", android) | SetString(so, "adMobIOSAppId", ios);
                if (!any) Debug.LogWarning("[RF] GoogleMobileAdsSettings has no adMobAndroidAppId / adMobIOSAppId field - enter the app IDs there by hand.");
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();
        }

        static bool SetString(SerializedObject so, string name, string v)
        {
            var p = so.FindProperty(name);
            if (p == null || p.propertyType != SerializedPropertyType.String) return false;
            p.stringValue = v;
            return true;
        }

        /// <summary>The iOS GADApplicationIdentifier for the current build (same rules as above).</summary>
        public static string IosAppId(bool development) =>
            development || StoreConfig.IsPlaceholder(StoreConfig.AdMobAppIdIOS) ? StoreConfig.TestAppIdIOS : StoreConfig.AdMobAppIdIOS;
    }

#if UNITY_ANDROID
    /// <summary>Android manifest: INTERNET + AD_ID (AdMob, Android 13+) and no dangerous permissions.</summary>
    public class RFAndroidManifest : UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string ToolsNs = "http://schemas.android.com/tools";

        static readonly string[] Add = { "android.permission.INTERNET", "com.google.android.gms.permission.AD_ID" };
        // the game needs none of these; a library that asks for one would trigger Play Console permission declarations
        static readonly string[] Remove =
        {
            "android.permission.READ_PHONE_STATE", "android.permission.ACCESS_FINE_LOCATION", "android.permission.ACCESS_COARSE_LOCATION",
            "android.permission.RECORD_AUDIO", "android.permission.CAMERA", "android.permission.READ_EXTERNAL_STORAGE",
            "android.permission.WRITE_EXTERNAL_STORAGE", "android.permission.READ_MEDIA_IMAGES", "android.permission.READ_MEDIA_VIDEO"
        };

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            // the launcher manifest has the highest merge priority, so tools:node="remove" wins over libraries
            string launcher = Path.Combine(unityLibraryPath, "..", "launcher", "src", "main", "AndroidManifest.xml");
            string path = File.Exists(launcher) ? launcher : Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(path)) { Debug.LogWarning("[RF] AndroidManifest.xml not found in the generated Gradle project."); return; }
            var doc = new XmlDocument();
            doc.Load(path);
            var root = doc.DocumentElement;
            if (root == null) return;
            if (!root.HasAttribute("xmlns:tools")) root.SetAttribute("xmlns:tools", ToolsNs);
            foreach (var p in Add) Permission(doc, root, p, false);
            foreach (var p in Remove) Permission(doc, root, p, true);
            doc.Save(path);
        }

        static void Permission(XmlDocument doc, XmlElement root, string name, bool remove)
        {
            var old = new System.Collections.Generic.List<XmlElement>();
            foreach (XmlNode n in root.ChildNodes)
                if (n is XmlElement e && e.Name == "uses-permission" && e.GetAttribute("name", AndroidNs) == name) old.Add(e);
            foreach (var e in old) root.RemoveChild(e);
            var el = doc.CreateElement("uses-permission");
            var a = doc.CreateAttribute("android", "name", AndroidNs);
            a.Value = name;
            el.Attributes.Append(a);
            if (remove)
            {
                var t = doc.CreateAttribute("tools", "node", ToolsNs);
                t.Value = "remove";
                el.Attributes.Append(t);
            }
            root.InsertBefore(el, root.FirstChild);
        }
    }
#endif
}
