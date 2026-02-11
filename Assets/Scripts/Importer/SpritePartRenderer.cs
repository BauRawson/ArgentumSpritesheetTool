using UnityEngine;

/// <summary>
/// Renders a single character part (body, torso, hair, etc.).
/// Controlled by LayeredSpriteCharacter.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpritePartRenderer : MonoBehaviour
{
    [Header("Current Part")]
    [SerializeField] private SpritePartDefinition _partDefinition;

    private SpriteRenderer _spriteRenderer;
    private SpriteAnimationAsset _currentAnimation;
    private int _currentDirection;
    private int _currentFrame;

    public SpritePartDefinition PartDefinition => _partDefinition;
    public bool HasPart => _partDefinition != null;

    private SpriteRenderer SpriteRenderer
    {
        get
        {
            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();
            return _spriteRenderer;
        }
    }

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// Set or swap the part definition (e.g., change armor).
    /// </summary>
    public void SetPart(SpritePartDefinition definition)
    {
        _partDefinition = definition;

        if (definition != null)
        {
            SpriteRenderer.sortingOrder = definition.sortOrder;
            SpriteRenderer.enabled = true;
        }
        else
        {
            SpriteRenderer.sprite = null;
            SpriteRenderer.enabled = false;
        }

        // Re-apply current animation if we have one
        if (_currentAnimation != null && definition != null)
        {
            SetAnimation(_currentAnimation.animationName);
        }
    }

    /// <summary>
    /// Clear the part (hide this layer).
    /// </summary>
    public void ClearPart()
    {
        SetPart(null);
    }

    /// <summary>
    /// Set the current animation by name.
    /// </summary>
    public void SetAnimation(string animationName)
    {
        if (_partDefinition == null)
        {
            _currentAnimation = null;
            return;
        }

        _currentAnimation = _partDefinition.GetAnimation(animationName);

        if (_currentAnimation == null)
        {
            // This part doesn't have this animation - hide it
            SpriteRenderer.sprite = null;
            return;
        }

        UpdateSprite();
    }

    /// <summary>
    /// Set the current direction (0-7 for 8-directional).
    /// </summary>
    public void SetDirection(int directionIndex)
    {
        _currentDirection = directionIndex;
        UpdateSprite();
    }

    /// <summary>
    /// Set the current frame.
    /// </summary>
    public void SetFrame(int frameIndex)
    {
        _currentFrame = frameIndex;
        UpdateSprite();
    }

    /// <summary>
    /// Update direction and frame together.
    /// </summary>
    public void SetDirectionAndFrame(int directionIndex, int frameIndex)
    {
        _currentDirection = directionIndex;
        _currentFrame = frameIndex;
        UpdateSprite();
    }

    private void UpdateSprite()
    {
        if (_currentAnimation == null || _partDefinition == null)
        {
            SpriteRenderer.sprite = null;
            return;
        }

        SpriteRenderer.sprite = _currentAnimation.GetSprite(_currentDirection, _currentFrame);
    }
}
