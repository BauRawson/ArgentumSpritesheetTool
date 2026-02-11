using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that holds all animation data for a single character part variant.
/// e.g., "Torso_01_Clothes" or "Hair_02"
/// </summary>
[CreateAssetMenu(menuName = "Sprite Import/Part Definition")]
public class SpritePartDefinition : ScriptableObject
{
    [Header("Part Info")]
    public string partName;
    public int sortOrder;  // Lower = behind, higher = front

    [Header("Animations")]
    public List<SpriteAnimationAsset> animations = new();

    /// <summary>
    /// Get animation by name.
    /// </summary>
    public SpriteAnimationAsset GetAnimation(string animationName)
    {
        return animations.Find(a => a.animationName == animationName);
    }

    /// <summary>
    /// Check if this part has a specific animation.
    /// </summary>
    public bool HasAnimation(string animationName)
    {
        return animations.Exists(a => a.animationName == animationName);
    }
}

/// <summary>
/// Serializable animation data that can be saved in a ScriptableObject.
/// </summary>
[System.Serializable]
public class SpriteAnimationAsset
{
    public string animationName;
    public int fps;
    public List<string> directions;

    [Tooltip("Sprites for each direction. Each element is an array of frames for that direction.")]
    public List<DirectionSprites> directionSprites = new();

    public Sprite GetSprite(int directionIndex, int frameIndex)
    {
        if (directionIndex < 0 || directionIndex >= directionSprites.Count)
            return null;

        var frames = directionSprites[directionIndex].frames;
        if (frameIndex < 0 || frameIndex >= frames.Count)
            return null;

        return frames[frameIndex];
    }

    public int FrameCount => directionSprites.Count > 0 ? directionSprites[0].frames.Count : 0;
    public int DirectionCount => directionSprites.Count;
}

/// <summary>
/// Holds all frames for a single direction.
/// </summary>
[System.Serializable]
public class DirectionSprites
{
    public string direction;
    public List<Sprite> frames = new();
}
