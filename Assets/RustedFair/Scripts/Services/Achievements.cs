using System;
using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    public enum BadgeTier { Bronze, Silver, Gold }

    public class AchievementDef
    {
        public string id;
        public string icon;
        public BadgeTier tier;
        public int target;
        public bool hidden;

        public string NameKey => "ach_" + id;
        public string DescKey => "ach_" + id + "_d";

        public AchievementDef(string id, string icon, BadgeTier tier, int target = 1, bool hidden = false)
        {
            this.id = id; this.icon = icon; this.tier = tier; this.target = target; this.hidden = hidden;
        }
    }

    /// <summary>Local achievements + badges, saved with the player's progress.</summary>
    public static class Achievements
    {
        public static readonly List<AchievementDef> All = new List<AchievementDef>
        {
            new AchievementDef("wake_up",       "icon_eye",       BadgeTier.Bronze),
            new AchievementDef("lights_on",     "icon_fuse",      BadgeTier.Bronze),
            new AchievementDef("carousel",      "icon_crank",     BadgeTier.Bronze),
            new AchievementDef("fortune",       "icon_token",     BadgeTier.Bronze),
            new AchievementDef("ghost_train",   "icon_skull",     BadgeTier.Silver),
            new AchievementDef("on_air",        "icon_tape",      BadgeTier.Silver),
            new AchievementDef("ice_breaker",   "icon_hammer",    BadgeTier.Bronze),
            new AchievementDef("clockwork",     "icon_gear",      BadgeTier.Silver),
            new AchievementDef("all_notes",     "icon_notes",     BadgeTier.Silver),
            new AchievementDef("escape",        "icon_keycard",   BadgeTier.Gold),
            new AchievementDef("escape_hard",   "icon_skull",     BadgeTier.Gold),
            new AchievementDef("untouchable",   "icon_heart",     BadgeTier.Gold),
            new AchievementDef("speedrun",      "icon_sprint",    BadgeTier.Gold),
            new AchievementDef("custom",        "icon_trophy",    BadgeTier.Gold),   // v1.9: escape a Custom Night
            new AchievementDef("nightmare",     "icon_skull",     BadgeTier.Gold),   // v1.9: ... with all three robots at 20
            new AchievementDef("masked",        "icon_mask",      BadgeTier.Silver, 25),
            new AchievementDef("hide_seek",     "icon_hide",      BadgeTier.Bronze, 30),
            new AchievementDef("slippery",      "icon_oil",       BadgeTier.Silver, 10),
            new AchievementDef("short_circuit", "icon_water",     BadgeTier.Silver, 10),
            new AchievementDef("distraction",   "icon_toy",       BadgeTier.Bronze, 10),
            new AchievementDef("met_grin",      "icon_skull",     BadgeTier.Bronze, 1, true),
            new AchievementDef("met_rattle",    "icon_skull",     BadgeTier.Bronze, 1, true),
            new AchievementDef("met_brutus",    "icon_skull",     BadgeTier.Bronze, 1, true),
            new AchievementDef("persistent",    "icon_heart",     BadgeTier.Bronze, 25),
            new AchievementDef("supporter",     "icon_trophy",    BadgeTier.Bronze),
        };

        public static event Action<AchievementDef> OnUnlocked;

        public static void Init()
        {
            var list = SaveSystem.Data.achievements;
            foreach (var def in All)
                if (list.Find(a => a.id == def.id) == null)
                    list.Add(new AchievementState { id = def.id });
        }

        public static AchievementState State(string id)
        {
            var s = SaveSystem.Data.achievements.Find(a => a.id == id);
            if (s == null)
            {
                s = new AchievementState { id = id };
                SaveSystem.Data.achievements.Add(s);
            }
            return s;
        }

        public static AchievementDef Def(string id) => All.Find(d => d.id == id);

        public static bool Has(string id) { var s = SaveSystem.Data.achievements.Find(a => a.id == id); return s != null && s.unlocked; }

        /// <summary>v1.9: Custom Night is unlocked by the first escape.</summary>
        public static bool CustomNightUnlocked => Has("escape");

        public static int UnlockedCount()
        {
            int n = 0;
            foreach (var s in SaveSystem.Data.achievements) if (s.unlocked) n++;
            return n;
        }

        /// <summary>Adds progress (cumulative).</summary>
        public static void Report(string id, int amount = 1)
        {
            var def = Def(id);
            if (def == null) return;
            var s = State(id);
            if (s.unlocked) return;
            s.progress = Mathf.Min(def.target, s.progress + amount);
            if (s.progress >= def.target) Unlock(def, s);
            else SaveSystem.Save();
        }

        /// <summary>Sets progress to an absolute value (e.g. a lifetime stat).</summary>
        public static void SetProgress(string id, int value)
        {
            var def = Def(id);
            if (def == null) return;
            var s = State(id);
            if (s.unlocked) return;
            s.progress = Mathf.Clamp(value, 0, def.target);
            if (s.progress >= def.target) Unlock(def, s);
            else SaveSystem.Save();
        }

        static void Unlock(AchievementDef def, AchievementState s)
        {
            s.unlocked = true;
            s.progress = def.target;
            s.unlockedAt = DateTime.UtcNow.Ticks;
            SaveSystem.Save();
            OnUnlocked?.Invoke(def);
            Toasts.Achievement(def);
        }

        public static Color TierColor(BadgeTier t)
        {
            switch (t)
            {
                case BadgeTier.Gold: return new Color(1f, 0.82f, 0.35f);
                case BadgeTier.Silver: return new Color(0.82f, 0.86f, 0.9f);
                default: return new Color(0.85f, 0.55f, 0.32f);
            }
        }

        public static string BadgeSprite(BadgeTier t)
        {
            switch (t)
            {
                case BadgeTier.Gold: return "badge_gold";
                case BadgeTier.Silver: return "badge_silver";
                default: return "badge_bronze";
            }
        }
    }
}
