#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Statecraft.Game.Map;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Civ-style tile lines for the sandbox: a faint pale ribbon along every land hex edge, so tiles read without a
/// drawn grid, and a stronger gold ribbon where a city's hexes (core and quarters) meet hexes that are not that city's, as
/// Civ draws territory. Ribbons are draped on the terrain in short segments a few metres up, transparent and unlit, one
/// mesh per kind. Everything made is listed in <paramref name="owned"/>.</summary>
public static class HexSandboxEdges
{
    const int Segments = 12;                       // per edge, ~420 m each on a 5 km hex: follows the relief
    const float LiftM = 12f, FaintM = 55f, BorderM = 115f, BorderInsetShare = 0.035f;
    static readonly Color Faint = new Color(1f, 0.97f, 0.88f, 0.22f), Border = new Color(1f, 0.82f, 0.25f, 0.85f);

    public static void Build(Transform parent, IReadOnlyDictionary<(int q, int r), HexSandboxHex.Tile> tiles, GeoFrame frame, float hexSizeM,
        Func<Vector3, float> groundY, Func<Vector3, bool> isWater, List<Object> owned)
    {
        var faint = new List<(Vector3, Vector3)>();
        var border = new List<(Vector3, Vector3)>();
        foreach (var ((q, r), tile) in tiles)
        {
            if (!tile.Hex.land) continue;
            var centre = HexSandboxHex.Centre(tile.Hex, frame);
            var corners = HexSandboxHex.Corners(centre, hexSizeM);
            string? city = Urban(tile) ? tile.Settlement!.Id : null;
            for (int e = 0; e < 6; e++)
            {
                Vector3 a = corners[e], b = corners[(e + 1) % 6], mid = (a + b) * 0.5f;
                var across = Across(tiles, frame, centre, mid);
                // Each shared land edge once (the smaller key draws it); a coast or patch edge always.
                if (across == null || !across.Hex.land || (q, r).CompareTo((across.Hex.q, across.Hex.r)) < 0) faint.Add((a, b));
                if (city != null && (across == null || !Urban(across) || across.Settlement!.Id != city))
                {
                    // The territory line sits just inside its own city's hexes, as Civ's does.
                    float k = 1f - BorderInsetShare;
                    border.Add((centre + (a - centre) * k, centre + (b - centre) * k));
                }
            }
        }
        Emit(parent, "hex-edges", faint, FaintM, Faint, groundY, isWater, owned);
        Emit(parent, "city-borders", border, BorderM, Border, groundY, isWater, owned);
    }

    static bool Urban(HexSandboxHex.Tile t) => t.Settlement != null && t.Role is HexSandboxHex.Role.CityCore or HexSandboxHex.Role.Quarter;

    /// <summary>The tile across the edge whose midpoint is <paramref name="mid"/>: its centre is the mirror of this one.</summary>
    static HexSandboxHex.Tile? Across(IReadOnlyDictionary<(int q, int r), HexSandboxHex.Tile> tiles, GeoFrame frame, Vector3 centre, Vector3 mid)
    {
        var target = centre + (mid - centre) * 2f;
        HexSandboxHex.Tile? best = null; float bestD = float.MaxValue;
        foreach (var t in tiles.Values)
        {
            float d = (HexSandboxHex.Centre(t.Hex, frame) - target).sqrMagnitude;
            if (d < bestD) { bestD = d; best = t; }
        }
        return best != null && bestD < 1500f * 1500f ? best : null;
    }

    static void Emit(Transform parent, string name, List<(Vector3 a, Vector3 b)> edges, float widthM, Color colour, Func<Vector3, float> groundY,
        Func<Vector3, bool> isWater, List<Object> owned)
    {
        if (edges.Count == 0) return;
        var verts = new List<Vector3>(); var tris = new List<int>();
        foreach (var (a, b) in edges)
        {
            var side = Vector3.Cross(Vector3.up, (b - a).normalized) * (widthM * 0.5f);
            for (int s = 0; s < Segments; s++)
            {
                Vector3 p0 = Vector3.Lerp(a, b, s / (float)Segments), p1 = Vector3.Lerp(a, b, (s + 1) / (float)Segments);
                if (isWater(p0) && isWater(p1)) continue;   // tiles read on land; the sea keeps no grid
                p0.y = groundY(p0) + LiftM; p1.y = groundY(p1) + LiftM;
                int i = verts.Count;
                verts.Add(p0 - side); verts.Add(p0 + side); verts.Add(p1 - side); verts.Add(p1 + side);
                tris.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
            }
        }
        var mesh = new Mesh { name = "sandbox-" + name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts.Select(v => parent.InverseTransformPoint(v)).ToList()); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
        owned.Add(mesh);
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? throw new InvalidOperationException("URP Unlit not found");
        var m = new Material(shader) { name = "sandbox-" + name };
        m.SetColor("_BaseColor", colour);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_Cull", (float)CullMode.Off);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
        owned.Add(m);
        var go = new GameObject("sandbox-" + name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
    }
}
