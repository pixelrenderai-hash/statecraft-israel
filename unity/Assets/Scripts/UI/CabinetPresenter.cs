#nullable enable
using System.Linq;
using Statecraft.Sim;
using UnityEngine.UIElements;

namespace Statecraft.Game.UI
{
    /// <summary>The Cabinet screen: the government of the day, its coalition, and every office with its holder (portrait,
    /// party, competence and loyalty). Selecting an office lists every person with Appoint, or the reason they cannot be
    /// appointed (Cabinet.Check). Appointing queues the Appoint order for End Turn. Under the map table preview
    /// (UiPreview, "table") each seat also shows its parts as separate labels and the selected office gets a view-only
    /// detail above its candidates; without it the tree is exactly the shipped one.</summary>
    public sealed class CabinetPresenter : System.IDisposable
    {
        /// <summary>The selected office's portrait box under the map table (UiPreview.uss .preview-detail-portrait).</summary>
        public const float DetailPortraitWidth = 120f, DetailPortraitHeight = 150f;

        readonly VisualElement root, screen; readonly SimBridge bridge; readonly UiText text; readonly PortraitCatalog? portraits;

        public CabinetPresenter(VisualElement root, SimBridge bridge, UiText text, PortraitCatalog? portraits)
        {
            this.root = root; screen = root.Q<VisualElement>("screen-cabinet"); this.bridge = bridge; this.text = text; this.portraits = portraits;
            bridge.Changed += Render;
            Render();
        }

        /// <summary>Unsubscribes from the bridge and removes the rows (the owner calls this when disabled).</summary>
        public void Dispose()
        {
            bridge.Changed -= Render;
            screen.Q<ScrollView>("cabinet-seats")?.Clear();
            screen.Q<ScrollView>("cabinet-candidates")?.Clear();
            screen.Q<VisualElement>("cabinet-detail")?.RemoveFromHierarchy();
            screen.Q<VisualElement>("cabinet-pick").style.display = DisplayStyle.None;
        }

        /// <summary>The office whose candidates are listed, or null.</summary>
        public string? Selected { get; private set; }

        public void Select(string? office) { Selected = office; Render(); }

        string PartyName(Person p)
        {
            var c = bridge.Content; var party = Offices.PartyOf(c, p, bridge.State.Date);
            return party == "" ? text["cabinet.no_party"] : c.Parties.First(x => x.Id == party).Name;
        }

        string Describe(Person p) => text.Format("cabinet.person", p.Name, PartyName(p), p.Competence, p.Loyalty);

        public void Render()
        {
            var c = bridge.Content; var s = bridge.State;
            var table = UiPreview.IsActive(root, "table");   // read each time, so switching the preview applies on the next render
            var gov = Offices.Government(c, s.Date);
            var (seats, size) = PoliticsSystem.CoalitionSeats(c, s.Date);
            screen.Q<Label>("cabinet-government").text = gov?.Name ?? "";
            screen.Q<Label>("cabinet-coalition").text = text.Format("cabinet.coalition", seats, size, PlayerText.Percent(s.CoalitionStability));
            var list = screen.Q<ScrollView>("cabinet-seats");
            list.Clear();
            var lineup = Cabinet.Lineup(c, s);
            if (Selected != null && lineup.All(x => x.Office.Id != Selected)) Selected = null;   // that office no longer exists (abolished)
            foreach (var seat in lineup)
            {
                var office = seat.Office.Id;
                var row = new Button(() => Select(office)) { name = "seat-" + office };
                row.AddToClassList("seat");
                row.EnableInClassList("seat-selected", office == Selected);
                var portrait = new VisualElement();
                portrait.AddToClassList("seat-portrait");
                var tex = portraits?.Get(seat.Holder?.Id);
                if (tex != null) portrait.style.backgroundImage = new StyleBackground(tex);
                portrait.style.display = tex != null ? DisplayStyle.Flex : DisplayStyle.None;   // never an invented portrait
                row.Add(portrait);
                var who = seat.Holder == null ? text["cabinet.vacant"] : Describe(seat.Holder) + (seat.Appointed ? text["cabinet.appointed"] : "");
                var label = new Label(text.Format("cabinet.seat", seat.Office.Title, who)) { name = "seat-" + office + "-holder" };
                label.EnableInClassList("appointed", seat.Appointed);
                row.Add(label);
                if (table) Structure(row, label, seat);
                list.Add(row);
            }
            var pick = screen.Q<VisualElement>("cabinet-pick");
            var candidates = screen.Q<ScrollView>("cabinet-candidates");
            candidates.Clear();
            pick.Q<VisualElement>("cabinet-detail")?.RemoveFromHierarchy();   // the pick column is UXML: rebuild the detail, never stack it
            pick.style.display = Selected == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (Selected != null)
            {
                var office = c.Offices.First(o => o.Id == Selected);
                var title = screen.Q<Label>("cabinet-pick-title");
                title.text = text.Format("cabinet.pick", office.Title);
                if (table) pick.Insert(pick.IndexOf(title), Detail(lineup.First(x => x.Office.Id == Selected)));
                foreach (var (person, reason) in Cabinet.Candidates(c, s, Selected))
                {
                    var p = person;
                    var row = new VisualElement { name = "cand-" + p.Id };
                    row.AddToClassList("candidate");
                    var appoint = new Button(() => Appoint(office.Id, p.Id)) { name = "appoint-" + p.Id, text = Describe(p) };
                    appoint.SetEnabled(reason == null);
                    row.Add(appoint);
                    if (reason != null) { var why = new Label(reason) { name = "cand-" + p.Id + "-reason" }; why.AddToClassList("reason"); row.Add(why); }
                    candidates.Add(row);
                }
            }
            var queued = bridge.Queued.OfType<Appoint>().Select(a =>
                text.Format("cabinet.queued", c.People.First(p => p.Id == a.Person).Name, c.Offices.First(o => o.Id == a.Office).Title));
            screen.Q<Label>("cabinet-status").text = string.Join("\n", queued);
        }

        /// <summary>Map table: the seat's office, holder, party and ratings as separate labels, for a readable hierarchy. The
        /// combined holder label stays with its exact text (tests and the other directions read it); the table style hides it.</summary>
        void Structure(Button row, Label combined, Seat seat)
        {
            var id = "seat-" + seat.Office.Id; var p = seat.Holder;
            combined.AddToClassList("preview-seat-summary");
            var who = Part(row, "preview-seat-who");
            Line(who, id + "-office", "preview-seat-office", seat.Office.Title);
            Line(who, id + "-name", "preview-seat-name", p?.Name ?? text["cabinet.vacant"]);
            if (p == null) return;
            Line(who, id + "-party", "preview-seat-party", PartyName(p));
            if (seat.Appointed) Line(who, id + "-appointed", "preview-tag", text["cabinet.appointee"]);
            var scores = Part(row, "preview-seat-scores");
            Line(scores, id + "-competence", "preview-seat-score", text.Format("cabinet.competence", p.Competence));
            Line(scores, id + "-loyalty", "preview-seat-score", text.Format("cabinet.loyalty", p.Loyalty));
        }

        /// <summary>Map table: the selected office, view only (selecting queues nothing): the holder's larger portrait when
        /// the catalog has one (never an invented one), framed on the face for its own box, then name, office, party and
        /// ratings out of 10.</summary>
        VisualElement Detail(Seat seat)
        {
            var p = seat.Holder;
            var detail = new VisualElement { name = "cabinet-detail", pickingMode = PickingMode.Ignore };
            detail.AddToClassList("preview-detail");
            Line(detail, "cabinet-detail-eyebrow", "preview-eyebrow", text["cabinet.selected"]);
            var entry = portraits?.Find(p?.Id);
            var tex = entry?.image;
            if (tex != null)
            {
                var portrait = new VisualElement { name = "cabinet-detail-portrait", pickingMode = PickingMode.Ignore };
                portrait.AddToClassList("preview-detail-portrait");
                portrait.style.backgroundImage = new StyleBackground(tex);
                portrait.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top,
                    new Length(PortraitFraming.PositionYPercent(tex.width, tex.height, DetailPortraitWidth, DetailPortraitHeight, entry!.Value.focusY), LengthUnit.Percent));
                Part(detail, "preview-detail-frame").Add(portrait);
            }
            Line(detail, "cabinet-detail-name", "preview-detail-name", p?.Name ?? text["cabinet.vacant"]);
            Line(detail, "cabinet-detail-office", "preview-detail-office", seat.Office.Title);
            if (p == null) return detail;
            Line(detail, "cabinet-detail-party", "preview-detail-party", PartyName(p));
            if (seat.Appointed) Line(detail, "cabinet-detail-appointed", "preview-tag", text["cabinet.appointee"]);
            Line(detail, "cabinet-detail-competence", "preview-detail-score", text.Format("cabinet.competence_of", p.Competence));
            Line(detail, "cabinet-detail-loyalty", "preview-detail-score", text.Format("cabinet.loyalty_of", p.Loyalty));
            return detail;
        }

        static VisualElement Part(VisualElement parent, string className)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };   // layout only: a seat's click lands on its Button
            e.AddToClassList(className);
            parent.Add(e);
            return e;
        }

        static void Line(VisualElement parent, string name, string className, string value)
        {
            var l = new Label(value) { name = name, pickingMode = PickingMode.Ignore };
            l.AddToClassList(className);
            parent.Add(l);
        }

        void Appoint(string office, string person)
        {
            // The bridge reports a refusal itself (Refused); the sim re-checks legality when the turn runs.
            bridge.Queue(new Appoint(office, person));
        }
    }
}
