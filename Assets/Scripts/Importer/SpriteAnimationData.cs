using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds sprite data for a single animation (all directions and frames).
/// </summary>
[Serializable]
public class SpriteAnimationData
{
    public string animationName;
    public int fps;
    public int framesPerDirection;
    public List<string> directions;

    // Sprites organized as [direction][frame]
    // e.g., sprites[0] = all frames for direction N
    public Sprite[][] sprites;

    public Sprite GetSprite(int directionIndex, int frameIndex)
    {
        if (sprites == null || directionIndex >= sprites.Length)
            return null;

        var dirSprites = sprites[directionIndex];
        if (dirSprites == null || frameIndex >= dirSprites.Length)
            return null;

        return dirSprites[frameIndex];
    }

    public int DirectionCount => directions?.Count ?? 0;
    public int FrameCount => framesPerDirection;
}
