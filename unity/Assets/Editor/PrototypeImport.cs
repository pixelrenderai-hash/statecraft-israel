#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Statecraft.Game;
using Statecraft.Game.Map;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Imports the local Tripo building prototypes (BuildingPreview.Prototypes) from a staging folder holding
/// &lt;name&gt;/fbx/&lt;one .fbx&gt; and, beside it, &lt;same name&gt;.fbm/&lt;one base-colour JPEG&gt;. Copies them to
/// <see cref="Folder"/>/Models/&lt;name&gt;/&lt;name&gt;.fbx and &lt;name&gt;_basecolor.jpg; imports the mesh alone (no cameras,
/// lights, animation, rig, colliders or materials; not readable; global scale 1) and the texture at most
/// <see cref="MaxTexture"/> px with mipmaps, sRGB; makes a matte URP Lit material like MapMaterials; and saves
/// Resources/SettlementPrototypes/&lt;name&gt;.prefab for BuildingPreview.LoadFromResources. Logs
/// "PROTOTYPE &lt;name&gt; tris=&lt;n&gt; bounds=&lt;x,y,z&gt; unitScale=&lt;s&gt;" each. Every source is checked before anything is
/// copied, and a missing one fails the run (exit 1 in batch mode). Idempotent: a re-run replaces the assets.
/// The folder is git-ignored and must stay so: AI-generated models, redistribution rights pending, never committed or
/// shipped (docs/art-prototypes.md); Build refuses a -shareable build while it exists.
/// Run headless (a relative source is taken from the repo root):
/// Unity -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity -executeMethod PrototypeImport.Run -prototypeSource <folder of name/fbx/ exports></summary>
public static class PrototypeImport
{
    public const string Folder = "Assets/Art/Settlements/Prototype";
    const string Models = Folder + "/Models", Prefabs = Folder + "/Resources/" + BuildingPreview.ResourcesFolder;
    public const int MaxTexture = 1024;

    /// <summary>Turn (Euler degrees) on each model before it is measured. Identity to start: the FBX files (Tripo's Blender
    /// preset, read 10 October 2026) are Y-up in metres, each Z-up mesh stood up by its node's -90 about x, which the
    /// importer keeps. Change a row after viewing BuildingPreviewCapture's renders if a model lies on its side or faces away.</summary>
    static readonly Dictionary<string, Vector3> Correction = new Dictionary<string, Vector3>
    {
        ["kibbutz-dining-hall-v1"] = Vector3.zero,
        ["tel-aviv-white-city-v1"] = Vector3.zero,
        ["stone-village-v1"] = Vector3.zero,
    };

    public static void Run()
    {
        try
        {
            var source = Arg("-prototypeSource") ?? throw new ArgumentException("PrototypeImport needs -prototypeSource <dir> (the staging folder holding <name>/fbx/)");
            Import(Path.GetFullPath(Path.Combine(Paths.RepoRoot, source)));
        }
        catch (Exception e)
        {
            Debug.LogError("PROTOTYPE IMPORT FAILED " + e.Message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    public static void Import(string source)
    {
        // Check every source first, so a missing one leaves the folder as it was.
        var (found, missing) = Sources(source);
        foreach (var line in missing) Debug.LogError(line);
        if (missing.Count > 0) throw new FileNotFoundException($"{missing.Count} of {BuildingPreview.Prototypes.Count} prototypes missing under {source}");

        foreach (var (name, fbx, image) in found)
        {
            var dir = EnsureFolder(Models + "/" + name);
            string mesh = $"{dir}/{name}.fbx", tex = $"{dir}/{name}_basecolor.jpg";
            File.Copy(image, tex, true);
            AssetDatabase.ImportAsset(tex, ImportAssetOptions.ForceUpdate);
            ConfigureTexture(tex);
            File.Copy(fbx, mesh, true);
            AssetDatabase.ImportAsset(mesh, ImportAssetOptions.ForceUpdate);
            float unitScale = ConfigureModel(mesh);
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(tex) ?? throw new FileNotFoundException("texture did not import", tex);
            var (tris, size) = SavePrefab(name, mesh, MakeMaterial($"{dir}/{name}.mat", baseMap));
            Debug.Log(FormattableString.Invariant($"PROTOTYPE {name} tris={tris} bounds={size.x:0.###},{size.y:0.###},{size.z:0.###} unitScale={unitScale}"));
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>Each prototype's one .fbx under &lt;source&gt;/&lt;name&gt;/fbx and the one JPEG in its .fbm folder, and a
    /// "PROTOTYPE &lt;name&gt; MISSING" line for each one without exactly one of either. Reads only; public for the tests.</summary>
    public static (List<(string name, string fbx, string image)> found, List<string> missing) Sources(string source)
    {
        var found = new List<(string name, string fbx, string image)>();
        var missing = new List<string>();
        foreach (var name in BuildingPreview.Prototypes)
        {
            var dir = Path.Combine(source, name, "fbx");
            var fbx = Files(dir).Where(f => Ext(f) == ".fbx").ToList();
            if (fbx.Count != 1) { missing.Add($"PROTOTYPE {name} MISSING: {fbx.Count} .fbx files in {dir}, expected one"); continue; }
            var fbm = Path.ChangeExtension(fbx[0], ".fbm");
            var image = Files(fbm).Where(f => Ext(f) == ".jpg" || Ext(f) == ".jpeg").ToList();
            if (image.Count != 1) { missing.Add($"PROTOTYPE {name} MISSING: {image.Count} base-colour JPEGs in {fbm}, expected one"); continue; }
            found.Add((name, fbx[0], image[0]));
        }
        return (found, missing);
    }

    static void ConfigureTexture(string path)
    {
        var t = (TextureImporter)AssetImporter.GetAtPath(path);
        t.textureType = TextureImporterType.Default; t.maxTextureSize = MaxTexture; t.mipmapEnabled = true; t.sRGBTexture = true;
        t.SaveAndReimport();
    }

    /// <summary>The mesh alone, at scale 1; returns the file's own unit scale (the bounds normalise the size either way).</summary>
    static float ConfigureModel(string path)
    {
        var m = (ModelImporter)AssetImporter.GetAtPath(path);
        m.importCameras = false; m.importLights = false;
        m.importAnimation = false; m.animationType = ModelImporterAnimationType.None;
        m.addCollider = false; m.materialImportMode = ModelImporterMaterialImportMode.None;
        m.isReadable = false; m.globalScale = 1f;
        m.SaveAndReimport();
        return m.fileScale;
    }

    /// <summary>Matte URP Lit with the base-colour map, like MapMaterials' marker (Shader.Find is editor-only here; players
    /// get the saved asset). No property block will tint it.</summary>
    static Material MakeMaterial(string path, Texture2D baseMap)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit"); // editor only: fine here
        if (shader == null) throw new InvalidOperationException("shader not found: Universal Render Pipeline/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        else mat.shader = shader;
        mat.enableInstancing = true;
        mat.SetTexture("_BaseMap", baseMap); mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0f); mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>An empty root at the origin over a "pivot" that holds the correction turn and the normalising scale and
    /// offset, over the model exactly as imported (its node's own turn stands the mesh up). Normalised from the measured
    /// renderer bounds: largest horizontal extent 1, bottom at y = 0, footprint centred on the root. Returns the triangle
    /// count and the normalised size.</summary>
    static (long tris, Vector3 size) SavePrefab(string name, string meshPath, Material mat)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(meshPath) ?? throw new FileNotFoundException("model did not import", meshPath);
        var root = new GameObject(name);
        try
        {
            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localRotation = Quaternion.Euler(Correction.TryGetValue(name, out var turn) ? turn : Vector3.zero);
            var instance = Object.Instantiate(model, pivot, false);
            instance.name = "model";
            foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = Enumerable.Repeat(mat, Math.Max(1, r.sharedMaterials.Length)).ToArray();
                r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
            }
            long tris = 0;
            foreach (var f in instance.GetComponentsInChildren<MeshFilter>(true))
                for (int i = 0; f.sharedMesh != null && i < f.sharedMesh.subMeshCount; i++)
                    if (f.sharedMesh.GetTopology(i) == MeshTopology.Triangles) tris += f.sharedMesh.GetIndexCount(i) / 3;
            var b = Measure(root, name);
            float across = Mathf.Max(b.size.x, b.size.z);
            if (across <= 0f) throw new InvalidOperationException($"{name}: the model has no horizontal extent");
            pivot.localScale = Vector3.one / across;
            b = Measure(root, name);   // re-measured after the scale, then moved onto the origin
            pivot.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = Measure(root, name);
            PrefabUtility.SaveAsPrefabAsset(root, $"{EnsureFolder(Prefabs)}/{name}.prefab", out bool saved);
            if (!saved) throw new InvalidOperationException($"{name}: the prefab could not be saved");
            return (tris, b.size);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static Bounds Measure(GameObject go, string name)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) throw new InvalidOperationException($"{name}: the model has no renderer");
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Creates the asset folder and its parents through the AssetDatabase, which writes their .meta files.</summary>
    static string EnsureFolder(string path)
    {
        var parts = path.Split('/');
        var at = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = at + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(at, parts[i]);
            at = next;
        }
        return path;
    }

    // Extensions compared without case (the kibbutz texture is .JPEG) and without the file system's pattern rules.
    static string Ext(string path) => Path.GetExtension(path).ToLowerInvariant();
    static string[] Files(string dir) => Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>();

    static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
