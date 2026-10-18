#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Statecraft.Game.Map;
using UnityEngine;
using Plant = HexSandboxPlanting.Plant;
using Region = HexSandboxPlanting.Region;
using Role = HexSandboxHex.Role;

/// <summary>Civ-style nature tiles for the sandbox: one generator per role fills its hexagon, inset by a margin (6% of the
/// hex size) so tile edges read at a 60 km camera. Farmland is a patchwork of parcels with hedgerows, Orchard planted rows
/// in blocks with cypress windbreaks, Forest a near-closed canopy with clearings, Scrub shrub patches with outcrops, Desert
/// wadis lined with acacia. Species come from HexSandboxPlanting.All by region; trees go through its TryPlant, fields and
/// terrace walls (exact, aligned boxes) through a private copy of its field checks. Pure and seeded by (q, r).</summary>
public static class HexSandboxTiles
{
    const float InsetShare = 0.06f, FieldGapM = 180f;
    const int FieldCap = 16, HedgeCap = 60;

    /// <summary>Plants per hex: Forest 650, Orchard 450, Farmland 16 fields + 60 hedgerow trees, Scrub 250, Desert 120.</summary>
    public static int Budget(Role r) => r switch
    {
        Role.Forest => 650, Role.Orchard => 450, Role.Farmland => FieldCap + HedgeCap, Role.Scrub => 250, Role.Desert => 120, _ => 0,
    };

    // Seed multipliers per role; 1, 7 and 31 are taken by Planting, NatureRole and the herds.
    static int Salt(Role r) => r switch { Role.Farmland => 11, Role.Orchard => 13, Role.Forest => 17, Role.Scrub => 19, _ => 23 };

    /// <summary>The plants of one nature tile (Farmland, Orchard, Forest, Scrub, Desert); empty for any other role.</summary>
    public static List<Plant> Plan(HexSandboxHex.Tile tile, GeoFrame frame, float hexSizeM,
        Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg,
        IReadOnlyList<(Vector3 at, float radius)> avoid, Func<Vector3, bool> isWater)
    {
        var h = tile.Hex;
        int cap = Budget(tile.Role);
        if (cap == 0 || !h.land) return new List<Plant>();
        var rng = new System.Random(MapLookScatter.Seed * Salt(tile.Role) ^ (h.q * 73856093) ^ (h.r * 19349663));
        var c = new Ctx(rng, frame, HexSandboxHex.Centre(h, frame), hexSizeM, InsetShare * hexSizeM, cap, groundY, slopeDeg, avoid, isWater);
        var region = HexSandboxPlanting.RegionOf(h.lat);
        string biome = h.biome ?? "";
        switch (tile.Role)
        {
            case Role.Farmland: Farmland(c, region); break;
            case Role.Orchard: Orchard(c, region, biome); break;
            case Role.Forest: Forest(c, region); break;
            case Role.Scrub: Scrub(c, region); break;
            case Role.Desert: Desert(c); break;
        }
        return c.Plants;
    }

    // --- Farmland: a patchwork of parcels in rows of varying depth, thin tracks between, hedgerows on some edges ---

    static void Farmland(Ctx c, Region region)
    {
        bool negev = region == Region.Negev;
        float s = c.Size;
        var cells = new List<(float x, float z, float w, float d)>();
        var rows = new List<float>();
        for (float z0 = -s - c.Next(0f, 0.3f) * s; z0 < s;)
        {
            float dz = c.Next(0.30f, 0.40f) * s;
            for (float x0 = -s - c.Next(0f, 0.4f) * s; x0 < s;)
            {
                float dx = c.Next(0.34f, 0.46f) * s;
                cells.Add((x0 + dx * 0.5f, z0 + dz * 0.5f, dx, dz));
                x0 += dx;
            }
            z0 += dz;
            rows.Add(z0);
        }
        int fields = 0;
        string last = "";
        var placed = new List<(float x, float z, float w, float d)>();
        foreach (var (x, z, w, d) in cells.OrderBy(k => k.x * k.x + k.z * k.z).ToList())
        {
            if (fields >= FieldCap) break;
            string sp = FieldSpecies(c, negev, last);
            int n = Parcel(c, sp, x, z, w - FieldGapM, d - FieldGapM, FieldCap - fields);
            if (n > 0) { placed.Add((x, z, w, d)); last = sp; }
            fields += n;
        }

        // Hedgerows in the tracks: a few short ones on parcel sides, then one or two long ones on row boundaries.
        float spacing = negev ? 560f : 280f;
        int trees = 0;
        void Hedge(string sp, Vector3 a, Vector3 b)
        {
            float length = Vector3.Distance(a, b);
            for (float t = 0f; t <= length && trees < HedgeCap; t += spacing)
            {
                if (c.Chance(0.18)) continue;
                var p = Vector3.Lerp(a, b, length > 0f ? t / length : 0f) + HexSandboxPlanting.Disc(c.Rng, 40f);
                if (c.Tree(sp, p)) trees++;
            }
        }
        string HedgeSpecies() => negev ? "acacia" : c.Chance(0.65) ? "cypress" : "olive";
        var sides = placed.Take(10).OrderBy(_ => c.Rng.Next()).Take(negev ? 1 : 3).ToList();
        foreach (var (x, z, w, d) in sides)
            Hedge(HedgeSpecies(), c.Local(x - w * 0.5f, z - d * 0.5f), c.Local(x - w * 0.5f, z + d * 0.5f));
        var lines = rows.Where(zb => Mathf.Abs(zb) < 0.8f * s).OrderBy(_ => c.Rng.Next()).Take(negev ? 1 : 2).ToList();
        foreach (var zb in lines)
            Hedge(HedgeSpecies(), c.Local(-s, zb), c.Local(s, zb));
    }

    static string FieldSpecies(Ctx c, bool negev, string last)
    {
        for (int i = 0; ; i++)
        {
            double u = c.Rng.NextDouble();
            string sp = negev ? (u < 0.65 ? "barley_dry" : "ploughed")
                : u < 0.34 ? "wheat" : u < 0.58 ? "green_field" : u < 0.78 ? "ploughed" : "vegetable_rows";
            if (sp != last || i >= 2) return sp;
        }
    }

    /// <summary>One field in its cell, pulled in and shrunk until its corners sit inside the inset hexagon; one that fails
    /// the water, slope or rise checks is tried again as two halves. Returns the fields placed (at most room).</summary>
    static int Parcel(Ctx c, string sp, float x, float z, float w, float d, int room)
    {
        for (int i = 0; i < 6 && !c.CornersInside(x, z, w, d); i++) { x *= 0.9f; z *= 0.9f; w *= 0.85f; d *= 0.85f; }
        if (w < 500f || d < 500f || !c.CornersInside(x, z, w, d)) return 0;
        if (c.Box(sp, x, z, w, d, 0f)) return 1;
        if (room < 2) return 0;
        int n = 0;
        if (w >= d)
        {
            float half = (w - FieldGapM) * 0.5f, off = (half + FieldGapM) * 0.5f;
            if (half < 400f) return 0;
            if (c.Box(sp, x - off, z, half, d, 0f)) n++;
            if (c.Box(sp, x + off, z, half, d, 0f)) n++;
        }
        else
        {
            float half = (d - FieldGapM) * 0.5f, off = (half + FieldGapM) * 0.5f;
            if (half < 400f) return 0;
            if (c.Box(sp, x, z - off, w, half, 0f)) n++;
            if (c.Box(sp, x, z + off, w, half, 0f)) n++;
        }
        return n;
    }

    // --- Orchard: straight rows in blocks across the hex, cypress windbreaks in the lanes, terrace walls on hills ---

    static void Orchard(Ctx c, Region region, string biome)
    {
        const float RowM = 450f, TreeM = 380f, WindbreakM = 260f;
        float s = c.Size;
        bool hills = biome == "hills" || biome == "mountains", coast = biome == "coastal_plain";
        var edges = new List<float>();
        for (float x = -s + c.Next(1800f, 3000f); x < s - 1200f; x += c.Next(2200f, 3400f)) edges.Add(x);
        var blocks = new string[edges.Count + 1];
        for (int i = 0; i < blocks.Length; i++) blocks[i] = BlockSpecies(c, region, hills, coast);

        int breaks = 0;
        foreach (var e in edges)
        {
            if (breaks >= 3 || !c.Chance(0.75)) continue;
            breaks++;
            for (float z = -s; z <= s; z += WindbreakM) c.Tree("cypress", c.Local(e + c.Next(-20f, 20f), z));
        }

        float wallM = HexSandboxPlanting.All["terrace_wall"].HeightM, x0 = -s + c.Next(0f, TreeM);
        int row = 0;
        for (float z = -s + c.Next(0f, RowM); z <= s; z += RowM, row++)
        {
            if (hills && row % 3 == 2)
            {
                for (float x = -s + c.Next(0f, 400f); x <= s; x += 600f) c.Box("terrace_wall", x, z, 520f, 50f, wallM);
                continue;
            }
            int col = 0;
            for (float x = x0; x <= s; x += TreeM, col++)
            {
                int b = 0;
                float lane = float.MaxValue;
                foreach (var e in edges) { if (x > e) b++; lane = Mathf.Min(lane, Mathf.Abs(x - e)); }
                if (lane < 0.8f * TreeM) continue;
                // Palms are twice an orchard tree's width: plant them in a quincunx.
                if (blocks[b] == "date_palm" && (col + row) % 2 == 1) continue;
                c.Tree(blocks[b], c.Local(x + c.Next(-25f, 25f), z + c.Next(-25f, 25f)));
            }
        }
    }

    static string BlockSpecies(Ctx c, Region region, bool hills, bool coast)
    {
        double u = c.Rng.NextDouble();
        if (region == Region.Negev) return u < 0.5 ? "date_palm" : "olive";
        if (hills) return region == Region.Centre && u < 0.15 ? "carob" : "olive";
        if (coast) return u < 0.75 ? "citrus" : u < 0.9 ? "olive" : "date_palm";
        return u < 0.5 ? "citrus" : "olive";   // valleys
    }

    // --- Forest: a jittered grid of canopy with dropouts and clearings; species in Perlin stands ---

    static void Forest(Ctx c, Region region)
    {
        float s = c.Size, step = region == Region.Negev ? 520f : 330f, drop = c.Next(0.10f, 0.20f);
        var clearings = new List<(Vector3 at, float radius)>();
        int count = c.Rng.Next(2, 4);
        for (int i = 0; i < count; i++) clearings.Add((c.Anywhere(1200f), c.Next(550f, 900f)));
        foreach (var (at, radius) in clearings)
        {
            int rocks = c.Rng.Next(3, 6);
            for (int k = 0; k < rocks; k++) c.Tree("limestone", at + HexSandboxPlanting.Disc(c.Rng, radius * 0.6f));
            for (int k = 0; k < 2; k++) c.Tree("garrigue", at + HexSandboxPlanting.Disc(c.Rng, radius * 0.7f));
        }
        bool InClearing(Vector3 p)
        {
            foreach (var (at, radius) in clearings)
                if (new Vector2(p.x - at.x, p.z - at.z).sqrMagnitude < radius * radius) return true;
            return false;
        }
        float ox = c.Next(0f, 10000f), oz = c.Next(0f, 10000f), x0 = -s + c.Next(0f, step);
        int row = 0;
        for (float z = -s + c.Next(0f, step); z <= s; z += step, row++)
            for (float x = x0 + (row & 1) * 0.5f * step; x <= s; x += step)
            {
                var p = c.Local(x + c.Next(-0.35f, 0.35f) * step, z + c.Next(-0.35f, 0.35f) * step);
                if (c.Chance(drop) || InClearing(p) || !c.Inside(p)) continue;
                float stand = Mathf.PerlinNoise((p.x + ox) / 2600f, (p.z + oz) / 2600f);
                c.Tree(ForestSpecies(c, region, stand), p);
            }
    }

    static string ForestSpecies(Ctx c, Region region, float stand) => region switch
    {
        Region.North => stand < 0.5f ? "tabor_oak" : "aleppo_pine",
        Region.Centre => stand > 0.72f ? (c.Chance(0.5) ? "carob" : "tabor_oak") : "aleppo_pine",
        _ => "acacia",
    };

    // --- Scrub: rock outcrops, shrub patches, scattered single trees, a thin shrub fill ---

    static void Scrub(Ctx c, Region region)
    {
        bool negev = region == Region.Negev;
        string shrub = negev ? "retama_scrub" : "garrigue", rock = negev ? "desert_rock" : "limestone";
        int outcrops = c.Rng.Next(3, 6);
        for (int i = 0; i < outcrops; i++)
        {
            var at = c.Anywhere(600f);
            int k = c.Rng.Next(4, 8);
            for (int j = 0; j < k; j++) c.Tree(rock, at + HexSandboxPlanting.Disc(c.Rng, 380f));
        }
        int patches = c.Rng.Next(7, 10);
        for (int i = 0; i < patches; i++)
        {
            var at = c.Anywhere(500f);
            float radius = c.Next(450f, 800f);
            int k = c.Rng.Next(10, 17);
            for (int j = 0; j < k; j++) c.Tree(shrub, at + HexSandboxPlanting.Disc(c.Rng, radius));
        }
        int singles = c.Rng.Next(18, 31);
        for (int i = 0; i < singles; i++) c.Tree(ScrubTree(c, region), c.Anywhere());
        for (int i = 0; i < 30; i++) c.Tree(shrub, c.Anywhere());
    }

    static string ScrubTree(Ctx c, Region region)
    {
        double u = c.Rng.NextDouble();
        return region switch
        {
            Region.North => u < 0.5 ? "tabor_oak" : u < 0.75 ? "carob" : "olive",
            Region.Centre => u < 0.45 ? "carob" : u < 0.8 ? "olive" : "tabor_oak",
            _ => "acacia",
        };
    }

    // --- Desert: rock clusters, two or three meandering wadis lined with acacia and retama, mostly bare ground ---

    static void Desert(Ctx c)
    {
        float s = c.Size;
        int clusters = c.Rng.Next(3, 5);
        for (int i = 0; i < clusters; i++)
        {
            var at = c.Anywhere(500f);
            int k = c.Rng.Next(3, 6);
            for (int j = 0; j < k; j++) c.Tree("desert_rock", at + HexSandboxPlanting.Disc(c.Rng, 400f));
        }
        int wadis = c.Rng.Next(2, 4);
        float baseAngle = c.Next(0f, Mathf.PI);
        for (int w = 0; w < wadis; w++)
        {
            float angle = baseAngle + w * Mathf.PI / wadis + c.Next(-0.25f, 0.25f);
            var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var side = new Vector3(-dir.z, 0f, dir.x);
            var start = c.Centre + side * (c.Next(-0.5f, 0.5f) * s) - dir * s;
            float length = 2f * s, amp = c.Next(350f, 800f), wave = c.Next(2200f, 3200f), phase = c.Next(0f, 2f * Mathf.PI);
            for (float t = 0f; t <= length; t += 380f)
            {
                float bend = amp * Mathf.Sin(2f * Mathf.PI * t / wave + phase) * Mathf.Sin(Mathf.PI * t / length);
                var p = start + dir * t + side * bend;
                if (!c.Inside(p)) continue;
                if (c.Chance(0.3)) c.Tree("acacia", p + HexSandboxPlanting.Disc(c.Rng, 160f));
                if (c.Chance(0.45)) c.Tree("retama_scrub", p + HexSandboxPlanting.Disc(c.Rng, 260f));
            }
        }
        for (int i = 0; i < 8; i++) c.Tree("retama_scrub", c.Anywhere());
    }

    // --- shared placement ---

    /// <summary>One hex's generator state: the rng, the hexagon (centre, size, inset, one rotation), the cap and the
    /// terrain checks. Every placement stops once the cap is reached.</summary>
    sealed class Ctx
    {
        public readonly List<Plant> Plants = new();
        public readonly System.Random Rng;
        public readonly Vector3 Centre;
        public readonly float Size, Inset;
        public readonly Quaternion Turn;
        readonly int cap;
        readonly GeoFrame frame;
        readonly Func<Vector3, float> groundY, slopeDeg;
        readonly IReadOnlyList<(Vector3 at, float radius)> avoid;
        readonly Func<Vector3, bool> isWater;

        public Ctx(System.Random rng, GeoFrame frame, Vector3 centre, float size, float inset, int cap, Func<Vector3, float> groundY,
            Func<Vector3, float> slopeDeg, IReadOnlyList<(Vector3 at, float radius)> avoid, Func<Vector3, bool> isWater)
        {
            Rng = rng; this.frame = frame; Centre = centre; Size = size; Inset = inset; this.cap = cap;
            this.groundY = groundY; this.slopeDeg = slopeDeg; this.avoid = avoid; this.isWater = isWater;
            Turn = Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f);
        }

        public float Next(float a, float b) => a + (b - a) * (float)Rng.NextDouble();
        public bool Chance(double p) => Rng.NextDouble() < p;
        /// <summary>A point in the hex's own rotated frame (x, z in metres from the centre).</summary>
        public Vector3 Local(float x, float z) => Centre + Turn * new Vector3(x, 0f, z);
        public bool Inside(Vector3 p, float extra = 0f) => HexSandboxHex.InHex(p, Centre, Size, Inset + extra);
        public Vector3 Anywhere(float extra = 0f) => HexSandboxHex.RandomIn(Rng, Centre, Size, Inset + extra);

        public bool CornersInside(float x, float z, float w, float d)
        {
            for (int i = 0; i < 4; i++)
                if (!Inside(Local(x + ((i & 1) == 0 ? -0.5f : 0.5f) * w, z + ((i & 2) == 0 ? -0.5f : 0.5f) * d))) return false;
            return true;
        }

        /// <summary>A tree, shrub or rock through TryPlant, its canopy kept inside the inset hexagon.</summary>
        public bool Tree(string sp, Vector3 p)
        {
            if (Plants.Count >= cap) return false;
            var s = HexSandboxPlanting.All[sp];
            if (!Inside(p, 0.6f * s.WidthM)) return false;
            if (!HexSandboxPlanting.TryPlant(s, p, Rng, frame, groundY, slopeDeg, avoid, isWater, out var m)) return false;
            Plants.Add(new Plant(sp, m));
            return true;
        }

        /// <summary>An exact box in the hex's frame, w along local x and d along local z, with TryPlant's checks: a field
        /// (heightM 0) is flat, sunk FieldDepthM below its lowest corner and refused over MaxFieldRiseM; a wall stands
        /// heightM above its lowest corner and is refused over twice that rise.</summary>
        public bool Box(string sp, float x, float z, float w, float d, float heightM)
        {
            if (Plants.Count >= cap) return false;
            var p = Local(x, z);
            float footprint = new Vector2(w, d).magnitude * 0.5f;
            if (p.x - footprint < 0f || p.z - footprint < 0f || p.x + footprint > frame.WidthM || p.z + footprint > frame.LengthM) return false;
            if (isWater(p) || slopeDeg(p) > MapLookScatter.MaxTreeSlopeDeg) return false;
            foreach (var (at, radius) in avoid)
                if (new Vector2(p.x - at.x, p.z - at.z).sqrMagnitude < (radius + footprint) * (radius + footprint)) return false;
            float y = groundY(p), lo = y, hi = y;
            for (int i = 0; i < 4; i++)
            {
                var corner = Local(x + ((i & 1) == 0 ? -0.5f : 0.5f) * w, z + ((i & 2) == 0 ? -0.5f : 0.5f) * d);
                if (!Inside(corner) || isWater(corner) || slopeDeg(corner) > MapLookScatter.MaxTreeSlopeDeg) return false;
                float cy = groundY(corner); lo = Mathf.Min(lo, cy); hi = Mathf.Max(hi, cy);
            }
            Matrix4x4 m;
            if (heightM <= 0f)
            {
                if (hi - lo > MapLookScatter.MaxFieldRiseM) return false;
                m = Matrix4x4.TRS(new Vector3(p.x, lo - MapLookScatter.FieldDepthM, p.z), Turn, new Vector3(w, hi - lo + MapLookScatter.FieldDepthM + 6f, d));
            }
            else
            {
                if (hi - lo > 2f * MapLookScatter.MaxFieldRiseM) return false;
                m = Matrix4x4.TRS(new Vector3(p.x, lo - 0.1f * heightM, p.z), Turn, new Vector3(w, heightM * 1.1f + hi - lo, d));
            }
            Plants.Add(new Plant(sp, m));
            return true;
        }
    }
}
