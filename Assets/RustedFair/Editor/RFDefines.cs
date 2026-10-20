using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace RustedFair.EditorTools
{
    /// <summary>
    /// Adds RF_IAP when Unity In-App Purchasing is installed and RF_ADMOB when the Google Mobile Ads
    /// plugin is installed, so the project compiles with or without those SDKs.
    /// </summary>
    [InitializeOnLoad]
    public static class RFDefines
    {
        static RFDefines()
        {
            EditorApplication.delayCall += Refresh;
        }

        static bool TypeExists(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (asm.GetType(fullName, false) != null) return true; }
                catch { }
            }
            return false;
        }

        [MenuItem("Rusted Fair/Refresh SDK Defines", priority = 30)]
        public static void Refresh()
        {
            bool iap = TypeExists("UnityEngine.Purchasing.UnityPurchasing");
            bool admob = TypeExists("GoogleMobileAds.Api.MobileAds");
            foreach (var target in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.Standalone })
            {
                string current = PlayerSettings.GetScriptingDefineSymbols(target);
                var set = new List<string>(current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
                bool changed = false;
                changed |= Toggle(set, "RF_IAP", iap);
                changed |= Toggle(set, "RF_ADMOB", admob);
                if (changed) PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", set));
            }
        }

        static bool Toggle(List<string> set, string def, bool on)
        {
            bool has = set.Contains(def);
            if (on && !has) { set.Add(def); return true; }
            if (!on && has) { set.Remove(def); return true; }
            return false;
        }
    }
}
