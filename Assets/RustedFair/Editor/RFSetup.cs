using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RustedFair.EditorTools
{
    /// <summary>
    /// One-click project setup: layers, material assets, the game scene, build settings, player settings
    /// (landscape, IDs, IL2CPP/ARM64, 120 Hz), URP tuning (Forward+, cookies, shadows) and app icon.
    /// Menu: Rusted Fair ▸ Setup Project
    /// </summary>
    public static class RFSetup
    {
        const string Root = "Assets/RustedFair";
        const string ScenePath = Root + "/Scenes/RustedFair.unity";
        const string MatDir = Root + "/Resources/RF/Materials";

        [MenuItem("Rusted Fair/Setup Project (run once)", priority = 0)]
        public static void SetupAll()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Rusted Fair", "Layers...", 0.05f);
                SetupLayers();
                EditorUtility.DisplayProgressBar("Rusted Fair", "Re-importing art...", 0.15f);
                AssetDatabase.ImportAsset(Root + "/Resources", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
                EditorUtility.DisplayProgressBar("Rusted Fair", "Materials...", 0.45f);
                BakeMaterials();
                EditorUtility.DisplayProgressBar("Rusted Fair", "Player settings...", 0.6f);
                SetupPlayerSettings();
                EditorUtility.DisplayProgressBar("Rusted Fair", "URP...", 0.7f);
                SetupURP();
                EditorUtility.DisplayProgressBar("Rusted Fair", "Scene...", 0.85f);
                CreateScene();
                RFDefines.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Rusted Fair",
                "Setup complete.\n\nThe scene 'RustedFair' is open — press Play to test.\n" +
                "Editor controls: WASD move, Shift sprint, C crouch, E interact (hold for hold actions), F flashlight, Q mask, G throw, Tab cycle item, J journal, Esc pause, Right-click toggles mouse look.\n\n" +
                "The safe code of each night is printed in the Console.", "OK");
        }

        [MenuItem("Rusted Fair/Rebuild Materials", priority = 20)]
        public static void BakeMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MatDir))
            {
                Directory.CreateDirectory(MatDir);
                AssetDatabase.Refresh(); // CreateAsset needs the folder to be known to the AssetDatabase
            }
            string slDir = MatDir + "/" + MatLib.SimpleLitDir.TrimEnd('/');
            string slnDir = MatDir + "/" + MatLib.SimpleLitNDir.TrimEnd('/');
            foreach (var dir in new[] { slDir, slnDir })
            {
                if (AssetDatabase.IsValidFolder(dir)) continue;
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }
            foreach (var spec in MatLib.AllSpecs)
            {
                var m = MatLib.Create(spec, LoadTex);
                var asset = SaveMaterial(m, MatDir + "/" + spec.name + ".mat");
                // v1.0.2: low/medium quality use URP Simple Lit copies of the Lit materials. Baked as assets under
                // Resources so the Simple Lit shader and every keyword variant they use ship in the Android build
                // (runtime copies made through Shader.Find would hit stripped variants).
                var sl = MatLib.ToSimpleLit(asset);
                if (sl != null && sl != asset) SaveMaterial(sl, slDir + "/" + spec.name + ".mat");
                // v1.0.3: medium quality keeps normal maps
                var sln = MatLib.ToSimpleLit(asset, true);
                if (sln != null && sln != asset) SaveMaterial(sln, slnDir + "/" + spec.name + ".mat");
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Creates the material asset, or copies <paramref name="m"/> into the existing one (keeps its GUID).</summary>
        static Material SaveMaterial(Material m, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(m, path);
                return m;
            }
            existing.shader = m.shader;
            existing.CopyPropertiesFromMaterial(m);
            existing.shaderKeywords = m.shaderKeywords;
            existing.renderQueue = m.renderQueue;
            existing.globalIlluminationFlags = m.globalIlluminationFlags;
            existing.enableInstancing = true;
            string renderType = m.GetTag("RenderType", false);
            if (!string.IsNullOrEmpty(renderType)) existing.SetOverrideTag("RenderType", renderType);
            foreach (var pass in new[] { "DepthOnly", "ShadowCaster" })
                existing.SetShaderPassEnabled(pass, m.GetShaderPassEnabled(pass));
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(m);
            return existing;
        }

        static Texture2D LoadTex(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Resources/RF/Textures/" + name + ".png");
            if (t == null) t = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Resources/RF/UI/" + name + ".png");
            return t;
        }

        static void SetupLayers()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var tm = new SerializedObject(assets[0]);
            var layers = tm.FindProperty("layers");
            if (layers == null) return;
            void Set(int i, string n)
            {
                var p = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(p.stringValue) || p.stringValue.StartsWith("RF_")) p.stringValue = n;
            }
            Set(RF.LayerPlayer, "RF_Player");
            Set(RF.LayerRobot, "RF_Robot");
            Set(RF.LayerInteract, "RF_Interact");
            Set(RF.LayerProps, "RF_Props");
            Set(RF.LayerTrigger, "RF_Trigger");
            tm.ApplyModifiedProperties();
        }

        /// <summary>
        /// v1.0.0 store release settings (identity from StoreConfig, Google Play: AAB / API 36 / ARM64 / IL2CPP,
        /// App Store: iOS 15+, universal, landscape, full screen), release quality (no development build, engine +
        /// managed stripping), dark "LYFE STUDIOS" splash and the app icons.
        /// </summary>
        [MenuItem("Rusted Fair/Apply Store Settings (identity, Android, iOS, icons)", priority = 1)]
        public static void ApplyStoreSettings()
        {
            SetupPlayerSettings();
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Rusted Fair", "Store settings applied:\n" + StoreConfig.CompanyName + " · " + StoreConfig.ProductName +
                "\n" + StoreConfig.BundleId + "  v" + StoreConfig.Version + " (Android " + StoreConfig.AndroidVersionCode + ", iOS " + StoreConfig.IosBuildNumber + ")" +
                "\nAndroid: AAB, IL2CPP ARM64, API 25-36 · iOS 15+, iPhone + iPad", "OK");
        }

        public static void SetupPlayerSettings()
        {
            // ---- identity ----
            PlayerSettings.companyName = StoreConfig.CompanyName;
            PlayerSettings.productName = StoreConfig.ProductName;
            PlayerSettings.bundleVersion = StoreConfig.Version;
            PlayerSettings.Android.bundleVersionCode = StoreConfig.AndroidVersionCode;
            PlayerSettings.iOS.buildNumber = StoreConfig.IosBuildNumber;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, StoreConfig.BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, StoreConfig.BundleId);

            // ---- landscape left / right only ----
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.useAnimatedAutorotation = true;
            PlayerSettings.statusBarHidden = true;

            // ---- Android / Google Play ----
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;      // 64-bit only (smaller download)
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;            // Android 16 (required since 31 Aug 2026)
            PlayerSettings.Android.forceInternetPermission = true;                     // AdMob / UMP / billing
            PlayerSettings.Android.optimizedFramePacing = true;
            EditorUserBuildSettings.buildAppBundle = true;                             // .aab for Google Play

            // ---- iOS / App Store ----
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.iOS.requiresFullScreen = true;

            // ---- release quality ----
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorUserBuildSettings.development = false;
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium); // link.xml keeps IAP / Ads
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Medium);
            PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            QualitySettings.vSyncCount = 0;

            SetupSplash();
            SetupIcons();
            RFStoreBuild.ApplyAdMobAppIds(EditorUserBuildSettings.development);
        }

        const string IconDir = Root + "/Resources/RF/UI/";

        /// <summary>Dark splash with the "LYFE STUDIOS" logo. Unity 6 lets every plan hide the Unity logo; if the
        /// license still requires it, Unity shows it automatically (light on dark).</summary>
        static void SetupSplash()
        {
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
            PlayerSettings.SplashScreen.backgroundColor = new Color(0.04f, 0.03f, 0.03f);
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
            const string logoPath = Root + "/Branding/lyfe_studios_splash.png";
            AssetDatabase.ImportAsset(logoPath, ImportAssetOptions.ForceUpdate);
            var logo = AssetDatabase.LoadAssetAtPath<Sprite>(logoPath);
            if (logo != null) PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2.2f, logo) };
            else Debug.LogWarning("[RF] Splash logo not found: " + logoPath);
        }

        /// <summary>
        /// Default icon for every platform = app_icon_1024.png (falls back to the generated app_icon.png); Android
        /// adaptive icon = app_icon_bg_1024.png (background) + app_icon_fg_1024.png (foreground) when both exist.
        /// </summary>
        static Texture2D IconTex(string file)
        {
            // Branding/ is checked first: icons there are not packed into the build like everything under Resources/
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Branding/" + file);
            return t != null ? t : AssetDatabase.LoadAssetAtPath<Texture2D>(IconDir + file);
        }

        static void SetupIcons()
        {
            var icon = IconTex("app_icon_1024.png");
            if (icon == null) icon = IconTex("app_icon.png");
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            var fg = IconTex("app_icon_fg_1024.png");
            var bg = IconTex("app_icon_bg_1024.png");
            if (fg == null || bg == null) return;
            // AndroidPlatformIconKind lives in the Android module: resolved by reflection so this compiles without it
            var kindType = System.Type.GetType("UnityEditor.Android.AndroidPlatformIconKind, UnityEditor.Android.Extensions");
            var prop = kindType != null ? kindType.GetProperty("Adaptive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) : null;
            var kind = prop != null ? prop.GetValue(null) as PlatformIconKind : null;
            if (kind == null) { Debug.LogWarning("[RF] Android Build Support is not installed - adaptive icon not set."); return; }
            var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (var pi in icons) pi.SetTextures(bg, fg); // adaptive layers: background first, then foreground
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
        }

        [MenuItem("Rusted Fair/Android Keystore Help", priority = 50)]
        public static void KeystoreHelp()
        {
            EditorUtility.DisplayDialog("Rusted Fair - Android upload keystore",
                "Google Play needs a signed .aab. The keystore is a secret: it is NOT in this project and must never be committed.\n\n" +
                "1. Edit ▸ Project Settings ▸ Player ▸ Android ▸ Publishing Settings ▸ Keystore Manager ▸ Create New ▸ Anywhere.\n" +
                "   Save it OUTSIDE the project folder (e.g. Documents/Keys/rustedfair-upload.keystore), use a strong password,\n" +
                "   alias 'upload', validity 50 years, organization LYFE STUDIOS.\n" +
                "2. Back up the keystore file and both passwords (password manager). Losing them blocks future updates\n" +
                "   until Google resets the upload key.\n" +
                "3. Select it in Publishing Settings (Custom Keystore) - Unity asks for the passwords again after every restart.\n" +
                "4. Play Console ▸ your app ▸ Test and release ▸ Setup ▸ App signing: keep 'Google-generated key' (Play App Signing).\n" +
                "   The first .aab you upload registers this keystore as your upload key.\n\n" +
                "See KURULUM.md section 18 for the full checklist.", "Open Player Settings");
            SettingsService.OpenProjectSettings("Project/Player");
        }

        static void SetupURP()
        {
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                Debug.LogWarning("[RF] No URP asset is active. Create the project from the 'Universal 3D' template.");
                return;
            }
            // apply to every quality level's pipeline asset too
            var targets = new System.Collections.Generic.List<UniversalRenderPipelineAsset> { urp };
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset;
                if (a != null && !targets.Contains(a)) targets.Add(a);
            }
            foreach (var a in targets)
            {
                var so = new SerializedObject(a);
                SetInt(so, "m_AdditionalLightsRenderingMode", 1);   // per pixel
                SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);
                SetBool(so, "m_AdditionalLightShadowsSupported", true);
                SetInt(so, "m_AdditionalLightsShadowmapResolution", 1024);
                SetBool(so, "m_SupportsHDR", true);
                SetBool(so, "m_SoftShadowsSupported", true);
                SetBool(so, "m_SupportsLightCookies", true);
                SetInt(so, "m_MainLightShadowmapResolution", 512);
                SetFloat(so, "m_ShadowDistance", 30f);
                SetInt(so, "m_ShadowCascadeCount", 1);
                SetBool(so, "m_SupportsTerrainHoles", false);
                so.ApplyModifiedProperties();

                // renderer: Forward+ so many small lights work on big merged meshes
                var listProp = so.FindProperty("m_RendererDataList");
                if (listProp != null)
                    for (int i = 0; i < listProp.arraySize; i++)
                    {
                        var rd = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (rd == null) continue;
                        var rso = new SerializedObject(rd);
                        SetInt(rso, "m_RenderingMode", 2); // Forward+
                        rso.ApplyModifiedProperties();
                    }
                EditorUtility.SetDirty(a);
            }
        }

        static void SetInt(SerializedObject so, string n, int v) { var p = so.FindProperty(n); if (p != null) p.intValue = v; }
        static void SetBool(SerializedObject so, string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; }
        static void SetFloat(SerializedObject so, string n, float v) { var p = so.FindProperty(n); if (p != null) p.floatValue = v; }

        [MenuItem("Rusted Fair/Create Game Scene", priority = 21)]
        public static void CreateScene()
        {
            // NewScene(Single) silently discards unsaved changes in the open scene(s)
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!AssetDatabase.IsValidFolder(Root + "/Scenes")) AssetDatabase.CreateFolder(Root, "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var app = new GameObject("[RF App]");
            app.AddComponent<RFApp>();
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.04f, 0.04f, 0.055f);
            RenderSettings.fog = true;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Rusted Fair/Open Game Scene", priority = 22)]
        public static void OpenScene()
        {
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else CreateScene();
        }

        /// <summary>
        /// v1.2 self-check: builds the procedural fair for several random seeds (in a temporary scene, edit mode) and
        /// verifies that every quest/item spot, anchor and room is still reachable by the robots' A* from the respawn
        /// point after the extra dressing. Development builds and Play mode log the same check on every start.
        /// </summary>
        [MenuItem("Rusted Fair/Check Fair Reachability (8 seeds)", priority = 30)]
        public static void CheckReachability()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.Log("[RF] In Play mode the reachability check runs automatically at start (see the Console).");
                return;
            }
            var active = SceneManager.GetActiveScene();
            var tmp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(tmp);
            int total = 0;
            var sb = new System.Text.StringBuilder();
            try
            {
                for (int seed = 1; seed <= 8; seed++)
                {
                    EditorUtility.DisplayProgressBar("Rusted Fair", "Building the fair, seed " + seed + "/8...", seed / 9f);
                    UnityEngine.Random.InitState(seed * 7919);
                    var host = new GameObject("RF_ReachCheck");
                    try
                    {
                        var ls = host.AddComponent<LightSystem>();
                        var fb = new FairBuilder(host.transform, ls);
                        fb.Build();
                        var log = new System.Text.StringBuilder();
                        int bad = fb.VerifyReachability(log);
                        total += bad;
                        sb.AppendLine("seed " + seed + ": " + (bad == 0 ? "OK" : bad + " problem(s)") + "   " + fb.DressSummary());
                        sb.Append(log);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(host); }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (active.IsValid()) SceneManager.SetActiveScene(active);
                EditorSceneManager.CloseScene(tmp, true);
                EditorUtility.UnloadUnusedAssetsImmediate();
            }
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/rf_reachability.txt"), sb.ToString()); } catch { }
            if (total == 0) Debug.Log("[RF] Reachability check passed for all 8 seeds.\n" + sb);
            else Debug.LogError("[RF] Reachability check found " + total + " problem(s).\n" + sb);
        }

        [MenuItem("Rusted Fair/Reset Save Data", priority = 40)]
        public static void ResetSave()
        {
            PlayerPrefs.DeleteKey("RF_SAVE_V1");
            PlayerPrefs.Save();
            Debug.Log("[RF] Save data cleared.");
        }
    }
}
