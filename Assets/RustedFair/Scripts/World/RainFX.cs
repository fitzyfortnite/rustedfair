using UnityEngine;

namespace RustedFair
{
    /// <summary>Cheap but convincing rain: stretched streak particles + ground splashes.</summary>
    public static class RainFX
    {
        public static ParticleSystem Make(Transform parent, Vector3 area, int maxParticles, bool splashes = true)
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 0.9f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(17f, 22f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.025f, 0.04f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSizeZ = 1f;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.75f, 0.8f, 0.9f, 0.5f);
            main.gravityModifier = 0.4f;
            var em = ps.emission;
            em.rateOverTime = maxParticles / 0.9f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(area.x, area.z, area.y); // rotated so local Y spans world Z
            sh.rotation = new Vector3(90f, 0, 0); // emit downwards
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MatLib.Get("fx_rain");
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.035f;
            r.lengthScale = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            if (splashes)
            {
                var sub = new GameObject("Splashes");
                sub.transform.SetParent(go.transform, false);
                sub.transform.localPosition = new Vector3(0, -go.transform.position.y + 0.03f, 0);
                var sp = sub.AddComponent<ParticleSystem>();
                var sm = sp.main;
                sm.loop = true;
                sm.startLifetime = 0.25f;
                sm.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
                sm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                sm.startColor = new Color(0.8f, 0.85f, 0.95f, 0.35f);
                sm.gravityModifier = 1f;
                sm.maxParticles = maxParticles / 2;
                sm.simulationSpace = ParticleSystemSimulationSpace.World;
                var se = sp.emission;
                se.rateOverTime = maxParticles * 0.6f;
                var ssh = sp.shape;
                ssh.shapeType = ParticleSystemShapeType.Box;
                ssh.scale = new Vector3(area.x, area.z, 0.01f);
                ssh.rotation = new Vector3(-90f, 0, 0); // box emits along +Z: point it up, not sideways into the ground
                ssh.randomDirectionAmount = 0.6f;
                var sr = sub.GetComponent<ParticleSystemRenderer>();
                sr.sharedMaterial = MatLib.Get("fx_dust");
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sp.Play();
            }
            ps.Play();
            return ps;
        }
    }
}
