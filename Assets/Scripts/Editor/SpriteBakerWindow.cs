using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Bakes a painted idle skin sheet onto all animation frames using UV maps.
///
/// Artist receives IdleReference.png (64x256, 4 frames top-to-bottom: S, W, N, E).
/// They paint over it and return the same file as PaintedSkin.png.
/// The baker splits it back into 4 frames and builds per-direction UV lookups.
/// </summary>
public class SpriteBakerWindow : EditorWindow
{
    [MenuItem("Tools/Sprite Baker")]
    public static void Open() => GetWindow<SpriteBakerWindow>("Sprite Baker");

    private string uvmapPath      = "Assets/SpriteExports/Body_human_male_uvs_uvmap.png";
    private string paintedSkinPath = "Assets/SpriteExports/PaintedSkin.png";
    private string outputPath     = "Assets/SpriteExports/Baked.png";

    private int pixelSize = 64;

    // Must match IdleFrameExtractorWindow
    private int idleRowS = 8;
    private int idleRowW = 9;
    private int idleRowN = 10;
    private int idleRowE = 11;

    private bool  useFallbackTransparent = false;
    private Color fallbackColor          = new Color(1f, 0f, 1f, 1f);
    private float uvTolerance            = 1.5f;

    private Vector2 scroll;
    private string  status = "";

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Sprite Baker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. Export with 'Export UV Maps' ON to get *_uvmap.png\n" +
            "2. Run 'Extract Idle Frames' to get IdleReference.png\n" +
            "3. Artist paints over IdleReference.png, saves as PaintedSkin.png\n" +
            "   (same 64x256 sheet, same S/W/N/E order)\n" +
            "4. Set paths and click Bake",
            MessageType.Info);

        GUILayout.Space(8);
        EditorGUILayout.LabelField("Paths", EditorStyles.boldLabel);
        uvmapPath       = EditorGUILayout.TextField("UV Map Sheet",      uvmapPath);
        paintedSkinPath = EditorGUILayout.TextField("Painted Skin Sheet", paintedSkinPath);
        outputPath      = EditorGUILayout.TextField("Output Path",        outputPath);

        GUILayout.Space(8);
        EditorGUILayout.LabelField("Layout (must match your export)", EditorStyles.boldLabel);
        pixelSize = EditorGUILayout.IntField("Frame Pixel Size", pixelSize);
        EditorGUILayout.LabelField("Idle Row Indices (0 = top of sheet)", EditorStyles.miniLabel);
        idleRowS = EditorGUILayout.IntField("South Row", idleRowS);
        idleRowW = EditorGUILayout.IntField("West Row",  idleRowW);
        idleRowN = EditorGUILayout.IntField("North Row", idleRowN);
        idleRowE = EditorGUILayout.IntField("East Row",  idleRowE);

        GUILayout.Space(8);
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
        uvTolerance            = EditorGUILayout.Slider("UV Tolerance (px)", uvTolerance, 0.5f, 5f);
        useFallbackTransparent = EditorGUILayout.Toggle("Unmapped = Transparent", useFallbackTransparent);
        if (!useFallbackTransparent)
            fallbackColor = EditorGUILayout.ColorField("Fallback Color", fallbackColor);

        GUILayout.Space(12);
        if (GUILayout.Button("Bake", GUILayout.Height(36)))
            Bake();

        if (!string.IsNullOrEmpty(status))
        {
            GUILayout.Space(6);
            EditorGUILayout.HelpBox(status, status.StartsWith("ERROR") ? MessageType.Error : MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    void Bake()
    {
        status = "Working...";
        Repaint();

        // --- Load UV map sheet ---
        Texture2D uvSheet = LoadPNG(uvmapPath);
        if (uvSheet == null) { status = "ERROR: Could not load UV map: " + uvmapPath; return; }

        int totalRows = uvSheet.height / pixelSize;
        int totalCols = uvSheet.width  / pixelSize;

        // --- Load painted skin sheet and split into 4 direction frames ---
        Texture2D paintedSheet = LoadPNG(paintedSkinPath);
        if (paintedSheet == null) { status = "ERROR: Could not load painted skin: " + paintedSkinPath; return; }

        if (paintedSheet.width != pixelSize || paintedSheet.height != pixelSize * 4)
        {
            status = $"ERROR: Painted skin is {paintedSheet.width}x{paintedSheet.height}, " +
                     $"expected {pixelSize}x{pixelSize * 4} (4 stacked frames).";
            return;
        }

        // Split painted sheet into 4 frames (top-to-bottom: S, W, N, E)
        // In bottom-up texture: frame i is at Y = (4-1-i)*pixelSize
        int[]       idleRows   = new[] { idleRowS, idleRowW, idleRowN, idleRowE };
        string[]    dirNames   = new[] { "S", "W", "N", "E" };
        Texture2D[] dirFrames  = new Texture2D[4];

        for (int i = 0; i < 4; i++)
        {
            int y = (4 - 1 - i) * pixelSize;
            Color[] px = paintedSheet.GetPixels(0, y, pixelSize, pixelSize);
            dirFrames[i] = new Texture2D(pixelSize, pixelSize, TextureFormat.RGBA32, false);
            dirFrames[i].SetPixels(px);
            dirFrames[i].Apply();
        }

        // --- Build per-direction UV->Color lookups ---
        float tol = uvTolerance / pixelSize;
        var lookups = new UVColorLookup[4];

        for (int i = 0; i < 4; i++)
        {
            int idleRow = idleRows[i];
            Texture2D uvFrame = ExtractFrame(uvSheet, col: 0, row: idleRow, totalRows: totalRows);
            if (uvFrame == null)
            {
                Debug.LogWarning($"[SpriteBaker] Could not extract UV frame for {dirNames[i]} (row {idleRow})");
                continue;
            }

            Color[] uvPx     = uvFrame.GetPixels();
            Color[] paintedPx = dirFrames[i].GetPixels();
            var lookup = new UVColorLookup();

            for (int j = 0; j < uvPx.Length; j++)
            {
                if (uvPx[j].a < 0.5f) continue;
                lookup.Add(uvPx[j].r, uvPx[j].g, paintedPx[j]);
            }

            lookups[i] = lookup;
            DestroyImmediate(uvFrame);
            Debug.Log($"[SpriteBaker] Lookup {dirNames[i]} (row {idleRow}): {lookup.Count} entries.");
        }

        // --- Bake the full sheet ---
        Texture2D outSheet = new Texture2D(uvSheet.width, uvSheet.height, TextureFormat.RGBA32, false);
        Color[] clearPx = new Color[uvSheet.width * uvSheet.height];
        outSheet.SetPixels(clearPx);

        int mappedPx = 0, unmappedPx = 0;
        Color fallback = useFallbackTransparent ? Color.clear : fallbackColor;

        for (int row = 0; row < totalRows; row++)
        {
            // Which direction is this row? row % 4 gives index into S,W,N,E
            int dirIndex = row % 4;
            UVColorLookup lookup = lookups[dirIndex];
            bool isIdleRow = System.Array.IndexOf(idleRows, row) >= 0;

            for (int col = 0; col < totalCols; col++)
            {
                Texture2D uvFrame = ExtractFrame(uvSheet, col, row, totalRows);
                if (uvFrame == null) continue;

                Color[] outFramePx = new Color[pixelSize * pixelSize];

                // Idle row col 0: paste painted skin directly
                if (isIdleRow && col == 0)
                {
                    outFramePx = dirFrames[dirIndex].GetPixels();
                }
                else if (lookup != null)
                {
                    Color[] uvFramePx = uvFrame.GetPixels();
                    for (int i = 0; i < uvFramePx.Length; i++)
                    {
                        if (uvFramePx[i].a < 0.5f) { outFramePx[i] = Color.clear; continue; }
                        bool hit = lookup.Sample(uvFramePx[i].r, uvFramePx[i].g, tol, out Color found);
                        if (hit) { outFramePx[i] = found; mappedPx++; }
                        else     { outFramePx[i] = fallback; unmappedPx++; }
                    }
                }

                int destX = col * pixelSize;
                int destY = (totalRows - 1 - row) * pixelSize;
                outSheet.SetPixels(destX, destY, pixelSize, pixelSize, outFramePx);
                DestroyImmediate(uvFrame);
            }
        }

        outSheet.Apply();
        string outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        File.WriteAllBytes(outputPath, outSheet.EncodeToPNG());
        DestroyImmediate(outSheet);

        AssetDatabase.Refresh();

        int   total = mappedPx + unmappedPx;
        float pct   = total > 0 ? mappedPx * 100f / total : 0f;
        status = $"Done! {pct:F1}% pixels mapped ({unmappedPx} unmapped).\nSaved to: {outputPath}";
    }

    Texture2D ExtractFrame(Texture2D sheet, int col, int row, int totalRows)
    {
        int x = col * pixelSize;
        int y = (totalRows - 1 - row) * pixelSize;
        if (x < 0 || x + pixelSize > sheet.width)  return null;
        if (y < 0 || y + pixelSize > sheet.height) return null;
        Color[] px = sheet.GetPixels(x, y, pixelSize, pixelSize);
        Texture2D frame = new Texture2D(pixelSize, pixelSize, TextureFormat.RGBA32, false);
        frame.SetPixels(px);
        frame.Apply();
        return frame;
    }

    static Texture2D LoadPNG(string path)
    {
        if (!File.Exists(path)) return null;
        byte[]    bytes = File.ReadAllBytes(path);
        Texture2D tex   = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return tex.LoadImage(bytes) ? tex : null;
    }
}

public class UVColorLookup
{
    private struct Entry { public float u, v; public Color color; }
    private readonly List<Entry> entries = new List<Entry>();
    public int Count => entries.Count;

    public void Add(float u, float v, Color color)
        => entries.Add(new Entry { u = u, v = v, color = color });

    public bool Sample(float u, float v, float tolerance, out Color color)
    {
        color = Color.clear;
        if (entries.Count == 0) return false;
        float tSq = tolerance * tolerance, bestD = float.MaxValue;
        int bestI = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            float du = entries[i].u - u, dv = entries[i].v - v;
            float d  = du * du + dv * dv;
            if (d < bestD) { bestD = d; bestI = i; }
        }
        if (bestI >= 0 && bestD <= tSq) { color = entries[bestI].color; return true; }
        return false;
    }
}