#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Statecraft.Game.Map.Kits;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Statecraft.HexTiles
{
    /// <summary>Model ids and what they draw. An id is a Resources path, with prefixes for the existing local libraries:
    ///   "tiles:&lt;path&gt;"   Resources/HexTiles/&lt;path&gt; (a contributor's own models, one folder per prototype)
    ///   "sandbox:&lt;name&gt;" Resources/HexSandbox/&lt;name&gt; (EZ-Tree trees, Poly Haven rocks, decimated Tripo building kit)
    ///   "proto:&lt;name&gt;"   Resources/SettlementPrototypes/&lt;name&gt; (Tripo heroes, figures, animals, vehicles)
    ///   "town:&lt;folder&gt;[@mid|@&lt;n&gt;]" a shipped b*-v2 building (TownModels.Ids): TownModelSet's full mesh, its baked
    ///                     mid level, or generated level n from the hex tiles' HexTownLevels (the full mesh while that is
    ///                     not baked); the sandbox building ids draw these instead while the set is found (TownAliases)
    /// A trailing "*" picks among &lt;name&gt;, &lt;name&gt;_2, &lt;name&gt;_3 ... per placement. A missing model draws its stand-in, so a
    /// tile works on a machine without the local (git-ignored, pending-rights) models.</summary>
    public static class HexModels
    {
        public readonly struct Part
        {
            public readonly Mesh Mesh; public readonly int SubMesh; public readonly Material Material; public readonly Matrix4x4 Local;
            public Part(Mesh mesh, int subMesh, Material material, Matrix4x4 local) { Mesh = mesh; SubMesh = subMesh; Material = material; Local = local; }
        }

        public const string TownPrefix = "town:", MidSuffix = "@mid";
        /// <summary>Stands a Z-up town mesh up (mesh z to y, mesh y to -z), as SettlementKits.Fit does.</summary>
        public static readonly Matrix4x4 TownTurn = Matrix4x4.Rotate(Quaternion.Euler(-90f, 0f, 0f));

        const string B01 = TownPrefix + "b01-white-city-v2", B02a = TownPrefix + "b02a-tel-aviv-corner-block-v2",
            B02b = TownPrefix + "b02b-tel-aviv-narrow-block-v2", B03a = TownPrefix + "b03a-jerusalem-arches-v2",
            B03b = TownPrefix + "b03b-jerusalem-external-stair-v2", B03c = TownPrefix + "b03c-jerusalem-plain-v2",
            B04a = TownPrefix + "b04a-haifa-stone-balconies-v2", B04b = TownPrefix + "b04b-haifa-loading-doors-v2",
            B05a = TownPrefix + "b05a-moshava-house-v2", B07 = TownPrefix + "b07-kibbutz-dining-hall-v2",
            B09a = TownPrefix + "b09a-kibbutz-communal-building-v2", B13a = TownPrefix + "b13a-netanya-two-storey-v2",
            B16 = TownPrefix + "b16-farmhouse-v2", B17 = TownPrefix + "b17-stone-courtyard-house-v2",
            B24 = TownPrefix + "b24-agricultural-barn-v2", B28 = TownPrefix + "b28-water-tower-v2", B32 = TownPrefix + "b32-port-cargo-shed-v2";

        /// <summary>The sandbox building ids drawn as the shipped b*-v2 town models when the set is found (Tal, 19 October
        /// 2026: the decimated sandbox kit had lost faces, so the ground showed through walls and windows). Each names its
        /// "town:" id and whether it is the same Tripo model; false is a remap, because its own model (b11, b21) is not in
        /// the set and is not pulled into a build. Levels (the "floor" policy, for Tal to judge from captures; not final):
        /// the Tel Aviv landmark at full; b02a, b03b, b04a and b04b at full, as none of their lighter levels is whole; the
        /// rest at the lightest whole generated level of at least 700 triangles (HexTownLevelsBake's report), the set's
        /// own mid level where it is that one (b02b, b07, b28).</summary>
        public static readonly (string id, string town, bool same)[] TownAliases =
        {
            ("sandbox:hq_white_city_hero", B01, true),
            ("sandbox:hq_white_city", B02a, true),
            ("sandbox:hq_white_city_2", B02b + MidSuffix, true),
            ("sandbox:hq_white_city_3", B13a + "@2", true),
            ("sandbox:kit_white_city", B02a, true),
            ("sandbox:kit_white_city_2", B02b + MidSuffix, true),
            ("sandbox:kit_white_city_3", B13a + "@2", true),
            ("sandbox:kit_white_city_4", B13a + "@2", false),   // b11 shikun block
            ("sandbox:kit_jaffa", B17 + "@2", true),
            ("sandbox:kit_jaffa_2", B03a + "@2", false),   // b21 artisan workshop
            ("sandbox:kit_jaffa_3", B03c + "@2", true),
            ("sandbox:kit_haifa", B04a, true),
            ("sandbox:kit_haifa_2", B04b, true),
            ("sandbox:kit_haifa_3", B05a + "@2", true),
            ("sandbox:kit_jerusalem", B03a + "@2", true),
            ("sandbox:kit_jerusalem_2", B03b, true),
            ("sandbox:kit_jerusalem_3", B03c + "@2", true),
            ("sandbox:kit_jerusalem_4", B17 + "@2", true),
            ("sandbox:kit_negev", B17 + "@2", true),
            ("sandbox:kit_negev_2", B03c + "@2", true),
            ("sandbox:kit_negev_3", B03a + "@2", false),   // b21 artisan workshop
            ("sandbox:kit_moshava", B05a + "@2", true),
            ("sandbox:kit_moshava_2", B16 + "@1", true),
            ("sandbox:kit_moshava_3", B24 + "@2", true),
            ("sandbox:kit_kibbutz", B09a + "@2", true),
            ("sandbox:kit_kibbutz_2", B07 + MidSuffix, true),
            ("sandbox:kit_kibbutz_3", B24 + "@2", true),
            ("sandbox:kit_kibbutz_4", B28 + MidSuffix, true),
            ("sandbox:kit_stone_village", B17 + "@2", true),
            ("sandbox:kit_stone_village_2", B03c + "@2", true),
            ("sandbox:kit_port", B32 + "@2", true),
        };

        /// <summary>Off draws the sandbox prefabs again (a before shot, or a test of the fallback); call
        /// <see cref="Clear"/> after switching.</summary>
        public static bool DrawTownModels = true;

        static readonly Dictionary<string, GameObject?> prefabs = new Dictionary<string, GameObject?>(StringComparer.Ordinal);
        static readonly Dictionary<string, string[]> variants = new Dictionary<string, string[]>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> townOf = TownAliases.ToDictionary(a => a.id, a => a.town, StringComparer.Ordinal);
        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        static TownModelSet? townSet;
        static HexTownLevels? townLevels;
        static Transform? templateRoot;

        /// <summary>Where the editor finds the baked hex levels (a player loads Resources/<see cref="HexTownLevels.ResourcesPath"/>).</summary>
        public const string TownLevelsPath = "Assets/Resources/" + HexTownLevels.ResourcesPath + ".asset";

        /// <summary>Captures only: draws <paramref name="id"/> as <paramref name="town"/> instead of its alias, for an A/B
        /// shot of a remap without editing <see cref="TownAliases"/> (HexModelCapture -remap). Lasts the session.</summary>
        public static void Redirect(string id, string town) { townOf[id] = town; Clear(); }

        public static string PathOf(string id) =>
            id.StartsWith("tiles:", StringComparison.Ordinal) ? "HexTiles/" + id.Substring(6)
            : id.StartsWith("sandbox:", StringComparison.Ordinal) ? "HexSandbox/" + id.Substring(8)
            : id.StartsWith("proto:", StringComparison.Ordinal) ? "SettlementPrototypes/" + id.Substring(6)
            : "HexTiles/" + id;

        /// <summary>What <paramref name="id"/> draws: a town model's template (a "town:" id, or a sandbox id in
        /// <see cref="TownAliases"/>, while the set is found), else its Resources prefab, else null (its stand-in).</summary>
        public static GameObject? Prefab(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            // A cached template is made again if it was destroyed (Unity's fake null); a cached miss stays a miss.
            if (prefabs.TryGetValue(id, out var p) && (ReferenceEquals(p, null) || p != null)) return p;
            bool town = id.StartsWith(TownPrefix, StringComparison.Ordinal), alias = townOf.ContainsKey(id);
            var made = town || alias ? Town(town ? id : townOf[id]) : null;
            if (made != null) return prefabs[id] = made;
            var prefab = town ? null : Resources.Load<GameObject>(PathOf(id));
            // Without the set (a player before the map scene has loaded it), not remembered, so the set is looked for again.
            if (!(DrawTownModels && (town || alias))) prefabs[id] = prefab;
            return prefab;
        }

        /// <summary>Forgets every model looked up, the town set, the baked levels, the templates and the "*" variants.</summary>
        public static void Clear()
        {
            prefabs.Clear(); variants.Clear(); townSet = null; townLevels = null;
            foreach (var t in templates.Values) if (t != null) Object.DestroyImmediate(t);
            templates.Clear();
        }

        /// <summary>The hex tiles' baked levels (HexTownLevels): in the editor the asset, in a player
        /// Resources/HexTiles/TownLevels (for now: where it ships is the owner's call). Null when not baked.</summary>
        public static HexTownLevels? TownLevels()
        {
            if (townLevels != null) return townLevels;
#if UNITY_EDITOR
            townLevels = UnityEditor.AssetDatabase.LoadAssetAtPath<HexTownLevels>(TownLevelsPath);
#endif
            if (townLevels == null) townLevels = Resources.Load<HexTownLevels>(HexTownLevels.ResourcesPath);
            return townLevels;
        }

        /// <summary>A "town:" id's model index (TownModels.Ids) and level: "" (full), "mid" (the set's mid level), or a
        /// generated level's number ("town:&lt;folder&gt;@2"). Index -1 when the folder is not in the set.</summary>
        public static (int index, string level) ParseTown(string town)
        {
            var body = town.Substring(TownPrefix.Length);
            int at = body.IndexOf('@');
            var folder = at < 0 ? body : body.Substring(0, at);
            return (Array.IndexOf(TownModels.Ids, folder), at < 0 ? "" : body.Substring(at + 1));
        }

        /// <summary>The mesh a "town:" id draws, and whether it fell back to the full mesh (a generated level the baked
        /// HexTownLevels does not hold, or no HexTownLevels at all): whole, at full cost.</summary>
        public static (Mesh? mesh, bool fellBack) TownMesh(TownModelSet set, string town)
        {
            var (i, level) = ParseTown(town);
            if (i < 0) return (null, false);
            if (level == "") return (set.meshes[i], false);
            if (level == "mid") return (set.mid[i], false);
            var levels = TownLevels();
            bool held = levels != null && int.TryParse(level, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
                && i < levels.meshes.Length && i < levels.levels.Length && levels.levels[i] == n && levels.meshes[i] != null;
            return held ? (levels!.meshes[i], false) : (set.meshes[i], true);
        }

        /// <summary>The town models' list (TownModelSet, complete): in the editor the asset itself, in a player the one
        /// the map scene loaded (MapScene references it; no Resources folder, so it adds nothing to a build). Null until
        /// found; a miss is not remembered.</summary>
        public static TownModelSet? TownSet()
        {
            if (townSet != null) return townSet;
#if UNITY_EDITOR
            townSet = UnityEditor.AssetDatabase.LoadAssetAtPath<TownModelSet>(TownModels.SetPath);
#endif
            if (townSet == null) townSet = Resources.FindObjectsOfTypeAll<TownModelSet>().FirstOrDefault();
            if (townSet != null && townSet.Missing() != null) townSet = null;
            return townSet;
        }

        /// <summary>The template a "town:" id draws: an object under a hidden, inactive root holding its mesh
        /// (<see cref="TownMesh"/>) and the set's material, stood up from Z-up. The template's own object is active, so a
        /// copy of it (Parts) has renderer bounds; the inactive root keeps the template itself from drawing. Parts puts it
        /// in the unit frame (footprint centred, base at y = 0). A fallback to the full mesh is named so.</summary>
        static GameObject? Town(string town)
        {
            if (!DrawTownModels) return null;
            if (templates.TryGetValue(town, out var made) && made != null) return made;
            var set = TownSet();
            if (set == null) return null;
            var (i, _) = ParseTown(town);
            var (drawn, fellBack) = TownMesh(set, town);
            if (drawn == null) return null;
            if (templateRoot == null)
            {
                var root = new GameObject("HexModels town templates") { hideFlags = HideFlags.HideAndDontSave };
                root.SetActive(false);
                templateRoot = root.transform;
            }
            var go = new GameObject(town.Substring(TownPrefix.Length) + (fellBack ? " (full: not baked)" : "")) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(templateRoot, false);
            var child = new GameObject("mesh", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(go.transform, false);
            child.transform.localRotation = TownTurn.rotation;
            child.GetComponent<MeshFilter>().sharedMesh = drawn;
            child.GetComponent<MeshRenderer>().sharedMaterial = set.materials[i];
            return templates[town] = go;
        }

        /// <summary>The concrete id for a placement: a "*" id picks a present variant by the placement's position.</summary>
        public static string Resolve(string id, Vector3 at)
        {
            if (!id.EndsWith("*", StringComparison.Ordinal)) return id;
            if (!variants.TryGetValue(id, out var names))
            {
                var stem = id.Substring(0, id.Length - 1);
                var list = new List<string>();
                for (int v = 1; v < 32; v++) { var name = v == 1 ? stem : $"{stem}_{v}"; if (Prefab(name) == null) break; list.Add(name); }
                variants[id] = names = list.ToArray();
            }
            if (names.Length == 0) return id.Substring(0, id.Length - 1);
            uint h = unchecked((uint)(Mathf.FloorToInt(at.x) * 73856093 ^ Mathf.FloorToInt(at.z) * 19349663));
            return names[h % (uint)names.Length];
        }

        public static bool Skinned(GameObject prefab) => prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

        /// <summary>The prefab's static meshes in the placement's unit frame (footprint centred, base at y = 0) for
        /// <paramref name="fit"/>: Width and Height scale uniformly by the footprint's larger side or the height, Stretch
        /// by each axis. Skinned prefabs are instantiated instead (HexBoardRenderer).</summary>
        public static Part[] Parts(GameObject prefab, Fit fit)
        {
            var go = Object.Instantiate(prefab);
            try
            {
                var b = Bounds(go);
                var size = new Vector3(Mathf.Max(b.size.x, 1e-4f), Mathf.Max(b.size.y, 1e-4f), Mathf.Max(b.size.z, 1e-4f));
                var s = Normaliser(size, fit);
                var norm = Matrix4x4.Scale(s) * Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z));
                return go.GetComponentsInChildren<MeshFilter>()
                    .Where(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null)
                    .SelectMany(f => f.GetComponent<MeshRenderer>().sharedMaterials.Select((m, i) => (f, m, i)))
                    .Where(x => x.m != null && x.i < x.f.sharedMesh.subMeshCount)
                    .Select(x => new Part(x.f.sharedMesh, x.i, x.m, norm * x.f.transform.localToWorldMatrix)).ToArray();
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static Vector3 Normaliser(Vector3 size, Fit fit) => fit switch
        {
            Fit.Width => Vector3.one / Mathf.Max(size.x, size.z),
            Fit.Height => Vector3.one / size.y,
            _ => new Vector3(1f / size.x, 1f / size.y, 1f / size.z),
        };

        public static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Triangles one placement of <paramref name="id"/> draws (its stand-in's when the model is missing).</summary>
        public static int Triangles(string id, StandIn standIn, Vector3 at)
        {
            var prefab = Prefab(Resolve(id, at));
            if (prefab == null) return StandInTriangles(standIn);
            int t = 0;
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) t += IndexTriangles(f.sharedMesh);
            foreach (var s in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.sharedMesh != null) t += IndexTriangles(s.sharedMesh);
            return t;
        }

        // --- stand-ins: HexStandIns' unit shapes, proportioned per kind ---

        static readonly Dictionary<StandIn, (Mesh mesh, string colour, Matrix4x4 local)[]> standIns = new Dictionary<StandIn, (Mesh, string, Matrix4x4)[]>();

        public static (Mesh mesh, string colour, Matrix4x4 local)[] StandInParts(StandIn kind)
        {
            // Editor scene changes can release procedural meshes while this static cache survives.
            if (standIns.TryGetValue(kind, out var p) && p.All(part => part.mesh != null)) return p;
            (Mesh, string, Matrix4x4)[] From(HexStandIns.Shape shape, Vector3 scale) =>
                HexStandIns.Build(shape).Select(part => (part.mesh, part.colour, Matrix4x4.Scale(scale))).ToArray();
            return standIns[kind] = kind switch
            {
                StandIn.Box => From(HexStandIns.Shape.Box, new Vector3(1f, 0.65f, 1f)),
                StandIn.Tree => From(HexStandIns.Shape.Olive, new Vector3(0.8f, 1f, 0.8f)),
                StandIn.Pine => From(HexStandIns.Shape.Cypress, new Vector3(0.45f, 1f, 0.45f)),
                StandIn.Palm => From(HexStandIns.Shape.Palm, new Vector3(0.6f, 1f, 0.6f)),
                StandIn.Rock => From(HexStandIns.Shape.Rock, new Vector3(1f, 0.6f, 1f)),
                StandIn.Figure => new[] { (Resources.GetBuiltinResource<Mesh>("Capsule.fbx"), "hull", Matrix4x4.Translate(new Vector3(0f, 0.5f, 0f)) * Matrix4x4.Scale(new Vector3(0.3f, 0.5f, 0.3f))) },
                _ => From(HexStandIns.Shape.Field, Vector3.one),
            };
        }

        public static int StandInTriangles(StandIn kind) => StandInParts(kind).Sum(p => IndexTriangles(p.mesh));

        /// <summary>Triangles from index counts, which a player can read on any mesh (mesh.triangles needs Read/Write).</summary>
        public static int IndexTriangles(Mesh m)
        {
            // A player refuses even index counts on meshes imported without Read/Write; the counts only feed editor stats.
            if (!m.isReadable && !Application.isEditor) return 0;
            long n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i);
            return (int)(n / 3);
        }
    }
}
