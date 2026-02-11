using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using SpriteImporter;

/// <summary>
/// Editor window for importing exported spritesheets.
/// </summary>
public class SpriteCharacterImporter : EditorWindow
{
    private string _importPath = "/Users/bau/Documents/GitHub/SpritesheetTool/Assets/SpriteExports";
    private string _outputPath = "Assets/Sprites/Character";
    private Vector2 _scrollPos;
    private List<string> _foundManifests = new();
    private bool _createPrefab = true;

    [MenuItem("Tools/Sprite Character Importer")]
    public static void ShowWindow()
    {
        GetWindow<SpriteCharacterImporter>("Sprite Importer");
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.LabelField("Sprite Character Importer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Import path selection
        EditorGUILayout.BeginHorizontal();
        _importPath = EditorGUILayout.TextField("Export Folder", _importPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Export Folder", Application.dataPath, "");
            if (!string.IsNullOrEmpty(path))
            {
                _importPath = path;
                ScanForManifests();
            }
        }
        EditorGUILayout.EndHorizontal();

        // Output path
        _outputPath = EditorGUILayout.TextField("Output Path", _outputPath);

        // Options
        _createPrefab = EditorGUILayout.Toggle("Create Character Prefab", _createPrefab);

        EditorGUILayout.Space();

        // Scan button
        if (GUILayout.Button("Scan for Manifests"))
        {
            ScanForManifests();
        }

        // Show found manifests
        if (_foundManifests.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Found {_foundManifests.Count} manifest(s):", EditorStyles.boldLabel);

            foreach (var manifest in _foundManifests)
            {
                EditorGUILayout.LabelField("  " + GetRelativePath(manifest));
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Import All", GUILayout.Height(30)))
            {
                ImportAll();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void ScanForManifests()
    {
        _foundManifests.Clear();

        if (string.IsNullOrEmpty(_importPath) || !Directory.Exists(_importPath))
        {
            Debug.LogWarning("Invalid import path");
            return;
        }

        var manifests = Directory.GetFiles(_importPath, "*manifest.json", SearchOption.AllDirectories);
        _foundManifests.AddRange(manifests);

        Debug.Log($"Found {_foundManifests.Count} manifest files");
    }

    private void ImportAll()
    {
        if (_foundManifests.Count == 0)
        {
            Debug.LogWarning("No manifests to import");
            return;
        }

        // Create output directory
        if (!AssetDatabase.IsValidFolder(_outputPath))
        {
            CreateFolderRecursive(_outputPath);
        }

        List<SpritePartDefinition> importedParts = new();

        foreach (var manifestPath in _foundManifests)
        {
            var part = ImportManifest(manifestPath);
            if (part != null)
            {
                importedParts.Add(part);
            }
        }

        // Create prefab if requested
        if (_createPrefab && importedParts.Count > 0)
        {
            CreateCharacterPrefab(importedParts);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Import complete! Imported {importedParts.Count} part definitions.");
    }

    private SpritePartDefinition ImportManifest(string manifestPath)
    {
        // Read manifest
        string json = File.ReadAllText(manifestPath);
        SpriteExportManifest manifest = JsonUtility.FromJson<SpriteExportManifest>(json);

        if (manifest == null)
        {
            Debug.LogError($"Failed to parse manifest: {manifestPath}");
            return null;
        }

        string manifestDir = Path.GetDirectoryName(manifestPath);
        string partName = manifest.exportPrefix;

        Debug.Log($"Importing: {partName} (sortOrder: {manifest.sortOrder})");

        // Create part definition
        SpritePartDefinition partDef = ScriptableObject.CreateInstance<SpritePartDefinition>();
        partDef.partName = partName;
        partDef.sortOrder = manifest.sortOrder;

        bool isCombined = !string.IsNullOrEmpty(manifest.combinedSpritesheet);

        if (isCombined)
        {
            ImportCombinedSpritesheet(manifest, manifestDir, partName, partDef);
        }
        else
        {
            // Process each animation individually
            foreach (var animEntry in manifest.animations)
            {
                string spritesheetPath = Path.Combine(manifestDir, animEntry.spritesheet);

                if (!File.Exists(spritesheetPath))
                {
                    Debug.LogWarning($"Spritesheet not found: {spritesheetPath}");
                    continue;
                }

                var sprites = ImportAndSliceSpritesheet(
                    spritesheetPath,
                    partName,
                    animEntry.name,
                    manifest.pixelSize,
                    animEntry.framesPerDirection,
                    animEntry.directions.Count,
                    animEntry.rowsPerDirection,
                    manifest.maxFramesWidth
                );

                if (sprites == null) continue;

                SpriteAnimationAsset animAsset = new()
                {
                    animationName = animEntry.name,
                    fps = animEntry.fps,
                    directions = new List<string>(animEntry.directions)
                };

                for (int d = 0; d < animEntry.directions.Count; d++)
                {
                    DirectionSprites dirSprites = new()
                    {
                        direction = animEntry.directions[d],
                        frames = new List<Sprite>()
                    };

                    for (int f = 0; f < animEntry.framesPerDirection; f++)
                    {
                        int spriteIndex = d * animEntry.framesPerDirection + f;
                        if (spriteIndex < sprites.Length)
                        {
                            dirSprites.frames.Add(sprites[spriteIndex]);
                        }
                    }

                    animAsset.directionSprites.Add(dirSprites);
                }

                partDef.animations.Add(animAsset);
            }
        }

        // Save the part definition
        string assetPath = Path.Combine(_outputPath, $"{partName}.asset");
        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
        AssetDatabase.CreateAsset(partDef, assetPath);

        return partDef;
    }

    private Sprite[] ImportAndSliceSpritesheet(
        string sourcePath,
        string partName,
        string animName,
        int pixelSize,
        int framesPerDirection,
        int directionCount,
        int rowsPerDirection = 0,
        int maxFramesWidth = 0)
    {
        // Copy to project if outside Assets
        string destPath = Path.Combine(_outputPath, "Textures", partName, $"{animName}.png");
        string destDir = Path.GetDirectoryName(destPath);

        if (!AssetDatabase.IsValidFolder(destDir))
        {
            CreateFolderRecursive(destDir);
        }

        // Copy file
        File.Copy(sourcePath, destPath, true);
        AssetDatabase.Refresh();

        // Get the relative path for Unity
        string assetPath = destPath;
        if (assetPath.StartsWith(Application.dataPath))
        {
            assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
        }

        // Configure texture importer
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"Failed to get TextureImporter for: {assetPath}");
            return null;
        }

        // Configure for sprites
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = pixelSize;

        // Create sprite metadata for slicing
        int rowsPerDir = rowsPerDirection > 0 ? rowsPerDirection : 1;
        int totalRows = directionCount * rowsPerDir;

        List<SpriteMetaData> spriteData = new();

        for (int d = 0; d < directionCount; d++)
        {
            for (int f = 0; f < framesPerDirection; f++)
            {
                int col = maxFramesWidth > 0 ? f % maxFramesWidth : f;
                int rowWithinDir = maxFramesWidth > 0 ? f / maxFramesWidth : 0;
                int row = d * rowsPerDir + rowWithinDir;

                SpriteMetaData meta = new()
                {
                    name = $"{animName}_{d}_{f}",
                    rect = new Rect(
                        col * pixelSize,
                        (totalRows - 1 - row) * pixelSize,
                        pixelSize,
                        pixelSize
                    ),
                    pivot = new Vector2(0.5f, 0.5f),
                    alignment = (int)SpriteAlignment.Center
                };
                spriteData.Add(meta);
            }
        }

        #pragma warning disable 618  // Suppress obsolete warning - old API still works
        importer.spritesheet = spriteData.ToArray();
        #pragma warning restore 618

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        // Load the sliced sprites
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        List<Sprite> sprites = new();

        foreach (var asset in assets)
        {
            if (asset is Sprite sprite)
            {
                sprites.Add(sprite);
            }
        }

        // Sort sprites by name to ensure correct order
        sprites.Sort((a, b) => string.Compare(a.name, b.name));

        return sprites.ToArray();
    }

    private void ImportCombinedSpritesheet(
        SpriteExportManifest manifest,
        string manifestDir,
        string partName,
        SpritePartDefinition partDef)
    {
        string spritesheetPath = Path.Combine(manifestDir, manifest.combinedSpritesheet);
        if (!File.Exists(spritesheetPath))
        {
            Debug.LogWarning($"Combined spritesheet not found: {spritesheetPath}");
            return;
        }

        // Copy to project
        string destPath = Path.Combine(_outputPath, "Textures", partName, manifest.combinedSpritesheet);
        string destDir = Path.GetDirectoryName(destPath);

        if (!AssetDatabase.IsValidFolder(destDir))
            CreateFolderRecursive(destDir);

        File.Copy(spritesheetPath, destPath, true);
        AssetDatabase.Refresh();

        string assetPath = destPath;
        if (assetPath.StartsWith(Application.dataPath))
            assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"Failed to get TextureImporter for: {assetPath}");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = manifest.pixelSize;

        // Calculate total rows across all animations
        int totalRows = 0;
        foreach (var entry in manifest.animations)
        {
            int rowsPerDir = entry.rowsPerDirection > 0 ? entry.rowsPerDirection : 1;
            totalRows += entry.directions.Count * rowsPerDir;
        }

        int maxFrames = manifest.maxFramesWidth > 0 ? manifest.maxFramesWidth : manifest.sheetWidth;

        // Generate sprite rects for all animations
        List<SpriteMetaData> allSpriteData = new();

        foreach (var entry in manifest.animations)
        {
            int rowsPerDir = entry.rowsPerDirection > 0 ? entry.rowsPerDirection : 1;

            for (int d = 0; d < entry.directions.Count; d++)
            {
                for (int f = 0; f < entry.framesPerDirection; f++)
                {
                    int col = maxFrames > 0 ? f % maxFrames : f;
                    int rowWithinDir = maxFrames > 0 ? f / maxFrames : 0;
                    int row = entry.rowStart + d * rowsPerDir + rowWithinDir;

                    SpriteMetaData meta = new()
                    {
                        name = $"{entry.name}_{d}_{f}",
                        rect = new Rect(
                            col * manifest.pixelSize,
                            (totalRows - 1 - row) * manifest.pixelSize,
                            manifest.pixelSize,
                            manifest.pixelSize
                        ),
                        pivot = new Vector2(0.5f, 0.5f),
                        alignment = (int)SpriteAlignment.Center
                    };
                    allSpriteData.Add(meta);
                }
            }
        }

        #pragma warning disable 618
        importer.spritesheet = allSpriteData.ToArray();
        #pragma warning restore 618

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        // Load all sprites and index by name
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        Dictionary<string, Sprite> spriteMap = new();
        foreach (var asset in assets)
        {
            if (asset is Sprite sprite)
                spriteMap[sprite.name] = sprite;
        }

        // Distribute sprites to animation entries
        foreach (var entry in manifest.animations)
        {
            SpriteAnimationAsset animAsset = new()
            {
                animationName = entry.name,
                fps = entry.fps,
                directions = new List<string>(entry.directions)
            };

            for (int d = 0; d < entry.directions.Count; d++)
            {
                DirectionSprites dirSprites = new()
                {
                    direction = entry.directions[d],
                    frames = new List<Sprite>()
                };

                for (int f = 0; f < entry.framesPerDirection; f++)
                {
                    string spriteName = $"{entry.name}_{d}_{f}";
                    if (spriteMap.TryGetValue(spriteName, out Sprite sprite))
                        dirSprites.frames.Add(sprite);
                }

                animAsset.directionSprites.Add(dirSprites);
            }

            partDef.animations.Add(animAsset);
        }
    }

    private void CreateCharacterPrefab(List<SpritePartDefinition> parts)
    {
        // Sort by sortOrder
        parts.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));

        // Create root object
        GameObject root = new GameObject("LayeredCharacter");
        LayeredSpriteCharacter character = root.AddComponent<LayeredSpriteCharacter>();

        List<SpritePartRenderer> partRenderers = new();

        // Create child objects for each part
        foreach (var part in parts)
        {
            GameObject partObj = new GameObject(part.partName);
            partObj.transform.SetParent(root.transform);
            partObj.transform.localPosition = Vector3.zero;

            SpriteRenderer sr = partObj.AddComponent<SpriteRenderer>();
            sr.sortingOrder = part.sortOrder;

            SpritePartRenderer partRenderer = partObj.AddComponent<SpritePartRenderer>();

            // Use SerializedObject to set the part definition in editor
            SerializedObject so = new SerializedObject(partRenderer);
            so.FindProperty("_partDefinition").objectReferenceValue = part;
            so.ApplyModifiedPropertiesWithoutUndo();

            partRenderers.Add(partRenderer);
        }

        // Set up the character's parts list via SerializedObject
        SerializedObject charSo = new SerializedObject(character);
        SerializedProperty partsProp = charSo.FindProperty("_parts");
        partsProp.arraySize = partRenderers.Count;
        for (int i = 0; i < partRenderers.Count; i++)
        {
            partsProp.GetArrayElementAtIndex(i).objectReferenceValue = partRenderers[i];
        }
        charSo.ApplyModifiedPropertiesWithoutUndo();

        // Save prefab
        string prefabPath = Path.Combine(_outputPath, "LayeredCharacter.prefab");
        prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        DestroyImmediate(root);

        Debug.Log($"Created character prefab at: {prefabPath}");
    }

    private void CreateFolderRecursive(string path)
    {
        path = path.Replace("\\", "/");

        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path).Replace("\\", "/");
        string folderName = Path.GetFileName(path);

        if (!AssetDatabase.IsValidFolder(parent))
        {
            CreateFolderRecursive(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private string GetRelativePath(string fullPath)
    {
        if (string.IsNullOrEmpty(_importPath))
            return fullPath;

        if (fullPath.StartsWith(_importPath))
        {
            return fullPath.Substring(_importPath.Length).TrimStart('/', '\\');
        }

        return fullPath;
    }
}
