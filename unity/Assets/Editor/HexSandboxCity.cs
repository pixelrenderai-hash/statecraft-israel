#nullable enable
using System;
using System.Collections.Generic;
using Statecraft.Game.Map;
using UnityEngine;

/// <summary>Sandbox city tiles (Civ-style: a city is a cluster of hexes, each filled edge to edge). A core or quarter hex is
/// a street grid clipped to the inset hexagon, each block split into lots with one building box per lot (and a terracotta
/// roof box where the style has tiled roofs), denser and taller toward the core; the core keeps a clear disc for the Tripo
/// hero building (BuildingPreview). Styles: white_city (plaster Bauhaus grid, palm boulevard), jaffa (dense ochre old town,
/// no straight grid), haifa (stone and red roofs on the Carmel slope, warehouses by the water), jerusalem (walled stone old
/// city in the core, stone quarters), negev (low ochre Ottoman grid, Bedouin tents outside), stone_village, moshava and
/// kibbutz (villages). Box sizes are diorama metres, like MapLookTowns'. Placed with matrices of its own (not TryPlant).</summary>
public static class HexSandboxCity
{
    const float InsetShare = 0.06f, StreetM = 90f, SinkM = 20f;

    sealed class Look
    {
        public float BlockM, MinH, MaxH, MaxSlope, Fill, Jitter; public string Body, Alt; public bool Roofs, Grid;
        public Look(float blockM, float minH, float maxH, float maxSlope, float fill, float jitter, string body, string alt, bool roofs, bool grid)
        { BlockM = blockM; MinH = minH; MaxH = maxH; MaxSlope = maxSlope; Fill = fill; Jitter = jitter; Body = body; Alt = alt; Roofs = roofs; Grid = grid; }
    }

    static Look LookOf(string style) => style switch
    {
        "white_city" => new Look(640f, 140f, 330f, 20f, 0.82f, 4f, "block_plaster", "block_plaster", false, true),
        "jaffa" => new Look(420f, 110f, 220f, 22f, 0.92f, 25f, "block_ochre", "block_stone", false, false),
        "haifa" => new Look(520f, 120f, 260f, 30f, 0.78f, 10f, "block_stone", "block_plaster", true, true),
        "jerusalem" => new Look(500f, 120f, 260f, 26f, 0.85f, 8f, "block_stone", "block_stone", false, true),
        "negev" => new Look(700f, 90f, 150f, 18f, 0.55f, 3f, "block_ochre", "block_plaster", false, true),
        "stone_village" => new Look(420f, 90f, 170f, 26f, 0.7f, 30f, "block_stone", "block_ochre", false, false),
        _ => new Look(620f, 90f, 160f, 20f, 0.6f, 6f, "block_plaster", "block_plaster", true, true),   // moshava, kibbutz
    };

    public static List<HexSandboxPlanting.Plant> Plan(HexSandboxHex.Tile tile, GeoFrame frame, float hexSizeM,
        Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg, Func<Vector3, bool> isWater)
    {
        var plants = new List<HexSandboxPlanting.Plant>();
        if (tile.Settlement == null) return plants;
        var s = tile.Settlement;
        var look = LookOf(tile.Style);
        var centre = HexSandboxHex.Centre(tile.Hex, frame);
        var home = frame.ToWorld(s.Lon, s.Lat, 0f);
        float inset = hexSizeM * InsetShare, hero = SettlementPlacer.MarkerHeightM(s.Type);
        var rng = new System.Random(MapLookTowns.StableHash(s.Id) ^ (tile.Hex.q * 73856093) ^ (tile.Hex.r * 19349663));
        // One grid orientation per settlement, so the quarters' streets line up with the core's.
        float yaw = tile.Style == "white_city" ? -20f : (float)(new System.Random(MapLookTowns.StableHash(s.Id)).NextDouble() * 90f);
        var turn = Quaternion.Euler(0f, yaw, 0f);
        bool core = tile.Role == HexSandboxHex.Role.CityCore, village = tile.Role == HexSandboxHex.Role.Village;
        float reach = village ? 0.45f * hexSizeM : hexSizeM;   // a village is a cluster, not a full hex of streets
        int cells = Mathf.CeilToInt(reach / look.BlockM) + 1;
        // The grid is anchored on the settlement; walk the cells around this tile's own centre.
        var local = Quaternion.Inverse(turn) * (centre - home);
        int i0 = Mathf.RoundToInt(local.x / look.BlockM), j0 = Mathf.RoundToInt(local.z / look.BlockM);

        for (int i = i0 - cells; i <= i0 + cells; i++)
            for (int j = j0 - cells; j <= j0 + cells; j++)
            {
                var cell = home + turn * new Vector3(i * look.BlockM, 0f, j * look.BlockM);
                // Tile-local cells only: the hex owns the blocks whose centre falls inside it.
                var mid = HexSandboxHex.InHex(cell, centre, hexSizeM, inset) ? cell : (Vector3?)null;
                if (mid == null) continue;
                float fromHome = new Vector2(cell.x - home.x, cell.z - home.z).magnitude;
                if (village && fromHome > reach) continue;
                if ((core || village) && fromHome < hero * 0.9f) continue;   // the hero prototype's ground
                // Density and height fall off from the city's centre; the outer quarter thins to gardens.
                float t = Mathf.Clamp01(fromHome / (2.4f * hexSizeM));
                if (rng.NextDouble() > look.Fill * (1f - 0.55f * t)) { Garden(plants, cell, look, rng, frame, groundY, slopeDeg, isWater); continue; }
                Street(plants, cell, turn, look.BlockM, true, groundY, isWater);
                Street(plants, cell, turn, look.BlockM, false, groundY, isWater);
                Block(plants, cell, turn, look, tile.Style, 1f - t, rng, groundY, slopeDeg, isWater);
            }
        if (tile.Style == "jerusalem" && core) OldCityWall(plants, home, hero * 2.1f, groundY, isWater);
        if (tile.Style == "negev") Tents(plants, centre, hexSizeM, inset, rng, groundY, slopeDeg, isWater);
        return plants;
    }

    /// <summary>Lots of a block (2 x 2 or 3 x 3 by style), one building each, inside the block minus the street.</summary>
    static void Block(List<HexSandboxPlanting.Plant> plants, Vector3 cell, Quaternion turn, Look look, string style, float urban,
        System.Random rng, Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg, Func<Vector3, bool> isWater)
    {
        int n = look.BlockM < 500f ? 2 : 3;
        float lot = (look.BlockM - StreetM) / n;
        for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                if (rng.NextDouble() < 0.12) continue;   // a yard or a gap
                var off = new Vector3((a - (n - 1) * 0.5f) * lot, 0f, (b - (n - 1) * 0.5f) * lot);
                var p = cell + turn * off + HexSandboxPlanting.Disc(rng, lot * 0.08f);
                float w = lot * (0.62f + 0.3f * (float)rng.NextDouble()), d = lot * (0.62f + 0.3f * (float)rng.NextDouble());
                var rot = turn * Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 2f * look.Jitter, 0f);
                float h = Mathf.Lerp(look.MinH, look.MaxH, Mathf.Clamp01(urban * 0.8f + 0.35f * (float)rng.NextDouble()));
                string body = rng.NextDouble() < 0.75 ? look.Body : look.Alt;
                bool port = style == "haifa" && isWater(p + turn * new Vector3(0f, 0f, -look.BlockM));
                // A style with a building kit (Resources/HexSandbox/kit_<style>[_n]) places one kit building per lot, turned a
                // quarter at random, sized to the lot; HexSandboxPlanting.Emit picks the variant. Else the plain box below.
                string kit = port ? "kit_port" : "kit_" + style;
                if (HasKit(kit))
                {
                    float size = lot * (0.8f + 0.12f * (float)rng.NextDouble());
                    var quarter = turn * Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
                    Seat(plants, kit, p, quarter, size, look.MaxSlope, groundY, slopeDeg, isWater);
                    continue;
                }
                if (port) { w *= 1.6f; d *= 0.8f; h *= 0.6f; body = "block_ochre"; }   // port sheds
                if (!Box(plants, body, p, rot, w, d, h, look.MaxSlope, groundY, slopeDeg, isWater, out float top)) continue;
                if (look.Roofs && body != "block_ochre")
                    plants.Add(new HexSandboxPlanting.Plant("roof_terracotta", Matrix4x4.TRS(new Vector3(p.x, top - 4f, p.z), rot, new Vector3(w * 1.04f, h * 0.22f, d * 1.04f))));
            }
    }

    static readonly Dictionary<string, bool> kits = new Dictionary<string, bool>(StringComparer.Ordinal);
    static bool HasKit(string species) =>
        kits.TryGetValue(species, out var has) ? has : kits[species] = Resources.Load<GameObject>(HexSandboxPlanting.ModelsFolder + "/" + species) != null;

    /// <summary>A kit building (unit footprint, its own height ratio) scaled to <paramref name="size"/> and seated a little
    /// below its lowest corner; skipped on water, steep or uneven ground.</summary>
    static void Seat(List<HexSandboxPlanting.Plant> plants, string species, Vector3 p, Quaternion rot, float size, float maxSlope,
        Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg, Func<Vector3, bool> isWater)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var c in new[] { new Vector3(size, 0f, size), new Vector3(-size, 0f, size), new Vector3(size, 0f, -size), new Vector3(-size, 0f, -size), Vector3.zero })
        {
            var q = p + rot * (c * 0.5f);
            if (isWater(q) || slopeDeg(q) > maxSlope) return;
            float y = groundY(q); lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
        }
        if (hi - lo > size * 0.35f) return;
        plants.Add(new HexSandboxPlanting.Plant(species, Matrix4x4.TRS(new Vector3(p.x, lo - 8f, p.z), rot, Vector3.one * size)));
    }

    /// <summary>A building box seated below its lowest corner (MapLookTowns' rule); false on water, steep or uneven ground.</summary>
    static bool Box(List<HexSandboxPlanting.Plant> plants, string species, Vector3 p, Quaternion rot, float w, float d, float h, float maxSlope,
        Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg, Func<Vector3, bool> isWater, out float top)
    {
        top = 0f;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var c in new[] { new Vector3(w, 0f, d), new Vector3(-w, 0f, d), new Vector3(w, 0f, -d), new Vector3(-w, 0f, -d), Vector3.zero })
        {
            var q = p + rot * (c * 0.5f);
            if (isWater(q) || slopeDeg(q) > maxSlope) return false;
            float y = groundY(q); lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
        }
        if (hi - lo > h * 0.6f) return false;
        top = hi + h;
        plants.Add(new HexSandboxPlanting.Plant(species, Matrix4x4.TRS(new Vector3(p.x, lo - SinkM, p.z), rot, new Vector3(w, top - lo + SinkM, d))));
        return true;
    }

    /// <summary>The block's south (or west) street as a thin flat strip, skipped where it would cross water or float.</summary>
    static void Street(List<HexSandboxPlanting.Plant> plants, Vector3 cell, Quaternion turn, float blockM, bool alongX,
        Func<Vector3, float> groundY, Func<Vector3, bool> isWater)
    {
        var p = cell + turn * (alongX ? new Vector3(0f, 0f, -blockM * 0.5f) : new Vector3(-blockM * 0.5f, 0f, 0f));
        float w = alongX ? blockM : StreetM * 0.7f, d = alongX ? StreetM * 0.7f : blockM;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var c in new[] { new Vector3(w, 0f, d), new Vector3(-w, 0f, d), new Vector3(w, 0f, -d), new Vector3(-w, 0f, -d) })
        {
            var q = p + turn * (c * 0.5f);
            if (isWater(q)) return;
            float y = groundY(q); lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
        }
        if (hi - lo > 60f) return;
        plants.Add(new HexSandboxPlanting.Plant("street", Matrix4x4.TRS(new Vector3(p.x, lo - 10f, p.z), turn, new Vector3(w, hi - lo + 13f, d))));
    }

    /// <summary>A left-out block: a few garden trees (palms in the White City, olives elsewhere).</summary>
    static void Garden(List<HexSandboxPlanting.Plant> plants, Vector3 cell, Look look, System.Random rng, GeoFrame frame,
        Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg, Func<Vector3, bool> isWater)
    {
        var species = HexSandboxPlanting.All[look.Body == "block_plaster" && look.Grid ? "date_palm" : "olive"];
        var none = Array.Empty<(Vector3, float)>();
        for (int k = 0; k < 3; k++)
            if (HexSandboxPlanting.TryPlant(species, cell + HexSandboxPlanting.Disc(rng, look.BlockM * 0.35f), rng, frame, groundY, slopeDeg, none, isWater, out var m))
                plants.Add(new HexSandboxPlanting.Plant(species.Name, m));
    }

    /// <summary>The Old City's wall: a square of wall segments about the hero, a little rotated, as the Ottoman wall reads.</summary>
    static void OldCityWall(List<HexSandboxPlanting.Plant> plants, Vector3 home, float side, Func<Vector3, float> groundY, Func<Vector3, bool> isWater)
    {
        var turn = Quaternion.Euler(0f, 8f, 0f);
        const int perSide = 8;
        float seg = side / perSide;
        for (int e = 0; e < 4; e++)
            for (int k = 0; k < perSide; k++)
            {
                float u = (k + 0.5f) * seg - side * 0.5f;
                var local = e switch { 0 => new Vector3(u, 0f, side * 0.5f), 1 => new Vector3(side * 0.5f, 0f, -u), 2 => new Vector3(-u, 0f, -side * 0.5f), _ => new Vector3(-side * 0.5f, 0f, u) };
                var p = home + turn * local;
                if (isWater(p)) continue;
                var rot = turn * Quaternion.Euler(0f, e % 2 == 0 ? 0f : 90f, 0f);
                plants.Add(new HexSandboxPlanting.Plant("wall_old_city", Matrix4x4.TRS(new Vector3(p.x, groundY(p) - SinkM, p.z), rot, new Vector3(seg * 1.02f, 140f, 70f))));
            }
    }

    /// <summary>Bedouin encampments on the town's outskirts: low dark tent boxes in loose groups of 3 to 6.</summary>
    static void Tents(List<HexSandboxPlanting.Plant> plants, Vector3 centre, float hexSizeM, float inset, System.Random rng,
        Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg, Func<Vector3, bool> isWater)
    {
        for (int g = 0; g < 4; g++)
        {
            var camp = HexSandboxHex.RandomIn(rng, centre, hexSizeM, inset);
            if ((camp - centre).magnitude < hexSizeM * 0.5f) continue;   // outside the town
            int n = rng.Next(3, 7);
            for (int k = 0; k < n; k++)
            {
                var p = camp + HexSandboxPlanting.Disc(rng, 420f);
                var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                Box(plants, "tent_bedouin", p, rot, 200f, 110f, 55f, 18f, groundY, slopeDeg, isWater, out _);
            }
        }
    }
}
