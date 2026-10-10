#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace Statecraft.Game.Map
{
    /// <summary>Opt-in 3D building prototypes, for a local look only. A mapped settlement keeps its marker box (object, name,
    /// transform, material) with the renderer off, and a prefab stands on the box's base under one "SettlementPrototypes"
    /// root beside "Settlements". The prefabs are git-ignored pending-rights models (PrototypeImport;
    /// docs/art-prototypes.md), so a clean clone, or a launch without the flag, draws the boxes as before.
    /// Web: ?building-preview or ?building-preview=1. Desktop: -building-preview. No simulation state, label, toggle or
    /// SettlementPlacer value is touched: labels stay on the box top (MarkerHeightM), which no prototype rises above.</summary>
    public static class BuildingPreview
    {
        public const string RootName = "SettlementPrototypes", InstancePrefix = "proto-", ResourcesFolder = "SettlementPrototypes";
        const string Hall = "kibbutz-dining-hall-v1", WhiteCity = "tel-aviv-white-city-v1", StoneVillage = "stone-village-v1";

        /// <summary>Every prototype a settlement can map to: PrototypeImport's names, and its prefabs' names.</summary>
        public static IReadOnlyList<string> Prototypes { get; } = new[] { Hall, WhiteCity, StoneVillage };

        // By id first, then by type. White City fits only Tel Aviv. The stone village on every arab_town is a symbolic
        // stand-in (the content has no village row) pending the owner's decision on where it belongs.
        static readonly Dictionary<string, string> ById = new Dictionary<string, string> { ["tel_aviv"] = WhiteCity };
        static readonly Dictionary<string, string> ByType = new Dictionary<string, string> { ["kibbutz"] = Hall, ["arab_town"] = StoneVillage };

        // Turn about the vertical (degrees): constant per prototype, so every launch and capture draws the same map (no
        // Random, no GetHashCode). Off the poses' yaw 10 so two facades show. The hall and White City were turned half round
        // after the first editor render showed their backs to the south-facing camera (veranda, balconies).
        static readonly Dictionary<string, float> Yaw = new Dictionary<string, float> { [Hall] = 210f, [WhiteCity] = 200f, [StoneVillage] = 40f };

        /// <summary>How far a prototype's footprint may spread, in marker edges. A cluster of separate buildings inside the
        /// box's own footprint read smaller than the solid box it replaced at the start view (first WebGL capture, 10 October
        /// 2026), so it may cover twice the box's width; its height stays within the box.</summary>
        public const float Spread = 2f;

        static readonly Regex Flag = new Regex(@"(?:^|&)building-preview(?:=1)?(?=&|$)", RegexOptions.CultureInvariant);

        public static bool Requested() =>
            Requested(Application.absoluteURL, Environment.GetCommandLineArgs(), Application.platform == RuntimePlatform.WebGLPlayer);

        /// <summary>Web: only a top-level query parameter counts, bare or "=1", never a fragment or a nested URL's question
        /// mark (UiPreview's rule). Desktop: only the exact argument.</summary>
        public static bool Requested(string? url, string[] args, bool webgl)
        {
            if (!webgl) return Array.IndexOf(args, "-building-preview") >= 0;
            var address = (url ?? "").Split('#')[0];
            var start = address.IndexOf('?');
            return start >= 0 && Flag.IsMatch(address.Substring(start + 1));
        }

        /// <summary>The prototype standing in for <paramref name="s"/>, or null when it keeps its box.</summary>
        public static string? PrototypeFor(Settlement s) =>
            ById.TryGetValue(s.Id, out var byId) ? byId : ByType.TryGetValue(s.Type, out var byType) ? byType : null;

        /// <summary>The default loader: Resources/SettlementPrototypes/&lt;name&gt; (PrototypeImport's prefabs), null when absent.</summary>
        public static GameObject? LoadFromResources(string name) => Resources.Load<GameObject>(ResourcesFolder + "/" + name);

        /// <summary>Removes what an earlier Apply placed (showing those boxes again); then, when <paramref name="enabled"/>,
        /// places <paramref name="load"/>'s prefab on every mapped settlement: at the box's base, turned by its prototype's
        /// yaw, scaled uniformly so its measured footprint (after the turn) spans at most Spread x the edge h and its height
        /// at most h (so the label anchored at MarkerHeightM stays on top), with the bottom on the base and the footprint centred, no collider, shadows on, no property block. A
        /// null prefab, or one with nothing to draw, keeps the box. Off with nothing placed touches nothing (the default
        /// path), and so does a destroyed map (a teardown may outlive it). Returns the number placed. MapScene.Build's Clear
        /// destroys the root with the rest of the map.</summary>
        public static int Apply(MapScene map, bool enabled, Func<string, GameObject?> load)
        {
            if (map == null) return 0;
            var markers = map.transform.Find("Settlements");   // SettlementPlacer.Place's root
            var old = map.transform.Find(RootName);
            if (old != null)
            {
                foreach (var s in map.Settlements) if (PrototypeFor(s) != null) Show(markers, s.Id, true);
                Discard(old.gameObject);
            }
            if (!enabled || markers == null) return 0;
            Transform? root = null; int placed = 0;
            foreach (var s in map.Settlements)
            {
                var name = PrototypeFor(s); var box = markers.Find(s.Id);
                if (name == null || box == null) continue;
                var prefab = load(name);
                if (prefab == null) continue;
                if (root == null) { root = new GameObject(RootName).transform; root.SetParent(map.transform, true); }
                float h = SettlementPlacer.MarkerHeightM(s.Type);
                var at = box.position - Vector3.up * h / 2;
                var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.Euler(0f, Yaw[name], 0f), root);
                go.name = InstancePrefix + s.Id;
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Discard(c);
                var size = Measure(go)?.size ?? Vector3.zero;
                float footprint = Mathf.Max(size.x, size.z);
                if (footprint <= 0f || size.y <= 0f) { Discard(go); continue; }
                go.transform.localScale *= Mathf.Min(Spread * h / footprint, h / size.y);
                var b = Measure(go)!.Value;   // re-measured: centre and bottom move with the scale unless the pivot is there
                go.transform.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.On; r.SetPropertyBlock(null); }
                Show(markers, s.Id, false);
                placed++;
            }
            return placed;
        }

        /// <summary>World bounds of every active renderer under <paramref name="go"/>, or null when there is none.</summary>
        static Bounds? Measure(GameObject go)
        {
            Bounds? all = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (all is Bounds b) { b.Encapsulate(r.bounds); all = b; }
                else all = r.bounds;
            }
            return all;
        }

        static void Show(Transform? markers, string id, bool on)
        {
            var box = markers != null ? markers.Find(id) : null;
            if (box != null && box.TryGetComponent(out MeshRenderer r)) r.enabled = on;
        }

        /// <summary>Destroys now in edit mode; in play mode unparents first (as MapScene.Clear does), since Destroy waits for
        /// the frame's end and a second Apply in the same frame must not find the old root.</summary>
        static void Discard(UnityEngine.Object o)
        {
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(o); return; }
            if (o is GameObject go) go.transform.SetParent(null);
            UnityEngine.Object.Destroy(o);
        }
    }
}
