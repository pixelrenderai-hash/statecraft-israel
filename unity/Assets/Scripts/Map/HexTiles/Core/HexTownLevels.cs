#nullable enable
using UnityEngine;

namespace Statecraft.HexTiles
{
    /// <summary>PROPOSAL (lane A, 19 October 2026): the generated levels of detail the hex tiles draw for the town
    /// models, in TownModel order, one per model: the level the "town:&lt;folder&gt;@&lt;n&gt;" ids in HexModels.TownAliases
    /// name (a whole level that keeps the model's shape: HexTownLevelsBake). Null where the aliases take the full mesh or
    /// the set's own mid level. Baked by the editor into Resources/HexTiles/TownLevels.asset from the tracked b*-v2
    /// models; the shipped TownModelSet is not changed. Without it the hex tiles draw the full meshes.</summary>
    public sealed class HexTownLevels : ScriptableObject
    {
        public const string ResourcesPath = "HexTiles/TownLevels";

        public Mesh?[] meshes = System.Array.Empty<Mesh?>();
        /// <summary>The generator's level index each mesh is (0: the full mesh, not stored).</summary>
        public int[] levels = System.Array.Empty<int>();
    }
}
