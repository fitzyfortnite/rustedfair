using UnityEngine;

namespace RustedFair
{
    /// <summary>
    /// v1.0.0 store release: the ONE place for store identity, ad / purchase IDs and legal URLs.
    /// Replace every "REPLACE_WITH_..." value before uploading a release build (see KURULUM.md section 18).
    /// Development builds and the editor always use Google's official test ad IDs; release builds use the
    /// production IDs below and refuse to start ads while any of them is still a placeholder.
    /// The editor tools copy the AdMob app IDs into the Google Mobile Ads plugin settings, the Android manifest
    /// (through the plugin) and the iOS Info.plist (GADApplicationIdentifier) at build time.
    /// </summary>
    public static class StoreConfig
    {
        // ------------------------------------------------------------------ identity (RFSetup applies these)
        public const string CompanyName = "LYFE STUDIOS";
        public const string ProductName = "Rusted Fair";
        public const string BundleId = "com.lyfestudios.rustedfair";       // Android applicationId + iOS bundle identifier
        public const string Version = "1.0.0";                              // shown to players (versionName / CFBundleShortVersionString)
        public const int AndroidVersionCode = 3;                            // +1 for every Google Play upload
        public const string IosBuildNumber = "1";                           // +1 for every App Store Connect upload

        // ------------------------------------------------------------------ AdMob (production)
        // AdMob ▸ Apps ▸ (your app) ▸ App settings ▸ "App ID"  (format ca-app-pub-XXXXXXXXXXXXXXXX~YYYYYYYYYY)
        public const string AdMobAppIdAndroid = "ca-app-pub-1253448354278850~5893941534";
        public const string AdMobAppIdIOS = "ca-app-pub-1253448354278850~3299567782";
        // AdMob ▸ Apps ▸ (your app) ▸ Ad units ▸ Interstitial  (format ca-app-pub-XXXXXXXXXXXXXXXX/ZZZZZZZZZZ)
        public const string InterstitialIdAndroid = "ca-app-pub-1253448354278850/5706239273";
        public const string InterstitialIdIOS = "ca-app-pub-1253448354278850/8098714308";

        // ------------------------------------------------------------------ AdMob (Google's public test IDs)
        public const string TestAppIdAndroid = "ca-app-pub-3940256099942544~3347511713";
        public const string TestAppIdIOS = "ca-app-pub-3940256099942544~1458002511";
        public const string TestInterstitialAndroid = "ca-app-pub-3940256099942544/1033173712";
        public const string TestInterstitialIOS = "ca-app-pub-3940256099942544/4411468910";

        // ------------------------------------------------------------------ in-app purchases (create exactly these in both stores)
        public const string IapNoAdsMonthly = "com.lyfestudios.rustedfair.noads.monthly";   // auto-renewable subscription, 1 month
        public const string IapNoAdsLifetime = "com.lyfestudios.rustedfair.noads.lifetime"; // non-consumable

        // ------------------------------------------------------------------ legal
        public const string PrivacyUrl = "https://claude.ai/artifact/UD9mgE2TU7Y56yuRbjyMiq";                // PRIVACY_URL (14-language page)
        public const string TermsUrl = "https://www.apple.com/legal/internet-services/itunes/dev/stdeula/";  // TERMS_URL: Apple's standard EULA

        /// <summary>The privacy page opens in the game's language: PRIVACY_URL + "#" + language code (zh -> #zh).</summary>
        public static string PrivacyUrlForCurrentLanguage => PrivacyUrl + "#" + Loc.Code;

        public static bool IsPlaceholder(string id) => string.IsNullOrEmpty(id) || id.StartsWith("REPLACE") || id.EndsWith("/REPLACE");

        /// <summary>Editor and development builds use Google's test ads; only release builds use the production IDs.</summary>
        public static bool UseTestAds => Debug.isDebugBuild;

        public static string InterstitialUnitId
        {
            get
            {
#if UNITY_IOS
                return UseTestAds ? TestInterstitialIOS : InterstitialIdIOS;
#else
                return UseTestAds ? TestInterstitialAndroid : InterstitialIdAndroid;
#endif
            }
        }

        /// <summary>Release build with a placeholder ad unit: ads must stay off (never show test ads in production).</summary>
        public static bool AdsMisconfigured => !UseTestAds && IsPlaceholder(InterstitialUnitId);
    }
}
