#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Statecraft.Game;
using Statecraft.Game.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Before/after editor renders of the opt-in building prototypes (BuildingPreview, PrototypeImport): for each pose,
/// &lt;-captureDir&gt;/before-&lt;pose&gt;.png with the marker boxes and after-&lt;pose&gt;.png with the prototypes from
/// Resources, 1920x1080 at the current quality tier, the CameraRig's pitch and FOV, no UI (editor renders show no UI
/// Toolkit labels). Opens and builds Map.unity as MapCapture does, without saving it; leaves the boxes shown again. Fails
/// (exit 1 in batch mode) when a prototype does not load. A relative directory is taken from the repo root; the default,
/// captures/building-preview, is git-ignored.
/// Unity -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity -executeMethod BuildingPreviewCapture.Run [-captureDir &lt;dir&gt;]</summary>
public static class BuildingPreviewCapture
{
    const string ScenePath = "Assets/Map.unity";

    /// <summary>Poses of its own, not PromoPose's lists (tests pin PromoPose.All and CloseChecks, and art_checkpoint wants a
    /// WebGL shot of every pose in PromoPose.cs). Gameplay03 is the art checkpoint's own frame.</summary>
    public static IReadOnlyList<PromoPose> Poses { get; } = new[]
    {
        new PromoPose("tel_aviv", 34.78, 32.075, 9000f, 10f),      // the White City cluster close up
        new PromoPose("lod_ramla", 34.88, 31.94, 9000f, 10f),      // the stone-village stand-in on two arab_towns
        new PromoPose("degania", 35.5747, 32.7081, 6000f, 10f),    // a kibbutz hall at the 6 km hero zoom
        PromoPose.Gameplay03,
    };

    public static void Run()
    {
        try { Capture(Path.GetFullPath(Path.Combine(Paths.RepoRoot, Arg("-captureDir") ?? "captures/building-preview"))); }
        catch (Exception e)
        {
            Debug.LogError("BUILDING PREVIEW CAPTURE FAILED " + e.Message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    static void Capture(string dir)
    {
        var missing = BuildingPreview.Prototypes.Where(n => BuildingPreview.LoadFromResources(n) == null).ToList();
        if (missing.Count > 0)
            throw new FileNotFoundException($"not under Resources/{BuildingPreview.ResourcesFolder} (run PrototypeImport first): {string.Join(", ", missing)}");
        // MapCapture.OpenAndBuild: open the scene (made only when missing), build in memory, never save.
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) MapCapture.MakeScene();
        else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var map = MapCapture.RequireMapScene(); map.Build();
        var cam = Camera.main;
        var rig = cam.GetComponent<CameraRig>();
        try
        {
            BuildingPreview.Apply(map, false, BuildingPreview.LoadFromResources);
            foreach (var pose in Poses) Shoot(cam, rig, map, pose, Path.Combine(dir, $"before-{pose.Name}.png"));
            int placed = BuildingPreview.Apply(map, true, BuildingPreview.LoadFromResources);
            if (placed == 0) throw new InvalidOperationException("no prototype was placed: the after images would equal the before ones");
            foreach (var pose in Poses) Shoot(cam, rig, map, pose, Path.Combine(dir, $"after-{pose.Name}.png"));
            Debug.Log($"BUILDING PREVIEW CAPTURE {placed} prototypes, {Poses.Count} poses, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} -> {dir}");
        }
        finally { BuildingPreview.Apply(map, false, BuildingPreview.LoadFromResources); }
    }

    static void Shoot(Camera cam, CameraRig rig, MapScene map, PromoPose pose, string path)
    {
        pose.Apply(rig, map.Frame);
        Save(cam, 1920, 1080, path);
    }

    /// <summary>MapCapture.Save: one camera render read back into a PNG.</summary>
    static void Save(Camera cam, int w, int h, string path)
    {
        var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }

    static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
