#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>Schematic infrastructure, not operational train services: Jaffa–Jerusalem, Lydda–Haifa via Tulkarm,
    /// Lydda–Gaza and the Tel Aviv–Jerusalem road, draped on the terrain. Alignments checked on 12 October 2026 against
    /// Survey of Palestine, April 1946 (Truman Library M700, SourceMap). Rail chronology cross-checked with Israel
    /// Railways' History and Heritage, HeritageSource (the coastal rail opened in 1953, so it is deliberately absent).
    /// These hand-placed points simplify bends at symbolic map scale; no claim of service on 15 May 1948 is made.</summary>
    public static class MapLookLines
    {
        public readonly struct Route
        {
            public readonly string Id, Colour, SourceUrl; public readonly float WidthM; public readonly (double lon, double lat)[] Points;
            public Route(string id, string colour, float widthM, string sourceUrl, params (double, double)[] points)
            { Id = id; Colour = colour; WidthM = widthM; SourceUrl = sourceUrl; Points = points; }
        }

        public const float StepM = 400f, LiftM = 25f, RailWidthM = 140f, RoadWidthM = 190f;
        public const string SourceMap = "https://www.trumanlibrary.gov/maps/m700-map-palestine";
        public const string HeritageSource = "https://rail.co.il/?lan=en&page=StationInTime";

        public static readonly Route[] Routes =
        {
            new Route("rail-jaffa-jerusalem", "rail", RailWidthM, SourceMap, (34.765, 32.054), (34.882, 31.946), (34.872, 31.928), (34.860, 31.875),
                (34.920, 31.820), (34.980, 31.770), (35.040, 31.750), (35.080, 31.730), (35.135, 31.730), (35.225, 31.765)),
            new Route("rail-lydda-haifa", "rail", RailWidthM, SourceMap, (34.882, 31.946), (34.955, 32.095), (34.975, 32.190), (35.020, 32.315),
                (34.920, 32.440), (34.950, 32.520), (34.940, 32.690), (35.005, 32.814)),
            new Route("rail-lydda-gaza", "rail", RailWidthM, SourceMap, (34.882, 31.946), (34.815, 31.898), (34.750, 31.875),
                (34.670, 31.750), (34.585, 31.670), (34.460, 31.510)),
            new Route("road-telaviv-jerusalem", "road", RoadWidthM, SourceMap, (34.780, 32.080), (34.872, 31.928), (34.980, 31.840), (35.010, 31.810), (35.210, 31.780)),
        };

        /// <summary>A strip <paramref name="width"/> wide along <paramref name="path"/>, resampled every StepM, each edge vertex
        /// LiftM above the ground under it. Windings face up (Unity's front face is clockwise seen from above).</summary>
        public static Mesh Ribbon(IReadOnlyList<Vector3> path, float width, Func<Vector3, float> groundY)
        {
            if (path == null || path.Count < 2) throw new ArgumentException("a ribbon needs two distinct points", nameof(path));
            if (!(width > 0f) || float.IsInfinity(width)) throw new ArgumentOutOfRangeException(nameof(width));
            if (groundY == null) throw new ArgumentNullException(nameof(groundY));
            var dense = Densify(path);
            if (dense.Count < 2) throw new ArgumentException("a ribbon needs two distinct points", nameof(path));
            var verts = new List<Vector3>(); var tris = new List<int>();
            for (int i = 0; i < dense.Count; i++)
            {
                var dir = dense[Mathf.Min(i + 1, dense.Count - 1)] - dense[Mathf.Max(i - 1, 0)]; dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = dense[Mathf.Min(i + 1, dense.Count - 1)] - dense[i];
                dir.Normalize();
                var side = new Vector3(-dir.z, 0f, dir.x) * (width * 0.5f);
                Vector3 a = dense[i] - side, b = dense[i] + side;
                a.y = groundY(a) + LiftM; b.y = groundY(b) + LiftM;
                verts.Add(a); verts.Add(b);
                if (i == 0) continue;
                int n = verts.Count;   // previous pair n-4 (a), n-3 (b); this pair n-2 (a), n-1 (b)
                tris.Add(n - 3); tris.Add(n - 1); tris.Add(n - 2);
                tris.Add(n - 3); tris.Add(n - 2); tris.Add(n - 4);
            }
            var m = new Mesh { name = "look-ribbon" };
            m.SetVertices(verts); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static List<Vector3> Densify(IReadOnlyList<Vector3> path)
        {
            var dense = new List<Vector3> { path[0] };
            for (int i = 1; i < path.Count; i++)
            {
                var delta = path[i] - path[i - 1]; delta.y = 0f;
                if (delta.sqrMagnitude < 0.01f) continue;
                int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / StepM));
                for (int s = 1; s <= steps; s++) dense.Add(Vector3.Lerp(path[i - 1], path[i], (float)s / steps));
            }
            return dense;
        }

        public static GameObject Build(MapScene map, MapLookAssets assets, Transform root)
        {
            var go = new GameObject(MapLook.LinesToggle);
            go.transform.SetParent(root, false);
            var t = map.Terrain; var origin = t.transform.position;
            float Ground(Vector3 p) => t.SampleHeight(p) + origin.y;
            foreach (var route in Routes)
            {
                var path = new List<Vector3>();
                foreach (var (lon, lat) in route.Points) path.Add(map.Frame.ToWorld(lon, lat, 0f));
                var line = new GameObject(route.Id, typeof(MeshFilter), typeof(MeshRenderer));
                line.transform.SetParent(go.transform, false);
                var mesh = Ribbon(path, route.WidthM, Ground);
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = go.transform.InverseTransformPoint(vertices[i]);
                mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                line.GetComponent<MeshFilter>().sharedMesh = assets.Own(mesh);
                var r = line.GetComponent<MeshRenderer>();
                r.sharedMaterial = assets.MaterialFor(route.Colour);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            MapLookCoast.Build(map, assets, go.transform);
            return go;
        }
    }
}
