using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    /// <summary>
    /// Central audio: pooled 3D one-shots, 2D one-shots, and cross-fading named layers
    /// (menu music, ambience, chase music, heartbeat...). Volumes follow the settings.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        class Layer
        {
            public AudioSource src;
            public float baseVol, target, current, fadeSpeed;
            public bool music;
            public bool stopWhenSilent;
        }

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        readonly List<float> poolVol = new List<float>();
        readonly List<AudioSource> pool2D = new List<AudioSource>();
        readonly List<float> pool2DVol = new List<float>();
        readonly Dictionary<string, Layer> layers = new Dictionary<string, Layer>();
        int poolIndex, pool2DIndex;
        AudioLowPassFilter listenerFilter;
        float muffleTarget = 22000f, muffleCurrent = 22000f;

        const int Pool3D = 28;
        const int Pool2DCount = 10;

        public static void Create()
        {
            if (I != null) return;
            var go = new GameObject("[RF Audio]");
            DontDestroyOnLoad(go);
            I = go.AddComponent<AudioManager>();
            I.Build();
        }

        void Build()
        {
            for (int i = 0; i < Pool3D; i++)
            {
                var s = new GameObject("sfx3d_" + i).AddComponent<AudioSource>();
                s.transform.SetParent(transform, false);
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.dopplerLevel = 0f;
                s.spread = 30f;
                pool.Add(s);
                poolVol.Add(1f);
            }
            for (int i = 0; i < Pool2DCount; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                pool2D.Add(s);
                pool2DVol.Add(1f);
            }
            SaveSystem.OnSettingsChanged += RefreshVolumes;
        }

        void OnDestroy()
        {
            SaveSystem.OnSettingsChanged -= RefreshVolumes;
        }

        float Sfx => SaveSystem.Data.sfxVolume;
        float Mus => SaveSystem.Data.musicVolume;

        public AudioClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (clips.TryGetValue(name, out var c)) return c;
            c = Resources.Load<AudioClip>(RF.AudioPath + name);
            if (c == null) Debug.LogWarning("[RF] Missing audio clip: " + name);
            clips[name] = c;
            return c;
        }

        /// <summary>Picks a random variant: name_1..name_n</summary>
        public static string Var(string baseName, int count) => baseName + "_" + Random.Range(1, count + 1);

        public AudioSource Play2D(string name, float vol = 1f, float pitch = 1f, bool ignorePause = false)
        {
            var clip = Clip(name);
            if (clip == null) return null;
            pool2DIndex = (pool2DIndex + 1) % pool2D.Count;
            var s = pool2D[pool2DIndex];
            s.Stop();
            s.clip = clip;
            s.loop = false;
            s.pitch = pitch;
            s.ignoreListenerPause = ignorePause;
            pool2DVol[pool2DIndex] = vol;
            s.volume = vol * Sfx;
            s.Play();
            return s;
        }

        public void UI(string name = "ui_click") => Play2D(name, 0.7f, 1f, true);

        public AudioSource PlayAt(string name, Vector3 pos, float vol = 1f, float minDist = 2f, float maxDist = 30f, float pitchVar = 0.06f, float pitch = 1f)
        {
            var clip = Clip(name);
            if (clip == null) return null;
            // prefer an idle source; otherwise steal the next in rotation
            AudioSource s = null;
            int idx = -1;
            for (int i = 0; i < pool.Count; i++)
            {
                int k = (poolIndex + i) % pool.Count;
                if (!pool[k].isPlaying) { s = pool[k]; idx = k; break; }
            }
            if (s == null) { idx = poolIndex; s = pool[idx]; }
            poolIndex = (idx + 1) % pool.Count;

            s.transform.position = pos;
            s.clip = clip;
            s.loop = false;
            s.minDistance = minDist;
            s.maxDistance = maxDist;
            s.pitch = pitch * (1f + Random.Range(-pitchVar, pitchVar));
            poolVol[idx] = vol;
            s.volume = vol * Sfx;
            s.Play();
            return s;
        }

        /// <summary>Creates a dedicated source on a transform (loops such as buzzing lights, wind-up toys).</summary>
        public static AudioSource Attach(GameObject host, string name, float vol, bool loop, float minDist = 1.5f, float maxDist = 18f, bool play = true)
        {
            var s = host.AddComponent<AudioSource>();
            s.clip = I != null ? I.Clip(name) : Resources.Load<AudioClip>(RF.AudioPath + name);
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = minDist;
            s.maxDistance = maxDist;
            s.dopplerLevel = 0f;
            s.volume = vol * SaveSystem.Data.sfxVolume;
            if (play && s.clip != null)
            {
                if (loop) s.time = Random.Range(0f, s.clip.length * 0.9f);
                s.Play();
            }
            return s;
        }

        // ---------- layers ----------
        public void SetLayer(string id, string clipName, float vol, float fadeSeconds = 1.5f, bool music = true, bool loop = true)
        {
            if (!layers.TryGetValue(id, out var L))
            {
                L = new Layer { src = gameObject.AddComponent<AudioSource>() };
                L.src.playOnAwake = false;
                L.src.spatialBlend = 0f;
                layers[id] = L;
            }
            var clip = Clip(clipName);
            if (clip == null) return;
            L.music = music;
            L.baseVol = vol;
            L.target = 1f;
            L.fadeSpeed = fadeSeconds <= 0.01f ? 1000f : 1f / fadeSeconds;
            L.stopWhenSilent = false;
            L.src.loop = loop;
            if (L.src.clip != clip || !L.src.isPlaying)
            {
                L.src.clip = clip;
                L.current = fadeSeconds <= 0.01f ? 1f : 0f;
                L.src.Play();
            }
            Apply(L);
        }

        public void FadeLayer(string id, float targetMul, float fadeSeconds)
        {
            if (!layers.TryGetValue(id, out var L)) return;
            L.target = targetMul;
            L.fadeSpeed = fadeSeconds <= 0.01f ? 1000f : 1f / fadeSeconds;
        }

        public void StopLayer(string id, float fadeSeconds = 1f)
        {
            if (!layers.TryGetValue(id, out var L)) return;
            L.target = 0f;
            L.stopWhenSilent = true;
            L.fadeSpeed = fadeSeconds <= 0.01f ? 1000f : 1f / fadeSeconds;
        }

        public bool LayerPlaying(string id) => layers.TryGetValue(id, out var L) && L.src.isPlaying && L.target > 0f;

        public void StopAllLayers(float fade = 1f)
        {
            foreach (var kv in layers) StopLayer(kv.Key, fade);
        }

        public void StopAll3D()
        {
            foreach (var s in pool) s.Stop();
        }

        void Apply(Layer L)
        {
            L.src.volume = L.baseVol * L.current * (L.music ? Mus : Sfx);
        }

        void RefreshVolumes()
        {
            foreach (var kv in layers) Apply(kv.Value);
            for (int i = 0; i < pool.Count; i++) if (pool[i].isPlaying) pool[i].volume = poolVol[i] * Sfx;
            for (int i = 0; i < pool2D.Count; i++) if (pool2D[i].isPlaying) pool2D[i].volume = pool2DVol[i] * Sfx;
        }

        // ---------- listener muffling (hiding, death, pause) ----------
        public void RegisterListener(AudioListener listener)
        {
            if (listener == null) { listenerFilter = null; return; }
            listenerFilter = listener.GetComponent<AudioLowPassFilter>();
            if (listenerFilter == null) listenerFilter = listener.gameObject.AddComponent<AudioLowPassFilter>();
            listenerFilter.cutoffFrequency = 22000f;
            muffleCurrent = muffleTarget = 22000f;
        }

        /// <summary>0 = clear, 1 = heavily muffled.</summary>
        public void Muffle(float amount)
        {
            muffleTarget = Mathf.Lerp(22000f, 700f, Mathf.Clamp01(amount));
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var kv in layers)
            {
                var L = kv.Value;
                if (Mathf.Abs(L.current - L.target) > 0.0001f)
                {
                    L.current = Mathf.MoveTowards(L.current, L.target, L.fadeSpeed * dt);
                    Apply(L);
                }
                if (L.stopWhenSilent && L.current <= 0.0001f && L.src.isPlaying) L.src.Stop();
            }
            if (listenerFilter != null)
            {
                muffleCurrent = Mathf.Lerp(muffleCurrent, muffleTarget, 1f - Mathf.Exp(-6f * dt));
                listenerFilter.cutoffFrequency = muffleCurrent;
                listenerFilter.enabled = muffleCurrent < 21000f;
            }
        }
    }
}
