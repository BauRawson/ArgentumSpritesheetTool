using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Sprite Export/Export Batch")]
public class SpriteExportBatch : ScriptableObject
{
    public string exportPrefix;
    public int pixelSize = 512;
    public List<SpriteAnimationDefinition> animations;
}
