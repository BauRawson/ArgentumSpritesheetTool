using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Sprite Export/Animation Definition")]
public class SpriteAnimationDefinition : ScriptableObject
{
    public AnimationClip clip;
    public int fps = 30;
    public int framesPerDirection = 8;

    [Tooltip("Frame indices to export (after sampling)")]
    public List<int> exportedFrames = new();

    [System.Serializable]
    public struct DirectionConfig
    {
        public SpriteDirection direction;
        public float angle; // Y rotation
        public float xAngle; // X rotation for camera matching
    }

    [Header("Directions")]
    public List<DirectionConfig> directionConfigs = new()
    {
        new DirectionConfig { direction = SpriteDirection.N, angle = 0f, xAngle = 0f },
        new DirectionConfig { direction = SpriteDirection.NE, angle = 45f, xAngle = 0f },
        new DirectionConfig { direction = SpriteDirection.E, angle = 90f, xAngle = 0f },
        new DirectionConfig { direction = SpriteDirection.SE, angle = 135f, xAngle = 0f },
        new DirectionConfig { direction = SpriteDirection.S, angle = 180f, xAngle = 0f },
        new DirectionConfig { direction = SpriteDirection.SW, angle = 225f, xAngle = 0f },
        new DirectionConfig { direction = SpriteDirection.W, angle = 270f, xAngle = 0f },
        new DirectionConfig { direction = SpriteDirection.NW, angle = 315f, xAngle = 0f }
    };

    [Header("Frame Import")]
    public int importEveryNthFrame = 1;

    [Header("Exported Frames")]
    public List<int> frameIndices = new();

    [HideInInspector] public int totalFrames;
    [HideInInspector] public float frameRate;

    public void Recalculate()
    {
        if (!clip) return;
        frameRate = clip.frameRate;
        totalFrames = Mathf.RoundToInt(clip.length * frameRate);
    }

    public void AutoGenerateFrames()
    {
        frameIndices.Clear();
        Recalculate();

        int step = Mathf.Max(1, importEveryNthFrame);
        for (int i = 0; i < totalFrames; i += step)
            frameIndices.Add(i);
    }

    public SpriteDirection[] GetEffectiveDirections()
    {
        return System.Array.ConvertAll(directionConfigs.ToArray(), c => c.direction);
    }

    public float[] GetAngles()
    {
        return System.Array.ConvertAll(directionConfigs.ToArray(), c => c.angle);
    }

    public float[] GetXAngles()
    {
        return System.Array.ConvertAll(directionConfigs.ToArray(), c => c.xAngle);
    }
}
