#nullable enable
using System;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>Reads the same baked coast mask as StatecraftTerrain.hlsl. Dry rift land may be below ocean level; height
    /// alone cannot distinguish it from water. The temporary readable copy exists only while MapLook builds its layers,
    /// and never changes the imported texture or its settings.</summary>
    public sealed class MapLookSurface : IDisposable
    {
        readonly Vector3 origin, size;
        Texture2D? mask;
        bool ownsMask;

        public MapLookSurface(MapScene map)
        {
            var terrain = map.Terrain;
            origin = terrain.transform.position; size = terrain.terrainData.size;
            var source = terrain.materialTemplate.GetTexture("_ScCoastTex") as Texture2D;
            if (source == null) throw new InvalidOperationException("Map look requires the terrain's coast mask");
            if (source.isReadable) { mask = source; return; }

            // The source is deliberately non-readable. One opt-in readback is less disruptive than changing its importer
            // for every player. Preserve the caller's active target even if a graphics operation fails.
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Texture2D? readback = null;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                readback = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true) { name = "look-coast-readback", wrapMode = TextureWrapMode.Clamp };
                readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                readback.Apply(false, false);
                mask = readback; ownsMask = true;
            }
            catch
            {
                if (readback != null) Kill(readback);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        public bool IsWater(Vector3 point)
        {
            if (mask == null) throw new ObjectDisposedException(nameof(MapLookSurface));
            float u = (point.x - origin.x) / size.x, v = (point.z - origin.z) / size.z;
            if (u < 0f || v < 0f || u > 1f || v > 1f) return true;
            // ScWater is fully land at 0.503. Excluding its shore blend keeps vegetation clear of the foam band too.
            return mask.GetPixelBilinear(u, v).r < 0.503f;
        }

        public void Dispose()
        {
            if (ownsMask && mask != null) Kill(mask);
            mask = null; ownsMask = false;
        }

        static void Kill(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
