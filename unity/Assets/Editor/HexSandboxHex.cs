#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Statecraft.Game.Map;
using UnityEngine;

/// <summary>Civ-style hex tiles for the sandbox: every land hex gets one role, and a generator fills that hexagon (inset by
/// a margin so tile edges read) instead of planting groves that spill across hexes. Roles come from the settlements (a city
/// core plus quarters grown hex by hex, a village) and otherwise from biome and region. Hexes are pointy-top (HexGrid):
/// circumradius = hex_size_km, flat sides east and west. Pure and seeded by (q, r), like the rest of the sandbox.</summary>
public static class HexSandboxHex
{
    public enum Role { Water, CityCore, Quarter, Village, Farmland, Orchard, Forest, Scrub, Desert }

    /// <summary>Axial neighbours in turn (BuildingPreviewCapture's order).</summary>
    public static readonly (int q, int r)[] Neighbours = { (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1) };

    /// <summary>Urban hexes per city, core included (the owner's symbolic-scale ruling: settlements sprawl over neighbours).</summary>
    static readonly Dictionary<string, int> CityHexes = new(StringComparer.Ordinal) { ["tel_aviv"] = 4, ["haifa"] = 3, ["jerusalem"] = 3 };

    public sealed class Tile
    {
        public readonly HexJson Hex; public readonly Role Role; public readonly Settlement? Settlement; public readonly string Style;
        public Tile(HexJson hex, Role role, Settlement? settlement, string style) { Hex = hex; Role = role; Settlement = settlement; Style = style; }
    }

    /// <summary>The settlement's look: white_city, jaffa, haifa, jerusalem, negev, stone_village, kibbutz or moshava.</summary>
    public static string StyleOf(Settlement s) => s.Id switch
    {
        "tel_aviv" or "petah_tikva" or "netanya" => "white_city",
        "jaffa" or "acre" or "lod" or "ramla" or "gaza" => "jaffa",
        "haifa" => "haifa",
        "jerusalem" or "safed" or "tiberias" or "nazareth" => "jerusalem",
        "beersheba" or "revivim" => "negev",
        _ => s.Type switch { "arab_town" => "stone_village", "kibbutz" or "moshav" => "kibbutz", _ => "moshava" },
    };

    /// <summary>A role for every hex of <paramref name="patch"/>: settlements first (a city grows its quarters outward over
    /// land neighbours in Neighbours order), then nature by biome and region with a per-hex draw.</summary>
    public static Dictionary<(int q, int r), Tile> Roles(HexFile all, HexFile patch, IEnumerable<Settlement> settlements)
    {
        var byCell = all.hexes.ToDictionary(h => (h.q, h.r));
        var tiles = new Dictionary<(int, int), Tile>();
        foreach (var s in settlements.Where(s => !double.IsNaN(s.Lon) && !double.IsNaN(s.Lat)).OrderBy(s => s.Type == "city" ? 0 : 1).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            var home = HexSandbox.Patch(all, s.Lon, s.Lat, 0).hexes[0];
            if (tiles.ContainsKey((home.q, home.r))) continue;
            string style = StyleOf(s);
            bool urban = s.Type == "city" || s.Type == "town" || s.Type == "arab_town";
            tiles[(home.q, home.r)] = new Tile(home, urban ? Role.CityCore : Role.Village, s, style);
            int want = CityHexes.TryGetValue(s.Id, out var n) ? n : s.Type == "city" ? 3 : 1;
            var frontier = new Queue<HexJson>(new[] { home });
            for (int grown = 1; grown < want && frontier.Count > 0;)
            {
                var from = frontier.Dequeue();
                foreach (var (dq, dr) in Neighbours)
                {
                    if (grown >= want) break;
                    if (!byCell.TryGetValue((from.q + dq, from.r + dr), out var next) || !next.land || tiles.ContainsKey((next.q, next.r))) continue;
                    tiles[(next.q, next.r)] = new Tile(next, Role.Quarter, s, style);
                    frontier.Enqueue(next); grown++;
                }
            }
        }
        foreach (var h in patch.hexes)
        {
            if (tiles.ContainsKey((h.q, h.r))) continue;
            tiles[(h.q, h.r)] = new Tile(h, NatureRole(h), null, "");
        }
        return tiles.Where(kv => patch.hexes.Any(h => h.q == kv.Key.Item1 && h.r == kv.Key.Item2)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public static Role NatureRole(HexJson h)
    {
        if (!h.land) return Role.Water;
        double draw = new System.Random(MapLookScatter.Seed * 7 ^ (h.q * 73856093) ^ (h.r * 19349663)).NextDouble();
        var region = HexSandboxPlanting.RegionOf(h.lat);
        return (region, h.biome) switch
        {
            (_, "desert") => Role.Desert,
            (HexSandboxPlanting.Region.Negev, _) => draw < 0.35 ? Role.Farmland : Role.Scrub,
            (_, "coastal_plain") => draw < 0.55 ? Role.Orchard : Role.Farmland,
            (_, "valley") => draw < 0.75 ? Role.Farmland : Role.Orchard,
            (HexSandboxPlanting.Region.North, _) => draw < 0.6 ? Role.Forest : draw < 0.8 ? Role.Scrub : Role.Orchard,
            _ => draw < 0.45 ? Role.Orchard : draw < 0.75 ? Role.Forest : Role.Scrub,   // centre hills: olive terraces, JNF pine
        };
    }

    // --- hexagon geometry (world x/z; pointy-top) ---

    public static Vector3 Centre(HexJson h, GeoFrame frame) => frame.ToWorld(h.lon, h.lat, 0f);

    /// <summary>Inside the hexagon of circumradius <paramref name="sizeM"/> about <paramref name="centre"/>, shrunk by
    /// <paramref name="insetM"/> on every side. Pointy-top: flat sides at x = +-apothem, the others at 60 and 120 degrees.</summary>
    public static bool InHex(Vector3 p, Vector3 centre, float sizeM, float insetM = 0f)
    {
        float a = sizeM * 0.8660254f - insetM, dx = p.x - centre.x, dz = p.z - centre.z;
        return Mathf.Abs(dx) <= a && Mathf.Abs(dx * 0.5f + dz * 0.8660254f) <= a && Mathf.Abs(-dx * 0.5f + dz * 0.8660254f) <= a;
    }

    /// <summary>A uniform point in the inset hexagon (rejection from its bounding box).</summary>
    public static Vector3 RandomIn(System.Random rng, Vector3 centre, float sizeM, float insetM = 0f)
    {
        for (int i = 0; i < 64; i++)
        {
            var p = centre + new Vector3((float)(rng.NextDouble() * 2 - 1) * sizeM, 0f, (float)(rng.NextDouble() * 2 - 1) * sizeM);
            if (InHex(p, centre, sizeM, insetM)) return p;
        }
        return centre;
    }

    /// <summary>The six corners, starting north and turning clockwise seen from above.</summary>
    public static Vector3[] Corners(Vector3 centre, float sizeM) =>
        Enumerable.Range(0, 6).Select(i => { float a = Mathf.Deg2Rad * (90f - 60f * i); return centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * sizeM; }).ToArray();
}
