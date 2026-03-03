using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Extracts the 4 idle reference frames from a combined spritesheet
/// and stitches them into a single 64x256 reference sheet for the artist.
/// Order top-to-bottom: S, W, N, E (matching the sheet direction order).
/// </summary>
public class IdleFrameExtractorWindow : EditorWindow
{
    [MenuItem("Tools/Extract Idle Frames")]
    public static void Open() => GetWindow<IdleFrameExtractorWindow>("Extract Idle Frames");

    private string spritesheetPath = "Assets/SpriteExports/Body_human_male_uvs.png";
    private string outputPath      = "Assets/SpriteExports/IdleReference.png";
    private int    pixelSize       = 64;

    private int idleRowS = 8;
    private int idleRowW = 9;
    private int idleRowN = 10;
    private int idleRowE = 11;

    private string status = "";

    void OnGUI()
    {
        EditorGUILayout.LabelField("Extract Idle Reference Frames", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Extracts the 4 idle frames and combines them into one sheet.\n" +
            "Order (top to bottom): S, W, N, E.\n\n" +
            "Give IdleReference.png to the artist. They paint over it and return it.\n" +
            "The baker expects the painted file in the same S/W/N/E order.",
            MessageType.Info);

        GUILayout.Space(8);
        spritesheetPath = EditorGUILayout.TextField("Source Spritesheet", spritesheetPath);
        outputPath      = EditorGUILayout.TextField("Output Path",        outputPath);
        pixelSize       = EditorGUILayout.IntField ("Frame Pixel Size",   pixelSize);

        GUILayout.Space(8);
        EditorGUILayout.LabelField("Idle Row Indices (0 = top of sheet)", EditorStyles.boldLabel);
        idleRowS = EditorGUILayout.IntField("South Row", idleRowS);
        idleRowW = EditorGUILayout.IntField("West Row",  idleRowW);
        idleRowN = EditorGUILayout.IntField("North Row", idleRowN);
        idleRowE = EditorGUILayout.IntField("East Row",  idleRowE);

        GUILayout.Space(12);
        if (GUILayout.Button("Extract", GUILayout.Height(32)))
            Extract();

        if (!string.IsNullOrEmpty(status))
        {
            GUILayout.Space(6);
            EditorGUILayout.HelpBox(status, status.StartsWith("ERROR") ? MessageType.Error : MessageType.Info);
        }
    }

    void Extract()
    {
        Texture2D sheet = LoadPNG(spritesheetPath);
        if (sheet == null) { status = "ERROR: Could not load: " + spritesheetPath; return; }

        int totalRows = sheet.height / pixelSize;
        int[] rows = new[] { idleRowS, idleRowW, idleRowN, idleRowE };

        // Validate all rows
        foreach (int row in rows)
        {
            if (row < 0 || row >= totalRows)
            {
                status = $"ERROR: Row {row} is out of bounds (sheet has {totalRows} rows).";
                return;
            }
        }

        // Output sheet: pixelSize wide, pixelSize*4 tall (one frame per direction, stacked)
        Texture2D outSheet = new Texture2D(pixelSize, pixelSize * 4, TextureFormat.RGBA32, false);

        for (int i = 0; i < rows.Length; i++)
        {
            int srcX = 0; // col 0 = first frame
            int srcY = (totalRows - 1 - rows[i]) * pixelSize; // bottom-up

            Color[] px = sheet.GetPixels(srcX, srcY, pixelSize, pixelSize);

            // Stack top-to-bottom: i=0 (S) at top, i=3 (E) at bottom
            // In bottom-up texture coords: top row = highest Y
            int destY = (4 - 1 - i) * pixelSize;
            outSheet.SetPixels(0, destY, pixelSize, pixelSize, px);
        }

        outSheet.Apply();
        string outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        File.WriteAllBytes(outputPath, outSheet.EncodeToPNG());
        DestroyImmediate(outSheet);

        AssetDatabase.Refresh();
        status = "Done! Saved to: " + outputPath + "\nOrder (top to bottom): S, W, N, E.";
    }

    static Texture2D LoadPNG(string path)
    {
        if (!File.Exists(path)) return null;
        byte[]    bytes = File.ReadAllBytes(path);
        Texture2D tex   = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return tex.LoadImage(bytes) ? tex : null;
    }
}