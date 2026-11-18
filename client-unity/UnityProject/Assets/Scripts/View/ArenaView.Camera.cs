using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client
{
    /// <summary>
    /// Camera follow and smoothing, lamp visibility, screen-to-server mapping and quality application.
    /// </summary>
    public sealed partial class ArenaView
    {
        /// <summary>
        /// Follows the owner with light smoothing (absorbs 2 to 6 u reconciliation corrections, legacy CameraFollow). The cursor is
        /// always mapped through the camera as it actually stands (<see cref="ScreenToServer"/>), so smoothing never skews aim.
        /// Cuts straight to the target on a jump, a long frame, or when <paramref name="cut"/> is set.
        /// </summary>
        public void FollowSmoothed(Camera cam, float serverX, float serverY, float dt, bool cut)
        {
            float dx = serverX - _smoothX, dy = serverY - _smoothY;
            if (cut || !_smoothValid || !(dt >= 0f) || dt > FollowMaxDt || dx * dx + dy * dy > FollowCutUnits * FollowCutUnits)
            { _smoothX = serverX; _smoothY = serverY; }
            else
            {
                float alpha = 1f - Mathf.Exp(-dt / FollowTauSeconds);
                _smoothX += dx * alpha; _smoothY += dy * alpha;
            }
            _smoothValid = true;
            Follow(cam, _smoothX, _smoothY);
        }
        /// <summary>Raw follow (map peek); the next smoothed follow starts from its target.</summary>
        public void FollowExact(Camera cam, float serverX, float serverY) { _smoothValid = false; Follow(cam, serverX, serverY); }
        public void Follow(Camera cam, float serverX, float serverY)
        {
            var p = WorldPoint(serverX, serverY); cam.transform.position = new Vector3(p.x, p.y + 1.5f, -30);
            cam.transform.rotation = Quaternion.identity;
            _lastFollow = p; _hasFollow = true;
            _camCenter = cam.transform.position; _camHalf = cam.orthographic ? new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize) : new Vector2(12, 9);
            UpdateLampVisibility(p);
            var pmAtmos = PerfMarks.Begin();
            if (_atmos != null) _atmos.OnFollow(cam, Time.unscaledDeltaTime);
            PerfMarks.End(PerfMarks.Sec.Atmosphere, pmAtmos);
            bool ambient = Quality.AmbientParticles && Quality.Particles > 0f;
            bool motion = ShowcaseSettings.Current == null || !ShowcaseSettings.Current.ReducedMotion;
            int moteLimit = ambient ? Mathf.Min(_motes.Count, Mathf.CeilToInt(_motes.Count * Mathf.Min(1f, Quality.Particles))) : 0;
            for(int i=0;i<_motes.Count;i++)
            {
                var m = _motes[i]; m.enabled = i < moteLimit && !(_atmos != null && _atmos.OwnsMotes);
                float t = motion ? Time.unscaledTime * .03f : 0;
                m.transform.position = p + new Vector3(Mathf.Repeat(i*7.31f+t,32)-16, Mathf.Repeat(i*3.71f+t*.7f,18)-9,0);
            }
            for(int i=0;i<_fog.Count;i++)
            {
                _fog[i].enabled=ambient;
                float t=motion ? Time.unscaledTime*.04f : 0;
                _fog[i].transform.position=p+new Vector3(Mathf.Sin(t+i*2)*6,(i-1)*5,0);
            }
        }
        void UpdateLampVisibility(Vector3 p)
        {
            int budget = Quality.DynamicLights;
            int count = 0;
            foreach (var lamp in _lamps)
            {
                bool near = Mathf.Abs(lamp.transform.position.x - p.x) < 18 && Mathf.Abs(lamp.transform.position.y - p.y) < 12;
                lamp.SetVisible(near && count++ < budget);
            }
        }
        public bool ScreenToServer(Camera cam, Vector2 screen, out float sx, out float sy)
        {
            var p = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            sx = p.x / Scale; sy = -p.y / (Scale * GroundSquash);
            return !float.IsNaN(sx) && !float.IsNaN(sy);
        }
        void ApplyQuality()
        {
            var quality = ShowcaseSettings.Current != null ? ShowcaseSettings.Current.EffectiveQuality : ShowcaseQuality.High;
            // Presets come from the Quality row (QualityModel); AtmosphereQuality turns them into the per-effect switches
            // (volume enable, bloom, grading, sun, shadows, lights, particles) and applies ?fx.* overrides.
            AtmosphereQuality.RefreshFromSettings();
            if (_atmos != null) _atmos.SetAmbientBase(AtmosphereQuality.PostProcessing ? .36f : .3f);   // near-constant base for every quality (no tonemapping at Low, so a little less)
            if (_visionFog != null) _visionFog.ApplyQuality();   // fog is information: presets only change its softness
            foreach (var glow in _lampGlows) glow.enabled = Quality.DynamicLights > 0;
            foreach (var light in _lamps) { light.SetShadows(Quality.Shadows); light.ApplyQuality(); }
            if (_hasFollow) UpdateLampVisibility(_lastFollow);
        }
    }
}
