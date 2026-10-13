#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>Faceted low-poly meshes for the opt-in map look, built in code (no asset file, download or licence). A shape
    /// is one or more parts, each a mesh and a palette key (MapLookAssets.Palette). Every shape's base is at y = 0, its top
    /// at y = 1 and its footprint centred on the origin within a unit square, so an instance's scale is its size in
    /// metres.</summary>
    public static class MapLookShapes
    {
        public readonly struct Part
        {
            public readonly Mesh Mesh; public readonly string Colour;
            public Part(Mesh mesh, string colour) { Mesh = mesh; Colour = colour; }
        }

        public const string Olive = "olive", Citrus = "citrus", Cypress = "cypress", Palm = "palm", Rock = "rock",
            FieldWheat = "field-wheat", FieldGreen = "field-green", BlockPlaster = "block-plaster", BlockStone = "block-stone",
            Wall = "wall", FishingBoat = "fishing-boat", Steamer = "steamer";
        public static readonly string[] All = { Olive, Citrus, Cypress, Palm, Rock, FieldWheat, FieldGreen, BlockPlaster, BlockStone, Wall, FishingBoat, Steamer };

        /// <summary>New meshes the caller owns (MapLookAssets destroys them with the root).</summary>
        public static Part[] Build(string shape) => shape switch
        {
            Olive => new[] { new Part(Trunk(0.06f, 0.35f), "bark"),
                new Part(new MeshBuilder().Frustum(6, 0.3f, 0.3f, 0.6f, 0.48f, false, true).Frustum(6, 0.6f, 0.48f, 1f, 0.18f, true, false).ToMesh(shape), "olive") },
            Citrus => new[] { new Part(Trunk(0.05f, 0.2f), "bark"),
                new Part(new MeshBuilder().Frustum(7, 0.15f, 0.3f, 0.45f, 0.45f, false, true).Frustum(7, 0.45f, 0.45f, 1f, 0.12f, true, false).ToMesh(shape), "citrus") },
            Cypress => new[] { new Part(Trunk(0.04f, 0.12f), "bark"),
                new Part(new MeshBuilder().Frustum(6, 0.1f, 0.1f, 0.35f, 0.17f, false, true).Frustum(6, 0.35f, 0.17f, 1f, 0f, false, false).ToMesh(shape), "cypress") },
            Palm => new[] { new Part(Trunk(0.03f, 0.85f), "bark"),
                new Part(new MeshBuilder().Frustum(8, 0.8f, 0.04f, 0.88f, 0.42f, false, true).Frustum(8, 0.88f, 0.42f, 1f, 0.05f, true, false).ToMesh(shape), "palm") },
            Rock => new[] { new Part(new MeshBuilder().Frustum(5, 0f, 0.5f, 1f, 0.28f, true, false).ToMesh(shape), "rock") },
            FieldWheat => new[] { new Part(UnitBox(shape), "wheat") },
            FieldGreen => new[] { new Part(UnitBox(shape), "green") },
            BlockPlaster => new[] { new Part(UnitBox(shape), "plaster") },
            BlockStone => new[] { new Part(UnitBox(shape), "stone") },
            Wall => new[] { new Part(UnitBox(shape), "stone") },
            FishingBoat => new[] { new Part(Hull(shape), "bark"),
                new Part(new MeshBuilder().Box(-0.12f, 0.3f, -0.24f, 0.12f, 0.65f, 0.08f).Box(-0.015f, 0.65f, 0f, 0.015f, 1f, 0.03f).ToMesh(shape + "-deck"), "plaster") },
            Steamer => new[] { new Part(Hull(shape), "hull"),
                new Part(new MeshBuilder().Box(-0.15f, 0.3f, -0.3f, 0.15f, 0.68f, 0.22f).Box(-0.12f, 0.68f, -0.18f, 0.12f, 0.8f, 0.08f).ToMesh(shape + "-deck"), "plaster"),
                new Part(new MeshBuilder().Frustum(6, 0.8f, 0.065f, 1f, 0.065f, true, false).ToMesh(shape + "-funnel"), "rail") },
            _ => throw new ArgumentException($"unknown shape '{shape}'", nameof(shape)),
        };

        static Mesh Trunk(float radius, float height) => new MeshBuilder().Frustum(5, 0f, radius, height, radius * 0.8f, false, false).ToMesh("trunk");

        /// <summary>A 1 x 1 x 1 box: a four-sided frustum turned 45 degrees, half-diagonal 0.7071.</summary>
        static Mesh UnitBox(string name) => new MeshBuilder().Frustum(4, 0f, 0.70710678f, 1f, 0.70710678f, true, true, 45f).ToMesh(name);

        static Mesh Hull(string name)
        {
            var mesh = new MeshBuilder().Frustum(6, 0f, 0.32f, 0.3f, 0.5f, true, true, 30f).ToMesh(name + "-hull");
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i].x *= 0.5f;
            mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Unshared vertices per triangle, so RecalculateNormals gives flat facets. Unity's front face is clockwise
        /// as seen by the viewer; the windings below were derived for a viewer outside, above (top cap) and below (bottom
        /// cap), and SideFacesPointOutward checks them.</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<int> t = new List<int>();

            public MeshBuilder Box(float x0, float y0, float z0, float x1, float y1, float z1)
            {
                Vector3 a = new Vector3(x0, y0, z0), b = new Vector3(x1, y0, z0), c = new Vector3(x1, y0, z1), d = new Vector3(x0, y0, z1);
                Vector3 e = new Vector3(x0, y1, z0), f = new Vector3(x1, y1, z0), g = new Vector3(x1, y1, z1), h = new Vector3(x0, y1, z1);
                Quad(a, b, c, d); Quad(h, g, f, e); Quad(d, c, g, h); Quad(b, a, e, f); Quad(c, b, f, g); Quad(a, d, h, e);
                return this;
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }

            public MeshBuilder Frustum(int sides, float y0, float r0, float y1, float r1, bool capTop, bool capBottom, float startDeg = 0f)
            {
                for (int i = 0; i < sides; i++)
                {
                    float a0 = (startDeg + 360f * i / sides) * Mathf.Deg2Rad, a1 = (startDeg + 360f * (i + 1) / sides) * Mathf.Deg2Rad;
                    Vector3 p00 = At(a0, r0, y0), p01 = At(a1, r0, y0), p10 = At(a0, r1, y1), p11 = At(a1, r1, y1);
                    if (r1 > 0f) Tri(p10, p11, p01);
                    if (r0 > 0f) Tri(p10, p01, p00);
                    if (capTop && r1 > 0f) Tri(new Vector3(0f, y1, 0f), p11, p10);
                    if (capBottom && r0 > 0f) Tri(new Vector3(0f, y0, 0f), p00, p01);
                }
                return this;
            }

            void Tri(Vector3 a, Vector3 b, Vector3 c) { int n = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(n); t.Add(n + 1); t.Add(n + 2); }
            static Vector3 At(float a, float r, float y) => new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = "look-" + name };
                m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }
    }
}
