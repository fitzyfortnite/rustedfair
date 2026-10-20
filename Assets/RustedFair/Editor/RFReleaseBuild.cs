using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RustedFair.EditorTools
{
    /// <summary>
    /// Rusted Fair ▸ Build Android AAB (Google Play): applies the store settings and builds the signed release .aab to
    /// Builds/Android/RustedFair-&lt;version&gt;-&lt;code&gt;.aab. The upload keystore passwords must be entered in
    /// Player Settings ▸ Publishing Settings first (Unity keeps them only for the editor session).
    /// Rusted Fair ▸ Export iOS Xcode Project: same for iOS (Builds/iOS/), to archive and upload on a Mac.
    /// </summary>
    public static class RFReleaseBuild
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static string[] Scenes() => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        [MenuItem("Rusted Fair/Build Android AAB (Google Play)", priority = 2)]
        public static void BuildAndroid()
        {
            RFSetup.SetupPlayerSettings();
            AssetDatabase.SaveAssets();
            if (!PlayerSettings.Android.useCustomKeystore || string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) || string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
            {
                EditorUtility.DisplayDialog("Rusted Fair", "Enter the upload keystore and key passwords in Project Settings ▸ Player ▸ Android ▸ Publishing Settings, then run this again.", "OK");
                return;
            }
            string dir = Path.Combine(Root, "Builds", "Android");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "RustedFair-" + StoreConfig.Version + "-" + StoreConfig.AndroidVersionCode + ".aab");
            Run(new BuildPlayerOptions { scenes = Scenes(), locationPathName = path, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None }, path);
        }

        [MenuItem("Rusted Fair/Export iOS Xcode Project (App Store)", priority = 3)]
        public static void BuildIOS()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
            {
                EditorUtility.DisplayDialog("Rusted Fair", "iOS Build Support is not installed. Unity Hub ▸ Installs ▸ 6000.3 ▸ Add modules ▸ iOS Build Support.", "OK");
                return;
            }
            RFSetup.SetupPlayerSettings();
            AssetDatabase.SaveAssets();
            string path = Path.Combine(Root, "Builds", "iOS");
            Directory.CreateDirectory(path);
            Run(new BuildPlayerOptions { scenes = Scenes(), locationPathName = path, target = BuildTarget.iOS, targetGroup = BuildTargetGroup.iOS, options = BuildOptions.None }, path);
        }

        /// <summary>Command-line iOS export for the GitHub Actions workflow (.github/workflows/ios-testflight.yml): build
        /// number from RF_BUILD_NUMBER, exits non-zero when the build fails so the workflow stops.</summary>
        public static void BuildIOSCI()
        {
            RFSetup.SetupPlayerSettings();
            string n = System.Environment.GetEnvironmentVariable("RF_BUILD_NUMBER");
            if (!string.IsNullOrEmpty(n)) PlayerSettings.iOS.buildNumber = n;
            string team = System.Environment.GetEnvironmentVariable("RF_TEAM_ID");
            if (!string.IsNullOrEmpty(team)) PlayerSettings.iOS.appleDeveloperTeamID = team;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            AssetDatabase.SaveAssets();
            string path = Path.Combine(Root, "Builds", "iOS");
            Directory.CreateDirectory(path);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = Scenes(), locationPathName = path, target = BuildTarget.iOS, targetGroup = BuildTargetGroup.iOS, options = BuildOptions.None });
            Debug.Log("[RF] iOS export: " + report.summary.result + ", " + report.summary.totalErrors + " error(s), build " + PlayerSettings.iOS.buildNumber);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>Rusted Fair ▸ Export Project Zip (for CI): Assets, Packages, ProjectSettings and Tools in one zip
        /// (Builds/RustedFair-project.zip) - what the cloud iOS build needs, without Library/Temp/Builds/Logs.</summary>
        [MenuItem("Rusted Fair/Export Project Zip (for CI)", priority = 4)]
        public static void ExportProjectZip()
        {
            AssetDatabase.SaveAssets();
            string zip = Path.Combine(Root, "Builds", "RustedFair-project.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zip));
            if (File.Exists(zip)) File.Delete(zip);
            int n = 0;
            using (var za = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (var top in new[] { "Assets", "Packages", "ProjectSettings", "Tools" })
                {
                    string dir = Path.Combine(Root, top);
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(Root.Length + 1).Replace('\\', '/');
                        if (rel.Contains("/__pycache__/")) continue;
                        System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(za, f, rel, System.IO.Compression.CompressionLevel.Optimal);
                        n++;
                    }
                }
                foreach (var f in new[] { ".gitignore", "KURULUM.md" })
                    if (File.Exists(Path.Combine(Root, f))) { System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(za, Path.Combine(Root, f), f); n++; }
            }
            string msg = "[RF] Project zip: " + n + " files, " + (new FileInfo(zip).Length / (1024f * 1024f)).ToString("0.0") + " MB\n" + zip;
            try { File.WriteAllText(Path.Combine(Root, "Logs", "rf_project_zip.txt"), msg + "\n"); } catch { }
            Debug.Log(msg);
        }

        static void Run(BuildPlayerOptions o, string path)
        {
            var report = BuildPipeline.BuildPlayer(o);
            var s = report.summary;
            string msg = "[RF] " + o.target + " release build: " + s.result + ", " + (s.totalSize / (1024f * 1024f)).ToString("0.0") + " MB, " +
                         s.totalErrors + " error(s), " + s.totalWarnings + " warning(s), " + s.totalTime.TotalMinutes.ToString("0.0") + " min\n" + path;
            try { File.WriteAllText(Path.Combine(Root, "Logs", "rf_release_build.txt"), msg + "\n"); } catch { }
            if (s.result == BuildResult.Succeeded) { Debug.Log(msg); EditorUtility.RevealInFinder(path); }
            else Debug.LogError(msg);
        }
    }
}
