using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Main controller for a layered sprite character.
/// Manages multiple SpritePartRenderers and keeps them synchronized.
/// </summary>
public class LayeredSpriteCharacter : MonoBehaviour
{
    [Header("Parts")]
    [SerializeField] private List<SpritePartRenderer> _parts = new();

    [Header("Animation State")]
    [SerializeField] private string _currentAnimation;
    [SerializeField] private int _currentDirection;
    [SerializeField] private int _currentFrame;

    [Header("Playback")]
    [SerializeField] private bool _isPlaying;
    [SerializeField] private float _playbackSpeed = 1f;

    private float _frameTimer;
    private int _animationFps = 12;
    private int _totalFrames = 1;

    public string CurrentAnimation => _currentAnimation;
    public int CurrentDirection => _currentDirection;
    public int CurrentFrame => _currentFrame;
    public bool IsPlaying => _isPlaying;

    public IReadOnlyList<SpritePartRenderer> Parts => _parts;

    private void Update()
    {
        if (!_isPlaying || _totalFrames <= 1)
            return;

        _frameTimer += Time.deltaTime * _playbackSpeed;
        float frameDuration = 1f / _animationFps;

        if (_frameTimer >= frameDuration)
        {
            _frameTimer -= frameDuration;
            _currentFrame = (_currentFrame + 1) % _totalFrames;
            UpdateAllParts();
        }
    }

    /// <summary>
    /// Play an animation by name.
    /// </summary>
    public void PlayAnimation(string animationName, bool loop = true)
    {
        _currentAnimation = animationName;
        _currentFrame = 0;
        _frameTimer = 0;
        _isPlaying = loop;

        // Get fps and frame count from the first part that has this animation
        foreach (var part in _parts)
        {
            if (part.PartDefinition == null) continue;

            var anim = part.PartDefinition.GetAnimation(animationName);
            if (anim != null)
            {
                _animationFps = anim.fps;
                _totalFrames = anim.FrameCount;
                break;
            }
        }

        UpdateAllParts();
    }

    /// <summary>
    /// Stop playback at current frame.
    /// </summary>
    public void Stop()
    {
        _isPlaying = false;
    }

    /// <summary>
    /// Resume playback.
    /// </summary>
    public void Resume()
    {
        _isPlaying = true;
    }

    /// <summary>
    /// Set the facing direction (0-7 for 8-directional).
    /// </summary>
    public void SetDirection(int directionIndex)
    {
        _currentDirection = Mathf.Clamp(directionIndex, 0, 7);
        UpdateAllParts();
    }

    /// <summary>
    /// Set direction using a Vector2 (e.g., from input or velocity).
    /// </summary>
    public void SetDirectionFromVector(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.01f)
            return;

        // Convert to angle and then to direction index
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // Convert angle to 8-direction index
        // 0=E, 1=NE, 2=N, 3=NW, 4=W, 5=SW, 6=S, 7=SE
        // But our directions are: N, NE, E, SE, S, SW, W, NW
        // So we need to remap

        angle = (angle + 360f) % 360f;  // Normalize to 0-360

        // Map angle to direction index (N=0, NE=1, E=2, etc.)
        // N = 90, NE = 45, E = 0, SE = 315, S = 270, SW = 225, W = 180, NW = 135
        int[] angleToDir = { 2, 1, 0, 7, 6, 5, 4, 3 };  // Mapping for each 45-degree sector
        int sector = Mathf.RoundToInt(angle / 45f) % 8;

        SetDirection(angleToDir[sector]);
    }

    /// <summary>
    /// Manually set the current frame.
    /// </summary>
    public void SetFrame(int frameIndex)
    {
        _currentFrame = frameIndex;
        UpdateAllParts();
    }

    /// <summary>
    /// Set a specific part's definition (equip/unequip).
    /// </summary>
    public void SetPart(int partIndex, SpritePartDefinition definition)
    {
        if (partIndex < 0 || partIndex >= _parts.Count)
            return;

        _parts[partIndex].SetPart(definition);

        // Re-apply current animation to the new part
        if (!string.IsNullOrEmpty(_currentAnimation))
        {
            _parts[partIndex].SetAnimation(_currentAnimation);
            _parts[partIndex].SetDirectionAndFrame(_currentDirection, _currentFrame);
        }
    }

    /// <summary>
    /// Get a part renderer by index.
    /// </summary>
    public SpritePartRenderer GetPart(int index)
    {
        if (index < 0 || index >= _parts.Count)
            return null;
        return _parts[index];
    }

    /// <summary>
    /// Add a part renderer to this character.
    /// </summary>
    public void AddPart(SpritePartRenderer part)
    {
        if (!_parts.Contains(part))
        {
            _parts.Add(part);

            // Apply current state to new part
            if (!string.IsNullOrEmpty(_currentAnimation))
            {
                part.SetAnimation(_currentAnimation);
                part.SetDirectionAndFrame(_currentDirection, _currentFrame);
            }
        }
    }

    private void UpdateAllParts()
    {
        foreach (var part in _parts)
        {
            part.SetAnimation(_currentAnimation);
            part.SetDirectionAndFrame(_currentDirection, _currentFrame);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor helper to find all child SpritePartRenderers.
    /// </summary>
    [ContextMenu("Find Child Parts")]
    private void FindChildParts()
    {
        _parts.Clear();
        _parts.AddRange(GetComponentsInChildren<SpritePartRenderer>());
    }
#endif
}
