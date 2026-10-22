#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Statecraft.Game.Boot;
using Statecraft.Game.Map;
using Statecraft.Game.Map.Collection;
using Statecraft.Game.Map.Kits;
using Statecraft.Game.Map.Units;
using Statecraft.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Statecraft.Game.EditorTools
{
    /// <summary>Matched renders of the actual game map, without UI Toolkit/HUD. Baseline catalog choices are changed
    /// only in memory, restored in finally, and never saved. No scene, prefab, material or existing source asset is edited.</summary>
    public static class Generated1948GameplayEvidence
    {
        const int Width = 1920, Height = 1080;
        const string ScenePath = "Assets/Map.unity";
        const string AlaRow = "arab_liberation_army";

        [Serializable] sealed class Evidence
        {
            public string kind = "Actual Unity game-map renders; editor, no HUD; not a WebGL/player capture";
            public string state = "", contentHash = "", seed = "", utc = "";
            public int width = Width, height = Height;
            public string baseline = "Original B13a/B17 house choices; ALA uses C09; Egypt/Syria/Iraq have no figure";
            public string overrides = "In-memory catalog entries only; restored in finally; no assets or scenes saved";
            public List<Frame> frames = new List<Frame>();
            public List<string> afterModels = new List<string>(), beforeModels = new List<string>();
        }

        [Serializable] sealed class Frame
        {
            public string file = "", label = "", version = "", quality = "", town = "", changedModel = "";
            public Vector3 lookAt, cameraPosition, cameraEuler;
            public float distanceM, fov, pitch, yaw;
            public int submittedTownInstances;
            public long submittedTownTriangles;
        }

        sealed class Shot
        {
            public string Name = "", Label = "", Town = "", Model = "";
            public Vector3 LookAt;
            public float Distance;
        }

        /// <summary>Run with -captureDir PATH. Add -generated1948Units for matched ALA frames at 20 and 60 km.
        /// The same Content, initial GameState and MapInputs objects are reused on both sides of the comparison.</summary>
        [MenuItem("Statecraft/Generated 1948/Capture matched gameplay evidence")]
        public static void Capture()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Capture outside play mode.");
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-captureDir");
            string output = Path.GetFullPath(at >= 0 && at + 1 < args.Length ? args[at + 1]
                : Path.Combine(Paths.RepoRoot, "captures", "generated-1948-gameplay"));
            bool units = Array.IndexOf(args, "-generated1948Units") >= 0;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new FileNotFoundException("Use the existing game scene; this tool does not create or save it.", ScenePath);
            int pc = Array.IndexOf(QualitySettings.names, "PC");
            if (pc < 0) throw new InvalidOperationException("The PC quality tier is missing.");
            var set = AssetDatabase.LoadAssetAtPath<TownModelSet>(TownModels.SetPath);
            if (set == null) throw new FileNotFoundException("Run TownModelAssets.Create first.", TownModels.SetPath);
            set.Validate();
            var content = ContentLoader.Load(Paths.Content);
            var state = Simulation.NewGame(content, GoldenRun.Seed, Mode.Historical);
            var inputs = GameInputs.MapFromDisk(Paths.Content, Paths.StreamingMap);
            var rows = UnitData.Parse(File.ReadAllText(Path.Combine(Paths.Content, "map", UnitData.DefaultFileName)));
            var evidence = new Evidence { state = state.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                contentHash = content.Hash, seed = GoldenRun.Seed.ToString(CultureInfo.InvariantCulture), utc = DateTime.UtcNow.ToString("O") };
            int quality = QualitySettings.GetQualityLevel();
            var source = UnitFigures.Source;
            var side = UnitFigures.BySide as IDictionary<string, string>
                ?? throw new InvalidOperationException("The side catalog no longer supports this editor-only comparison.");
            var savedSides = side.ToArray();
            MapScene? map = null;
            TownModel[]? netanya = null, tiberias = null, savedNetanya = null, savedTiberias = null;
            try
            {
                QualitySettings.SetQualityLevel(pc, true);
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                map = MapCapture.RequireMapScene();
                map.ConfigureTownModels(set);
                map.ShowcaseClearing = false;
                map.RouteDate = state.Date;
                UnitFigures.Source = (id, detail, ready) => ready(AssetDatabase.LoadAssetAtPath<GameObject>(CollectionCatalog.PrefabPath(id, detail)));
                UnitFigures.Forget();
                map.Build(inputs);
                var n = map.Settlements.Single(s => s.Id == "netanya");
                var t = map.Settlements.Single(s => s.Id == "tiberias");
                // These public selectors return the very arrays read by the town builders. Mutate their elements only;
                // retaining their length preserves the hash draw and flip. Do not assign readonly fields or save assets.
                netanya = TownModels.ForArchetype(SettlementKits.ArchetypeOf(n), n);
                tiberias = TownModels.ForArchetype(SettlementKits.ArchetypeOf(t), t);
                savedNetanya = (TownModel[])netanya.Clone();
                savedTiberias = (TownModel[])tiberias.Clone();
                var shots = HouseShots(map);
                UnitLayer? layer = units ? ShowUnits(map, rows, content, state) : null;
                if (layer != null)
                {
                    var ala = layer.Tokens.Single(p => p.Token.Unit.Id == AlaRow);
                    var centre = new Vector3((float)ala.Spot.X, 0f, (float)ala.Spot.Z);
                    centre.y = Ground(map, centre) + UnitBadge.FigureHeightM * UnitShowcase.FigureScale * .4f;
                    shots.Add(new Shot { Name = "ala20", Label = "ALA: current own-side infantry versus previous C09",
                        Model = UnitFigures.AlaInfantry, LookAt = centre, Distance = 20000f });
                    shots.Add(new Shot { Name = "ala60", Label = "ALA at gameplay zoom", Model = UnitFigures.AlaInfantry,
                        LookAt = centre, Distance = 60000f });
                }
                Directory.CreateDirectory(output);
                CaptureVersion(map, layer, shots, "after", output, evidence);
                evidence.afterModels.AddRange(ModelSummary(map));

                Array.Fill(netanya, TownModel.B13aTwoStorey);
                Array.Fill(tiberias, TownModel.B17CourtyardHouse);
                side.Remove("egypt"); side.Remove("syria"); side.Remove("iraq"); side["ala"] = UnitFigures.Irregular;
                UnitFigures.Forget();
                map.Build(inputs);
                layer = units ? ShowUnits(map, rows, content, state) : null;
                if (layer != null && layer.Tokens.Single(p => p.Token.Unit.Id == AlaRow).Token.ArtType != UnitFigures.Irregular)
                    throw new InvalidOperationException("The ALA baseline did not restore C09.");
                if (Instances(map).Any(i => i.Model == TownModel.NetanyaBalcony2026 || i.Model == TownModel.NetanyaPorch2026 || i.Model == TownModel.TiberiasStone2026))
                    throw new InvalidOperationException("A generated house remained in the original-only baseline.");
                CaptureVersion(map, layer, shots, "before", output, evidence);
                evidence.beforeModels.AddRange(ModelSummary(map));
                File.WriteAllText(Path.Combine(output, "evidence.json"), JsonUtility.ToJson(evidence, true) + "\n");
                File.WriteAllText(Path.Combine(output, "README.txt"),
                    "Actual Unity game-map renders without HUD. Identical content/new-game state, image dimensions, camera poses and PC tier.\n" +
                    "*-before.png: original house selections; ALA C09; Egypt/Syria/Iraq unrendered.\n" +
                    "*-after.png: current branch catalog and models. No units are moved and no deployment is invented.\n" +
                    "Changed area is centered; per-frame town/model labels, camera values, state and selected model counts are in evidence.json.\n" +
                    "Water time is pinned at 100 s; unit idle pose is sampled at time zero. These are not HUD/player/WebGL screenshots.\n");
                Debug.Log("Generated1948GameplayEvidence: saved " + evidence.frames.Count + " actual map frames to " + output);
            }
            finally
            {
                if (netanya != null && savedNetanya != null) Array.Copy(savedNetanya, netanya, netanya.Length);
                if (tiberias != null && savedTiberias != null) Array.Copy(savedTiberias, tiberias, tiberias.Length);
                side.Clear(); foreach (var pair in savedSides) side.Add(pair.Key, pair.Value);
                // Leave an interactive editor showing the real current catalog, never the comparison baseline.
                try
                {
                    UnitFigures.Forget();
                    if (map != null)
                    {
                        map.Build(inputs);
                        if (units) ShowUnits(map, rows, content, state);
                    }
                }
                finally
                {
                    UnitFigures.Source = source;
                    UnitFigures.Forget();
                    MapShaderGlobals.PinWaterTime(null);
                    QualitySettings.SetQualityLevel(quality, true);
                }
            }
        }

        static IEnumerable<SettlementKits.ModelInstance> Instances(MapScene map) =>
            map.GetComponentsInChildren<TownModelLayer>(true).SelectMany(l => l.InstancesOf("netanya").Concat(l.InstancesOf("tiberias")));

        static IEnumerable<string> ModelSummary(MapScene map) => Instances(map).GroupBy(i => (i.Id, i.Model))
            .OrderBy(g => g.Key.Id, StringComparer.Ordinal).ThenBy(g => g.Key.Model)
            .Select(g => g.Key.Id + ":" + g.Key.Model + "=" + g.Count());

        static List<Shot> HouseShots(MapScene map)
        {
            var all = Instances(map).ToArray();
            var shots = new List<Shot>();
            foreach (var (town, model, name) in new[] { ("netanya", TownModel.NetanyaBalcony2026, "netanya-balcony06"),
                ("netanya", TownModel.NetanyaPorch2026, "netanya-porch06"), ("tiberias", TownModel.TiberiasStone2026, "tiberias-stone06") })
            {
                var settlement = map.Settlements.Single(s => s.Id == town);
                var home = map.Frame.ToWorld(settlement.Lon, settlement.Lat, 0f);
                var candidates = all.Where(i => i.Id == town && i.Model == model).OrderBy(i => (i.C - new Vector2(home.x, home.z)).sqrMagnitude).ToArray();
                if (candidates.Length == 0) throw new InvalidOperationException("No actually placed " + model + " in " + town);
                var instance = candidates[0];
                shots.Add(new Shot { Name = name, Town = town, Model = model.ToString(), Label = town + ": " + model,
                    LookAt = new Vector3(instance.C.x, instance.Ground + instance.H * .5f, instance.C.y), Distance = 6000f });
            }
            foreach (string town in new[] { "netanya", "tiberias" })
            {
                var s = map.Settlements.Single(p => p.Id == town);
                var home = map.Frame.ToWorld(s.Lon, s.Lat, 0f); home.y = Ground(map, home);
                shots.Add(new Shot { Name = town + "12", Label = town + " town context", Town = town, LookAt = home, Distance = 12000f });
            }
            return shots;
        }

        static float Ground(MapScene map, Vector3 at) => map.Terrain.SampleHeight(at) + map.Terrain.transform.position.y;

        static UnitLayer ShowUnits(MapScene map, List<UnitEntry> rows, Content content, GameState state)
        {
            // Real game prefabs loaded synchronously, so the editor capture observes the arrived state, without fake meshes.
            foreach (var id in UnitFigures.BySide.Values.Distinct()) UnitFigures.Prefetch(id);
            var layer = UnitLayer.Create(map, rows, content.Hexes, still: true);
            layer.Show(content, state);
            return layer;
        }

        static void CaptureVersion(MapScene map, UnitLayer? layer, List<Shot> shots, string version, string output, Evidence evidence)
        {
            var cam = Camera.main ?? throw new InvalidOperationException("No main game camera.");
            var rig = cam.GetComponent<CameraRig>();
            foreach (var shot in shots)
            {
                rig.LookAt = shot.LookAt; rig.Distance = shot.Distance; rig.Yaw = 10f; rig.TopInset = 0f; rig.Apply();
                MapShaderGlobals.PinWaterTime(100f);
                layer?.FaceCamera(cam);
                var file = shot.Name + "-" + version + ".png";
                Save(cam, Path.Combine(output, file));
                var layers = map.GetComponentsInChildren<TownModelLayer>(true);
                evidence.frames.Add(new Frame { file = file, label = shot.Label, town = shot.Town, changedModel = shot.Model,
                    version = version, quality = QualitySettings.names[QualitySettings.GetQualityLevel()], lookAt = rig.LookAt,
                    cameraPosition = cam.transform.position, cameraEuler = cam.transform.eulerAngles, distanceM = rig.Distance,
                    fov = cam.fieldOfView, pitch = CameraRig.Pitch, yaw = rig.Yaw,
                    submittedTownInstances = layers.Sum(l => l.LastDrawn), submittedTownTriangles = layers.Sum(l => l.LastTriangles) });
            }
        }

        static void Save(Camera cam, string path)
        {
            var previousTarget = cam.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(Width, Height, 24);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = target;
                cam.Render(); // Let the normal render callback instantiate the close Collection prefabs first.
                foreach (var animator in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                    if (animator.enabled && animator.runtimeAnimatorController != null) { animator.Rebind(); animator.Update(0f); }
                cam.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = previousTarget; RenderTexture.active = previousActive;
                Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            }
        }
    }
}
