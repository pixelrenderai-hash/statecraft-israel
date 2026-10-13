#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>The map look's town blocks: a ring of flat-roofed blocks around each city, town and arab town, outside the
    /// marker's own footprint (where the box or prototype stands) and under its label (MarkerHeightM). Uses the marker
    /// palette's plaster and sandstone groups; these are symbolic blocks, not reconstructed buildings. Seeded by the settlement id
    /// through a stable hash, so every launch draws the same streets.</summary>
    public static class MapLookTowns
    {
        public const float InnerShare = 1.05f, OuterShare = 2.6f, MaxHeightShare = 0.55f, MinHeightM = 110f, MaxSlopeDeg = 20f, SinkM = 20f;

        static readonly Dictionary<string, (int count, string shape)> ByType = new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            ["city"] = (46, MapLookShapes.BlockPlaster), ["town"] = (22, MapLookShapes.BlockPlaster), ["arab_town"] = (16, MapLookShapes.BlockStone),
        };

        public static List<MapLookScatter.Plant> Plan(IEnumerable<Settlement> settlements, Func<double, double, Vector3> toWorld,
            Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg, float seaY, Func<Vector3, bool>? isWater = null)
        {
            var blocks = new List<MapLookScatter.Plant>();
            foreach (var s in settlements)
            {
                if (!ByType.TryGetValue(s.Type, out var rule) || double.IsNaN(s.Lon) || double.IsNaN(s.Lat)) continue;
                float h = SettlementPlacer.MarkerHeightM(s.Type);
                var centre = toWorld(s.Lon, s.Lat);
                float labelY = groundY(centre) + h;
                bool Water(Vector3 point) => isWater != null ? isWater(point) : groundY(point) < seaY + MapLookScatter.SeaClearanceM;
                var rng = new System.Random(StableHash(s.Id));
                for (int i = 0; i < rule.count; i++)
                {
                    float a = (float)(rng.NextDouble() * 2 * Math.PI), r = Mathf.Lerp(InnerShare * h, OuterShare * h, Mathf.Sqrt((float)rng.NextDouble()));
                    float w = 140f + 220f * (float)rng.NextDouble(), d = 140f + 220f * (float)rng.NextDouble();
                    float t = (float)rng.NextDouble(), height = Mathf.Lerp(MinHeightM, MaxHeightShare * h, t * t);
                    float yaw = 10f + (rng.NextDouble() < 0.8 ? 0f : 45f);
                    var p = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    float footprint = new Vector2(w, d).magnitude * 0.5f;
                    // Keep even the nearest corner outside the marker/prototype footprint.
                    if (r - footprint < h) continue;
                    float y = groundY(p), hi = y, lo = y;
                    if (Water(p) || slopeDeg(p) > MaxSlopeDeg) continue;
                    var turn = Quaternion.Euler(0f, yaw, 0f);
                    bool valid = true;
                    foreach (var c in new[] { new Vector3(w, 0f, d), new Vector3(-w, 0f, d), new Vector3(w, 0f, -d), new Vector3(-w, 0f, -d) })
                    {
                        var corner = p + turn * (c * 0.5f);
                        float cy = groundY(corner);
                        if (Water(corner) || slopeDeg(corner) > MaxSlopeDeg) { valid = false; break; }
                        hi = Mathf.Max(hi, cy); lo = Mathf.Min(lo, cy);
                    }
                    if (!valid || hi - lo > height * 0.5f) continue;
                    // Seat the base below the lowest ground, cap the top at the centre's label-height allowance.
                    float top = Mathf.Min(labelY, Mathf.Min(hi + height, y + MaxHeightShare * h));
                    if (top - hi < MinHeightM) continue;
                    blocks.Add(new MapLookScatter.Plant(rule.shape, new Vector3(p.x, lo - SinkM, p.z), yaw, new Vector3(w, top - lo + SinkM, d)));
                }
            }
            return blocks;
        }

        /// <summary>FNV-1a over the id's characters: stable across runs and platforms (string.GetHashCode is not).</summary>
        public static int StableHash(string s) { unchecked { uint h = 2166136261; foreach (char c in s) h = (h ^ c) * 16777619; return (int)h; } }

        public static GameObject Build(MapScene map, MapLookAssets assets, Transform root, Func<Vector3, bool>? isWater = null)
        {
            var go = new GameObject(MapLook.TownsToggle);
            go.transform.SetParent(root, false);
            var t = map.Terrain; var data = t.terrainData; var origin = t.transform.position;
            float Ground(Vector3 p) => t.SampleHeight(p) + origin.y;
            float Slope(Vector3 p) => data.GetSteepness((p.x - origin.x) / data.size.x, (p.z - origin.z) / data.size.z);
            var blocks = Plan(map.Settlements, (lon, lat) => map.Frame.ToWorld(lon, lat, 0f), Ground, Slope, PreviewProps.SeaLevelY(), isWater);
            assets.Emit(go.transform, "towns", blocks.Select(b => (b.Shape, b.Matrix)), shadows: true);
            return go;
        }
    }
}
