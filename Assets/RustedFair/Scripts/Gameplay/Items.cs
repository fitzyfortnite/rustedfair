using System;
using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    public enum ItemId
    {
        None, Fuse, LockerKey, Crank, Token, RideKey, Tape, KitchenKey, Hammer, Gear, Keycard,
        Mask, Battery, Oil, Water, Toy
    }

    public static class Items
    {
        public static string Icon(ItemId id)
        {
            switch (id)
            {
                case ItemId.Fuse: return "icon_fuse";
                case ItemId.LockerKey: return "icon_lockerkey";
                case ItemId.Crank: return "icon_crank";
                case ItemId.Token: return "icon_token";
                case ItemId.RideKey: return "icon_ridekey";
                case ItemId.Tape: return "icon_tape";
                case ItemId.KitchenKey: return "icon_kitchenkey";
                case ItemId.Hammer: return "icon_hammer";
                case ItemId.Gear: return "icon_gear";
                case ItemId.Keycard: return "icon_keycard";
                case ItemId.Mask: return "icon_mask";
                case ItemId.Battery: return "icon_battery";
                case ItemId.Oil: return "icon_oil";
                case ItemId.Water: return "icon_water";
                case ItemId.Toy: return "icon_toy";
                default: return "icon_hand";
            }
        }

        public static string NameKey(ItemId id) => "item_" + id.ToString().ToLowerInvariant();

        public static bool IsThrowable(ItemId id) => id == ItemId.Oil || id == ItemId.Water || id == ItemId.Toy;

        public static int Max(ItemId id)
        {
            switch (id)
            {
                case ItemId.Oil: case ItemId.Water: case ItemId.Toy: return 3;
                case ItemId.Battery: return 3;
                default: return 99;
            }
        }

        public static Color Tint(ItemId id)
        {
            switch (id)
            {
                case ItemId.Oil: return new Color(0.75f, 0.68f, 0.35f);
                case ItemId.Water: return new Color(0.45f, 0.75f, 1f);
                case ItemId.Toy: return new Color(1f, 0.6f, 0.75f);
                case ItemId.Keycard: return new Color(1f, 0.35f, 0.3f);
                case ItemId.Token: return new Color(1f, 0.82f, 0.35f);
                default: return UIKit.Bone;
            }
        }
    }

    public class Inventory
    {
        readonly Dictionary<ItemId, int> counts = new Dictionary<ItemId, int>();
        public event Action OnChanged;
        public event Action<ItemId> OnAdded;

        public int Count(ItemId id) => counts.TryGetValue(id, out var c) ? c : 0;
        public bool Has(ItemId id, int n = 1) => Count(id) >= n;

        public bool CanAdd(ItemId id) => Count(id) < Items.Max(id);

        public bool Add(ItemId id, int n = 1)
        {
            if (id == ItemId.None) return false;
            int c = Count(id);
            int max = Items.Max(id);
            if (c >= max) return false;
            counts[id] = Mathf.Min(max, c + n);
            OnAdded?.Invoke(id);
            OnChanged?.Invoke();
            return true;
        }

        public bool Remove(ItemId id, int n = 1)
        {
            int c = Count(id);
            if (c < n) return false;
            counts[id] = c - n;
            OnChanged?.Invoke();
            return true;
        }

        public IEnumerable<KeyValuePair<ItemId, int>> All => counts;

        public List<ItemId> KeyItems()
        {
            var list = new List<ItemId>();
            foreach (var kv in counts)
                if (kv.Value > 0 && !Items.IsThrowable(kv.Key) && kv.Key != ItemId.Battery && kv.Key != ItemId.Mask)
                    list.Add(kv.Key);
            list.Sort();
            return list;
        }
    }

    public enum NoiseKind { Footstep, Loud, Distraction, Alarm }

    /// <summary>Sound events that robots can hear.</summary>
    public static class Noise
    {
        public static event Action<Vector3, float, NoiseKind> OnNoise;

        public static void Emit(Vector3 pos, float radius, NoiseKind kind = NoiseKind.Loud)
        {
            OnNoise?.Invoke(pos, radius, kind);
        }

        public static void Clear() => OnNoise = null;
    }

    /// <summary>Fairground power (restored by task 1).</summary>
    public static class Power
    {
        public static bool On { get; private set; }
        public static event Action<bool> OnChanged;

        public static void Set(bool on)
        {
            if (On == on) return;
            On = on;
            OnChanged?.Invoke(on);
        }

        public static void Reset()
        {
            On = false;
            OnChanged = null;
        }
    }
}
