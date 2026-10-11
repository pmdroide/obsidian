using System;
using Microsoft.Xna.Framework;

namespace Engine.Voxels
{
    /// <summary>
    /// One voxel, stored as a byte. Scripts and scene settings use these names, so never renumber or rename
    /// one; append new blocks before <see cref="VoxelBlocks.Count"/> grows.
    /// </summary>
    public enum VoxelBlock : byte
    {
        Air,
        Grass,
        Dirt,
        Stone,
        Sand,
        Snow,
        Wood,
        Leaves,
        Planks,
        Brick,
        Lamp,
    }

    /// <summary>The shape of the generated terrain (Inspector > Environment > Voxel World). Saved by name: never rename one.</summary>
    public enum VoxelTerrain
    {
        Flat,
        Hills,
        Mountains,
        Islands,
    }

    /// <summary>The look of one block face: each has its own material, textures and roughness.</summary>
    public enum VoxelSurface
    {
        GrassTop,
        GrassSide,
        Dirt,
        Stone,
        Sand,
        Snow,
        Bark,
        LogTop,
        Leaves,
        Planks,
        Brick,
        Lamp,
    }

    /// <summary>Which way a block face looks (Z is up).</summary>
    public enum VoxelFace
    {
        Top,
        Bottom,
        Side,
    }

    /// <summary>Fixed facts about each <see cref="VoxelBlock"/>: whether it is solid and which surface each face shows.</summary>
    public static class VoxelBlocks
    {
        /// <summary>Number of block types, Air included.</summary>
        public const int Count = 11;
        public const int SurfaceCount = 12;

        /// <summary>The blocks a player can place, in hotbar order.</summary>
        public static readonly VoxelBlock[] Placeable =
        {
            VoxelBlock.Grass, VoxelBlock.Dirt, VoxelBlock.Stone, VoxelBlock.Sand, VoxelBlock.Snow,
            VoxelBlock.Wood, VoxelBlock.Leaves, VoxelBlock.Planks, VoxelBlock.Brick, VoxelBlock.Lamp,
        };

        public static bool IsSolid(VoxelBlock block) => block != VoxelBlock.Air && (byte)block < Count;

        public static VoxelSurface Surface(VoxelBlock block, VoxelFace face) => block switch
        {
            VoxelBlock.Grass => face == VoxelFace.Top ? VoxelSurface.GrassTop
                : face == VoxelFace.Side ? VoxelSurface.GrassSide : VoxelSurface.Dirt,
            VoxelBlock.Dirt => VoxelSurface.Dirt,
            VoxelBlock.Stone => VoxelSurface.Stone,
            VoxelBlock.Sand => VoxelSurface.Sand,
            VoxelBlock.Snow => VoxelSurface.Snow,
            VoxelBlock.Wood => face == VoxelFace.Side ? VoxelSurface.Bark : VoxelSurface.LogTop,
            VoxelBlock.Leaves => VoxelSurface.Leaves,
            VoxelBlock.Planks => VoxelSurface.Planks,
            VoxelBlock.Brick => VoxelSurface.Brick,
            VoxelBlock.Lamp => VoxelSurface.Lamp,
            _ => VoxelSurface.Stone,
        };

        /// <summary>A colour that stands for the block in UI (the average of its side texture, roughly).</summary>
        public static Color SwatchColor(VoxelBlock block) => block switch
        {
            VoxelBlock.Grass => new Color(92, 150, 62),
            VoxelBlock.Dirt => new Color(124, 88, 60),
            VoxelBlock.Stone => new Color(128, 128, 132),
            VoxelBlock.Sand => new Color(222, 204, 150),
            VoxelBlock.Snow => new Color(236, 242, 248),
            VoxelBlock.Wood => new Color(104, 76, 48),
            VoxelBlock.Leaves => new Color(58, 112, 48),
            VoxelBlock.Planks => new Color(176, 136, 86),
            VoxelBlock.Brick => new Color(160, 74, 58),
            VoxelBlock.Lamp => new Color(255, 214, 140),
            _ => Color.Transparent,
        };

        public static string Name(VoxelBlock block) => Enum.IsDefined(block) ? block.ToString() : "Unknown";
    }
}
