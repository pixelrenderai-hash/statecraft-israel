#nullable enable
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Statecraft.Game;
using Statecraft.Game.Map;
using Statecraft.Game.Presentation;
using Statecraft.Game.UI;
using Statecraft.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public class WarRoomTests
{
    static readonly Content C = ContentLoader.Load(Paths.Content);
    static SimBridge Bridge() => new SimBridge(GameSession.New(C, GoldenRun.Seed, Mode.Historical),
        UiText.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(UiPaths.Strings).text));

    [TestCase("https://localhost/?experience=war-room", true)]
    [TestCase("https://localhost/?map-look&experience=war-room#play", true)]
    [TestCase("https://localhost/experience=war-room", false)]
    [TestCase("https://localhost/?experience=war-room-extra", false)]
    [TestCase("https://localhost/?experience=command", false)]
    [TestCase("https://localhost/#?experience=war-room", false)]
    [TestCase("https://localhost/?return=https://example/?experience=war-room", false)]
    public void OnlyTheExactTopLevelFlagEnablesThePresentation(string url, bool expected) =>
        Assert.AreEqual(expected, WarRoomPresentation.Requested(url, Array.Empty<string>(), true));

    [Test]
    public void DesktopFlagRequiresTheExactValue()
    {
        Assert.IsTrue(WarRoomPresentation.Requested("", new[] { "app", "-experience", "war-room" }, false));
        Assert.IsFalse(WarRoomPresentation.Requested("?experience=war-room", new[] { "app", "-experience" }, false));
        Assert.IsFalse(WarRoomPresentation.Requested("", new[] { "app", "-experience", "war-room-extra" }, false));
    }

    [Test]
    public void ReportsUnitsAndTheBriefOnlyReadSimulationState()
    {
        var b = Bridge(); var opening = b.State.Clone(); var hash = b.State.Hash();
        var m = WarRoomModel.From(b, opening);
        Assert.AreEqual(hash, b.State.Hash()); Assert.IsEmpty(b.Queued);
        Assert.AreEqual("15 May 1948", m.Date);
        Assert.AreEqual("IL 20.0m", m.Resources[0].Value);
        Assert.AreEqual("$40.0m", m.Resources[1].Value);
        Assert.AreEqual(6, m.Resources.Length);
        Assert.AreEqual(0, m.ElapsedTurns); Assert.AreEqual(3, m.Goals.Length);
        StringAssert.Contains("Defence Ministry", m.Report);
        Assert.Greater(m.BrigadeSymbols, 0);
        Assert.AreEqual(0, m.ShipSymbols, "no arrivals before a turn has run");
        Assert.AreEqual(0, m.CampTruckSymbols, "no invented camp placements");
    }

    [Test]
    public void FiveTurnSummaryAndShipsFollowTheActualSession()
    {
        var b = Bridge(); var opening = b.State.Clone();
        for (int i = 0; i < 5; i++) b.EndTurn();
        var hash = b.State.Hash(); var m = WarRoomModel.From(b, opening);
        Assert.AreEqual(5, m.ElapsedTurns); Assert.IsTrue(m.BriefComplete);
        Assert.AreEqual(b.Session.Last!.Why.Where(w => w.System == "population").Sum(w => w.Delta), m.Arrivals);
        Assert.Greater(m.ShipSymbols, 0);
        StringAssert.Contains("Five turns complete", m.BriefTitle);
        Assert.AreEqual(hash, b.State.Hash()); Assert.IsEmpty(b.Queued);
        Assert.AreEqual(b.State.Indicators["security"] >= opening.Indicators["security"], m.Goals[2].Met);
    }

    [Test]
    public void AttachDisposeRestoresTheTreeAndForwardsExistingNavigation()
    {
        using var ui = new UiTestKit();
        var b = Bridge(); var before = b.State.Hash();
        var map = ui.Go.AddComponent<MapScene>();
        var camera = new GameObject("War room test camera"); camera.AddComponent<Camera>();
        var rig = camera.AddComponent<CameraRig>();
        using var nav = new ScreenNav(ui.Root);
        var oldVisibility = ui.Q<VisualElement>("top-bar").style.visibility;
        try
        {
            var war = new WarRoomPresentation(ui.Go, ui.Root, b, map, rig);
            Assert.IsNotNull(ui.Root.Q("war-room"));
            Assert.IsTrue(ui.Q<VisualElement>("root").ClassListContains("war-room-active"));
            ui.Press("war-command-cabinet"); Assert.AreEqual("cabinet", nav.Current);
            Assert.AreEqual(before, b.State.Hash()); Assert.IsEmpty(b.Queued);
            war.Dispose(); war.Dispose();
            Assert.IsNull(ui.Root.Q("war-room"));
            Assert.IsFalse(ui.Q<VisualElement>("root").ClassListContains("war-room-active"));
            Assert.AreEqual(oldVisibility, ui.Q<VisualElement>("top-bar").style.visibility);
            Assert.AreEqual(before, b.State.Hash());
        }
        finally { Object.DestroyImmediate(camera); }
    }

    [Test]
    public void ShipsFloatAtTheBuiltMapsActualSeaSurfaceAndHaveNoColliders()
    {
        EditorSceneManager.OpenScene("Assets/Map.unity", OpenSceneMode.Single);
        var map=Object.FindFirstObjectByType<MapScene>(); map.Build();
        using var ui=new UiTestKit(); var b=Bridge(); b.EndTurn();
        try
        {
            using var war=new WarRoomPresentation(ui.Go,ui.Root,b,map,Camera.main.GetComponent<CameraRig>());
            var symbols=map.transform.Find("WarRoomOperationalSymbols"); Assert.IsNotNull(symbols);
            Assert.IsEmpty(symbols.GetComponentsInChildren<Collider>());
            var ship=symbols.Find("WarRoom ARRIVALS"); Assert.IsNotNull(ship,"actual population causes create a ship symbol");
            var renderers=ship.GetComponentsInChildren<MeshRenderer>(); Assert.IsNotEmpty(renderers);
            var bounds=renderers[0].bounds; foreach(var r in renderers) bounds.Encapsulate(r.bounds);
            float sea=PreviewProps.SeaLevelY(); Assert.Greater(sea,1000,"the negative elevation origin makes sea far above world y=0");
            Assert.Less(bounds.min.y,sea,"the keel is below the actual sea surface");
            Assert.Greater(bounds.max.y,sea,"the mast is above the actual sea surface");
            Assert.AreEqual(sea-PreviewProps.DraftShare*bounds.size.y,bounds.min.y,2,"the same resting rule as existing sea props");
        }
        finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);VisualToggles.Clear(); }
    }

    [UnityTest]
    public IEnumerator OpaqueRailsBlockTheMapAndTheTwoLayoutsKeepControlsInside()
    {
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})
        {
            var rt=new RenderTexture(size.x,size.y,0);
            var settings=Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(UiPaths.Panel));settings.targetTexture=rt;
            var go=new GameObject("War room rail layout");go.SetActive(false);
            var doc=go.AddComponent<UIDocument>();doc.panelSettings=settings;doc.visualTreeAsset=AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiPaths.GameUxml);go.SetActive(true);
            var camera=new GameObject("War room layout camera");camera.AddComponent<Camera>();var rig=camera.AddComponent<CameraRig>();
            var root=doc.rootVisualElement;root.Q("root").RemoveFromClassList("loading");
            var text=UiText.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(UiPaths.Strings).text);
            UiPreview.Apply(root,"table",text);
            root.Q<Label>("info-title").text=text["info.hint"];
            var option=new Button {text="Issue the order and bring every armed force into the IDF"};root.Q("event-options").Add(option);
            root.Q("event-card").style.display=DisplayStyle.Flex;
            using var war=new WarRoomPresentation(go,root,Bridge(),go.AddComponent<MapScene>(),rig);
            try
            {
                for(int i=0;i<20 && !(root.Q("war-header").worldBound.width>0);i++)yield return null;
                yield return null;
                var panel=root.panel;float h=Screen.height;
                Vector2 o=UiPointer.ToPanel(panel,Vector2.zero,h),ex=UiPointer.ToPanel(panel,Vector2.right*100,h)-o,ey=UiPointer.ToPanel(panel,Vector2.up*100,h)-o;
                Vector2 ScreenFor(Vector2 p)=>new Vector2((p-o).x/ex.x*100,(p-o).y/ey.y*100);
                foreach(var name in new[]{"war-header","war-dock"})
                {
                    var blank=root.Q(name).worldBound.min+new Vector2(4,4);
                    Assert.IsTrue(UiPointer.IsOverUi(panel,ScreenFor(blank),h),size+": blank "+name+" blocks map input");
                }
                var header=root.Q("war-header").worldBound;var dock=root.Q("war-dock").worldBound;
                Assert.IsFalse(dock.Overlaps(root.Q("minimap").worldBound));
                foreach(var name in new[]{"war-end-turn","war-resources"})Assert.LessOrEqual(root.Q(name).worldBound.xMax,header.xMax+.5f,size+": "+name+" stays in header");
                foreach(var b in root.Q("war-commands").Query<Button>().ToList())Assert.LessOrEqual(b.worldBound.xMax,dock.xMax+.5f,size+": "+b.name+" stays in dock");
                Assert.LessOrEqual(root.Q<Label>(className:"war-next-action").worldBound.yMax,dock.yMax+.5f,size+": brief text stays in dock");
                var commandPane=root.Q("war-command-pane").worldBound;
                for(int i=0;i<3;i++)
                {
                    var goal=root.Q("war-goal-"+i).worldBound;
                    Assert.Greater(goal.width,0,size+": goal "+i+" is visible");
                    Assert.GreaterOrEqual(goal.xMin,commandPane.xMin-.5f,size+": goal "+i+" starts inside command pane");
                    Assert.LessOrEqual(goal.xMax,commandPane.xMax+.5f,size+": goal "+i+" ends inside command pane");
                    if(i>0)Assert.LessOrEqual(root.Q("war-goal-"+(i-1)).worldBound.xMax,goal.xMin+.5f,size+": adjacent goals do not overlap");
                }
                var brand=root.Q<Label>(className:"war-brand");
                var brandText=brand.MeasureTextSize(brand.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined);
                Assert.LessOrEqual(brandText.x,brand.contentRect.width+.5f,size+": title glyphs fit before Treasury");
                var hint=root.Q<Label>("info-title");
                Assert.AreEqual(WhiteSpace.Normal,hint.resolvedStyle.whiteSpace,size+": location text wraps");
                var hintText=hint.MeasureTextSize(hint.text,hint.contentRect.width,VisualElement.MeasureMode.Exactly,0,VisualElement.MeasureMode.Undefined);
                Assert.LessOrEqual(hintText.y,hint.contentRect.height+.5f,size+": wrapped location glyphs fit vertically");
                Assert.Greater(option.resolvedStyle.color.grayscale-option.resolvedStyle.backgroundColor.grayscale,.5f,size+": decision text contrasts with its dark button under Map Table");
            }
            finally {war.Dispose();Object.DestroyImmediate(go);Object.DestroyImmediate(camera);Object.DestroyImmediate(settings);rt.Release();Object.DestroyImmediate(rt);}
        }
    }
}
