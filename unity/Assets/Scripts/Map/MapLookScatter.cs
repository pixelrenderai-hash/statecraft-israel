#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>The map look's vegetation, fields and rocks. Plan is pure: per land hex, groups of a shape (a grove, a row,
    /// a field) by the hex's biome, from a generator seeded by (q, r) alone, so a hex always gets the same planting
    /// whatever the order. Diorama scale: a tree stands a few hundred metres tall, as the buildings and figures do
    /// (SettlementPlacer, PreviewProps).</summary>
    public static class MapLookScatter
    {
        public readonly struct Plant
        {
            public readonly string Shape; public readonly Vector3 At; public readonly float Yaw; public readonly Vector3 Scale;
            public Plant(string shape, Vector3 at, float yaw, Vector3 scale) { Shape = shape; At = at; Yaw = yaw; Scale = scale; }
            public Matrix4x4 Matrix => Matrix4x4.TRS(At, Quaternion.Euler(0f, Yaw, 0f), Scale);
        }

        public const int Seed = 1948;
        /// <summary>Multiplies every rule's group count; tuned from captures and the frame budget (plan Tasks 7-8).</summary>
        public const float DensityScale = 1f, LowTierDensity = 0.6f;
        public const float MaxTreeSlopeDeg = 28f, MaxFieldRiseM = 35f, SeaClearanceM = 5f, AvoidShare = 2.8f;
        public const float OliveM = 260f, CitrusM = 220f, CypressM = 480f, PalmM = 560f, RockM = 170f, FieldDepthM = 16f;

        // Per biome: (shape, groups per hex, shapes per group, spacing in a group). A group of 9 is a 3 x 3 grove.
        static readonly Dictionary<string, (string shape, int groups, int per, float spacing)[]> Rules =
            new Dictionary<string, (string, int, int, float)[]>(StringComparer.Ordinal)
        {
            // Sized for coverage (hexes.json: 109 coastal, 31 valley, 147 hills, 251 mountains, 332 desert land hexes of
            // about 65 km2): a coastal hex gets about 14 km2 of orchard and field (about 22%), so the plain reads green
            // and gold instead of tan. About 35,000 shapes in all.
            ["coastal_plain"] = new[] { (MapLookShapes.Citrus, 8, 9, 420f), (MapLookShapes.Palm, 3, 3, 500f), (MapLookShapes.FieldWheat, 3, 1, 0f), (MapLookShapes.FieldGreen, 3, 1, 0f), (MapLookShapes.Olive, 2, 6, 450f) },
            ["valley"] = new[] { (MapLookShapes.FieldWheat, 6, 1, 0f), (MapLookShapes.FieldGreen, 4, 1, 0f), (MapLookShapes.Olive, 4, 6, 450f) },
            ["hills"] = new[] { (MapLookShapes.Olive, 10, 6, 420f), (MapLookShapes.Cypress, 4, 3, 380f), (MapLookShapes.Rock, 3, 2, 400f), (MapLookShapes.Wall, 2, 3, 500f) },
            ["mountains"] = new[] { (MapLookShapes.Cypress, 6, 3, 400f), (MapLookShapes.Rock, 5, 2, 400f), (MapLookShapes.Olive, 2, 4, 450f) },
            ["desert"] = new[] { (MapLookShapes.Rock, 3, 2, 500f) },
        };

        public static List<Plant> Plan(HexFile hexes, GeoFrame frame, Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg,
            float seaY, IReadOnlyList<(Vector3 at, float radius)> avoid, float density = 1f, Func<Vector3, bool>? isWater = null)
        {
            var plants = new List<Plant>();
            float hexM = hexes.hex_size_km * 1000f;
            // Canonical order also fixes BiomeBlender's accumulation order when input hex rows are reordered.
            var ordered = hexes.hexes.OrderBy(h => h.q).ThenBy(h => h.r).ToArray();
            var names = MapBuilder.BiomeColours.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var centres = ordered.Select(h => { var p = frame.ToWorld(h.lon, h.lat, 0f); return new Vector2(p.x, p.z); }).ToArray();
            var layers = ordered.Select(h => Array.IndexOf(names, h.biome)).ToArray();
            var blender = new BiomeBlender(centres, layers, hexM, names.Length);
            var weights = new float[names.Length];
            foreach (var h in ordered)
            {
                if (!h.land || h.biome == null || !Rules.TryGetValue(h.biome, out var rules)) continue;
                var rng = new System.Random(Seed ^ (h.q * 73856093) ^ (h.r * 19349663));
                var centre = frame.ToWorld(h.lon, h.lat, 0f);
                foreach (var (shape, groups, per, spacing) in rules)
                {
                    int count = Mathf.RoundToInt(groups * DensityScale * Mathf.Clamp01(density));
                    for (int g = 0; g < count; g++)
                    {
                        var origin = centre + Disc(rng, 0.8f * hexM);
                        var row = Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f);
                        // A boundary carries both adjacent biomes' groves, thinned by their existing blended weights.
                        blender.Weights(new Vector2(origin.x, origin.z), weights);
                        bool accepted = rng.NextDouble() <= weights[Array.IndexOf(names, h.biome)];
                        for (int i = 0; i < per; i++)
                        {
                            var offset = per == 1 ? Vector3.zero : row * new Vector3((i % 3 - 1) * spacing, 0f, (i / 3 - 1) * spacing) + Disc(rng, spacing * 0.2f);
                            if (TryPlant(shape, origin + offset, rng, frame, groundY, slopeDeg, seaY, avoid, isWater, out var plant) && accepted) plants.Add(plant);
                        }
                    }
                }
            }
            return plants;
        }

        static bool TryPlant(string shape, Vector3 p, System.Random rng, GeoFrame frame, Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg,
            float seaY, IReadOnlyList<(Vector3 at, float radius)> avoid, Func<Vector3, bool>? isWater, out Plant plant)
        {
            plant = default;
            float yaw = (float)rng.NextDouble() * 360f, k = 0.8f + 0.4f * (float)rng.NextDouble();
            float w = 900f + 800f * (float)rng.NextDouble(), depth = 900f + 800f * (float)rng.NextDouble();
            bool field = shape == MapLookShapes.FieldWheat || shape == MapLookShapes.FieldGreen;
            float size = field ? 0f : SizeOf(shape) * k;
            if (!field) { w = size; depth = size; }
            // Reserve the full rotated footprint: a centre outside a marker is insufficient for a broad field.
            float footprint = new Vector2(w, depth).magnitude * 0.5f;
            if (p.x - footprint < 0f || p.z - footprint < 0f || p.x + footprint > frame.WidthM || p.z + footprint > frame.LengthM) return false;
            float y = groundY(p);
            bool Water(Vector3 point) => isWater != null ? isWater(point) : groundY(point) < seaY + SeaClearanceM;
            if (Water(p)) return false;
            foreach (var (at, radius) in avoid)
                if (new Vector2(p.x - at.x, p.z - at.z).sqrMagnitude < (radius + footprint) * (radius + footprint)) return false;
            var turn = Quaternion.Euler(0f, yaw, 0f);
            float lo = y, hi = y;
            foreach (var c in new[] { new Vector3(w, 0f, depth), new Vector3(-w, 0f, depth), new Vector3(w, 0f, -depth), new Vector3(-w, 0f, -depth) })
            {
                var corner = p + turn * (c * 0.5f);
                float cy = groundY(corner);
                if (Water(corner) || slopeDeg(corner) > MaxTreeSlopeDeg) return false;
                lo = Mathf.Min(lo, cy); hi = Mathf.Max(hi, cy);
            }
            if (slopeDeg(p) > MaxTreeSlopeDeg) return false;
            if (field)
            {
                // A flat box: its top just above the highest corner, its bottom below the lowest, so it never floats.
                if (hi - lo > MaxFieldRiseM) return false;
                plant = new Plant(shape, new Vector3(p.x, lo - FieldDepthM, p.z), yaw, new Vector3(w, hi - lo + FieldDepthM + 6f, depth));
                return true;
            }
            var scale = shape == MapLookShapes.Wall ? new Vector3(size, 70f * k, 65f * k) : Vector3.one * size;
            plant = new Plant(shape, new Vector3(p.x, y - 0.03f * size, p.z), yaw, scale);
            return true;
        }

        static bool IsTree(string shape) => shape != MapLookShapes.Rock && shape != MapLookShapes.Wall && shape != MapLookShapes.FieldWheat && shape != MapLookShapes.FieldGreen;

        static float SizeOf(string shape) => shape switch
        {
            MapLookShapes.Olive => OliveM, MapLookShapes.Citrus => CitrusM, MapLookShapes.Cypress => CypressM,
            MapLookShapes.Palm => PalmM, MapLookShapes.Rock => RockM,
            MapLookShapes.Wall => 500f,
            _ => throw new ArgumentException($"no size for '{shape}'", nameof(shape)),
        };

        static Vector3 Disc(System.Random rng, float radius)
        {
            double a = rng.NextDouble() * 2 * Math.PI, d = radius * Math.Sqrt(rng.NextDouble());
            return new Vector3((float)(d * Math.Cos(a)), 0f, (float)(d * Math.Sin(a)));
        }

        /// <summary>Each settlement keeps a clear disc of AvoidShare x its marker edge.</summary>
        public static List<(Vector3 at, float radius)> Avoid(MapScene map) => map.Settlements
            .Where(s => !double.IsNaN(s.Lon) && !double.IsNaN(s.Lat))
            .Select(s => (map.Frame.ToWorld(s.Lon, s.Lat, 0f), AvoidShare * SettlementPlacer.MarkerHeightM(s.Type))).ToList();

        public static GameObject Build(MapScene map, MapLookAssets assets, Transform root, Func<Vector3, bool>? isWater = null)
        {
            var go = new GameObject(MapLook.ScatterToggle);
            go.transform.SetParent(root, false);
            var t = map.Terrain; var data = t.terrainData; var origin = t.transform.position;
            float Ground(Vector3 p) => t.SampleHeight(p) + origin.y;
            float Slope(Vector3 p) => data.GetSteepness((p.x - origin.x) / data.size.x, (p.z - origin.z) / data.size.z);
            // QualityTierValues configures Mobile (index 0) / PC (index 1); only this opt-in layer adds density scaling.
            float density = QualitySettings.GetQualityLevel() == 0 ? LowTierDensity : 1f;
            var plants = Plan(map.Hexes, map.Frame, Ground, Slope, PreviewProps.SeaLevelY(), Avoid(map), density, isWater);
            // Trees cast shadows; flat fields and low rocks would add caster geometry (the diorama02 shadow cost,
            // QualityTierValues) for no visible gain.
            assets.Emit(go.transform, "trees", plants.Where(p => IsTree(p.Shape)).Select(p => (p.Shape, p.Matrix)), shadows: true);
            assets.Emit(go.transform, "ground", plants.Where(p => !IsTree(p.Shape)).Select(p => (p.Shape, p.Matrix)), shadows: false);
            return go;
        }
    }
}
