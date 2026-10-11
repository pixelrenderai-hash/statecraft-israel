#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>Opt-in ambient dressing for the building preview: ships, an aircraft, a vehicle, a fort and figures from the
    /// same git-ignored prototype prefabs, at fixed spots under one "PreviewProps" root beside "Settlements".
    /// BuildingPreview.Apply places and removes it (so GameRoot is unchanged), and MapScene.Build's Clear destroys it with
    /// the rest of the map. Art dressing, not sourced content: the spots are approximate, chosen to read at the 6-9 km zoom
    /// and to clear every settlement's prototype footprint (BuildingPreviewDressingTests checks the table against the
    /// settlements). Nothing here touches the simulation, and nothing has a collider, so map clicks still pick only the
    /// terrain (MapPointer). An entry none of whose prefabs loads places nothing.</summary>
    public static class PreviewProps
    {
        public const string RootName = "PreviewProps", InstancePrefix = "prop-";

        /// <summary>Symbolic map scale, each prop's largest dimension (m). The settlements are symbolic too (a city's box is
        /// 1.8 km), and a true-size 100 m ship would be a few pixels at the 6-9 km zoom: ships near a city box, figures about
        /// a fifth of one.</summary>
        public const float ShipM = 1600f, AircraftM = 900f, VehicleM = 450f, FortM = 800f, FigureM = 350f;
        /// <summary>Height of an airborne prop's bottom above the highest ground under it: low, on approach.</summary>
        public const float AirAltitudeM = 600f;
        /// <summary>Share of a floating hull's height below the sea surface, so it sits in the water rather than on it.</summary>
        public const float DraftShare = 0.1f;
        /// <summary>PreviewBob's gentle motion: a sea swell, and a slower drift aloft. Amplitude (m), period (s), roll (degrees).
        /// About 2 px of rise at the 9 km zoom.</summary>
        public const float SeaBobM = 12f, SeaBobPeriodS = 7f, SeaRollDeg = 1.2f, AirBobM = 30f, AirBobPeriodS = 11f, AirRollDeg = 2f;
        /// <summary>Spreads the bob phases (the golden ratio's fraction): fixed per entry, so every run moves alike.</summary>
        const float PhaseStep = 0.618034f;

        /// <summary>Where a prop rests: on the highest ground under it, on the sea surface (RestY), or AirAltitudeM above
        /// the ground.</summary>
        public enum Kind { Land, Sea, Air }
        /// <summary>None stands still (an Animator is switched off); Bob adds PreviewBob; Idle lets the prefab's Animator play
        /// its default state (PrototypeImport makes it the idle clip).</summary>
        public enum Motion { None, Bob, Idle }

        /// <summary>One prop: the first of <see cref="Names"/> whose prefab loads, centred on (Lon, Lat), turned Yaw degrees
        /// and scaled so its own largest dimension is SizeM, whatever the turn.</summary>
        public sealed class Prop
        {
            public readonly double Lon, Lat; public readonly float Yaw, SizeM; public readonly Kind Kind; public readonly Motion Motion;
            public readonly IReadOnlyList<string> Names;

            public Prop(double lon, double lat, float yaw, float sizeM, Kind kind, Motion motion, params string[] names)
            { Lon = lon; Lat = lat; Yaw = yaw; SizeM = sizeM; Kind = kind; Motion = motion; Names = names; }
        }

        /// <summary>The props, one line each; a later name is a fallback (another export of the same model). Coordinates are
        /// approximate and offset where needed to keep clear of a settlement's footprint (Spread x its edge); yaws are first
        /// guesses, to be tuned from BuildingPreviewCapture's renders.</summary>
        public static IReadOnlyList<Prop> All { get; } = new[]
        {
            // The Altalena off the Tel Aviv beach: 3.8 km out, so its hull clears the White City's footprint (open sea all round).
            new Prop(34.740, 32.085, 20f, ShipM, Kind.Sea, Motion.Bob, "altalena-lst-v1", "altalena-lst-v1-7k"),
            // An immigrant steamer in Haifa Bay, 4 km north-east of the city: the port itself lies inside the city's footprint.
            new Prop(35.030, 32.845, 60f, ShipM, Kind.Sea, Motion.Bob, "immigrant-steamer-v1"),
            // A C-54 on approach over Lod airport, 6 km north of the Lod marker.
            new Prop(34.885, 32.005, 300f, AircraftM, Kind.Air, Motion.Bob, "airliner-c54-v1"),
            // A "sandwich" armoured truck on the Bab al-Wad road up to Jerusalem.
            new Prop(35.020, 31.810, 75f, VehicleM, Kind.Land, Motion.None, "sandwich-armoured-truck-v1"),
            // The Tegart fort at Latrun.
            new Prop(34.980, 31.840, BuildingPreview.DefaultYaw, FortM, Kind.Land, Motion.None, "tegart-fort-v1"),
            // Figures: a pioneer farmer in the fields 1.3 km south of Degania Alef, an immigrant woman on the Carmel above
            // Haifa, a soldier below the Latrun fort.
            new Prop(35.578, 32.697, BuildingPreview.DefaultYaw, FigureM, Kind.Land, Motion.Idle, "figure-pioneer-farmer-v1-tpose", "figure-pioneer-farmer-v1"),
            new Prop(35.000, 32.790, BuildingPreview.DefaultYaw, FigureM, Kind.Land, Motion.Idle, "figure-immigrant-woman-v1"),
            new Prop(34.970, 31.835, BuildingPreview.DefaultYaw, FigureM, Kind.Land, Motion.Idle, "figure-soldier-1948-v1"),
        };

        /// <summary>Places <see cref="All"/> (BuildingPreview.Apply's call); off, as on GameRoot's default path, it does not
        /// read the table.</summary>
        public static int Apply(MapScene map, bool enabled, Func<string, GameObject?> load) =>
            Apply(map, enabled, load, enabled ? All : Array.Empty<Prop>());

        /// <summary>Removes what an earlier Apply placed; then, when <paramref name="enabled"/> and the map is built, places
        /// each of <paramref name="props"/> whose prefab <paramref name="load"/> returns (BuildingPreview.Stand: no collider,
        /// shadows on), scaled to its SizeM unturned and then turned (Stand fits the turned bounds, which grow with the turn: a
        /// hull at 60 degrees would come out about 6% too long), recentred on its spot, resting as its Kind says, moving as its
        /// Motion says. Off with nothing placed loads nothing and touches nothing. Returns the number placed.</summary>
        public static int Apply(MapScene map, bool enabled, Func<string, GameObject?> load, IReadOnlyList<Prop> props)
        {
            if (map == null) return 0;
            var old = map.transform.Find(RootName);
            if (old != null) BuildingPreview.Discard(old.gameObject);
            if (!enabled || map.Terrain == null) return 0;
            Transform? root = null; int placed = 0;
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                var (name, prefab) = BuildingPreview.Resolve(p.Names, load);
                if (name == null || prefab == null) continue;
                if (root == null) { root = new GameObject(RootName).transform; root.SetParent(map.transform, true); }
                var at = map.Frame.ToWorld(p.Lon, p.Lat, 0);
                var go = BuildingPreview.Stand(prefab, at, 0f, root, $"{InstancePrefix}{i:00}-{name}", p.SizeM, p.SizeM);
                if (go == null) continue;
                go.transform.rotation = Quaternion.Euler(0f, p.Yaw, 0f);
                Animate(go, p, i);
                var b = BuildingPreview.Measure(go)!.Value;
                go.transform.position += new Vector3(at.x - b.center.x, 0f, at.z - b.center.z);   // the turn moved the footprint
                b = BuildingPreview.Measure(go)!.Value;
                go.transform.position += Vector3.up * (RestY(map.Terrain, p.Kind, b) - b.min.y);
                if (p.Motion == Motion.Bob)
                {
                    bool air = p.Kind == Kind.Air;
                    go.AddComponent<PreviewBob>().Init(air ? AirBobM : SeaBobM, air ? AirBobPeriodS : SeaBobPeriodS, air ? AirRollDeg : SeaRollDeg, Mathf.Repeat(i * PhaseStep, 1f));
                }
                placed++;
            }
            return placed;
        }

        /// <summary>The world height of 0 m (the sea surface), as MapShaderGlobals published it when the map was built: the
        /// water shader's own level, so a hull meets the drawn water.</summary>
        public static float SeaLevelY() => Shader.GetGlobalVector(MapShaderGlobals.Map).z;

        /// <summary>Where the bottom of a prop with bounds <paramref name="b"/> rests. The ground is the highest terrain under
        /// its centre and the corners of its footprint, so a long prop on a slope (the relief is exaggerated 3x) is not buried
        /// at its uphill end (MapScene.Select's idiom). A sea prop floats where all that ground is open sea (within
        /// MapBuilder.SeaToleranceM of 0 m): its keel DraftShare of its height below the surface by design, so it sits in the
        /// water. The heightmap has no bathymetry and the terrain itself draws the water there, so that keel is the one thing
        /// below the terrain's surface. Elsewhere (a shore, or a lake below sea level such as the Kinneret, which the 0 m
        /// surface would leave it hanging hundreds of metres above) it rests on the ground.</summary>
        public static float RestY(Terrain terrain, Kind kind, Bounds b)
        {
            float ground = float.MinValue;
            foreach (var (x, z) in new[] { (b.center.x, b.center.z), (b.min.x, b.min.z), (b.min.x, b.max.z), (b.max.x, b.min.z), (b.max.x, b.max.z) })
                ground = Mathf.Max(ground, terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y);
            if (kind == Kind.Air) return ground + AirAltitudeM;
            if (kind == Kind.Land) return ground;
            float sea = SeaLevelY();
            return Mathf.Abs(ground - sea) <= MapBuilder.SeaToleranceM * MapBuilder.VerticalExaggeration ? sea - DraftShare * b.size.y : ground;
        }

        /// <summary>Idle: every Animator plays (no root motion), and in edit mode, where Animators do not run, the default
        /// clip's first frame is sampled so captures show the idle pose rather than the bind pose. Otherwise every Animator is
        /// off, holding the pose the prefab was saved in.</summary>
        static void Animate(GameObject go, Prop p, int index)
        {
            foreach (var a in go.GetComponentsInChildren<Animator>(true))
            {
                a.applyRootMotion = false;
                a.enabled = p.Motion == Motion.Idle;
                if (a.enabled && !Application.isPlaying) SampleIdle(a);
            }
        }

        /// <summary>Samples the Animator's idle clip (the first named like "idle", else its first clip) at time 0 onto its own
        /// object: edit-mode renders only (captures, turntables). No clip, nothing done.</summary>
        public static void SampleIdle(Animator a)
        {
            var clips = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.animationClips : Array.Empty<AnimationClip>();
            var clip = clips.FirstOrDefault(c => c != null && c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? clips.FirstOrDefault(c => c != null);
            if (clip != null) clip.SampleAnimation(a.gameObject, 0f);
        }
    }
}
