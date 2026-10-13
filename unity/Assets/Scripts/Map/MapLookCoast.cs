#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>Generic fishing boats and a steamer as symbolic coast dressing, not a historical ship's recorded position.
    /// Code-built alternatives to the pending-rights wave-3 models keep map-look usable independently of building-preview.</summary>
    public static class MapLookCoast
    {
        public static void Build(MapScene map, MapLookAssets assets, Transform parent)
        {
            var placements = new List<(string shape, Matrix4x4 matrix)>();
            float sea = PreviewProps.SeaLevelY();
            var terrain = map.Terrain;
            foreach (var p in new[]
            {
                (lon: 34.735, lat: 32.090, shape: MapLookShapes.FishingBoat, yaw: 325f, scale: new Vector3(520f, 200f, 650f)),
                (lon: 34.860, lat: 32.430, shape: MapLookShapes.FishingBoat, yaw: 340f, scale: new Vector3(450f, 180f, 580f)),
                (lon: 34.965, lat: 32.835, shape: MapLookShapes.Steamer, yaw: 35f, scale: new Vector3(1050f, 330f, 1600f)),
            })
            {
                var at = map.Frame.ToWorld(p.lon, p.lat, sea - 0.08f * p.scale.y);
                var rotation = Quaternion.Euler(0f, p.yaw, 0f);
                bool openWater = true;
                foreach (var corner in new[] { Vector3.zero, new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f) })
                {
                    var point = at + rotation * Vector3.Scale(corner, p.scale);
                    float ground = terrain.SampleHeight(point) + terrain.transform.position.y;
                    if (Mathf.Abs(ground - sea) > MapBuilder.SeaToleranceM * MapBuilder.VerticalExaggeration) { openWater = false; break; }
                }
                if (openWater) placements.Add((p.shape, Matrix4x4.TRS(at, rotation, p.scale)));
            }
            assets.Emit(parent, "coast", placements, shadows: true);
        }
    }
}
