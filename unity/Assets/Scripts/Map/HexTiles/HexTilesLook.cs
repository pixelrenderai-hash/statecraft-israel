#nullable enable
using System;
using System.Text.RegularExpressions;
using Statecraft.Game.Map;
using UnityEngine;

namespace Statecraft.HexTiles
{
    /// <summary>The hex-tile map (docs/hex-tiles/README.md), opt-in while it covers only central Israel and its models
    /// are local: Web ?hex-tiles or ?hex-tiles=1, desktop -hex-tiles. Puts the board on the real map (HexRealMap) under one
    /// "HexTiles" root beside the map's own; off with nothing applied touches nothing, so the shipped map is unchanged.
    /// Presentation only: no simulation state is read or written.</summary>
    public static class HexTilesLook
    {
        public const string RootName = "HexTiles", Board = "central_israel";
        static readonly Regex Flag = new Regex(@"(?:^|&)hex-tiles(?:=1)?(?=&|$)", RegexOptions.CultureInvariant);

        public static bool Requested() =>
            Requested(Application.absoluteURL, Environment.GetCommandLineArgs(), Application.platform == RuntimePlatform.WebGLPlayer);

        /// <summary>Web: only a top-level query parameter counts, bare or "=1" (MapLook's rule). Desktop: the exact argument.</summary>
        public static bool Requested(string? url, string[] args, bool webgl)
        {
            if (!webgl) return Array.IndexOf(args, "-hex-tiles") >= 0;
            var address = (url ?? "").Split('#')[0];
            var start = address.IndexOf('?');
            return start >= 0 && Flag.IsMatch(address.Substring(start + 1));
        }

        /// <summary>Removes an earlier board (which restores the real map), then builds it when <paramref name="enabled"/>.</summary>
        public static void Apply(MapScene map, bool enabled)
        {
            if (map == null) return;
            var old = map.transform.Find(RootName);
            if (old != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(old.gameObject); else UnityEngine.Object.DestroyImmediate(old.gameObject); }
            if (!enabled || map.Terrain == null) return;
            var holder = new GameObject(RootName).transform;
            holder.SetParent(map.transform, false);
            HexRealMap.Place(map, HexBoard.Load(Board), HexBoard.GroundFromResources, holder);
        }
    }
}
