#nullable enable
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Statecraft.Game;
using Statecraft.Game.UI;
using Statecraft.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public class UiPreviewTests
{
    [TestCase("archive")]
    [TestCase("clear")]
    [TestCase("table")]
    public void PreviewKeepsCabinetActionsTextAndTheSimulationUnchanged(string direction)
    {
        using var ui = new UiTestKit();
        UiPreview.Apply(ui.Root, direction);
        var content = ContentLoader.Load(Paths.Content);
        var text = UiText.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(UiPaths.Strings).text);
        var bridge = new SimBridge(GameSession.New(content, GoldenRun.Seed, Mode.Historical), text);
        var before = bridge.State.Hash();
        using var nav = new ScreenNav(ui.Root);
        using var cabinet = new CabinetPresenter(ui.Root, bridge, text, null);
        UiTestKit.Press(ui.Q<Button>("nav-cabinet"));
        Assert.AreEqual("cabinet", nav.Current);
        Assert.IsTrue(ui.Q<Button>("nav-cabinet").ClassListContains("nav-selected"));
        UiTestKit.Press(ui.Q<Button>("seat-finance_minister"));
        Assert.AreEqual("finance_minister", cabinet.Selected);
        Assert.AreEqual("Minister of Finance: Eliezer Kaplan · Mapai · competence 8, loyalty 9", ui.Q<Label>("seat-finance_minister-holder").text);
        foreach (var (person, reason) in Cabinet.Candidates(content, bridge.State, cabinet.Selected!))
        {
            Assert.AreEqual(reason == null, ui.Q<Button>("appoint-" + person.Id).enabledSelf);
            if (reason != null) Assert.AreEqual(reason, ui.Q<Label>("cand-" + person.Id + "-reason").text);
        }
        Assert.IsEmpty(bridge.Queued);
        Assert.AreEqual(before, bridge.State.Hash());
        UiTestKit.Press(ui.Q<Button>("nav-cabinet"));
        Assert.IsNull(nav.Current);
        Assert.IsFalse(ui.Q<Button>("nav-cabinet").ClassListContains("nav-selected"));
    }

    [Test]
    public void SwitchingOrRemovingThePreviewLeavesNoOldDirectionActive()
    {
        using var ui = new UiTestKit();
        var root = ui.Q<VisualElement>("root");
        UiPreview.Apply(ui.Root, "archive");
        UiPreview.Apply(ui.Root, "clear");
        Assert.IsFalse(root.ClassListContains("preview-archive"));
        Assert.IsTrue(root.ClassListContains("preview-clear"));
        UiPreview.Apply(ui.Root, null);
        Assert.IsFalse(root.GetClasses().Any(c => c == "ui-preview" || c.StartsWith("preview-")));
    }

    [TestCase("https://localhost/?ui-preview=archive", "archive")]
    [TestCase("https://localhost/?smoke=5&ui-preview=clear#map", "clear")]
    [TestCase("https://localhost/ui-preview=table", null)]
    [TestCase("https://localhost/?ui-preview=table-extra", null)]
    [TestCase("https://localhost/?ui-preview=table=extra", null)]
    [TestCase("https://localhost/?ui-preview=unknown", null)]
    [TestCase("https://localhost/?ui-preview", null)]
    [TestCase("https://localhost/#?ui-preview=archive", null)]
    [TestCase("https://localhost/?other=1#&ui-preview=table", null)]
    [TestCase("https://localhost/?return=https://example/?ui-preview=clear", null)]
    public void OnlyAWholeKnownQueryValueActivatesAStyle(string url, string? expected)
    {
        Assert.AreEqual(expected, UiPreview.Requested(url, new[] { "-ui-preview", "table" }, true));
    }

    [Test]
    public void DesktopUsesOnlyAnExplicitKnownArgument()
    {
        Assert.AreEqual("table", UiPreview.Requested(null, new[] { "game", "-ui-preview", "table" }, false));
        Assert.IsNull(UiPreview.Requested("?ui-preview=archive", new[] { "game", "-ui-preview" }, false));
        Assert.IsNull(UiPreview.Requested(null, new[] { "game", "-ui-preview", "other" }, false));
    }

    static readonly Content C = ContentLoader.Load(Paths.Content);
    static UiText Text => UiText.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(UiPaths.Strings).text);
    static SimBridge Opening() => new SimBridge(GameSession.New(C, GoldenRun.Seed, Mode.Historical), Text);

    /// <summary>Names a presenter or a test looks up (Unity's own ScrollView and Scroller parts carry "unity-" names).</summary>
    static string[] DuplicateNames(VisualElement root) => root.Query<VisualElement>().ToList()
        .Where(e => !string.IsNullOrEmpty(e.name) && !e.name.StartsWith("unity-"))
        .GroupBy(e => e.name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();

    /// <summary>Map table (the owner's choice): every seat shows its office, holder, party and ratings as separate labels, and
    /// the selected office sits above its candidates; the combined holder label keeps its exact text, Appoint and the
    /// refusal reasons still work, every name stays unique, and selecting queues nothing.</summary>
    [Test]
    public void TheMapTableCabinetShowsStructuredSeatsAndTheSelectedOffice()
    {
        using var ui = new UiTestKit();
        UiPreview.Apply(ui.Root, "table", Text);
        var bridge = Opening();
        var before = bridge.State.Hash();
        using var nav = new ScreenNav(ui.Root);
        using var cabinet = new CabinetPresenter(ui.Root, bridge, Text, null);
        UiTestKit.Press(ui.Q<Button>("nav-cabinet"));
        var holder = ui.Q<Label>("seat-finance_minister-holder");
        Assert.AreEqual("Minister of Finance: Eliezer Kaplan · Mapai · competence 8, loyalty 9", holder.text);
        Assert.IsTrue(holder.ClassListContains("preview-seat-summary"), "the combined label stays; the table style hides it");
        Assert.AreEqual("Minister of Finance", ui.Q<Label>("seat-finance_minister-office").text);
        Assert.AreEqual("Eliezer Kaplan", ui.Q<Label>("seat-finance_minister-name").text);
        Assert.AreEqual("Mapai", ui.Q<Label>("seat-finance_minister-party").text);
        Assert.AreEqual("Competence 8", ui.Q<Label>("seat-finance_minister-competence").text);
        Assert.AreEqual("Loyalty 9", ui.Q<Label>("seat-finance_minister-loyalty").text);
        Assert.IsNull(ui.Root.Q("seat-finance_minister-appointed"), "nobody is the player's appointee at the opening");
        Assert.AreEqual(1, ui.Q<Button>("seat-finance_minister").Query<VisualElement>(className: "seat-portrait").ToList().Count);
        Assert.IsNull(ui.Root.Q("cabinet-detail"), "no office is selected yet");

        UiTestKit.Press(ui.Q<Button>("seat-finance_minister"));
        Assert.AreEqual("finance_minister", cabinet.Selected);
        var pick = ui.Q<VisualElement>("cabinet-pick");
        var detail = ui.Q<VisualElement>("cabinet-detail");
        Assert.AreSame(pick, detail.parent);
        Assert.Less(pick.IndexOf(detail), pick.IndexOf(ui.Q<Label>("cabinet-pick-title")), "the selected office sits above its candidates");
        Assert.AreEqual("Selected office", ui.Q<Label>("cabinet-detail-eyebrow").text);
        Assert.AreEqual("Eliezer Kaplan", ui.Q<Label>("cabinet-detail-name").text);
        Assert.AreEqual("Minister of Finance", ui.Q<Label>("cabinet-detail-office").text);
        Assert.AreEqual("Mapai", ui.Q<Label>("cabinet-detail-party").text);
        Assert.AreEqual("Competence 8 of 10", ui.Q<Label>("cabinet-detail-competence").text);
        Assert.AreEqual("Loyalty 9 of 10", ui.Q<Label>("cabinet-detail-loyalty").text);
        Assert.IsNull(ui.Root.Q("cabinet-detail-portrait"), "no catalog, so no portrait (never an invented one)");
        foreach (var (person, reason) in Cabinet.Candidates(C, bridge.State, "finance_minister"))
        {
            Assert.AreEqual(reason == null, ui.Q<Button>("appoint-" + person.Id).enabledSelf, person.Id);
            if (reason != null) Assert.AreEqual(reason, ui.Q<Label>("cand-" + person.Id + "-reason").text);
        }
        Assert.IsEmpty(DuplicateNames(ui.Root), "every named element is unique (presenters and tests take the first match)");
        UiTestKit.Press(ui.Q<Button>("seat-finance_minister"));   // pressing again re-renders: still one detail block
        Assert.AreEqual(1, ui.Root.Query<VisualElement>("cabinet-detail").ToList().Count);
        Assert.IsEmpty(bridge.Queued);
        Assert.AreEqual(before, bridge.State.Hash());
        cabinet.Select(null);
        Assert.IsNull(ui.Root.Q("cabinet-detail"));
        Assert.AreEqual(DisplayStyle.None, pick.style.display.value);
    }

    /// <summary>Without the table class the Cabinet builds exactly the shipped tree (a seat is its portrait and its holder
    /// label; the pick column is its title and its list), and switching the preview off restores it on the next render.</summary>
    [Test]
    public void WithoutTheMapTableTheCabinetTreeIsTheShippedOne()
    {
        void AssertShipped(UiTestKit kit, string state)
        {
            var seatParts = kit.Root.Query<VisualElement>().ToList()
                .Where(e => !string.IsNullOrEmpty(e.name) && e.name.StartsWith("seat-finance_minister-")).Select(e => e.name);
            CollectionAssert.AreEqual(new[] { "seat-finance_minister-holder" }, seatParts, state);
            Assert.AreEqual(1, kit.Q<Button>("seat-finance_minister").Query<Label>().ToList().Count, state);
            Assert.IsFalse(kit.Q<Label>("seat-finance_minister-holder").ClassListContains("preview-seat-summary"), state);
            CollectionAssert.AreEqual(new[] { "cabinet-pick-title", "cabinet-candidates" }, kit.Q<VisualElement>("cabinet-pick").Children().Select(e => e.name), state);
            Assert.IsEmpty(kit.Root.Query<VisualElement>(className: "preview-seat-who").ToList(), state);
        }
        using (var ui = new UiTestKit())
        {
            using var cabinet = new CabinetPresenter(ui.Root, Opening(), Text, null);
            cabinet.Select("finance_minister");
            AssertShipped(ui, "no preview");
        }
        using (var ui = new UiTestKit())
        {
            UiPreview.Apply(ui.Root, "table", Text);
            using var cabinet = new CabinetPresenter(ui.Root, Opening(), Text, null);
            cabinet.Select("finance_minister");
            Assert.IsNotNull(ui.Root.Q("cabinet-detail"));
            UiPreview.Apply(ui.Root, null, Text);
            cabinet.Select("finance_minister");   // the class is read at each render
            AssertShipped(ui, "after switching the preview off");
        }
    }

    /// <summary>The selected office's larger portrait follows the seat's rule: shown only when the catalog has one, and
    /// framed on the face for its own 120x150 box (a tall picture, so the box changes the position; a square one is 50%
    /// in any taller box).</summary>
    [Test]
    public void TheSelectedOfficeShowsAPortraitOnlyWhenTheCatalogHasOne()
    {
        using var ui = new UiTestKit();
        UiPreview.Apply(ui.Root, "table", Text);
        var catalog = ScriptableObject.CreateInstance<PortraitCatalog>();
        var face = new Texture2D(40, 100);
        try
        {
            catalog.entries.Add(new PortraitCatalog.Entry { personId = "kaplan", image = face, focusY = 0.3f });
            using var cabinet = new CabinetPresenter(ui.Root, Opening(), Text, catalog);
            cabinet.Select("finance_minister");
            var portrait = ui.Q<VisualElement>("cabinet-detail-portrait");
            Assert.AreEqual(face, portrait.style.backgroundImage.value.texture);
            Assert.AreEqual(face, ui.Q<Button>("seat-finance_minister").Q<VisualElement>(className: "seat-portrait").style.backgroundImage.value.texture);
            var y = portrait.style.backgroundPositionY.value;
            Assert.AreEqual(LengthUnit.Percent, y.offset.unit);
            Assert.AreEqual(PortraitFraming.PositionYPercent(40, 100, CabinetPresenter.DetailPortraitWidth, CabinetPresenter.DetailPortraitHeight, 0.3f), y.offset.value, 1e-3f);
            Assert.AreNotEqual(PortraitFraming.PositionYPercent(40, 100, EventCardPresenter.PortraitWidth, EventCardPresenter.PortraitHeight, 0.3f), y.offset.value, "framed for the detail box, not the event card's");
            cabinet.Select("prime_minister");
            Assert.IsNull(ui.Root.Q("cabinet-detail-portrait"), "no invented portrait for the Prime Minister");
        }
        finally { Object.DestroyImmediate(catalog); Object.DestroyImmediate(face); }
    }

    /// <summary>A player's appointee is tagged on the seat and on the selected office, and the combined label still ends
    /// "(your appointee)".</summary>
    [Test]
    public void TheMapTableTagsThePlayersAppointee()
    {
        var g = GameSession.New(C, GoldenRun.Seed, Mode.Historical);
        while (g.State.Date < new System.DateTime(1950, 2, 1)) g.Step(HistoricalAI.Commands(C, g.State));
        var bridge = new SimBridge(g, Text);
        using var ui = new UiTestKit();
        UiPreview.Apply(ui.Root, "table", Text);
        using var cabinet = new CabinetPresenter(ui.Root, bridge, Text, null);
        cabinet.Select("finance_minister");
        UiTestKit.Press(ui.Q<Button>("appoint-golda_meir"));
        bridge.EndTurn();
        StringAssert.EndsWith("(your appointee)", ui.Q<Label>("seat-finance_minister-holder").text);
        Assert.AreEqual("Golda Meir", ui.Q<Label>("seat-finance_minister-name").text);
        Assert.AreEqual("Your appointee", ui.Q<Label>("seat-finance_minister-appointed").text);
        Assert.AreEqual("Golda Meir", ui.Q<Label>("cabinet-detail-name").text);
        Assert.AreEqual("Your appointee", ui.Q<Label>("cabinet-detail-appointed").text);
    }

    /// <summary>A vacant office says so in the structured row and the detail, with no party or ratings to show.</summary>
    [Test]
    public void AVacantSeatUnderTheMapTableSaysSoWithoutRatings()
    {
        var g = GameSession.New(C, GoldenRun.Seed, Mode.Historical);
        Seat? vacant = null;
        for (int i = 0; i < 120 && g.State.GameOver == null && vacant == null; i++)
        {
            vacant = Cabinet.Lineup(C, g.State).FirstOrDefault(x => x.Holder == null);
            if (vacant == null) g.Step(HistoricalAI.Commands(C, g.State));
        }
        Assert.IsNotNull(vacant, "the historical game has a vacant seat to test with");
        using var ui = new UiTestKit();
        UiPreview.Apply(ui.Root, "table", Text);
        using var cabinet = new CabinetPresenter(ui.Root, new SimBridge(g, Text), Text, null);
        var id = "seat-" + vacant!.Office.Id;
        Assert.AreEqual(vacant.Office.Title + ": (vacant)", ui.Q<Label>(id + "-holder").text);
        Assert.AreEqual(vacant.Office.Title, ui.Q<Label>(id + "-office").text);
        Assert.AreEqual("(vacant)", ui.Q<Label>(id + "-name").text);
        foreach (var part in new[] { "-party", "-competence", "-loyalty", "-appointed" }) Assert.IsNull(ui.Root.Q(id + part), id + part);
        cabinet.Select(vacant.Office.Id);
        Assert.AreEqual("(vacant)", ui.Q<Label>("cabinet-detail-name").text);
        Assert.IsNull(ui.Root.Q("cabinet-detail-competence"));
    }

    /// <summary>The map table heads the event card (in its heading, beside the portrait) and the info panel with captions
    /// from strings.en.yaml: first in their block (never before an option button), taking no clicks, made once however often the preview is applied (GameRoot
    /// applies it on every Begin), and removed by another direction, by none, or when no strings are given.</summary>
    [Test]
    public void TheMapTableCaptionsAreMadeOnceAndRemovedWithThePreview()
    {
        using var ui = new UiTestKit();
        int Count(string name) => ui.Root.Query<Label>(name).ToList().Count;
        var captions = new[] { ("preview-event-eyebrow", ui.Q<VisualElement>("event-card").Q<VisualElement>(className: "event-heading"), "Current decision"),
            ("preview-info-eyebrow", ui.Q<VisualElement>("info-panel"), "Location") };
        UiPreview.Apply(ui.Root, "table", Text);
        UiPreview.Apply(ui.Root, "table", Text);
        foreach (var (name, panel, words) in captions)
        {
            Assert.AreEqual(1, Count(name), name);
            var label = ui.Q<Label>(name);
            Assert.AreEqual(words, label.text);
            Assert.AreSame(panel, label.parent, name);
            Assert.AreEqual(0, label.parent.IndexOf(label), name + " heads its block");
            Assert.AreEqual(PickingMode.Ignore, label.pickingMode, name);
        }
        UiPreview.Apply(ui.Root, "clear", Text);
        foreach (var (name, _, _) in captions) Assert.AreEqual(0, Count(name), "clear: " + name);
        UiPreview.Apply(ui.Root, "table", Text);
        foreach (var (name, _, _) in captions) Assert.AreEqual(1, Count(name), "table again: " + name);
        UiPreview.Apply(ui.Root, null, Text);
        foreach (var (name, _, _) in captions) Assert.AreEqual(0, Count(name), "off: " + name);
        UiPreview.Apply(ui.Root, "table");
        foreach (var (name, _, _) in captions) Assert.AreEqual(0, Count(name), "no strings: " + name);
        Assert.IsTrue(ui.Q<VisualElement>("root").ClassListContains("preview-table"), "the style still applies without strings");
    }

    /// <summary>The preview stylesheet can never restyle the shipped UI: every selector starts with a preview class.</summary>
    [Test]
    public void EveryPreviewSelectorIsScopedToAPreviewClass()
    {
        var uss = Regex.Replace(System.IO.File.ReadAllText(UiPaths.Folder + "/UiPreview.uss"), @"/\*.*?\*/", "", RegexOptions.Singleline);
        var selectors = Regex.Matches(uss, @"([^{}]*)\{[^}]*\}").Cast<Match>()
            .SelectMany(m => m.Groups[1].Value.Split(',')).Select(x => x.Trim()).ToList();
        Assert.Greater(selectors.Count, 20, "the stylesheet was read");
        foreach (var s in selectors)
            Assert.IsTrue(s.StartsWith(".ui-preview") || s.StartsWith(".preview-"), "unscoped selector: " + s);
    }

    /// <summary>The map table adds no click target of its own: the empty middle of the screen still reaches the map, End
    /// Turn is still the button under the pointer, and a caption passes its clicks to its panel (copied from
    /// UiFoundationTests.PickingFindsButtonsButNotTheTransparentRoot, with the game ready).</summary>
    [UnityTest]
    public IEnumerator UnderTheMapTableTheMiddleOfTheScreenIsNotUi()
    {
        using var ui = new UiTestKit();
        UiPreview.Apply(ui.Root, "table", Text);
        foreach (var name in new[] { "screen-cabinet", "screen-budget", "screen-defence", "screen-why", "toast" })
            ui.Q<VisualElement>(name).style.display = DisplayStyle.None;   // closed, as at game start
        ui.Q<VisualElement>("root").RemoveFromClassList("loading");        // ready, as GameRoot leaves it
        var button = ui.Q<Button>("end-turn");
        var caption = ui.Q<Label>("preview-info-eyebrow");
        for (int i = 0; i < 10 && (float.IsNaN(button.worldBound.width) || float.IsNaN(caption.worldBound.width)); i++) yield return null;
        var panel = ui.Root.panel;
        Assert.AreSame(button, panel.Pick(button.worldBound.center));
        Assert.IsNull(panel.Pick(ui.Root.worldBound.center), "the empty middle of the screen must reach the map");
        Assert.AreSame(ui.Q<VisualElement>("info-panel"), panel.Pick(caption.worldBound.center), "the caption is not a click target");
    }
}
