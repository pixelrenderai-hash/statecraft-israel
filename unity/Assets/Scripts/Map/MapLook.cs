#nullable enable
using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Statecraft.Game.Map
{
    /// <summary>Opt-in map look (spec docs/superpowers/specs/2026-10-12-look-and-play-two-options-design.md, Step 0):
    /// orchards, groves, fields and rocks by biome (MapLookScatter), town blocks (MapLookTowns), the 1948 railway
    /// (MapLookLines), and a livelier grade with a lighter tilt-shift, all under one "MapLook" root beside "Settlements".
    /// Web: ?map-look or ?map-look=1. Desktop: -map-look. Off with nothing applied touches nothing, so the shipped map is
    /// unchanged. No simulation state is read or written.</summary>
    public static class MapLook
    {
        public const string RootName = "MapLook";
        public const string ScatterToggle = "look-scatter", TownsToggle = "look-towns", LinesToggle = "look-lines", GradeToggle = "look-grade";
        /// <summary>Starting values for local capture review; the shipped grade is MapGrade's.</summary>
        public const float Saturation = 18f, Contrast = 22f, Temperature = 9f, PostExposure = 0.2f, TiltRadiusScale = 0.45f;

        static readonly Regex Flag = new Regex(@"(?:^|&)map-look(?:=1)?(?=&|$)", RegexOptions.CultureInvariant);

        public static bool Requested() =>
            Requested(Application.absoluteURL, Environment.GetCommandLineArgs(), Application.platform == RuntimePlatform.WebGLPlayer);

        /// <summary>Web: only a top-level query parameter counts, bare or "=1", never a fragment or a nested URL's question
        /// mark (BuildingPreview's rule). Desktop: only the exact argument.</summary>
        public static bool Requested(string? url, string[] args, bool webgl)
        {
            if (!webgl) return Array.IndexOf(args, "-map-look") >= 0;
            var address = (url ?? "").Split('#')[0];
            var start = address.IndexOf('?');
            return start >= 0 && Flag.IsMatch(address.Substring(start + 1));
        }

        /// <summary>Removes what an earlier Apply placed and restores the picture; then, when <paramref name="enabled"/>, builds
        /// the layers and applies the look's picture. Off with nothing applied changes nothing (the default path).</summary>
        public static void Apply(MapScene map, bool enabled)
        {
            if (map == null) return;
            var old = map.transform.Find(RootName);
            if (old != null)
            {
                // Destroy is deferred in players. Restore state now, before the new look takes its snapshot.
                var lifetime = old.GetComponent<MapRuntimeAssets>();
                if (lifetime != null) lifetime.RunTeardown();
                BuildingPreview.Discard(old.gameObject);
            }
            if (!enabled || map.Terrain == null || map.Hexes == null) return;
            var root = new GameObject(RootName).transform;
            root.SetParent(map.transform, false);
            var owned = root.gameObject.AddComponent<MapRuntimeAssets>();
            try
            {
                var assets = root.gameObject.AddComponent<MapLookAssets>();
                assets.Init(map.MarkerMaterial);
                using var surface = new MapLookSurface(map);
                var scatter = MapLookScatter.Build(map, assets, root, surface.IsWater);
                var towns = MapLookTowns.Build(map, assets, root, surface.IsWater);
                var lines = MapLookLines.Build(map, assets, root);
                owned.OnTeardown(VisualToggles.Register(ScatterToggle, on => { if (scatter != null) scatter.SetActive(on); }));
                owned.OnTeardown(VisualToggles.Register(TownsToggle, on => { if (towns != null) towns.SetActive(on); }));
                owned.OnTeardown(VisualToggles.Register(LinesToggle, on => { if (lines != null) lines.SetActive(on); }));
                var rig = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
                var picture = new Picture(rig != null && rig.Volume != null ? rig.Volume.profile : null);
                owned.OnTeardown(() => picture.Set(false));
                owned.OnTeardown(VisualToggles.Register(GradeToggle, picture.Set));
                picture.Set(true);
            }
            catch
            {
                owned.RunTeardown();
                BuildingPreview.Discard(root.gameObject);
                throw;
            }
        }

        /// <summary>The snapshot belongs to this root, not CameraRig's next Volume after a map rebuild. Keep both values
        /// and override states so toggling or removing the look restores the preceding picture exactly.</summary>
        sealed class Picture
        {
            readonly ColorAdjustments? colour;
            readonly WhiteBalance? balance;
            readonly Parameter saturation, contrast, exposure, temperature;
            readonly float radius = TiltShift.RadiusScale;

            public Picture(VolumeProfile? profile)
            {
                if (profile == null) return;
                if (profile.TryGet(out ColorAdjustments ca))
                {
                    colour = ca;
                    saturation = new Parameter(ca.saturation); contrast = new Parameter(ca.contrast);
                    exposure = new Parameter(ca.postExposure);
                }
                if (profile.TryGet(out WhiteBalance wb))
                {
                    balance = wb; temperature = new Parameter(wb.temperature);
                }
            }

            public void Set(bool look)
            {
                TiltShift.RadiusScale = look ? TiltRadiusScale : radius;
                if (colour != null)
                {
                    saturation.Set(colour.saturation, look, Saturation);
                    contrast.Set(colour.contrast, look, Contrast);
                    exposure.Set(colour.postExposure, look, PostExposure);
                }
                if (balance != null) temperature.Set(balance.temperature, look, Temperature);
            }
        }

        readonly struct Parameter
        {
            readonly float value;
            readonly bool overridden;
            public Parameter(FloatParameter parameter) { value = parameter.value; overridden = parameter.overrideState; }
            public void Set(FloatParameter parameter, bool look, float lookValue)
            {
                parameter.value = look ? lookValue : value;
                parameter.overrideState = look || overridden;
            }
        }
    }
}
