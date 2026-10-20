using UnityEngine;

namespace RustedFair
{
    /// <summary>Hand-held flashlight with battery, lag-follow, and failure flicker when robots are near.</summary>
    public class FlashlightController : MonoBehaviour
    {
        public Light spot;
        public Transform follow;          // camera
        public float Charge { get; private set; } = 1f;
        public bool On { get; private set; }
        /// <summary>Web: SpotLight(0xfff0d1, 4.4, 30 m, 0.58 rad, penumbra 0.5, decay 1.3). v1.0.2: URP spot lights fall off as
        /// I/d² instead of the web's (1−d/30)^1.3, so the old 4.7 lit surfaces ~50x darker than the web at 10 m. Converted to the URP
        /// intensity with the same irradiance at 8.5 m (≈210): walls 10–20 m away read clearly, as in the web build.</summary>
        public static readonly float BaseIntensity = LightSystem.WebToURP(4.4f, Range, 1.3f, 8.5f);
        public const float Range = 30f;
        static readonly Vector3 HandOffset = new Vector3(0.18f, -0.18f, 0.1f);
        public float DrainPerSecond = 1f / 1200f; // v1.5: 20 minutes per full battery (was 9)
        public float interference;        // 0..1 set by GameController (robots nearby)
        public float dim = 1f;            // v1.0.3: < 1 during a jumpscare (the face is 0.3-0.6 m from the lens)
        float flickerT;
        Quaternion smoothRot;
        /// <summary>v1.0.3: eye adaptation. I/d² makes anything within ~3 m glare white (a robot in your face, a crate);
        /// the beam dims while it points at something close (one sphere cast per frame).</summary>
        float adapt = 1f;
        const float AdaptRef = 6f;

        float AdaptTarget()
        {
            if (Physics.SphereCast(transform.position, 0.12f, transform.forward, out var hit, AdaptRef, ~0, QueryTriggerInteraction.Ignore))
                return Mathf.Clamp(Mathf.Pow(Mathf.Max(0.3f, hit.distance) / AdaptRef, 1.5f), 0.22f, 1f);
            return 1f;
        }

        public static FlashlightController Create(Transform cam)
        {
            var go = new GameObject("Flashlight");
            go.transform.SetParent(cam.parent, false);
            go.transform.localPosition = cam.localPosition + HandOffset;
            var f = go.AddComponent<FlashlightController>();
            f.follow = cam;
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            // v1.5: stronger and a bit wider (web: 3.2 -> 4.4, 24 -> 30 m, half-angle 0.58 rad, penumbra 0.5)
            l.range = Range;
            l.spotAngle = 66.5f;
            l.innerSpotAngle = 33f;
            l.intensity = BaseIntensity;
            l.color = new Color(1f, 0.94f, 0.82f);
            l.shadows = GraphicsQuality.FlashlightShadows ? LightShadows.Soft : LightShadows.None;
            l.shadowBias = 0.04f;
            l.shadowNormalBias = 0.35f;
            l.shadowNearPlane = 0.2f;
            var cookie = RF.Tex("flashlight_cookie");
            if (cookie != null) l.cookie = cookie;
            l.enabled = false;
            f.spot = l;
            f.smoothRot = cam.rotation;
            GraphicsQuality.OnChanged += f.OnQuality;
            return f;
        }

        void OnDestroy() => GraphicsQuality.OnChanged -= OnQuality;

        /// <summary>Puts the light in the hand of a camera that was moved directly (editor screenshot tool), with no lag.</summary>
        public void SnapTo(Transform cam)
        {
            if (cam == null) return;
            transform.SetPositionAndRotation(cam.position + cam.rotation * HandOffset, cam.rotation);
            smoothRot = cam.rotation;
            adapt = AdaptTarget();
            if (spot != null && On) spot.intensity = BaseIntensity * adapt * dim;
        }

        void OnQuality(int level)
        {
            if (spot != null) spot.shadows = GraphicsQuality.FlashlightShadows ? LightShadows.Soft : LightShadows.None;
        }

        public void Toggle()
        {
            if (!On && Charge <= 0.001f)
            {
                if (AudioManager.I != null) AudioManager.I.Play2D("flashlight_off", 0.6f);
                return;
            }
            SetOn(!On);
        }

        public void SetOn(bool on)
        {
            On = on;
            spot.enabled = on;
            if (AudioManager.I != null) AudioManager.I.Play2D(on ? "flashlight_on" : "flashlight_off", 0.6f);
        }

        public void AddCharge(float amount)
        {
            Charge = Mathf.Clamp01(Charge + amount);
        }

        public void TickBattery(Inventory inv)
        {
            if (!On) return;
            Charge -= DrainPerSecond * Time.deltaTime;
            if (Charge <= 0f)
            {
                Charge = 0f;
                if (inv != null && inv.Remove(ItemId.Battery))
                {
                    Charge = 1f;
                    if (AudioManager.I != null) AudioManager.I.Play2D("item_insert", 0.6f);
                }
                else SetOn(false);
            }
        }

        void LateUpdate()
        {
            if (follow == null) return;
            // lag-follow for a natural hand-held feel
            smoothRot = Quaternion.Slerp(smoothRot, follow.rotation, 1f - Mathf.Exp(-14f * Time.deltaTime));
            transform.rotation = smoothRot;
            if (!On) return;
            float at = AdaptTarget();
            adapt = Mathf.Lerp(adapt, at, 1f - Mathf.Exp(-(at < adapt ? 10f : 4f) * Time.deltaTime));
            float k = adapt;
            float fail = Mathf.Max(interference, Charge < 0.15f ? (0.15f - Charge) * 5f : 0f);
            if (fail > 0.01f)
            {
                flickerT += Time.deltaTime * (8f + fail * 20f);
                float n = Mathf.PerlinNoise(flickerT, 7.7f);
                if (n < 0.25f + fail * 0.35f) k *= 0.05f;
                else k *= 0.7f + n * 0.3f;
            }
            spot.intensity = BaseIntensity * k * dim * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(Charge * 4f));
        }
    }
}
