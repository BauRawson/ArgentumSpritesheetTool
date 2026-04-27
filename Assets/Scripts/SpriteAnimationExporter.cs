using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class SpriteAnimationExporter : MonoBehaviour
{
    [Header("Character")]
    public Animator animator;
    public Transform characterRoot;

    [Header("Camera")]
    public Camera captureCamera;
    public int pixelSize = 512;

    [Header("Offset")]
    [Tooltip("Vertical pixel offset applied to each captured frame (positive moves image up).")]
    public int exportYOffset = 11;

    [Header("Export")]
    public SpriteExportBatch batch;
    public string exportRootFolder = "SpriteExports";
    public bool flattenFolders = false;

    [Header("Sheet Layout")]
    [Tooltip("Max frames per row. 0 = no limit (all frames in one row).")]
    public int maxFramesWidth = 0;
    [Tooltip("Stitch all animations into a single spritesheet per variant.")]
    public bool combineAnimations = false;

    [Header("Square Sheet (512×512, 8×8 grid of 64×64)")]
    [Tooltip("When enabled, exports one square.png per variant instead of the standard layout.")]
    public bool exportSquareSheet = false;
    [Tooltip("Frame size in pixels. 64 → 512×512 sheet.")]
    public int squareFrameSize = 64;
    [Tooltip("Walk animation — 8 frames fill Row A per direction.")]
    public SpriteAnimationDefinition squareWalk;
    [Tooltip("Idle animation — 1 frame at col 0 of Row B per direction.")]
    public SpriteAnimationDefinition squareIdle;
    [Tooltip("Attack animation — 3 frames at cols 1–3 of Row B per direction.")]
    public SpriteAnimationDefinition squareAttack;
    [Tooltip("Block animation — 3 frames at cols 4–6 of Row B per direction.")]
    public SpriteAnimationDefinition squareBlock;

    [Header("Color Limiting")]
    public bool limitColors = true;
    public Texture2D paletteTexture; // If set, uses this palette for all variants. Otherwise, uses each variant's material mainTexture.

    [Header("Parts")]
    public List<GameObject> body;
    public List<GameObject> head;
    public List<GameObject> hair;
    public List<GameObject> torso;
    public List<GameObject> legs;
    public List<GameObject> arms;
    public List<GameObject> weapons;
    public List<GameObject> shields;
    public List<GameObject> helmets;

    // Directions are now driven per-animation by SpriteAnimationDefinition.

    void Start()
    {
        StartCoroutine(ExportAll());
    }

    IEnumerator ExportAll()
    {
        Debug.Log("Starting export in 3 seconds...");
        yield return new WaitForSeconds(0.1f);

        // Sort order: lower = behind, higher = front
        yield return ExportGroup("Body", body, 0);
        yield return ExportGroup("Legs", legs, 1);
        yield return ExportGroup("Arms", arms, 2);
        yield return ExportGroup("Torso", torso, 3);
        yield return ExportGroup("Weapon", weapons, 4);
        yield return ExportGroup("Shield", shields, 5);
        yield return ExportGroup("Head", head, 6);
        yield return ExportGroup("Hair", hair, 7);
        yield return ExportGroup("Helmet", helmets, 8);

        Debug.Log("✅ EXPORT FINISHED");
    }

    IEnumerator ExportGroup(string groupName, List<GameObject> variants, int sortOrder)
    {
        foreach (var variant in variants)
        {
            yield return ExportVariant(groupName, variant, sortOrder);
        }
    }

    IEnumerator ExportVariant(string groupName, GameObject variant, int sortOrder)
    {
        DisableAll();
        variant.SetActive(true);

        // Wait a frame for Unity to initialize the variant's skinned meshes/bones
        yield return null;

        List<Color> paletteColors = limitColors ? GetPaletteColors(variant) : null;

        string folder = flattenFolders
            ? Path.Combine(Application.dataPath, exportRootFolder)
            : Path.Combine(Application.dataPath, exportRootFolder, groupName, variant.name);

        Directory.CreateDirectory(folder);

        SpriteExportManifest manifest = new()
        {
            groupName = groupName,
            exportPrefix = variant.name,
            pixelSize = pixelSize,
            sortOrder = sortOrder,
            maxFramesWidth = maxFramesWidth
        };

        if (exportSquareSheet)
            yield return ExportSquareSheetForVariant(folder, manifest, variant, paletteColors);
        else if (combineAnimations)
            yield return ExportCombinedSheet(folder, manifest, variant, paletteColors);
        else
            foreach (var anim in batch.animations)
                yield return ExportAnimation(anim, folder, manifest, variant, paletteColors);

        string manifestName = flattenFolders ? $"{groupName}_{variant.name}_manifest.json" : "manifest.json";
        File.WriteAllText(
            Path.Combine(folder, manifestName),
            JsonUtility.ToJson(manifest, true)
        );
    }

    IEnumerator ExportAnimation(
        SpriteAnimationDefinition def,
        string folder,
        SpriteExportManifest manifest,
        GameObject variant,
        List<Color> paletteColors)
    {
        SpriteDirection[] dirs = def.GetEffectiveDirections();
        string[] dirNames = System.Array.ConvertAll(dirs, d => d.ToString());
        float[] dirAngles = def.GetAngles();
        float[] xAngles = def.GetXAngles();

        Debug.Log($"Exporting animation: {def.name} (clip: {def.clip.name}, {def.clip.length}s @ {def.clip.frameRate}fps, {dirs.Length} directions)");
        List<int> frames = ResolveFrames(def);

        int effectiveWidth = maxFramesWidth > 0 ? Mathf.Min(frames.Count, maxFramesWidth) : frames.Count;
        int rowsPerDir = maxFramesWidth > 0 ? Mathf.CeilToInt((float)frames.Count / maxFramesWidth) : 1;

        int width = pixelSize * effectiveWidth;
        int height = pixelSize * dirs.Length * rowsPerDir;

        Texture2D sheet =
            new Texture2D(width, height, TextureFormat.RGBA32, false);

        AnimationEntry entry = new()
        {
            name = def.name,
            fps = def.fps,
            framesPerDirection = frames.Count,
            directions = new List<string>(dirNames),
            rowsPerDirection = rowsPerDir
        };

        // Total frames in the clip
        int totalFrames = Mathf.RoundToInt(def.clip.length * def.clip.frameRate);
        int totalRows = dirs.Length * rowsPerDir;

        for (int d = 0; d < dirs.Length; d++)
        {
            characterRoot.rotation =
                Quaternion.Euler(xAngles[d], dirAngles[d], 0);

            for (int i = 0; i < frames.Count; i++)
            {
                // Map frame index to time in seconds
                float time = (float)frames[i] / totalFrames * def.clip.length;

                // Sample directly on the variant
                def.clip.SampleAnimation(variant, time);

                if (d == 0 && i == 0)
                    Debug.Log($"Sampling frame {frames[i]} at time {time:F3}s on {variant.name}");

                yield return new WaitForEndOfFrame();

                Texture2D frame = Capture();

                if (paletteColors != null && paletteColors.Count > 0)
                {
                    QuantizeColors(frame, paletteColors);
                }

                if (exportYOffset != 0)
                    frame = ShiftTexture(frame, exportYOffset);

                int col = maxFramesWidth > 0 ? i % maxFramesWidth : i;
                int rowWithinDir = maxFramesWidth > 0 ? i / maxFramesWidth : 0;
                int row = d * rowsPerDir + rowWithinDir;

                sheet.SetPixels(
                    col * pixelSize,
                    (totalRows - 1 - row) * pixelSize,
                    pixelSize,
                    pixelSize,
                    frame.GetPixels()
                );
            }
        }

        sheet.Apply();

        string fileName = flattenFolders 
            ? $"{manifest.groupName}_{manifest.exportPrefix}_{def.name}.png" 
            : $"{def.name}.png";

        File.WriteAllBytes(
            Path.Combine(folder, fileName),
            sheet.EncodeToPNG()
        );

        entry.spritesheet = fileName;
        manifest.animations.Add(entry);
    }

    IEnumerator ExportCombinedSheet(
        string folder,
        SpriteExportManifest manifest,
        GameObject variant,
        List<Color> paletteColors)
    {
        // First pass: calculate total sheet dimensions
        int sheetWidthInFrames = 0;
        int totalRows = 0;

        var animLayouts = new List<(SpriteAnimationDefinition def, List<int> frames,
            SpriteDirection[] dirs, float[] angles, float[] xAngles, string[] dirNames,
            int rowsPerDir, int startRow)>();

        foreach (var anim in batch.animations)
        {
            List<int> frames = ResolveFrames(anim);
            SpriteDirection[] dirs = anim.GetEffectiveDirections();
            float[] angles = anim.GetAngles();
            float[] xAngles = anim.GetXAngles();
            string[] dirNames = System.Array.ConvertAll(dirs, d => d.ToString());

            int rowsPerDir = maxFramesWidth > 0
                ? Mathf.CeilToInt((float)frames.Count / maxFramesWidth) : 1;
            int framesWidth = maxFramesWidth > 0
                ? Mathf.Min(frames.Count, maxFramesWidth) : frames.Count;

            sheetWidthInFrames = Mathf.Max(sheetWidthInFrames, framesWidth);

            animLayouts.Add((anim, frames, dirs, angles, xAngles, dirNames, rowsPerDir, totalRows));
            totalRows += dirs.Length * rowsPerDir;
        }

        int width = sheetWidthInFrames * pixelSize;
        int height = totalRows * pixelSize;

        Debug.Log($"Combined sheet: {sheetWidthInFrames}x{totalRows} tiles, {width}x{height}px, {animLayouts.Count} animations");

        manifest.sheetWidth = sheetWidthInFrames;
        string combinedFileName = flattenFolders 
            ? $"{manifest.groupName}_{variant.name}.png" 
            : $"{variant.name}.png";
        manifest.combinedSpritesheet = combinedFileName;

        Texture2D combinedSheet = new Texture2D(width, height, TextureFormat.RGBA32, false);

        // Initialize to transparent
        Color[] clearPixels = new Color[width * height];
        combinedSheet.SetPixels(clearPixels);

        // Second pass: render each animation into the combined sheet
        foreach (var (def, frames, dirs, angles, xAngles, dirNames, rowsPerDir, startRow) in animLayouts)
        {
            int totalClipFrames = Mathf.RoundToInt(def.clip.length * def.clip.frameRate);

            Debug.Log($"Combined: {def.name} starts at row {startRow}, {frames.Count} frames, {dirs.Length} dirs, {rowsPerDir} rows/dir");

            AnimationEntry entry = new()
            {
                name = def.name,
                fps = def.fps,
                framesPerDirection = frames.Count,
                directions = new List<string>(dirNames),
                spritesheet = combinedFileName,
                rowStart = startRow,
                rowsPerDirection = rowsPerDir
            };

            for (int d = 0; d < dirs.Length; d++)
            {
                characterRoot.rotation = Quaternion.Euler(xAngles[d], angles[d], 0);

                for (int i = 0; i < frames.Count; i++)
                {
                    float time = (float)frames[i] / totalClipFrames * def.clip.length;
                    def.clip.SampleAnimation(variant, time);

                    yield return new WaitForEndOfFrame();

                    Texture2D frame = Capture();

                    if (paletteColors != null && paletteColors.Count > 0)
                        QuantizeColors(frame, paletteColors);

                    if (exportYOffset != 0)
                        frame = ShiftTexture(frame, exportYOffset);

                    int col = maxFramesWidth > 0 ? i % maxFramesWidth : i;
                    int rowWithinDir = maxFramesWidth > 0 ? i / maxFramesWidth : 0;
                    int row = startRow + d * rowsPerDir + rowWithinDir;

                    combinedSheet.SetPixels(
                        col * pixelSize,
                        (totalRows - 1 - row) * pixelSize,
                        pixelSize,
                        pixelSize,
                        frame.GetPixels()
                    );
                }
            }

            manifest.animations.Add(entry);
        }

        combinedSheet.Apply();

        File.WriteAllBytes(
            Path.Combine(folder, combinedFileName),
            combinedSheet.EncodeToPNG()
        );
    }

    List<int> ResolveFrames(SpriteAnimationDefinition def)
    {
        // Check frameIndices first (populated by AutoGenerateFrames button)
        if (def.frameIndices != null && def.frameIndices.Count > 0)
        {
            Debug.Log($"[{def.name}] Using frameIndices: [{string.Join(", ", def.frameIndices)}]");
            return def.frameIndices;
        }

        // Then check exportedFrames
        if (def.exportedFrames != null && def.exportedFrames.Count > 0)
        {
            Debug.Log($"[{def.name}] Using exportedFrames: [{string.Join(", ", def.exportedFrames)}]");
            return def.exportedFrames;
        }

        // Fallback: generate evenly spaced frames
        int totalFrames = Mathf.RoundToInt(def.clip.length * def.clip.frameRate);
        List<int> frames = new();

        float step = (float)totalFrames / def.framesPerDirection;

        for (int i = 0; i < def.framesPerDirection; i++)
            frames.Add(Mathf.RoundToInt(i * step));

        Debug.Log($"[{def.name}] FALLBACK - Generated {frames.Count} frames: [{string.Join(", ", frames)}]");
        return frames;
    }

    Texture2D Capture()
    {
        RenderTexture rt =
            new RenderTexture(pixelSize, pixelSize, 24);

        captureCamera.targetTexture = rt;
        captureCamera.Render();

        RenderTexture.active = rt;
        Texture2D tex =
            new Texture2D(pixelSize, pixelSize, TextureFormat.RGBA32, false);

        tex.ReadPixels(
            new Rect(0, 0, pixelSize, pixelSize),
            0,
            0
        );
        tex.Apply();

        captureCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);

        return tex;
    }

    Texture2D ShiftTexture(Texture2D src, int offsetY)
    {
        if (src == null || offsetY == 0) return src;
        int w = src.width;
        int h = src.height;

        Color[] srcPixels = src.GetPixels();
        Color[] dstPixels = new Color[w * h];

        Color clear = new Color(0f, 0f, 0f, 0f);
        for (int i = 0; i < dstPixels.Length; i++) dstPixels[i] = clear;

        for (int y = 0; y < h; y++)
        {
            int dy = y + offsetY;
            if (dy < 0 || dy >= h) continue;
            int srcRow = y * w;
            int dstRow = dy * w;
            for (int x = 0; x < w; x++)
                dstPixels[dstRow + x] = srcPixels[srcRow + x];
        }

        Texture2D dst = new Texture2D(w, h, src.format, false);
        dst.SetPixels(dstPixels);
        dst.Apply();

        Destroy(src);
        return dst;
    }

    void DisableAll()
    {
        DisableList(body);
        DisableList(head);
        DisableList(hair);
        DisableList(torso);
        DisableList(legs);
        DisableList(arms);
        DisableList(weapons);
        DisableList(shields);
        DisableList(helmets);
    }

    void DisableList(List<GameObject> list)
    {
        foreach (var go in list)
            if (go) go.SetActive(false);
    }

    List<Color> GetPaletteColors(GameObject variant)
    {
        Texture2D palTex = paletteTexture;
        if (palTex == null)
        {
            Renderer rend = variant.GetComponentInChildren<Renderer>();
            if (rend == null || rend.material.mainTexture == null) return new List<Color>();
            palTex = rend.material.mainTexture as Texture2D;
        }
        if (palTex == null) return new List<Color>();
        Color[] pixels = palTex.GetPixels();
        HashSet<Color> unique = new HashSet<Color>(pixels);
        return new List<Color>(unique);
    }

    void QuantizeColors(Texture2D tex, List<Color> palette)
    {
        Color[] pixels = tex.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = FindClosestColor(pixels[i], palette);
        }
        tex.SetPixels(pixels);
        tex.Apply();
    }

    Color FindClosestColor(Color c, List<Color> palette)
    {
        if (palette.Count == 0) return c;
        Color closest = palette[0];
        float minDist = ColorDistance(c, closest);
        foreach (Color p in palette)
        {
            float dist = ColorDistance(c, p);
            if (dist < minDist)
            {
                minDist = dist;
                closest = p;
            }
        }
        // Preserve original alpha
        return new Color(closest.r, closest.g, closest.b, c.a);
    }

    float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db);
    }

    // ── Square Sheet ────────────────────────────────────────────────────────────
    // Layout (visual top→bottom):
    //   Rows 0-1: South  | Rows 2-3: North  | Rows 4-5: East  | Rows 6-7: West
    //   Row A (even): Walk frames 0-7  (cols 0-7)
    //   Row B (odd):  Idle×1 | Attack×3 | Block×3 | Empty×1  (cols 0-7)

    IEnumerator ExportSquareSheetForVariant(
        string folder,
        SpriteExportManifest manifest,
        GameObject variant,
        List<Color> paletteColors)
    {
        if (squareWalk == null || squareIdle == null || squareAttack == null || squareBlock == null)
        {
            Debug.LogError("[Square] Assign squareWalk, squareIdle, squareAttack, and squareBlock in the Inspector.");
            yield break;
        }

        const int GRID = 8;
        int fs = squareFrameSize;
        int totalPx = fs * GRID;

        Texture2D sheet = new Texture2D(totalPx, totalPx, TextureFormat.RGBA32, false);
        sheet.SetPixels(new Color[totalPx * totalPx]); // transparent

        SpriteDirection[] dirs = { SpriteDirection.S, SpriteDirection.N, SpriteDirection.E, SpriteDirection.W };

        for (int d = 0; d < 4; d++)
        {
            SpriteDirection dir = dirs[d];
            int walkRow   = d * 2;
            int actionRow = d * 2 + 1;

            yield return RenderSquareFrames(variant, squareWalk,   dir, 8, walkRow,   0, sheet, fs, GRID, paletteColors);
            yield return RenderSquareFrames(variant, squareIdle,   dir, 1, actionRow, 0, sheet, fs, GRID, paletteColors);
            yield return RenderSquareFrames(variant, squareAttack, dir, 3, actionRow, 1, sheet, fs, GRID, paletteColors);
            yield return RenderSquareFrames(variant, squareBlock,  dir, 3, actionRow, 4, sheet, fs, GRID, paletteColors);
            // col 7 stays transparent (empty slot)
        }

        sheet.Apply();

        string fileName = flattenFolders
            ? $"{manifest.groupName}_{manifest.exportPrefix}_square.png"
            : "square.png";

        File.WriteAllBytes(Path.Combine(folder, fileName), sheet.EncodeToPNG());

        manifest.combinedSpritesheet = fileName;
        manifest.sheetWidth = GRID;
        manifest.animations.Add(new AnimationEntry
        {
            name = "square",
            fps = squareWalk.fps,
            framesPerDirection = GRID,
            directions = new List<string> { "S", "N", "E", "W" },
            spritesheet = fileName,
            rowsPerDirection = 2
        });

        Debug.Log($"[Square] Exported {fileName} ({totalPx}×{totalPx}px)");
    }

    IEnumerator RenderSquareFrames(
        GameObject variant,
        SpriteAnimationDefinition def,
        SpriteDirection dir,
        int maxFrames,
        int visualRow,
        int startCol,
        Texture2D sheet,
        int fs,
        int gridSize,
        List<Color> paletteColors)
    {
        if (def == null) yield break;

        bool found = false;
        float yAngle = 0f, xAngle = 0f;
        foreach (var dc in def.directionConfigs)
        {
            if (dc.direction == dir) { yAngle = dc.angle; xAngle = dc.xAngle; found = true; break; }
        }
        if (!found) { Debug.LogWarning($"[Square] Direction {dir} not found in {def.name}"); yield break; }

        characterRoot.rotation = Quaternion.Euler(xAngle, yAngle, 0);

        List<int> frames = ResolveFrames(def);
        int totalClipFrames = Mathf.RoundToInt(def.clip.length * def.clip.frameRate);
        int texY = (gridSize - 1 - visualRow) * fs; // flip: visual top → texture bottom

        int count = Mathf.Min(frames.Count, maxFrames);
        for (int i = 0; i < count; i++)
        {
            float time = (float)frames[i] / totalClipFrames * def.clip.length;
            def.clip.SampleAnimation(variant, time);

            yield return new WaitForEndOfFrame();

            Texture2D frame = CaptureAtSize(fs);
            if (paletteColors != null && paletteColors.Count > 0)
                QuantizeColors(frame, paletteColors);
            if (exportYOffset != 0)
                frame = ShiftTexture(frame, exportYOffset);

            sheet.SetPixels((startCol + i) * fs, texY, fs, fs, frame.GetPixels());
            Destroy(frame);
        }
    }

    Texture2D CaptureAtSize(int size)
    {
        RenderTexture rt = new RenderTexture(size, size, 24);
        captureCamera.targetTexture = rt;
        captureCamera.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply();

        captureCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        return tex;
    }
}
