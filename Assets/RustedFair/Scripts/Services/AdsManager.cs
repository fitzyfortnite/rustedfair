using System;
using UnityEngine;
#if RF_ADMOB
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
#endif

namespace RustedFair
{
    /// <summary>
    /// Interstitial ads after death — never on the first death, at most once every
    /// <see cref="MinSecondsBetweenAds"/> seconds and only after <see cref="DeathsPerAd"/> deaths.
    /// Disabled completely when the player owns "Remove Ads" (monthly or lifetime).
    /// Real ads use Google AdMob when the plugin is installed (the RF_ADMOB define is added automatically).
    /// </summary>
    public class AdsManager : MonoBehaviour
    {
        public static AdsManager I { get; private set; }

        public const int DeathsPerAd = 2;
        public const float MinSecondsBetweenAds = 150f;

        /// <summary>Test IDs in the editor / development builds, the production IDs from <see cref="StoreConfig"/> in release builds.</summary>
        public static string InterstitialUnitId => StoreConfig.InterstitialUnitId;

        /// <summary>v1.0.0: the UMP "privacy options" entry point is required (e.g. EEA users) - Settings shows "Privacy choices".</summary>
        public static bool PrivacyOptionsRequired
        {
            get
            {
#if RF_ADMOB
                try { return ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required; }
                catch { return false; }
#else
                return false;
#endif
            }
        }

        int deathsSinceAd;
        float lastAdTime;
        bool showing;

        public static void Create()
        {
            if (I != null) return;
            var go = new GameObject("[RF Ads]");
            DontDestroyOnLoad(go);
            I = go.AddComponent<AdsManager>();
        }

        void Start()
        {
            lastAdTime = Time.realtimeSinceStartup; // no ad in the first minutes of a session
            if (StoreConfig.AdsMisconfigured)
            {
                // a release build must never fall back to test ads: stay ad-free and say why
                Debug.LogError("[RF] Ads disabled: StoreConfig still has placeholder AdMob IDs (REPLACE_WITH_...) in a release build.");
                return;
            }
            if (!SaveSystem.Data.AdsRemoved) InitProvider();
        }

        public void OnPlayerDied() => deathsSinceAd++;

        public bool AdDue =>
            !SaveSystem.Data.AdsRemoved &&
            deathsSinceAd >= DeathsPerAd &&
            Time.realtimeSinceStartup - lastAdTime >= MinSecondsBetweenAds;

        /// <summary>Call when the player leaves the death screen. onDone always runs exactly once.</summary>
        public void ShowInterstitialIfDue(Action onDone)
        {
            if (showing || !AdDue) { onDone?.Invoke(); return; }
            showing = true;
            bool finished = false;
            Action done = () =>
            {
                if (finished) return;
                finished = true;
                showing = false;
                deathsSinceAd = 0;
                lastAdTime = Time.realtimeSinceStartup;
                AudioListener.pause = false;
                onDone?.Invoke();
            };
            AudioListener.pause = true;
            ShowProvider(done);
        }

        // ------------------------------------------------------------------
#if RF_ADMOB
        InterstitialAd interstitial;
        bool initialized;
        bool nonPersonalized;

        /// <summary>Order (v1.0.0): UMP consent -> (iOS) App Tracking Transparency prompt -> MobileAds.Initialize -> first ad request.</summary>
        void InitProvider()
        {
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
            var request = new ConsentRequestParameters();
            ConsentInformation.Update(request, updateError =>
            {
                if (updateError != null) Debug.LogWarning("[RF] UMP update error: " + updateError.Message);
                ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
                {
                    if (formError != null) Debug.LogWarning("[RF] UMP form error: " + formError.Message);
                    if (!ConsentInformation.CanRequestAds()) return;
                    TrackingAuthorization.Request(authorized =>
                    {
                        nonPersonalized = !authorized; // ATT denied / restricted -> non-personalized ads only
                        StartAdMob();
                    });
                });
            });
        }

        /// <summary>Settings ▸ "Privacy choices": re-opens the UMP privacy options form.</summary>
        public void ShowPrivacyOptions()
        {
            ConsentForm.ShowPrivacyOptionsForm(err => { if (err != null) Debug.LogWarning("[RF] UMP privacy options: " + err.Message); });
        }

        AdRequest BuildRequest()
        {
            var r = new AdRequest();
            if (nonPersonalized) r.Extras.Add("npa", "1");
            return r;
        }

        void StartAdMob()
        {
            if (initialized) return;
            initialized = true;
            MobileAds.Initialize(status => LoadInterstitial());
        }

        void LoadInterstitial()
        {
            // never request ads before UMP consent + MobileAds.Initialize (ShowProvider used to call this unconditionally)
            if (!initialized || SaveSystem.Data.AdsRemoved) return;
            if (interstitial != null) { interstitial.Destroy(); interstitial = null; }
            InterstitialAd.Load(InterstitialUnitId, BuildRequest(), (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogWarning("[RF] Interstitial failed to load: " + error);
                    Invoke(nameof(LoadInterstitial), 30f);
                    return;
                }
                interstitial = ad;
            });
        }

        void ShowProvider(Action done)
        {
            if (interstitial == null || !interstitial.CanShowAd())
            {
                done();
                LoadInterstitial();
                return;
            }
            interstitial.OnAdFullScreenContentClosed += () => { done(); LoadInterstitial(); };
            interstitial.OnAdFullScreenContentFailed += err => { done(); LoadInterstitial(); };
            interstitial.Show();
        }

        public void DisableAds()
        {
            if (interstitial != null) { interstitial.Destroy(); interstitial = null; }
        }
#else
        void InitProvider() { }

        public void ShowPrivacyOptions() { }

        void ShowProvider(Action done)
        {
#if UNITY_EDITOR
            // Simulated ad in the editor so the flow can be tested without the SDK.
            Toasts.ShowFakeAd(done);
#else
            done();
#endif
        }

        public void DisableAds() { }
#endif
    }
}
