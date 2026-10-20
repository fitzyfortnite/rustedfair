using System;
using UnityEngine;
#if RF_IAP
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
#endif

namespace RustedFair
{
    /// <summary>
    /// "Remove Ads" store: a monthly auto-renewing subscription or a one-time lifetime unlock.
    /// Uses Unity IAP 4.12.x when installed (the RF_IAP define is added automatically).
    /// Create the products with exactly these IDs in App Store Connect and Google Play Console.
    /// </summary>
    public class IAPManager : MonoBehaviour
#if RF_IAP
        , IDetailedStoreListener
#endif
    {
        public static IAPManager I { get; private set; }

        // v1.0.0 store IDs (StoreConfig): monthly auto-renewable subscription + lifetime non-consumable
        public const string MonthlyId = StoreConfig.IapNoAdsMonthly;
        public const string LifetimeId = StoreConfig.IapNoAdsLifetime;

        public static event Action OnEntitlementChanged;
        public static event Action<string> OnPurchaseMessage;

        public bool Ready { get; private set; }

        public static void Create()
        {
            if (I != null) return;
            var go = new GameObject("[RF IAP]");
            DontDestroyOnLoad(go);
            I = go.AddComponent<IAPManager>();
        }

        static void Grant(bool lifetime, DateTime? expiryUtc)
        {
            var d = SaveSystem.Data;
            if (lifetime) d.removeAdsLifetime = true;
            else
            {
                d.removeAdsSubscription = true;
                d.subscriptionExpiryUtcTicks = expiryUtc.HasValue ? expiryUtc.Value.Ticks : 0;
            }
            SaveSystem.Save();
            if (AdsManager.I != null) AdsManager.I.DisableAds();
            Achievements.Report("supporter", 1);
            OnEntitlementChanged?.Invoke();
        }

        static void RevokeSubscription()
        {
            var d = SaveSystem.Data;
            if (!d.removeAdsSubscription) return;
            d.removeAdsSubscription = false;
            d.subscriptionExpiryUtcTicks = 0;
            SaveSystem.Save();
            OnEntitlementChanged?.Invoke();
        }

#if RF_IAP
        IStoreController controller;
        IExtensionProvider extensions;

        async void Start()
        {
            try { await Unity.Services.Core.UnityServices.InitializeAsync(); }
            catch (Exception e) { Debug.LogWarning("[RF] UGS init failed (IAP continues): " + e.Message); }

            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            builder.AddProduct(MonthlyId, ProductType.Subscription);
            builder.AddProduct(LifetimeId, ProductType.NonConsumable);
            UnityPurchasing.Initialize(this, builder);
        }

        public void OnInitialized(IStoreController c, IExtensionProvider e)
        {
            controller = c;
            extensions = e;
            Ready = true;
            RefreshEntitlements();
        }

        void RefreshEntitlements()
        {
            if (controller == null) return;
            var life = controller.products.WithID(LifetimeId);
            if (life != null && life.hasReceipt) Grant(true, null);

            var sub = controller.products.WithID(MonthlyId);
            if (sub == null) return;
            if (!sub.hasReceipt) { RevokeSubscription(); return; }
            try
            {
                var info = new SubscriptionManager(sub, null).getSubscriptionInfo();
                if (info.isSubscribed() == Result.True && info.isExpired() != Result.True)
                    Grant(false, info.getExpireDate());
                else
                    RevokeSubscription();
            }
            catch (Exception ex)
            {
                // receipt could not be parsed (e.g. fake store in editor) — trust hasReceipt
                RF.Log("[RF] Subscription info unavailable: " + ex.Message);
                Grant(false, null);
            }
        }

        public void OnInitializeFailed(InitializationFailureReason error) =>
            Debug.LogWarning("[RF] IAP init failed: " + error);

        public void OnInitializeFailed(InitializationFailureReason error, string message) =>
            Debug.LogWarning("[RF] IAP init failed: " + error + " " + message);

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            string id = args.purchasedProduct.definition.id;
            if (id == LifetimeId) Grant(true, null);
            else if (id == MonthlyId) { Grant(false, null); RefreshEntitlements(); }
            OnPurchaseMessage?.Invoke(Loc.T("store_thanks"));
            return PurchaseProcessingResult.Complete;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason reason)
        {
            if (reason != PurchaseFailureReason.UserCancelled) OnPurchaseMessage?.Invoke(Loc.T("store_failed"));
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription description)
        {
            if (description.reason != PurchaseFailureReason.UserCancelled) OnPurchaseMessage?.Invoke(Loc.T("store_failed"));
        }

        public void Buy(string productId)
        {
            if (controller == null) { OnPurchaseMessage?.Invoke(Loc.T("store_unavailable")); return; }
            var p = controller.products.WithID(productId);
            if (p == null || !p.availableToPurchase) { OnPurchaseMessage?.Invoke(Loc.T("store_unavailable")); return; }
            controller.InitiatePurchase(p);
        }

        public void Restore()
        {
            if (extensions == null) { OnPurchaseMessage?.Invoke(Loc.T("store_unavailable")); return; }
#if UNITY_IOS
            extensions.GetExtension<IAppleExtensions>().RestoreTransactions((ok, msg) =>
            {
                RefreshEntitlements();
                OnPurchaseMessage?.Invoke(RestoreMessage(ok));
            });
#elif UNITY_ANDROID
            extensions.GetExtension<IGooglePlayStoreExtensions>().RestoreTransactions((ok, msg) =>
            {
                RefreshEntitlements();
                OnPurchaseMessage?.Invoke(RestoreMessage(ok));
            });
#else
            RefreshEntitlements();
            OnPurchaseMessage?.Invoke(RestoreMessage(true));
#endif
        }

        /// <summary>v1.0.0: "Purchases restored." only when something was actually restored.</summary>
        static string RestoreMessage(bool ok) =>
            !ok ? Loc.T("store_failed") : Loc.T(SaveSystem.Data.AdsRemoved ? "store_restored" : "iap_nothing_to_restore");

        public string Price(string productId)
        {
            if (controller == null) return "";
            var p = controller.products.WithID(productId);
            return p != null && p.metadata != null ? p.metadata.localizedPriceString : "";
        }
#else
        void Start() { Ready = true; }

        public void Buy(string productId)
        {
#if UNITY_EDITOR
            // Editor simulation so the UI flow can be tested without the IAP package.
            Grant(productId == LifetimeId, productId == MonthlyId ? DateTime.UtcNow.AddDays(30) : (DateTime?)null);
            OnPurchaseMessage?.Invoke(Loc.T("store_thanks"));
#else
            OnPurchaseMessage?.Invoke(Loc.T("store_unavailable"));
#endif
        }

#if UNITY_EDITOR
        public void Restore() => OnPurchaseMessage?.Invoke(Loc.T(SaveSystem.Data.AdsRemoved ? "store_restored" : "iap_nothing_to_restore"));
#else
        public void Restore() => OnPurchaseMessage?.Invoke(Loc.T("store_unavailable"));
#endif

        // placeholder prices only in the editor; a build without the IAP package must not show fake prices
#if UNITY_EDITOR
        public string Price(string productId) => productId == MonthlyId ? "$2.99" : "$6.99";
#else
        public string Price(string productId) => "";
#endif
#endif
    }
}
