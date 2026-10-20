using System;
using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    [Serializable]
    public class AchievementState
    {
        public string id;
        public int progress;
        public bool unlocked;
        public long unlockedAt;
    }

    /// <summary>v1.9 Custom Night settings: level 0-20 per robot (10 = story Normal, 0 = never wakes), wake schedule, lives.</summary>
    [Serializable]
    public class CustomNightSettings
    {
        public int grin = 10, rattle = 10, brutus = 10;
        public string wake = "normal";   // normal (story, one by one) | task1 (all after the first task) | start (all from the start)
        public int lives = 3;            // 1 / 3 / 5

        public int Level(RobotKind k) => k == RobotKind.Grin ? grin : (k == RobotKind.Rattle ? rattle : brutus);
        public void Sanitize()
        {
            grin = Mathf.Clamp(grin, 0, 20); rattle = Mathf.Clamp(rattle, 0, 20); brutus = Mathf.Clamp(brutus, 0, 20);
            if (wake != "normal" && wake != "task1" && wake != "start") wake = "normal";
            if (lives != 1 && lives != 3 && lives != 5) lives = 3;
        }
    }

    /// <summary>v1.9: which mode the current / next night runs in. Menu "Play" = story; Custom Night panel = custom;
    /// "Play again" on the end screen keeps the mode.</summary>
    public static class GameMode
    {
        public static bool Custom;
        public static CustomNightSettings Settings => SaveSystem.Data.custom;
    }

    [Serializable]
    public class SaveData
    {
        // settings
        public string language = "";
        public float masterVolume = 1f;
        public float musicVolume = 0.8f;
        public float sfxVolume = 1f;
        public float sensitivity = 1f;
        public bool invertY = false;
        public int fpsTarget = 60;        // v1.0.0: 60 by default; 120 is offered only on high-refresh displays
        public int quality = -1;          // -1 = auto detect on first launch
        public float fov = 72f;
        public bool showTips = true;
        public bool haptics = true;
        public int difficulty = 1;        // 0 easy, 1 normal, 2 hard

        // monetisation
        public bool removeAdsLifetime = false;
        public bool removeAdsSubscription = false;
        public long subscriptionExpiryUtcTicks = 0;

        // progress / stats
        public int totalDeaths;
        public int escapes;
        public int escapesHard;
        public int bestTimeSeconds;       // 0 = none
        public int maskUses;
        public int hides;
        public int robotsSlipped;
        public int robotsStunned;
        public int robotsDistracted;
        public int notesFound;
        public int gamesStarted;
        public List<AchievementState> achievements = new List<AchievementState>();
        public CustomNightSettings custom = new CustomNightSettings();

        public bool AdsRemoved
        {
            get
            {
                if (removeAdsLifetime) return true;
                if (!removeAdsSubscription) return false;
                // grace: trust the cached expiry when offline; store re-validates on launch
                if (subscriptionExpiryUtcTicks <= 0) return true;
                return DateTime.UtcNow.Ticks < subscriptionExpiryUtcTicks + TimeSpan.FromDays(3).Ticks;
            }
        }
    }

    public static class SaveSystem
    {
        const string Key = "RF_SAVE_V1";
        static SaveData data;
        public static event Action OnSettingsChanged;

        public static SaveData Data
        {
            get
            {
                if (data == null) Load();
                return data;
            }
        }

        public static void Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                data = string.IsNullOrEmpty(json) ? new SaveData() : JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RF] Save corrupted, resetting: " + e.Message);
                data = new SaveData();
            }
            if (data == null) data = new SaveData();
            if (data.achievements == null) data.achievements = new List<AchievementState>();
            if (data.custom == null) data.custom = new CustomNightSettings();
            data.custom.Sanitize();
        }

        public static void Save()
        {
            if (data == null) return;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public static void SettingsChanged()
        {
            Save();
            ApplyRuntimeSettings();
            OnSettingsChanged?.Invoke();
        }

        /// <summary>Live preview while dragging a slider: notifies listeners without writing PlayerPrefs every frame.</summary>
        public static void PreviewSettings() => OnSettingsChanged?.Invoke();

        /// <summary>v1.0.0: 120 only when the display refreshes at 90 Hz or more (ProMotion / 120 Hz Android), else 60.</summary>
        public static int MaxFps
        {
            get
            {
                double hz = Screen.currentResolution.refreshRateRatio.value;
                return hz >= 89.5 ? 120 : 60;
            }
        }

        public static void ApplyRuntimeSettings()
        {
            var d = Data;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.Clamp(d.fpsTarget, 30, MaxFps);
            AudioListener.volume = Mathf.Clamp01(d.masterVolume);
            GraphicsQuality.Apply(d.quality);
        }
    }
}
