using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Statecraft.Game.EditorTools
{
    /// <summary>Import and inspect candidates without adding unsourced placements to the runtime catalog.</summary>
    public static class Generated1948Review
    {
        public const string Folder = "Assets/Art/Generated1948Review";

        [MenuItem("Statecraft/Generated 1948/Import review candidates")]
        public static void Import()
        {
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(Folder, "*-near.fbx", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (Path.GetFileName(path).StartsWith("figure-", StringComparison.Ordinal)) continue;
                var file = path.Replace('\\', '/');
                var id = Path.GetFileNameWithoutExtension(file).Replace("-near", "");
                var dir = Path.GetDirectoryName(file).Replace('\\', '/');
                var imp = (ModelImporter)AssetImporter.GetAtPath(file);
                imp.useFileScale = true; // Exports bake units into FBX metadata, matching the existing models.
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importBlendShapes = false;
                imp.importVisibility = false;
                imp.importCameras = false;
                imp.importLights = false;
                imp.addCollider = false;
                imp.meshCompression = ModelImporterMeshCompression.Medium;
                imp.isReadable = false;
                imp.importNormals = ModelImporterNormals.Import;
                imp.importTangents = ModelImporterTangents.None;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.SaveAndReimport();
                var texPath = dir + "/" + id + "_basecolor.jpg";
                var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
                ti.sRGBTexture = true;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 512;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.crunchedCompression = true;
                ti.compressionQuality = 50;
                ti.SaveAndReimport();
                var matPath = dir + "/" + id + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = id };
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
                mat.SetFloat("_Smoothness", 0f);
                mat.SetFloat("_Cull", 2f); // Front-sided review exposes roof gaps rather than concealing them.
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(file));
                try
                {
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
                    PrefabUtility.SaveAsPrefabAsset(go, dir + "/" + id + ".prefab");
                }
                finally { Object.DestroyImmediate(go); }
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Statecraft/Generated 1948/Capture review candidates")]
        public static void Capture()
        {
            var output = Path.GetFullPath("../captures/generated-1948-review");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.75f, .75f, .75f);
            var light = new GameObject("Review light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            var cam = new GameObject("Review camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.35f, .42f, .46f);
            cam.orthographic = true;
            cam.orthographicSize = 1.35f;
            foreach (var file in Directory.GetFiles(Folder, "*.prefab", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(file));
                try
                {
                    var renderers = go.GetComponentsInChildren<Renderer>(true);
                    var b = renderers[0].bounds;
                    foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                    go.transform.localScale *= 2f / Mathf.Max(b.size.x, b.size.y, b.size.z);
                    b = renderers[0].bounds;
                    foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                    go.transform.position -= b.center;
                    foreach (var view in new[] { ("front", new Vector3(3f, 1.5f, 4f)), ("rear", new Vector3(-3f, 1.5f, -4f)), ("overhead", new Vector3(2f, 5f, 3f)) })
                    {
                        cam.transform.position = view.Item2;
                        cam.transform.LookAt(Vector3.zero);
                        var rt = new RenderTexture(768, 768, 24);
                        var image = new Texture2D(768, 768, TextureFormat.RGB24, false);
                        cam.targetTexture = rt;
                        cam.Render();
                        RenderTexture.active = rt;
                        image.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
                        image.Apply();
                        File.WriteAllBytes(Path.Combine(output, Path.GetFileNameWithoutExtension(file) + "-" + view.Item1 + ".png"), image.EncodeToPNG());
                        cam.targetTexture = null;
                        RenderTexture.active = null;
                        Object.DestroyImmediate(rt);
                        Object.DestroyImmediate(image);
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
            Debug.Log("Generated1948Review: captures are Unity asset previews, not gameplay captures: " + output);
        }
    }
}
