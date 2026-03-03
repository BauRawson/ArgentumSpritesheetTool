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
    public int pixelSize = 64;

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
    public bool exportUVMaps = false;
    public Material uvCaptureMaterial;

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
        yield return null;

        List<Color> palette = limitColors ? GetPaletteColors(variant) : null;

        string folder = flattenFolders
            ? Path.Combine(Application.dataPath, exportRootFolder)
            : Path.Combine(Application.dataPath, exportRootFolder, groupName, variant.name);
        Directory.CreateDirectory(folder);

        SpriteExportManifest manifest = new()
        {
            groupName      = groupName,
            exportPrefix   = variant.name,
            pixelSize      = pixelSize,
            sortOrder      = sortOrder,
            maxFramesWidth = maxFramesWidth
        };

        foreach (var anim in batch.animations)
            yield return ExportAnimation(anim, folder, manifest, variant, palette);

        string manifestName = flattenFolders
            ? groupName + "_" + variant.name + "_manifest.json" : "manifest.json";
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
        string[]   dirNames         = System.Array.ConvertAll(dirs, d => d.ToString());
        float[]    dirAngles        = def.GetAngles();
        float[]    xAngles          = def.GetXAngles();
        List<int>  frames           = ResolveFrames(def);
        int totalClipFrames = Mathf.RoundToInt(def.clip.length * def.clip.frameRate);

        int effectiveWidth = maxFramesWidth > 0 ? Mathf.Min(frames.Count, maxFramesWidth) : frames.Count;
        int rowsPerDir     = maxFramesWidth > 0 ? Mathf.CeilToInt((float)frames.Count / maxFramesWidth) : 1;
        int totalRows      = dirs.Length * rowsPerDir;
        int sheetW         = pixelSize * effectiveWidth;
        int sheetH         = pixelSize * totalRows;

        bool captureUV = exportUVMaps && uvCaptureMaterial != null;

        // uvCoverageOnly: export UV map only, no color sheet (used for T-pose)
        Texture2D colorSheet = def.uvCoverageOnly ? null
            : new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
        Texture2D uvSheet = captureUV
            ? new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false) : null;

        if (uvSheet != null)
            uvSheet.SetPixels(new Color[sheetW * sheetH]);

        for (int d = 0; d < dirs.Length; d++)
        {
            characterRoot.rotation = Quaternion.Euler(xAngles[d], dirAngles[d], 0);

            for (int i = 0; i < frames.Count; i++)
            {
                float time = (float)frames[i] / totalClipFrames * def.clip.length;
                def.clip.SampleAnimation(variant, time);
                yield return new WaitForEndOfFrame();

                int col          = maxFramesWidth > 0 ? i % maxFramesWidth : i;
                int rowWithinDir = maxFramesWidth > 0 ? i / maxFramesWidth : 0;
                int row          = d * rowsPerDir + rowWithinDir;
                int destX        = col * pixelSize;
                int destY        = (totalRows - 1 - row) * pixelSize;

                // Color frame — skip for uvCoverageOnly animations
                if (!def.uvCoverageOnly)
                {
                    Texture2D colorFrame = CaptureColor();
                    if (palette != null) QuantizeColors(colorFrame, palette);
                    if (exportYOffset != 0) colorFrame = ShiftTexture(colorFrame, exportYOffset);
                    colorSheet.SetPixels(destX, destY, pixelSize, pixelSize, colorFrame.GetPixels());
                    Destroy(colorFrame);
                }

                // UV frame
                if (captureUV)
                {
                    yield return new WaitForEndOfFrame();
                    Texture2D uvFrame = CaptureUV(variant);
                    if (exportYOffset != 0) uvFrame = ShiftTexture(uvFrame, exportYOffset);
                    uvSheet.SetPixels(destX, destY, pixelSize, pixelSize, uvFrame.GetPixels());
                    Destroy(uvFrame);
                }
            }
        }

        // Save color sheet (skip for uvCoverageOnly)
        if (!def.uvCoverageOnly && colorSheet != null)
        {
            colorSheet.Apply();
            string colorFile = flattenFolders
                ? manifest.groupName + "_" + manifest.exportPrefix + "_" + def.name + ".png"
                : def.name + ".png";
            File.WriteAllBytes(Path.Combine(folder, colorFile), colorSheet.EncodeToPNG());
            Destroy(colorSheet);
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

        // Save UV map sheet
        if (captureUV && uvSheet != null)
        {
            uvSheet.Apply();
            string uvFile = flattenFolders
                ? manifest.groupName + "_" + manifest.exportPrefix + "_" + def.name + "_uvmap.png"
                : def.name + "_uvmap.png";
            File.WriteAllBytes(Path.Combine(folder, uvFile), uvSheet.EncodeToPNG());
            Destroy(uvSheet);
            Debug.Log("[Exporter] UV map saved: " + uvFile + " (" + dirs.Length + " dirs, " + frames.Count + " frames)");
        }
    }

    Texture2D CaptureUV(GameObject variant)
    {
        Renderer[] renderers = variant.GetComponentsInChildren<Renderer>(true);
        var originals = new Material[renderers.Length][];
        for (int r = 0; r < renderers.Length; r++)
        {
            originals[r] = renderers[r].sharedMaterials;
            var uvMats = new Material[renderers[r].sharedMaterials.Length];
            for (int m = 0; m < uvMats.Length; m++) uvMats[m] = uvCaptureMaterial;
            renderers[r].materials = uvMats;
        }
        Texture2D tex = Capture();
        for (int r = 0; r < renderers.Length; r++)
            renderers[r].materials = originals[r];
        return tex;
    }

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
        Color[] srcPx = src.GetPixels(), dstPx = new Color[w * h];
        for (int i = 0; i < dstPx.Length; i++) dstPx[i] = Color.clear;
        for (int y = 0; y < h; y++)
        {
            int dy = y + offsetY;
            if (dy < 0 || dy >= h) continue;
            for (int x = 0; x < w; x++) dstPx[dy * w + x] = srcPx[y * w + x];
        }
        Texture2D dst = new Texture2D(w, h, src.format, false);
        dst.SetPixels(dstPx); dst.Apply();
        Destroy(src);
        return dst;
    }

    List<int> ResolveFrames(SpriteAnimationDefinition def)
    {
        if (def.frameIndices   != null && def.frameIndices.Count   > 0) return def.frameIndices;
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
        tex.SetPixels(px); tex.Apply();
    }

    Color FindClosestColor(Color c, List<Color> palette)
    {
        Color closest = palette[0]; float minDist = ColorDistance(c, closest);
        foreach (Color p in palette) { float d = ColorDistance(c, p); if (d < minDist) { minDist = d; closest = p; } }
        return new Color(closest.r, closest.g, closest.b, c.a);
    }

    float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db);
    }
}