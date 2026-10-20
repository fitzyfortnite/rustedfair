using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    /// <summary>Global constants and small helpers shared by every system.</summary>
    public static class RF
    {
        public const string GameTitle = "RUSTED FAIR";

        /// <summary>v1.0.0: info logs exist only in the editor and development builds (the call and its string building
        /// are compiled out of release builds). Warnings / errors keep using Debug.LogWarning / LogError.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Log(string message) => Debug.Log(message);

        // Physics layers (named by the editor setup; numbers work even if unnamed)
        public const int LayerDefault = 0;
        public const int LayerPlayer = 8;
        public const int LayerRobot = 9;
        public const int LayerInteract = 10;
        public const int LayerProps = 11;
        public const int LayerTrigger = 12;

        public static int MaskWorld => (1 << LayerDefault) | (1 << LayerProps);
        public static int MaskSight => (1 << LayerDefault);              // walls/large blockers only
        public static int MaskInteract => (1 << LayerInteract) | (1 << LayerDefault) | (1 << LayerProps);

        // Resource roots
        public const string AudioPath = "RF/Audio/";
        public const string TexPath = "RF/Textures/";
        public const string UIPath = "RF/UI/";
        public const string FontPath = "RF/Fonts/";
        public const string LocPath = "RF/Localization/";
        public const string MatPath = "RF/Materials/";

        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        public static Texture2D Tex(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (texCache.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(TexPath + name);
            if (t == null) t = Resources.Load<Texture2D>(UIPath + name);
            texCache[name] = t;
            return t;
        }

        /// <summary>Loads a UI sprite. Works whether the PNG was imported as Sprite or as a plain Texture2D.</summary>
        public static Sprite Sprite(string name, Vector4 border = default)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = border == Vector4.zero ? name : name + border; // HUD calls this every frame: avoid a Vector4.ToString alloc
            if (spriteCache.TryGetValue(key, out var s) && s != null) return s;
            s = Resources.Load<Sprite>(UIPath + name);
            if (s == null || border != Vector4.zero)
            {
                var tex = s != null ? s.texture : Resources.Load<Texture2D>(UIPath + name);
                if (tex == null) tex = Resources.Load<Texture2D>(TexPath + name);
                if (tex != null)
                    s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            }
            spriteCache[key] = s;
            return s;
        }

        public static Vector3 Flat(this Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static float Damp(float current, float target, float lambda, float dt)
            => Mathf.Lerp(current, target, 1f - Mathf.Exp(-lambda * dt));

        public static Vector3 Damp(Vector3 current, Vector3 target, float lambda, float dt)
            => Vector3.Lerp(current, target, 1f - Mathf.Exp(-lambda * dt));

        public static T Pick<T>(IList<T> list) => list[Random.Range(0, list.Count)];

        public static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        public static GameObject Child(Transform parent, string name, Vector3 localPos = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }

        /// <summary>Creates a primitive without its collider (visual only).</summary>
        public static GameObject Prim(PrimitiveType type, Transform parent, string mat, Vector3 localPos, Vector3 localScale, Vector3 localEuler = default, bool castShadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                // disabled right away: Destroy() only happens at the end of the frame, and the build-time physics
                // checks (dressing, task-item visibility) must not see these visual-only primitives.
                // The editor reachability check builds the fair in edit mode, where Destroy is not allowed.
                col.enabled = false;
                if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = MatLib.Get(mat);
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        public static void Vibrate()
        {
#if UNITY_IOS || UNITY_ANDROID
            if (SaveSystem.Data.haptics) Handheld.Vibrate();
#endif
        }
    }
}
