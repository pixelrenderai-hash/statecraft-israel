#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Statecraft.Game.Map.Kits;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Statecraft.HexTiles.Capture
{
    /// <summary>PROPOSAL (lane A, 19 October 2026). Every level of detail Unity's generator makes for each town model, run
    /// as TownModelAssets.Create runs it (MeshLodUtility.GenerateMeshLods(copy, -1)), measured for the hex tiles: its
    /// triangles, whether it keeps the model's shape (TownModelAssets.KeepsShape: each side within 0.2 of the full mesh's)
    /// and its see-through share (HexMeshCheck, on the level extracted as TownModelAssets.Bake does, base put back). The
    /// lightest level that keeps its shape and is within <see cref="Tolerance"/> is the model's "lightest whole level".
    /// KeepsShape and Bake are mirrored here: they live in Assembly-CSharp-Editor, which this assembly cannot reference.
    /// Measure writes the table and the tile triangles under each level policy; Bake saves the lightest whole levels to
    /// Resources/HexTiles/TownLevels.asset (HexTownLevels) and measures what it would add to a build. TownModelSet is
    /// only read.
    /// Unity -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity
    ///   -executeMethod Statecraft.HexTiles.Capture.HexTownLevelsBake.Measure | .Bake</summary>
    public static class HexTownLevelsBake
    {
        const float Tolerance = 0.005f;
        public const string AssetPath = HexModels.TownLevelsPath;
        static string Report(string file) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "asset-staging/ui-local/hex-tiles/round2/buildings", file));

        /// <summary>A measured level: the generator's index, its triangles, whether it keeps the shape, and its see-through.</summary>
        public readonly struct Level
        {
            public readonly int Index, Triangles; public readonly bool Shape; public readonly float SeeThrough;
            public Level(int index, int triangles, bool shape, float seeThrough) { Index = index; Triangles = triangles; Shape = shape; SeeThrough = seeThrough; }
            public bool Whole => Shape && SeeThrough <= Tolerance;
        }

        [MenuItem("Statecraft/Hex Tiles/Measure Town Model Levels")]
        public static void Measure() => Guard(() =>
        {
            var set = Set();
            var lines = new List<string>
            {
                "Town model levels of detail (MeshLodUtility.GenerateMeshLods(copy, -1), as TownModelAssets.Create) for the hex tiles.",
                "Each level extracted as TownModelAssets.Bake (base put back). shape: TownModelAssets.KeepsShape. seeThru: HexMeshCheck",
                $"(rays that meet no front face; whole within {Tolerance:P1}). * = the lightest whole level; set = TownModelSet's mid level.",
                "",
            };
            var chosen = new int[TownModels.Ids.Length]; var chosenTris = new int[TownModels.Ids.Length]; var floorTris = new int[TownModels.Ids.Length];
            for (int i = 0; i < TownModels.Ids.Length; i++)
            {
                var levels = Levels(set, i, out var midIndex);
                var pick = Lightest(levels);
                chosen[i] = pick.Index; chosenTris[i] = pick.Triangles; floorTris[i] = Lightest(levels, Floor).Triangles;
                lines.Add($"{TownModels.Ids[i]} (set mid = level {midIndex})");
                foreach (var l in levels)
                    lines.Add($"  {(l.Index == pick.Index ? "*" : " ")} level {l.Index,2}: {l.Triangles,6} tris, shape {(l.Shape ? "yes" : "NO "),3}, {l.SeeThrough,7:P2} see-through{(l.Index == midIndex ? "  (set mid)" : "")}");
            }
            lines.Add("");
            lines.Add("-- lightest whole level per model --");
            for (int i = 0; i < TownModels.Ids.Length; i++) lines.Add($"{TownModels.Ids[i],-38} level {chosen[i],2}: {chosenTris[i],6} tris; with a {Floor}-triangle floor {floorTris[i],6}");
            lines.Add("");
            lines.AddRange(Tiles(set, chosenTris, floorTris));
            File.WriteAllLines(Report("town-levels.txt"), lines);
            Debug.Log("HEX TOWN LEVELS -> " + Report("town-levels.txt") + "\n" + string.Join("\n", lines));
        });

        [MenuItem("Statecraft/Hex Tiles/Bake Town Levels (proposal)")]
        public static void Bake() => Guard(() =>
        {
            var set = Set();
            AssetDatabase.DeleteAsset(AssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Application.dataPath, "..", AssetPath))!);
            var asset = ScriptableObject.CreateInstance<HexTownLevels>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            int n = TownModels.Ids.Length;
            asset.meshes = new Mesh?[n]; asset.levels = new int[n];
            var lines = new List<string>();
            // The levels the aliases name ("town:<folder>@<n>"); -floor and -lightest bake those policies instead.
            bool floor = Environment.GetCommandLineArgs().Contains("-floor"), lightestOnly = Environment.GetCommandLineArgs().Contains("-lightest");
            var named = new Dictionary<int, int>();
            foreach (var (_, town, _) in HexModels.TownAliases)
            {
                var (index, level) = HexModels.ParseTown(town);
                if (!int.TryParse(level, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var l)) continue;
                if (named.TryGetValue(index, out var other) && other != l) throw new InvalidDataException($"{TownModels.Ids[index]}: aliases name levels {other} and {l}; HexTownLevels holds one");
                named[index] = l;
            }
            for (int i = 0; i < n; i++)
            {
                var levels = Levels(set, i, out _);
                var pick = floor || lightestOnly ? Lightest(levels, floor ? Floor : 0)
                    : named.TryGetValue(i, out var want) ? levels.First(x => x.Index == want) : levels[0];
                if (!pick.Whole) lines.Add($"WARNING {TownModels.Ids[i]} level {pick.Index}: not whole ({pick.SeeThrough:P2}, shape {pick.Shape})");
                asset.levels[i] = pick.Index;
                if (pick.Index == 0) { lines.Add($"{TownModels.Ids[i]}: not baked (full mesh or the set's mid level)"); continue; }
                var copy = Object.Instantiate(set.meshes[i]);
                MeshLodUtility.GenerateMeshLods(copy, -1);
                var m = Extract(copy, pick.Index, set.meshes[i].bounds.min.z, TownModels.Ids[i] + "_hex");
                Object.DestroyImmediate(copy);
                MeshUtility.SetMeshCompression(m, ModelImporterMeshCompression.Medium);
                AssetDatabase.AddObjectToAsset(m, asset);
                var so = new SerializedObject(m);
                so.FindProperty("m_IsReadable").boolValue = false;   // no CPU copy in a build, as TownModelAssets
                so.ApplyModifiedPropertiesWithoutUndo();
                asset.meshes[i] = m;
                lines.Add($"{TownModels.Ids[i]}: level {pick.Index}, {pick.Triangles} tris, {m.vertexCount} vertices");
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            long disk = new FileInfo(Path.Combine(Application.dataPath, "..", AssetPath)).Length;
            // What it adds to a build, approximately: the asset's objects in Unity's binary serialized form (meshes with
            // their compression applied), written to Logs; the WebGL build then Brotli- or gzip-compresses its data file.
            // (A real asset bundle build needs a player script compile, which fails in this checkout on a package.)
            var bin = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", floor ? "townlevels-floor.bin" : "townlevels.bin"));
            var objects = AssetDatabase.LoadAllAssetsAtPath(AssetPath).OfType<Mesh>();
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(objects.Select(o => Object.Instantiate(o)).ToArray(), bin, false);
            lines.Insert(0, $"{AssetPath}: {disk} bytes on disk (text); {new FileInfo(bin).Length} bytes binary serialized ({bin})");
            // For scale, the same measure of what ships today: the set's baked mid, far and farthest levels.
            var shipped = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "townmodelset-levels.bin"));
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(
                set.mid.Concat(set.far).Concat(set.farthest).Select(m => (Object)Object.Instantiate(m)).ToArray(), shipped, false);
            lines.Insert(1, $"for scale, TownModelSet's mid+far+farthest levels: {new FileInfo(shipped).Length} bytes binary serialized ({shipped})");
            File.WriteAllLines(Report(floor ? "town-levels-bake-floor.txt" : "town-levels-bake.txt"), lines);
            Debug.Log("HEX TOWN LEVELS BAKE " + string.Join(" | ", lines));
        });

        static TownModelSet Set() =>
            AssetDatabase.LoadAssetAtPath<TownModelSet>(TownModels.SetPath) is TownModelSet s && s.Missing() == null ? s : throw new InvalidDataException("no complete " + TownModels.SetPath);

        static void Guard(Action run)
        {
            try { run(); }
            catch (Exception e)
            {
                Debug.LogError("HEX TOWN LEVELS FAILED " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        /// <summary>Every level of model <paramref name="i"/>, measured; <paramref name="midIndex"/> is the one TownModelSet
        /// keeps as mid (TownModelAssets.Nearest to TownModelSet.MidTriangles among the levels that keep the shape).</summary>
        public static List<Level> Levels(TownModelSet set, int i, out int midIndex)
        {
            var full = set.meshes[i];
            var copy = Object.Instantiate(full);
            try
            {
                MeshLodUtility.GenerateMeshLods(copy, -1);
                var levels = new List<Level> { Measured(0, full, full.bounds) };
                for (int l = 1; l < copy.lodCount; l++)
                {
                    var m = Extract(copy, l, full.bounds.min.z, "level");
                    try { levels.Add(Measured(l, m, full.bounds, LevelBounds(copy, l))); }
                    finally { Object.DestroyImmediate(m); }
                }
                midIndex = levels.Where(x => x.Index > 0 && x.Shape)
                    .OrderBy(x => Mathf.Abs(x.Triangles - TownModelSet.MidTriangles)).ThenBy(x => x.Index).Select(x => x.Index).DefaultIfEmpty(-1).First();
                return levels;
            }
            finally { Object.DestroyImmediate(copy); }
        }

        static Level Measured(int index, Mesh m, Bounds full, Bounds? shape = null) =>
            new Level(index, HexModels.IndexTriangles(m), KeepsShape(shape ?? m.bounds, full), HexMeshCheck.Of(new[] { (m, HexModels.TownTurn) }).SeeThroughShare);

        /// <summary>A look floor for the "floor" policy: no level lighter than the set's mid target (a 64-triangle level is
        /// whole but a box).</summary>
        const int Floor = 700;

        /// <summary>The lightest whole level of at least <paramref name="floor"/> triangles (the full mesh, level 0, when no
        /// lighter one is).</summary>
        static Level Lightest(List<Level> levels, int floor = 0) =>
            levels.Where(l => l.Whole && (l.Triangles >= floor || l.Index == 0)).OrderBy(l => l.Triangles).DefaultIfEmpty(levels[0]).First();

        /// <summary>TownModelAssets.KeepsShape: width, depth and height within 0.2 (a fifth of a Tripo model's unit size).</summary>
        static bool KeepsShape(Bounds level, Bounds full) =>
            Mathf.Abs(level.size.x - full.size.x) <= 0.2f && Mathf.Abs(level.size.y - full.size.y) <= 0.2f && Mathf.Abs(level.size.z - full.size.z) <= 0.2f;

        static Bounds LevelBounds(Mesh m, int level)
        {
            var pos = m.vertices; var idx = m.GetIndices(0, level, true);
            var b = new Bounds(pos[idx[0]], Vector3.zero);
            foreach (int v in idx) b.Encapsulate(pos[v]);
            return b;
        }

        /// <summary>TownModelAssets.Bake without the asset: level <paramref name="level"/> as its own mesh (its triangles and
        /// the vertices they use: position, normal, uv0, colour), its lowest point moved to <paramref name="baseZ"/>.</summary>
        static Mesh Extract(Mesh src, int level, float baseZ, string name)
        {
            var idx = src.GetIndices(0, level, true);
            var pos = src.vertices; var nrm = src.normals; var uv = src.uv; var col = src.colors32;
            var map = new Dictionary<int, int>();
            var p = new List<Vector3>(); var nn = new List<Vector3>(); var u = new List<Vector2>(); var c = new List<Color32>();
            var tri = new int[idx.Length];
            for (int k = 0; k < idx.Length; k++)
            {
                int v = idx[k];
                if (!map.TryGetValue(v, out int at))
                {
                    at = p.Count; map[v] = at; p.Add(pos[v]);
                    if (nrm.Length > 0) nn.Add(nrm[v]);
                    if (uv.Length > 0) u.Add(uv[v]);
                    if (col.Length > 0) c.Add(col[v]);
                }
                tri[k] = at;
            }
            float lowest = p.Min(v => v.z);
            for (int k = 0; k < p.Count; k++) p[k] += new Vector3(0f, 0f, baseZ - lowest);
            var m = new Mesh { name = name, indexFormat = p.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(p);
            if (nn.Count > 0) m.SetNormals(nn);
            if (u.Count > 0) m.SetUVs(0, u);
            if (c.Count > 0) m.SetColors(c);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Each building tile's triangles under each level policy: on HexPrototypeTests' hex ((2,-1), flat, seed
        /// 1948), and for the two Tel Aviv tiles also on their israel board hex (the board's surface, as HexBoardCapture's
        /// stats.txt). "hero full": the Tel Aviv landmark (hq_white_city_hero) takes the full mesh, the rest the lightest
        /// whole level; "floor": the lightest whole level of at least <see cref="Floor"/> triangles, hero full.</summary>
        static List<string> Tiles(TownModelSet set, int[] lightest, int[] floor)
        {
            var alias = HexModels.TownAliases.ToDictionary(a => a.id, a => a.town);
            int Index(string town) => HexModels.ParseTown(town).index;
            var lines = new List<string> { "-- triangles per building tile by the level the aliases draw --",
                $"{"tile",-22} {"hex",-11} {"budget",7} {"sandbox",8} {"all full",9} {"set mid",8} {"lightest whole",15} {"hq full",8} {"hero full",10} {"floor",7}" };
            var board = HexBoardCapture.LoadBoard("israel");
            var places = HexPrototypes.All.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(id => (id, q: 2, r: -1, surface: (IHexSurface)new FlatSurface())).ToList();
            foreach (var h in board.Hexes.Values.Where(h => h.prototype == "tel_aviv_city" || h.prototype == "tel_aviv_city_mixed").OrderBy(h => h.prototype, StringComparer.Ordinal))
                places.Add((h.prototype, h.q, h.r, new HexBoardSurface(board)));
            foreach (var (id, q, r, surface) in places)
            {
                var proto = HexPrototypes.Create(id);
                var ctx = new HexTileContext(q, r, surface, 1948);
                proto.Build(ctx);
                if (!ctx.Placements.Any(p => alias.ContainsKey(HexModels.Resolve(p.Model, p.Position)))) continue;
                int Sum(Func<string, int, int> town) => ctx.Placements.Sum(p =>
                {
                    if (p.StandIn == StandIn.Flat) return 12;
                    var resolved = HexModels.Resolve(p.Model, p.Position);
                    return alias.TryGetValue(resolved, out var t) ? town(resolved, Index(t)) : HexModels.Triangles(p.Model, p.StandIn, p.Position);
                });
                int sandbox;
                try
                {
                    HexModels.DrawTownModels = false; HexModels.Clear();
                    sandbox = ctx.Placements.Sum(p => p.StandIn == StandIn.Flat ? 12 : HexModels.Triangles(p.Model, p.StandIn, p.Position));
                }
                finally { HexModels.DrawTownModels = true; HexModels.Clear(); }
                int Full(int i) => HexModels.IndexTriangles(set.meshes[i]);
                bool Hq(string resolved) => resolved.StartsWith("sandbox:hq_", StringComparison.Ordinal);
                bool Hero(string resolved) => resolved == "sandbox:hq_white_city_hero";
                lines.Add($"{id,-22} {$"({q},{r})",-11} {proto.TriangleBudget,7} {sandbox,8} {Sum((_, i) => Full(i)),9} {Sum((_, i) => HexModels.IndexTriangles(set.mid[i])),8} "
                    + $"{Sum((_, i) => lightest[i]),15} {Sum((res, i) => Hq(res) ? Full(i) : lightest[i]),8} {Sum((res, i) => Hero(res) ? Full(i) : lightest[i]),10} "
                    + $"{Sum((res, i) => Hero(res) ? Full(i) : floor[i]),7}");
            }
            return lines;
        }
    }
}
