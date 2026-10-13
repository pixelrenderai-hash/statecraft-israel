#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Statecraft.Game.Map
{
    /// <summary>Lives on the MapLook root and owns everything the look creates at run time (materials and meshes), destroying
    /// it with the root. Emit combines placed shapes into one renderer per ChunkM chunk and palette colour, so the map gets
    /// a few hundred static renderers that Unity culls by chunk, with no per-frame code.</summary>
    [ExecuteAlways]
    public sealed class MapLookAssets : MonoBehaviour
    {
        public const float ChunkM = 20000f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Smoothness = Shader.PropertyToID("_Smoothness");

        /// <summary>Colours tuned under the map light (start values; plan Task 8 tunes them from captures).</summary>
        public static readonly IReadOnlyDictionary<string, Color> Palette = new Dictionary<string, Color>
        {
            ["bark"] = new Color(0.36f, 0.27f, 0.20f), ["olive"] = new Color(0.47f, 0.52f, 0.36f),
            ["citrus"] = new Color(0.18f, 0.36f, 0.17f), ["cypress"] = new Color(0.16f, 0.30f, 0.18f),
            ["palm"] = new Color(0.33f, 0.48f, 0.22f), ["rock"] = new Color(0.62f, 0.57f, 0.50f),
            ["wheat"] = new Color(0.86f, 0.74f, 0.42f), ["green"] = new Color(0.55f, 0.62f, 0.30f),
            ["plaster"] = new Color(0.95f, 0.93f, 0.87f), ["stone"] = new Color(0.84f, 0.75f, 0.58f),
            ["rail"] = new Color(0.27f, 0.22f, 0.18f), ["road"] = new Color(0.80f, 0.72f, 0.56f),
            ["hull"] = new Color(0.20f, 0.28f, 0.30f),
        };

        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        readonly Dictionary<string, MapLookShapes.Part[]> shapes = new Dictionary<string, MapLookShapes.Part[]>(StringComparer.Ordinal);
        readonly List<Mesh> owned = new List<Mesh>();
        Material? template;

        /// <summary>The template is the map's marker material (URP Lit); each colour is a copy of it.</summary>
        public void Init(Material markerTemplate) => template = markerTemplate != null ? markerTemplate : throw new ArgumentNullException(nameof(markerTemplate));

        public Material MaterialFor(string colour)
        {
            if (materials.TryGetValue(colour, out var m)) return m;
            m = new Material(template ?? throw new InvalidOperationException("MapLookAssets.Init was not called")) { name = "look-" + colour };
            m.SetColor(BaseColor, Palette[colour]);
            if (m.HasProperty(Smoothness)) m.SetFloat(Smoothness, 0.05f);
            return materials[colour] = m;
        }

        public MapLookShapes.Part[] Shape(string name) =>
            shapes.TryGetValue(name, out var parts) ? parts : shapes[name] = MapLookShapes.Build(name);

        public Mesh Own(Mesh mesh) { owned.Add(mesh); return mesh; }

        /// <summary>One renderer per (chunk, colour) under <paramref name="parent"/>, named "&lt;layer&gt;-&lt;cx&gt;_&lt;cz&gt;/&lt;colour&gt;";
        /// the input matrices are world space, converted to the parent's frame. Returns the number of renderers.</summary>
        public int Emit(Transform parent, string layer, IEnumerable<(string shape, Matrix4x4 matrix)> items, bool shadows)
        {
            var groups = new SortedDictionary<string, List<CombineInstance>>(StringComparer.Ordinal);
            foreach (var (shape, matrix) in items)
            {
                var at = matrix.GetColumn(3);
                var chunk = $"{Mathf.FloorToInt(at.x / ChunkM)}_{Mathf.FloorToInt(at.z / ChunkM)}";
                foreach (var part in Shape(shape))
                {
                    var key = chunk + "/" + part.Colour;
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = part.Mesh, transform = parent.worldToLocalMatrix * matrix });
                }
            }
            foreach (var kv in groups)
            {
                var mesh = Own(new Mesh { name = $"look-{layer}-{kv.Key}", indexFormat = IndexFormat.UInt32 });
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var go = new GameObject($"{layer}-{kv.Key}", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = MaterialFor(kv.Key.Substring(kv.Key.IndexOf('/') + 1));
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            return groups.Count;
        }

        void OnDestroy()
        {
            foreach (var m in materials.Values) Kill(m);
            foreach (var parts in shapes.Values) foreach (var p in parts) Kill(p.Mesh);
            foreach (var m in owned) Kill(m);
            materials.Clear(); shapes.Clear(); owned.Clear();
        }

        static void Kill(UnityEngine.Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
    }
}
