#nullable enable
using System;
using System.IO;
using NUnit.Framework;
using Statecraft.Game;
using Statecraft.Game.Boot;
using Statecraft.Game.Map;
using Statecraft.Game.Presentation;
using Statecraft.Game.Prototype;
using Statecraft.Game.UI;
using Statecraft.Sim;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>The actual scene owns each opt-in experience, protects the campaign save and restores default play.</summary>
public sealed class ExperienceIntegrationTests
{
    string folder = "", saved = "";
    static GameInputs? inputs;
    static GameInputs Inputs => inputs ??= GameInputs.FromDisk(Paths.Content, Paths.StreamingMap);

    [SetUp]
    public void PlayerHasAnExistingSave()
    {
        folder = Path.Combine(Path.GetTempPath(), "statecraft-experiences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        saved = GameSession.New(Inputs.Content, 19, Mode.Historical).Save.Write();
        File.WriteAllText(Path.Combine(folder, SaveStore.AutosaveName), saved);
    }

    [TearDown]
    public void Cleanup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    GameRoot Start(bool warRoom, bool command)
    {
        EditorSceneManager.OpenScene("Assets/Map.unity", OpenSceneMode.Single);
        Object.FindFirstObjectByType<MapScene>().Build(Inputs.Map);
        var game = Object.FindFirstObjectByType<GameRoot>();
        game.Store = new SaveStore(folder);
        game.Begin(Inputs, 0, warRoom, command);
        return game;
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void EachExperienceStartsFreshAndCannotOverwriteTheCampaign(bool warRoom, bool command)
    {
        var game = Start(warRoom, command);
        var bridge = game.Bridge!;
        Assert.AreEqual(GoldenRun.Seed, bridge.Session.Save.Seed);
        Assert.AreEqual(0, bridge.State.Turn);
        var ui = Object.FindFirstObjectByType<UIDocument>().rootVisualElement;
        var session = bridge.Session;
        UiTestKit.Press(ui.Q<Button>(warRoom ? "war-command-new" : "new-game"));
        if (command) UiTestKit.Press(ui.Q<Button>("new-game"));
        if (command) Assert.AreSame(session, game.Bridge!.Session);
        else
        {
            Assert.AreNotSame(session, game.Bridge!.Session, "restart creates another isolated brief");
            Assert.AreEqual(0, game.Bridge.State.Turn);
            Assert.AreEqual(GoldenRun.Seed, game.Bridge.Session.Save.Seed);
        }
        game.Bridge!.EndTurn();
        Assert.AreEqual(saved, File.ReadAllText(Path.Combine(folder, SaveStore.AutosaveName)));
        Assert.AreEqual(1, Directory.GetFiles(folder).Length);
        Assert.IsNotNull(Object.FindFirstObjectByType<MapScene>().transform.Find(MapLook.RootName));
        game.enabled = false;
        Assert.IsNull(Object.FindFirstObjectByType<MapScene>().transform.Find(MapLook.RootName));
        Assert.AreEqual(1f, TiltShift.RadiusScale);
    }

    [Test]
    public void CommandWinsConflictingFlagsAndRestartRestoresOrdinaryPlay()
    {
        var game = Start(true, true);
        var ui = Object.FindFirstObjectByType<UIDocument>().rootVisualElement;
        Assert.IsNotNull(Object.FindFirstObjectByType<CommandPrototype>());
        Assert.IsNull(ui.Q("war-room"));
        game.Begin(Inputs, 0, false, false);
        Assert.IsNull(Object.FindFirstObjectByType<CommandPrototype>());
        Assert.IsNull(Object.FindFirstObjectByType<MapScene>().transform.Find(MapLook.RootName));
        Assert.IsTrue(Object.FindFirstObjectByType<MapPointer>().enabled);
        Assert.IsTrue(Camera.main!.GetComponent<CameraController>().enabled);
        Assert.IsNull(ui.Q("command-prototype"));
    }
}
