using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RustedFair
{
    /// <summary>
    /// Application root. Lives in the single "RustedFair" scene and switches between the
    /// main menu and the game by building/destroying procedural worlds.
    /// </summary>
    public class RFApp : MonoBehaviour
    {
        public static RFApp I { get; private set; }

        public enum AppState { Boot, Menu, Game }
        public AppState State { get; private set; } = AppState.Boot;

        GameObject currentRoot;
        Image fader;
        Canvas faderCanvas;
        bool switching;

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);

            // v1.0.0: release builds keep only warnings and errors in the device log
            if (!Debug.isDebugBuild) Debug.unityLogger.filterLogType = LogType.Warning;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;

            SaveSystem.Load();
            SaveSystem.ApplyRuntimeSettings();
            Loc.Init();
            AudioManager.Create();
            AdsManager.Create();
            IAPManager.Create();
            Achievements.Init();
            EnsureEventSystem();
            BuildFader();
            Toasts.Create();
        }

        void Start()
        {
            StartCoroutine(Boot());
        }

        IEnumerator Boot()
        {
            fader.color = Color.black;
            yield return null;
            BuildMenu();
            yield return Fade(0f, 1.2f);
        }

        // ---------------- transitions ----------------
        public void GoToMenu()
        {
            if (switching) return;
            StartCoroutine(Switch(BuildMenu, 0.8f));
        }

        public void StartGame()
        {
            if (switching) return;
            StartCoroutine(Switch(BuildGame, 1.6f, keepBlack: true));
        }

        IEnumerator Switch(System.Action build, float fadeOut, bool keepBlack = false)
        {
            switching = true;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            yield return Fade(1f, fadeOut);
            if (AudioManager.I != null)
            {
                AudioManager.I.StopAllLayers(0.2f);
                AudioManager.I.StopAll3D();
            }
            if (currentRoot != null) Destroy(currentRoot);
            yield return null;
            yield return Resources.UnloadUnusedAssets();
            System.GC.Collect();
            build();
            yield return null;
            if (!keepBlack) yield return Fade(0f, 1f);
            switching = false;
        }

        void BuildMenu()
        {
            State = AppState.Menu;
            currentRoot = new GameObject("[Menu]");
            currentRoot.AddComponent<MenuController>();
        }

        void BuildGame()
        {
            State = AppState.Game;
            SaveSystem.Data.gamesStarted++;
            SaveSystem.Save();
            currentRoot = new GameObject("[Game]");
            currentRoot.AddComponent<GameController>();
        }

        // ---------------- fader ----------------
        void BuildFader()
        {
            var go = new GameObject("[RF Fader]");
            go.transform.SetParent(transform, false);
            faderCanvas = go.AddComponent<Canvas>();
            faderCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            faderCanvas.sortingOrder = 5000;
            go.AddComponent<GraphicRaycaster>(); // without a raycaster the fader's raycastTarget never blocks taps during transitions
            var img = new GameObject("Black").AddComponent<Image>();
            img.transform.SetParent(go.transform, false);
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            img.color = Color.black;
            img.raycastTarget = false;
            fader = img;
        }

        public float FaderAlpha => fader != null ? fader.color.a : 0f;

        public IEnumerator Fade(float target, float duration)
        {
            if (fader == null) yield break;
            fader.raycastTarget = target > 0.5f;
            float start = fader.color.a;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration));
                fader.color = new Color(0f, 0f, 0f, a);
                yield return null;
            }
            fader.color = new Color(0f, 0f, 0f, target);
            fader.raycastTarget = target > 0.5f;
        }

        public void SetFaderInstant(float a)
        {
            if (fader == null) return;
            fader.color = new Color(0, 0, 0, a);
            fader.raycastTarget = a > 0.5f;
        }

        // ---------------- input/event system ----------------
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("[RF EventSystem]");
            DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            // added at runtime the module has no actions asset (defaults are only assigned by Reset() in the inspector)
            var module = go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (module.actionsAsset == null) module.AssignDefaultActions();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && GameController.I != null) GameController.I.RequestPause();
        }
    }
}
