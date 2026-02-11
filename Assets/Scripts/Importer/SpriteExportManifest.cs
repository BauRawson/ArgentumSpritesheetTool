using System;
using System.Collections.Generic;

namespace SpriteImporter
{
    /// <summary>
    /// Mirror of the export manifest structure for deserialization.
    /// This allows the importer to be self-contained.
    /// </summary>
    [Serializable]
    public class SpriteExportManifest
    {
        public string exportPrefix;
        public int pixelSize;
        public int sortOrder;
        public int sheetWidth;
        public int maxFramesWidth;
        public string combinedSpritesheet;
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
        public int rowStart;
        public int rowsPerDirection;
    }
}
