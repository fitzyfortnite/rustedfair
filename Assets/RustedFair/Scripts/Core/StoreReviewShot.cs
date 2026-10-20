#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

namespace RustedFair
{
    /// <summary>
    /// Editor-only: "Rusted Fair ▸ Capture Remove Ads Screen" opens the Remove Ads store panel on the main menu and renders
    /// it (UI included) at 2868x1320 and 2752x2064 into StoreAssets/Review/ - the in-app purchase review screenshot that
    /// App Store Connect and Google Play ask for.
    /// </summary>
    public class StoreReviewShot : MonoBehaviour
    {
        public static void Begin()
        {
            var go = new GameObject("[RF Review Shot]");
            DontDestroyOnLoad(go);
            go.AddComponent<StoreReviewShot>().StartCoroutine(go.GetComponent<StoreReviewShot>().Run());
        }

        IEnumerator Run()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "StoreAssets", "Review"));
            Directory.CreateDirectory(dir);
            if (RFApp.I != null && RFApp.I.State != RFApp.AppState.Menu) RFApp.I.GoToMenu();
            float t = 0f;
            MenuController menu = null;
            while ((menu = FindFirstObjectByType<MenuController>()) == null && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSeconds(2f);
            Canvas ui = null;
            if (menu != null) foreach (var c in menu.GetComponentsInChildren<Canvas>(true)) if (c.isRootCanvas) { ui = c; break; }
            var cam = Camera.main;
            if (ui == null || cam == null) { Debug.LogWarning("[RF] Review shot: menu not found"); Destroy(gameObject); yield break; }
            var panel = Panels.Store(ui.transform, null);
            yield return new WaitForSeconds(1.5f);
            // only the store panel: the menu behind it showed through the scrim in the camera-space render
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (Transform ch in ui.transform)
                if (panel != null && ch.gameObject != panel && !panel.transform.IsChildOf(ch) && ch.gameObject.activeSelf) { ch.gameObject.SetActive(false); hidden.Add(ch.gameObject); }
            // the editor's fake store reports $0.01: show the real store prices (monthly first, then lifetime)
            string[] real = { "$1.99", "$4.99" };
            int k = 0;
            if (panel != null)
                foreach (var tx in panel.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                    if (tx.text.Trim() == "$0.01" && k < real.Length) tx.text = real[k++];
            yield return null;
            foreach (var size in new[] { new Vector2Int(2868, 1320), new Vector2Int(2752, 2064) })
            {
                var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                var mode = ui.renderMode; var wc = ui.worldCamera; float pd = ui.planeDistance;
                cam.targetTexture = rt;
                ui.renderMode = RenderMode.ScreenSpaceCamera; ui.worldCamera = cam; ui.planeDistance = cam.nearClipPlane + 0.1f;
                for (int f = 0; f < 8; f++) yield return null; // font atlas rebuild at the new size (garbled glyphs otherwise)
                Canvas.ForceUpdateCanvases();
                yield return null; yield return new WaitForEndOfFrame();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); tex.Apply(false);
                RenderTexture.active = prev; cam.targetTexture = null;
                ui.renderMode = mode; ui.worldCamera = wc; ui.planeDistance = pd;
                File.WriteAllBytes(Path.Combine(dir, "remove_ads_" + size.x + "x" + size.y + ".png"), tex.EncodeToPNG());
                Destroy(tex); rt.Release(); Destroy(rt);
            }
            foreach (var g in hidden) if (g != null) g.SetActive(true);
            if (panel != null) Destroy(panel);
            Debug.Log("[RF] Review shot saved to " + dir);
            try { File.WriteAllText(Path.Combine(Application.dataPath, "..", "Logs", "rf_review_shot.txt"), "ok " + System.DateTime.Now.ToString("s")); } catch { }
            Destroy(gameObject);
        }
    }
}
#endif
