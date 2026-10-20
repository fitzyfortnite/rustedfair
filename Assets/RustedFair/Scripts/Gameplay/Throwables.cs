using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    /// <summary>
    /// Tools that slow or distract the robots:
    ///  • Oil can — poured on the floor; robots that step in it slip and crash (Brutus only slows down).
    ///  • Water balloon — thrown; short-circuits a robot it hits/splashes (stun).
    ///  • Wind-up toy — thrown; plays a loud tune that lures robots for a while.
    /// </summary>
    public static class Throwables
    {
        public static void Use(ItemId item, PlayerController p)
        {
            var cam = p.Cam.transform;
            switch (item)
            {
                case ItemId.Oil:
                {
                    Vector3 from = p.transform.position + p.transform.forward * 1.4f + Vector3.up * 1.2f;
                    Vector3 pos = p.transform.position + p.transform.forward * 1.4f;
                    if (Physics.Raycast(from, Vector3.down, out var hit, 3f, RF.MaskWorld, QueryTriggerInteraction.Ignore)) pos = hit.point;
                    OilSlick.Create(pos);
                    if (AudioManager.I != null) AudioManager.I.PlayAt("oil_pour", pos, 0.9f, 1.5f, 10f);
                    if (GameController.I != null) GameController.I.Tips.Show("tip_oil_placed");
                    break;
                }
                case ItemId.Water:
                case ItemId.Toy:
                {
                    var pr = Projectile.Create(item, cam.position + cam.forward * 0.4f - cam.up * 0.1f, cam.forward * (item == ItemId.Water ? 13f : 10f) + Vector3.up * 2.5f);
                    if (p.cc != null) foreach (var c in pr.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, p.cc);
                    if (AudioManager.I != null) AudioManager.I.Play2D("throw_whoosh", 0.7f);
                    break;
                }
            }
        }
    }

    public class OilSlick : MonoBehaviour
    {
        public static readonly List<OilSlick> All = new List<OilSlick>();
        public float radius = 1.35f;
        float life = 75f;

        public static OilSlick Create(Vector3 pos)
        {
            var go = new GameObject("OilSlick");
            if (GameController.I != null) go.transform.SetParent(GameController.I.transform, true);
            go.transform.position = pos;
            var q = RF.Prim(PrimitiveType.Quad, go.transform, "decal_oil", new Vector3(0, 0.02f, 0), new Vector3(2.8f, 2.8f, 1f), new Vector3(90, Random.Range(0, 360f), 0), false);
            q.name = "Decal";
            return go.AddComponent<OilSlick>();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f) Destroy(gameObject);
        }

        public static OilSlick At(Vector3 p)
        {
            foreach (var s in All)
            {
                var d = s.transform.position - p;
                d.y = 0;
                if (d.sqrMagnitude < s.radius * s.radius) return s;
            }
            return null;
        }

        /// <summary>Each slick trips one robot (Brutus wipes it away when he slows).</summary>
        public void Consume()
        {
            enabled = false;            // OnDisable removes it from All right away (no double trips)
            Destroy(gameObject, 0.1f);
        }
    }

    public class Projectile : MonoBehaviour
    {
        ItemId kind;
        bool done;
        Rigidbody rb;
        float life = 8f;

        public static Projectile Create(ItemId kind, Vector3 pos, Vector3 vel)
        {
            var go = new GameObject("Thrown_" + kind);
            if (GameController.I != null) go.transform.SetParent(GameController.I.transform, true);
            go.transform.position = pos;
            go.layer = RF.LayerProps;
            if (kind == ItemId.Water)
            {
                RF.Prim(PrimitiveType.Sphere, go.transform, "blue_paint", Vector3.zero, new Vector3(0.18f, 0.2f, 0.18f), default, false);
            }
            else
            {
                RF.Prim(PrimitiveType.Cube, go.transform, "red_paint", Vector3.zero, new Vector3(0.14f, 0.16f, 0.12f), default, false);
                RF.Prim(PrimitiveType.Sphere, go.transform, "white_paint", new Vector3(0, 0.12f, 0), Vector3.one * 0.12f, default, false);
                RF.Prim(PrimitiveType.Cube, go.transform, "gold_trim", new Vector3(0, 0, -0.09f), new Vector3(0.12f, 0.03f, 0.03f), default, false);
            }
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.1f;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = vel;
            rb.angularVelocity = Random.insideUnitSphere * 8f;
            var p = go.AddComponent<Projectile>();
            p.kind = kind;
            p.rb = rb;
            return p;
        }

        void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f && !done) Land(transform.position);
        }

        void OnCollisionEnter(Collision c)
        {
            if (done) return;
            Land(c.contactCount > 0 ? c.GetContact(0).point : transform.position);
        }

        void Land(Vector3 at)
        {
            done = true;
            if (kind == ItemId.Water)
            {
                if (AudioManager.I != null) AudioManager.I.PlayAt("water_splash", at, 1f, 2f, 18f);
                var puddle = RF.Prim(PrimitiveType.Quad, null, "decal_water", new Vector3(at.x, 0.02f, at.z), new Vector3(2.2f, 2.2f, 1f), new Vector3(90, Random.Range(0, 360f), 0), false);
                if (GameController.I != null) puddle.transform.SetParent(GameController.I.transform, true);
                Destroy(puddle, 40f);
                int hits = RobotAI.StunInRadius(at, 2.6f);
                if (hits > 0)
                {
                    SaveSystem.Data.robotsStunned += hits;
                    Achievements.Report("short_circuit", hits);
                }
                Noise.Emit(at, 8f, NoiseKind.Footstep);
                Destroy(gameObject);
            }
            else
            {
                rb.linearVelocity *= 0.3f;
                gameObject.AddComponent<WindupToy>();
                Destroy(this);
            }
        }
    }

    public class WindupToy : MonoBehaviour
    {
        float t = 11f;
        float pulse;
        AudioSource src;
        bool counted;

        void Start()
        {
            src = AudioManager.Attach(gameObject, "windup_toy", 1f, true, 3f, 40f);
        }

        void Update()
        {
            t -= Time.deltaTime;
            pulse -= Time.deltaTime;
            if (t > 0f && pulse <= 0f)
            {
                pulse = 0.7f;
                int n = RobotAI.DistractTo(transform.position, 30f);
                if (n > 0 && !counted)
                {
                    counted = true;
                    SaveSystem.Data.robotsDistracted++;
                    Achievements.Report("distraction");
                }
            }
            if (t > 0f)
            {
                transform.GetChild(1).localPosition = new Vector3(0, 0.12f + Mathf.Abs(Mathf.Sin(Time.time * 18f)) * 0.03f, 0);
            }
            else if (src != null)
            {
                Destroy(src);
                src = null;
                Destroy(gameObject, 20f);
            }
        }
    }
}
