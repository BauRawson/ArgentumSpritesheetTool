using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Exports sprite animation sheets from a 3D character.
/// Optionally exports a matching UV map sheet alongside each normal sheet.
/// UV map sheets encode surface UV coordinates as pixel colors (R=U, G=V, A=1).
/// These UV sheets are used by SpriteBakerWindow to bake painted skins.
/// </summary>
public class SpriteAnimationExporter : MonoBehaviour
{
    [Header("Character")]
    public Animator animator;
    public Transform characterRoot;

    [Header("Camera")]
    public Camera captureCamera;
    public int pixelSize = 512;

    [Header("Offset")]
    public int exportYOffset = 11;

    [Header("Export")]
    public SpriteExportBatch batch;
    public string exportRootFolder = "SpriteExports";
    public bool flattenFolders = false;

    [Header("Sheet Layout")]
    public int maxFramesWidth = 0;
    public bool combineAnimations = false;

    [Header("Color Limiting")]
    public bool limitColors = true;
    public Texture2D paletteTexture;

    [Header("UV Map Export")]
    [Tooltip("Export a UV map spritesheet alongside each normal sheet. " +
             "UV sheets have the same layout but encode UV coords as colors (R=U, G=V).")]
    public bool exportUVMaps = false;

    [Tooltip("Material using the UVCapture shader. Created from UVCapture.shader.")]
    public Material uvCaptureMaterial;

    [Tooltip("Only export UV maps for animations whose name contains this string. " +
             "Leave empty to export UV maps for all animations.")]
    public string uvMapOnlyForAnimation = "idle";

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

    void Start() => StartCoroutine(ExportAll());

    IEnumerator ExportAll()
    {
        yield return new WaitForSeconds(0.1f);
        yield return ExportGroup("Body",   body,    0);
        yield return ExportGroup("Legs",   legs,    1);
        yield return ExportGroup("Arms",   arms,    2);
        yield return ExportGroup("Torso",  torso,   3);
        yield return ExportGroup("Weapon", weapons, 4);
        yield return ExportGroup("Shield", shields, 5);
        yield return ExportGroup("Head",   head,    6);
        yield return ExportGroup("Hair",   hair,    7);
        yield return ExportGroup("Helmet", helmets, 8);
        Debug.Log("EXPORT FINISHED");
    }

    IEnumerator ExportGroup(string groupName, List<GameObject> variants, int sortOrder)
    {
        foreach (var v in variants)
            yield return ExportVariant(groupName, v, sortOrder);
    }

    IEnumerator ExportVariant(string groupName, GameObject variant, int sortOrder)
    {
        DisableAll();
        variant.SetActive(true);
        yield return null; // let Unity init skinned mesh

        List<Color> palette = limitColors ? GetPaletteColors(variant) : null;

        string folder = flattenFolders
            ? Path.Combine(Application.dataPath, exportRootFolder)
            : Path.Combine(Application.dataPath, exportRootFolder, groupName, variant.name);
        Directory.CreateDirectory(folder);

        SpriteExportManifest manifest = new()
        {
            groupName    = groupName,
            exportPrefix = variant.name,
            pixelSize    = pixelSize,
            sortOrder    = sortOrder,
            maxFramesWidth = maxFramesWidth
        };

        if (combineAnimations)
            yield return ExportCombinedSheet(folder, manifest, variant, palette);
        else
            foreach (var anim in batch.animations)
                yield return ExportAnimation(anim, folder, manifest, variant, palette);

        string manifestName = flattenFolders
            ? $"{groupName}_{variant.name}_manifest.json" : "manifest.json";
        File.WriteAllText(Path.Combine(folder, manifestName), JsonUtility.ToJson(manifest, true));
    }

    IEnumerator ExportAnimation(
        SpriteAnimationDefinition def,
        string folder,
        SpriteExportManifest manifest,
        GameObject variant,
        List<Color> palette)
    {
        SpriteDirection[] dirs      = def.GetEffectiveDirections();
        string[]          dirNames  = System.Array.ConvertAll(dirs, d => d.ToString());
        float[]           dirAngles = def.GetAngles();
        float[]           xAngles   = def.GetXAngles();
        List<int>         frames    = ResolveFrames(def);

        int effectiveWidth = maxFramesWidth > 0 ? Mathf.Min(frames.Count, maxFramesWidth) : frames.Count;
        int rowsPerDir     = maxFramesWidth > 0 ? Mathf.CeilToInt((float)frames.Count / maxFramesWidth) : 1;
        int totalRows      = dirs.Length * rowsPerDir;
        int sheetW         = pixelSize * effectiveWidth;
        int sheetH         = pixelSize * totalRows;
        int totalClipFrames = Mathf.RoundToInt(def.clip.length * def.clip.frameRate);

        bool captureUV = ShouldCaptureUV(def.name);

        Texture2D colorSheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
        Texture2D uvSheet    = captureUV ? new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false) : null;

        if (captureUV)
        {
            // Initialize UV sheet to transparent
            Color[] clear = new Color[sheetW * sheetH];
            uvSheet.SetPixels(clear);
        }

        for (int d = 0; d < dirs.Length; d++)
        {
            characterRoot.rotation = Quaternion.Euler(xAngles[d], dirAngles[d], 0);

            for (int i = 0; i < frames.Count; i++)
            {
                float time = (float)frames[i] / totalClipFrames * def.clip.length;
                def.clip.SampleAnimation(variant, time);

                // Wait a frame so skinned mesh deforms correctly before capture
                yield return new WaitForEndOfFrame();

                // --- Color frame ---
                Texture2D colorFrame = CaptureColor();
                if (palette != null) QuantizeColors(colorFrame, palette);
                if (exportYOffset != 0) colorFrame = ShiftTexture(colorFrame, exportYOffset);

                // --- UV frame (same pose, swap material, capture, restore) ---
                Texture2D uvFrame = null;
                if (captureUV)
                {
                    yield return new WaitForEndOfFrame(); // extra frame after color capture
                    uvFrame = CaptureUV(variant);
                    if (exportYOffset != 0) uvFrame = ShiftTexture(uvFrame, exportYOffset);
                }

                int col          = maxFramesWidth > 0 ? i % maxFramesWidth : i;
                int rowWithinDir = maxFramesWidth > 0 ? i / maxFramesWidth : 0;
                int row          = d * rowsPerDir + rowWithinDir;
                int destX        = col * pixelSize;
                int destY        = (totalRows - 1 - row) * pixelSize;

                colorSheet.SetPixels(destX, destY, pixelSize, pixelSize, colorFrame.GetPixels());
                if (captureUV && uvFrame != null)
                    uvSheet.SetPixels(destX, destY, pixelSize, pixelSize, uvFrame.GetPixels());

                Destroy(colorFrame);
                if (uvFrame != null) Destroy(uvFrame);
            }
        }

        colorSheet.Apply();
        string colorFile = flattenFolders
            ? $"{manifest.groupName}_{manifest.exportPrefix}_{def.name}.png"
            : $"{def.name}.png";
        File.WriteAllBytes(Path.Combine(folder, colorFile), colorSheet.EncodeToPNG());
        Destroy(colorSheet);

        if (captureUV)
        {
            uvSheet.Apply();
            string uvFile = flattenFolders
                ? $"{manifest.groupName}_{manifest.exportPrefix}_{def.name}_uvmap.png"
                : $"{def.name}_uvmap.png";
            File.WriteAllBytes(Path.Combine(folder, uvFile), uvSheet.EncodeToPNG());
            Destroy(uvSheet);
            Debug.Log($"UV map saved: {uvFile}");
        }

        manifest.animations.Add(new AnimationEntry
        {
            name               = def.name,
            fps                = def.fps,
            framesPerDirection = frames.Count,
            directions         = new List<string>(dirNames),
            rowsPerDirection   = rowsPerDir,
            spritesheet        = colorFile
        });
    }

    IEnumerator ExportCombinedSheet(
        string folder,
        SpriteExportManifest manifest,
        GameObject variant,
        List<Color> palette)
    {
        int sheetWidthInFrames = 0;
        int totalRows = 0;

        var layouts = new List<(SpriteAnimationDefinition def, List<int> frames,
            SpriteDirection[] dirs, float[] angles, float[] xAngles,
            string[] dirNames, int rowsPerDir, int startRow)>();

        foreach (var anim in batch.animations)
        {
            List<int> frames   = ResolveFrames(anim);
            SpriteDirection[] dirs = anim.GetEffectiveDirections();
            string[] dirNames  = System.Array.ConvertAll(dirs, d => d.ToString());
            int rowsPerDir     = maxFramesWidth > 0 ? Mathf.CeilToInt((float)frames.Count / maxFramesWidth) : 1;
            int framesWidth    = maxFramesWidth > 0 ? Mathf.Min(frames.Count, maxFramesWidth) : frames.Count;
            sheetWidthInFrames = Mathf.Max(sheetWidthInFrames, framesWidth);
            layouts.Add((anim, frames, dirs, anim.GetAngles(), anim.GetXAngles(), dirNames, rowsPerDir, totalRows));
            totalRows += dirs.Length * rowsPerDir;
        }

        int sheetW = sheetWidthInFrames * pixelSize;
        int sheetH = totalRows * pixelSize;

        manifest.sheetWidth          = sheetWidthInFrames;
        string colorFileName = flattenFolders ? $"{manifest.groupName}_{variant.name}.png" : $"{variant.name}.png";
        string uvFileName    = flattenFolders ? $"{manifest.groupName}_{variant.name}_uvmap.png" : $"{variant.name}_uvmap.png";
        manifest.combinedSpritesheet = colorFileName;

        Texture2D colorSheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
        bool anyUV = exportUVMaps && uvCaptureMaterial != null;
        Texture2D uvSheet = anyUV ? new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false) : null;

        Color[] clearPx = new Color[sheetW * sheetH];
        colorSheet.SetPixels(clearPx);
        if (uvSheet != null) uvSheet.SetPixels(clearPx);

        foreach (var (def, frames, dirs, angles, xAngles, dirNames, rowsPerDir, startRow) in layouts)
        {
            int totalClipFrames = Mathf.RoundToInt(def.clip.length * def.clip.frameRate);
            bool captureUV = anyUV && ShouldCaptureUV(def.name);

            manifest.animations.Add(new AnimationEntry
            {
                name               = def.name,
                fps                = def.fps,
                framesPerDirection = frames.Count,
                directions         = new List<string>(dirNames),
                spritesheet        = colorFileName,
                rowStart           = startRow,
                rowsPerDirection   = rowsPerDir
            });

            for (int d = 0; d < dirs.Length; d++)
            {
                characterRoot.rotation = Quaternion.Euler(xAngles[d], angles[d], 0);

                for (int i = 0; i < frames.Count; i++)
                {
                    float time = (float)frames[i] / totalClipFrames * def.clip.length;
                    def.clip.SampleAnimation(variant, time);
                    yield return new WaitForEndOfFrame();

                    Texture2D colorFrame = CaptureColor();
                    if (palette != null) QuantizeColors(colorFrame, palette);
                    if (exportYOffset != 0) colorFrame = ShiftTexture(colorFrame, exportYOffset);

                    Texture2D uvFrame = null;
                    if (captureUV)
                    {
                        yield return new WaitForEndOfFrame();
                        uvFrame = CaptureUV(variant);
                        if (exportYOffset != 0) uvFrame = ShiftTexture(uvFrame, exportYOffset);
                    }

                    int col          = maxFramesWidth > 0 ? i % maxFramesWidth : i;
                    int rowWithinDir = maxFramesWidth > 0 ? i / maxFramesWidth : 0;
                    int row          = startRow + d * rowsPerDir + rowWithinDir;
                    int destX        = col * pixelSize;
                    int destY        = (totalRows - 1 - row) * pixelSize;

                    colorSheet.SetPixels(destX, destY, pixelSize, pixelSize, colorFrame.GetPixels());
                    if (captureUV && uvFrame != null)
                        uvSheet.SetPixels(destX, destY, pixelSize, pixelSize, uvFrame.GetPixels());

                    Destroy(colorFrame);
                    if (uvFrame != null) Destroy(uvFrame);
                }
            }
        }

        colorSheet.Apply();
        File.WriteAllBytes(Path.Combine(folder, colorFileName), colorSheet.EncodeToPNG());
        Destroy(colorSheet);

        if (uvSheet != null)
        {
            uvSheet.Apply();
            File.WriteAllBytes(Path.Combine(folder, uvFileName), uvSheet.EncodeToPNG());
            Destroy(uvSheet);
            Debug.Log("UV map sheet saved: " + uvFileName);
        }
    }

    // -----------------------------------------------------------------------
    // UV Capture
    // -----------------------------------------------------------------------

    bool ShouldCaptureUV(string animName)
    {
        if (!exportUVMaps || uvCaptureMaterial == null) return false;
        if (string.IsNullOrEmpty(uvMapOnlyForAnimation)) return true;
        return animName.ToLower().Contains(uvMapOnlyForAnimation.ToLower());
    }

    /// <summary>
    /// Swaps all renderers on the variant to the UV capture material,
    /// renders one frame, then restores originals.
    /// Waits one frame after swap so skinned mesh re-deforms with new material.
    /// </summary>
    Texture2D CaptureUV(GameObject variant)
    {
        Renderer[] renderers = variant.GetComponentsInChildren<Renderer>(true);
        var originals = new Material[renderers.Length][];

        for (int r = 0; r < renderers.Length; r++)
        {
            originals[r] = renderers[r].sharedMaterials;
            var uvMats = new Material[renderers[r].sharedMaterials.Length];
            for (int m = 0; m < uvMats.Length; m++)
                uvMats[m] = uvCaptureMaterial;
            renderers[r].materials = uvMats;
        }

        // Render immediately — we already waited a WaitForEndOfFrame before calling this
        Texture2D tex = Capture();

        for (int r = 0; r < renderers.Length; r++)
            renderers[r].materials = originals[r];

        return tex;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    Texture2D CaptureColor() => Capture();

    Texture2D Capture()
    {
        RenderTexture rt = new RenderTexture(pixelSize, pixelSize, 24);
        captureCamera.targetTexture = rt;
        captureCamera.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(pixelSize, pixelSize, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, pixelSize, pixelSize), 0, 0);
        tex.Apply();
        captureCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        return tex;
    }

    Texture2D ShiftTexture(Texture2D src, int offsetY)
    {
        if (src == null || offsetY == 0) return src;
        int w = src.width, h = src.height;
        Color[] srcPx = src.GetPixels();
        Color[] dstPx = new Color[w * h];
        for (int i = 0; i < dstPx.Length; i++) dstPx[i] = Color.clear;
        for (int y = 0; y < h; y++)
        {
            int dy = y + offsetY;
            if (dy < 0 || dy >= h) continue;
            for (int x = 0; x < w; x++)
                dstPx[dy * w + x] = srcPx[y * w + x];
        }
        Texture2D dst = new Texture2D(w, h, src.format, false);
        dst.SetPixels(dstPx);
        dst.Apply();
        Destroy(src);
        return dst;
    }

    List<int> ResolveFrames(SpriteAnimationDefinition def)
    {
        if (def.frameIndices != null && def.frameIndices.Count > 0) return def.frameIndices;
        if (def.exportedFrames != null && def.exportedFrames.Count > 0) return def.exportedFrames;
        int total = Mathf.RoundToInt(def.clip.length * def.clip.frameRate);
        var frames = new List<int>();
        float step = (float)total / def.framesPerDirection;
        for (int i = 0; i < def.framesPerDirection; i++)
            frames.Add(Mathf.RoundToInt(i * step));
        return frames;
    }

    void DisableAll()
    {
        DisableList(body); DisableList(head);    DisableList(hair);
        DisableList(torso); DisableList(legs);   DisableList(arms);
        DisableList(weapons); DisableList(shields); DisableList(helmets);
    }

    void DisableList(List<GameObject> list)
    {
        foreach (var go in list) if (go) go.SetActive(false);
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
        var unique = new HashSet<Color>(palTex.GetPixels());
        return new List<Color>(unique);
    }

    void QuantizeColors(Texture2D tex, List<Color> palette)
    {
        Color[] px = tex.GetPixels();
        for (int i = 0; i < px.Length; i++) px[i] = FindClosestColor(px[i], palette);
        tex.SetPixels(px);
        tex.Apply();
    }

    Color FindClosestColor(Color c, List<Color> palette)
    {
        Color closest = palette[0];
        float minDist = ColorDistance(c, closest);
        foreach (Color p in palette)
        {
            float d = ColorDistance(c, p);
            if (d < minDist) { minDist = d; closest = p; }
        }
        return new Color(closest.r, closest.g, closest.b, c.a);
    }

    float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db);
    }
}