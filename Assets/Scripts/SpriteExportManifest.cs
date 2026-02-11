using System;
using System.Collections.Generic;

[Serializable]
public class SpriteExportManifest
{
    public string groupName;
    public string exportPrefix;
    public int pixelSize;
    public int sortOrder;  // Lower = rendered first (behind), higher = rendered last (front)
    public int sheetWidth; // Width of the sheet in frames (used when maxFramesWidth or combineAnimations is set)
    public int maxFramesWidth; // The wrapping threshold (0 = no wrapping)
    public string combinedSpritesheet; // Filename of combined sheet, if combineAnimations was used
    public List<AnimationEntry> animations = new();
}

[Serializable]
public class AnimationEntry
{
    public string name;
    public List<string> directions;
    public int framesPerDirection;
    public int fps;
    public string spritesheet;
    public int rowStart; // Row index where this animation starts in the combined sheet
    public int rowsPerDirection; // How many rows each direction takes (when frames wrap via maxFramesWidth)
}
