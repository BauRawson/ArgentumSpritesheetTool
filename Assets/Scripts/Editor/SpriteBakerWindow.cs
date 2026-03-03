using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class SpriteBakerWindow : EditorWindow
{
    [MenuItem("Tools/Sprite Baker")]
    public static void Open() => GetWindow<SpriteBakerWindow>("Sprite Baker");

    private string tposeUVMapPath   = "Assets/SpriteExports/tpose_uvmap.png";
    private string paintedSkinPath  = "Assets/SpriteExports/PaintedSkin.png";
    private string manifestPath     = "Assets/SpriteExports/manifest.json";
    private string outputPath       = "Assets/SpriteExports/Baked.png";

    private int   pixelSize  = 64;
    private bool  useFallbackTransparent = false;
    private Color fallbackColor          = new Color(1f, 0f, 1f, 1f);
    private float uvTolerance            = 1.5f;

    private Vector2 scroll;
    private string  status = "";

    void OnEnable()
    {
        tposeUVMapPath  = EditorPrefs.GetString("SB_tposeUV",     tposeUVMapPath);
        paintedSkinPath = EditorPrefs.GetString("SB_paintedSkin", paintedSkinPath);
        manifestPath    = EditorPrefs.GetString("SB_manifest",    manifestPath);
        outputPath      = EditorPrefs.GetString("SB_output",      outputPath);
        pixelSize       = EditorPrefs.GetInt   ("SB_pixelSize",   pixelSize);
        uvTolerance     = EditorPrefs.GetFloat ("SB_tolerance",   uvTolerance);
    }

    void SavePrefs()
    {
        EditorPrefs.SetString("SB_tposeUV",     tposeUVMapPath);
        EditorPrefs.SetString("SB_paintedSkin", paintedSkinPath);
        EditorPrefs.SetString("SB_manifest",    manifestPath);
        EditorPrefs.SetString("SB_output",      outputPath);
        EditorPrefs.SetInt   ("SB_pixelSize",   pixelSize);
        EditorPrefs.SetFloat ("SB_tolerance",   uvTolerance);
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Sprite Baker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Bakes a painted T-pose skin onto all animations, outputting a single combined spritesheet.\n\n" +
            "1. Export with UV Maps ON and a T-pose animation (uvCoverageOnly can be off so you get tpose.png as reference).\n" +
            "2. Artist paints PaintedSkin.png using tpose.png as reference (same dimensions).\n" +
            "3. Point manifest path at your export's manifest.json.\n" +
            "4. Click Bake.",
            MessageType.Info);

        GUILayout.Space(8);
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("Paths", EditorStyles.boldLabel);
        tposeUVMapPath  = EditorGUILayout.TextField("T-Pose UV Map",       tposeUVMapPath);
        paintedSkinPath = EditorGUILayout.TextField("Painted Skin (.png)", paintedSkinPath);
        manifestPath    = EditorGUILayout.TextField("Manifest (.json)",    manifestPath);
        outputPath      = EditorGUILayout.TextField("Output Path (.png)",  outputPath);

        GUILayout.Space(8);
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        pixelSize   = EditorGUILayout.IntField("Frame Pixel Size", pixelSize);

        GUILayout.Space(8);
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
        uvTolerance            = EditorGUILayout.Slider("UV Tolerance (px)", uvTolerance, 0.5f, 5f);
        useFallbackTransparent = EditorGUILayout.Toggle("Unmapped = Transparent", useFallbackTransparent);
        if (!useFallbackTransparent)
            fallbackColor = EditorGUILayout.ColorField("Fallback Color", fallbackColor);

        if (EditorGUI.EndChangeCheck()) SavePrefs();

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

        // --- Load T-pose UV map ---
        Texture2D tposeUV = LoadPNG(tposeUVMapPath);
        if (tposeUV == null) { status = "ERROR: Could not load T-pose UV map: " + tposeUVMapPath; return; }

        int tposeRows = tposeUV.height / pixelSize;
        int tposeCols = tposeUV.width  / pixelSize;
        float tol = uvTolerance / pixelSize;

        // --- Load painted skin (must match tpose dimensions) ---
        Texture2D paintedSkin = LoadPNG(paintedSkinPath);
        if (paintedSkin == null) { status = "ERROR: Could not load painted skin: " + paintedSkinPath; return; }
        if (paintedSkin.width != tposeUV.width || paintedSkin.height != tposeUV.height)
        {
            status = "ERROR: Painted skin is " + paintedSkin.width + "x" + paintedSkin.height +
                     " but T-pose UV map is " + tposeUV.width + "x" + tposeUV.height +
                     ". They must be the same size.";
            return;
        }

        // --- Build UV->Color lookup from all T-pose rows ---
        var lookup = new UVColorLookup();
        for (int row = 0; row < tposeRows; row++)
        {
            Texture2D paintedFrame = ExtractFrame(paintedSkin, 0, row, tposeRows);
            if (paintedFrame == null) continue;
            Color[] paintedPx = paintedFrame.GetPixels();

            for (int col = 0; col < tposeCols; col++)
            {
                Texture2D uvFrame = ExtractFrame(tposeUV, col, row, tposeRows);
                if (uvFrame == null) continue;
                Color[] uvPx = uvFrame.GetPixels();
                for (int i = 0; i < uvPx.Length; i++)
                {
                    if (uvPx[i].a < 0.5f) continue;
                    lookup.Add(uvPx[i].r, uvPx[i].g, paintedPx[i]);
                }
                DestroyImmediate(uvFrame);
            }
            DestroyImmediate(paintedFrame);
        }
        DestroyImmediate(tposeUV);
        DestroyImmediate(paintedSkin);

        Debug.Log("[SpriteBaker] Lookup: " + lookup.Count + " entries from " + tposeRows + " T-pose directions.");
        if (lookup.Count == 0) { status = "ERROR: UV lookup is empty. Check tpose_uvmap.png and pixelSize."; return; }

        // --- Load manifest ---
        if (!File.Exists(manifestPath)) { status = "ERROR: Manifest not found: " + manifestPath; return; }
        SpriteExportManifest manifest = JsonUtility.FromJson<SpriteExportManifest>(File.ReadAllText(manifestPath));
        if (manifest == null || manifest.animations == null || manifest.animations.Count == 0)
        {
            status = "ERROR: Manifest is empty or invalid.";
            return;
        }

        string exportFolder = Path.GetDirectoryName(manifestPath);

        // --- Calculate combined sheet dimensions ---
        // Each animation entry: framesPerDirection cols, directions.Count * rowsPerDirection rows
        // We need the max frame count across all anims for sheet width,
        // and sum of all direction rows for sheet height.
        int maxCols   = 0;
        int totalRows = 0;

        // Store layout per animation: (entry, startRow, uvSheet)
        var layouts = new List<(AnimationEntry entry, int startRow, Texture2D uvSheet)>();

        foreach (var entry in manifest.animations)
        {
            // Skip tpose — it has no uvmap we want to bake (it IS the source)
            if (entry.name.ToLower() == "tpose") continue;

            string uvFile = Path.Combine(exportFolder, manifest.groupName + "_" + manifest.exportPrefix + "_" + entry.name + "_uvmap.png");
            if (!File.Exists(uvFile))
            {
                Debug.LogWarning("[SpriteBaker] No UV map for animation '" + entry.name + "', skipping.");
                continue;
            }

            Texture2D uvSheet = LoadPNG(uvFile);
            if (uvSheet == null) continue;

            int animRows = entry.directions.Count * entry.rowsPerDirection;
            int animCols = entry.framesPerDirection;
            if (entry.rowsPerDirection > 1)
                animCols = manifest.maxFramesWidth > 0 ? manifest.maxFramesWidth : entry.framesPerDirection;

            maxCols = Mathf.Max(maxCols, animCols);
            layouts.Add((entry, totalRows, uvSheet));
            totalRows += animRows;
        }

        if (layouts.Count == 0) { status = "ERROR: No animations to bake. Check UV map files exist alongside manifest."; return; }

        int sheetW = pixelSize * maxCols;
        int sheetH = pixelSize * totalRows;

        Debug.Log("[SpriteBaker] Combined sheet: " + maxCols + " cols x " + totalRows + " rows = " + sheetW + "x" + sheetH + "px");

        Texture2D outSheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
        outSheet.SetPixels(new Color[sheetW * sheetH]); // init transparent

        Color fallback = useFallbackTransparent ? Color.clear : fallbackColor;
        int mappedPx = 0, unmappedPx = 0;

        foreach (var (entry, startRow, uvSheet) in layouts)
        {
            int animSheetRows = uvSheet.height / pixelSize;
            int animSheetCols = uvSheet.width  / pixelSize;

            for (int row = 0; row < animSheetRows; row++)
            {
                for (int col = 0; col < animSheetCols; col++)
                {
                    Texture2D uvFrame = ExtractFrame(uvSheet, col, row, animSheetRows);
                    if (uvFrame == null) continue;

                    Color[] uvPx       = uvFrame.GetPixels();
                    Color[] outFramePx = new Color[pixelSize * pixelSize];

                    for (int i = 0; i < uvPx.Length; i++)
                    {
                        if (uvPx[i].a < 0.5f) { outFramePx[i] = Color.clear; continue; }
                        bool hit = lookup.Sample(uvPx[i].r, uvPx[i].g, tol, out Color found);
                        if (hit) { outFramePx[i] = found; mappedPx++; }
                        else     { outFramePx[i] = fallback; unmappedPx++; }
                    }

                    int globalRow = startRow + row;
                    int destX = col * pixelSize;
                    int destY = (totalRows - 1 - globalRow) * pixelSize;
                    outSheet.SetPixels(destX, destY, pixelSize, pixelSize, outFramePx);
                    DestroyImmediate(uvFrame);
                }
            }

            DestroyImmediate(uvSheet);
            Debug.Log("[SpriteBaker] Baked animation: " + entry.name);
        }

        outSheet.Apply();
        string outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        File.WriteAllBytes(outputPath, outSheet.EncodeToPNG());
        DestroyImmediate(outSheet);

        // Write a baked manifest alongside the output so the game knows the layout
        manifest.combinedSpritesheet = Path.GetFileName(outputPath);
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(outputPath), "baked_manifest.json"),
            JsonUtility.ToJson(manifest, true)
        );

        AssetDatabase.Refresh();

        int   total = mappedPx + unmappedPx;
        float pct   = total > 0 ? mappedPx * 100f / total : 0f;
        status = "Done! " + pct.ToString("F1") + "% pixels mapped (" + unmappedPx + " unmapped).\n" +
                 "Saved to: " + outputPath;
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