#nullable enable
using UnityEngine;

namespace Statecraft.HexTiles
{
    /// <summary>PROPOSAL (lane A, 19 October 2026; not wired in): one whole level of detail per town model for the hex
    /// tiles, in TownModel order: the lightest of Unity's generated levels that keeps the model's shape and lets no view
    /// ray through its walls (HexTownLevelsBake). Each is null where the full mesh in TownModelSet is that level.
    /// Baked by the editor into Resources/HexTiles/TownLevels.asset from the tracked b*-v2 models; the shipped
    /// TownModelSet is not changed.</summary>
    public sealed class HexTownLevels : ScriptableObject
    {
        public const string ResourcesPath = "HexTiles/TownLevels";

        public Mesh?[] meshes = System.Array.Empty<Mesh?>();
        /// <summary>The generator's level index each mesh is (0: the full mesh, not stored).</summary>
        public int[] levels = System.Array.Empty<int>();
    }
}
