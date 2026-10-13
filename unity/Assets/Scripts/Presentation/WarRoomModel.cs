#nullable enable
using System;
using System.Globalization;
using System.Linq;
using Statecraft.Game.UI;
using Statecraft.Sim;

namespace Statecraft.Game.Presentation
{
    /// <summary>Read-only office summaries and a five-turn observation brief. Symbol counts are an illustration of
    /// state, never historical unit counts or additional simulation entities. No orders or state changes live here.</summary>
    public sealed class WarRoomModel
    {
        public readonly struct Resource
        {
            public readonly string Icon, Caption, Value, Delta; public readonly int Sign;
            public Resource(string icon, string caption, string value, string delta, int sign)
            { Icon = icon; Caption = caption; Value = value; Delta = delta; Sign = sign; }
        }
        public readonly struct Goal
        {
            public readonly string Text, Detail; public readonly bool Met;
            public Goal(string text, string detail, bool met) { Text = text; Detail = detail; Met = met; }
        }
        public string Date = "", Report = "", BriefTitle = "", NextAction = "", Summary = "";
        public Resource[] Resources = Array.Empty<Resource>(); public Goal[] Goals = Array.Empty<Goal>();
        public int ElapsedTurns, BrigadeSymbols, ShipSymbols, ConvoySymbols, CampTruckSymbols;
        public double Arrivals;
        public bool BriefComplete => ElapsedTurns >= 5;

        static string Number(double n) => n.ToString("N0", CultureInfo.InvariantCulture);
        static double Indicator(GameState s, string key) => s.Indicators.TryGetValue(key, out var v) ? v : 0;
        static string Change(double value, string suffix = "") => value == 0 ? "STEADY" : (value > 0 ? "+" : "") + Number(value) + suffix;

        public static WarRoomModel From(SimBridge b, GameState opening)
        {
            var s = b.State; var prev = b.Session.Previous; var c = b.Content;
            var m = new WarRoomModel { Date = PlayerText.Date(s.Date), ElapsedTurns = Math.Max(0, s.Turn - opening.Turn) };
            m.Arrivals = b.Session.Last?.Why.Where(w => w.System == "population").Sum(w => w.Delta) ?? 0;
            Resource Money(string icon, string caption, string key, double value, double? before)
            {
                var delta = before == null ? 0 : Math.Round(value - before.Value, 1, MidpointRounding.AwayFromZero);
                return new Resource(icon, caption, PlayerText.Money(c, key, value), delta == 0 ? "STEADY" : PlayerText.MoneyChange(c, key, delta), Math.Sign(delta));
            }
            Resource Percent(string icon, string caption, double value, double? before)
            {
                var delta = before == null ? 0 : PlayerText.ShownPoints(value) - PlayerText.ShownPoints(before.Value);
                return new Resource(icon, caption, PlayerText.Percent(value), Change(delta, " pts"), Math.Sign(delta));
            }
            var security = Indicator(s, "security"); var approval = EffectsSystem.Approval(c, s);
            m.Resources = new[]
            {
                Money("IL", "TREASURY", "treasury", s.Treasury - s.Debt, prev == null ? (double?)null : prev.Treasury - prev.Debt),
                Money("$", "FOREIGN CURRENCY", "fx_reserves", s.FxReserves, prev?.FxReserves),
                new Resource("+", "ARRIVALS / TURN", Number(m.Arrivals), m.Arrivals > 0 ? "VIA IMMIGRATION" : "AWAITING REPORT", 0),
                Percent("+", "SECURITY", security, prev == null ? (double?)null : Indicator(prev, "security")),
                Percent("O", "PUBLIC SUPPORT", approval, prev == null ? (double?)null : EffectsSystem.Approval(c, prev)),
                new Resource("!", "ATTENTION", b.AttentionLeft.ToString("0.0", CultureInfo.InvariantCulture) + " / " + c.Rules.AttentionPerTurn.ToString("0.0", CultureInfo.InvariantCulture), b.Queued.Count + " ORDERS QUEUED", 0),
            };
            // These are deliberately labelled symbols. The game does not simulate individual brigades or routes.
            m.BrigadeSymbols = Math.Min(6, Math.Max(0, s.Rung - 2) + (int)Math.Ceiling(s.Mobilization * 3));
            m.ShipSymbols = m.Arrivals > 0 ? Math.Min(3, Math.Max(1, (int)Math.Ceiling(m.Arrivals / 5000))) : 0;
            m.ConvoySymbols = s.Budget.TryGetValue("defense_minister", out var defence) && defence > 0 ? Math.Min(3, 1 + (int)(defence * 4)) : 0;
            m.CampTruckSymbols = Math.Min(6, s.Placements.Count(p => p.Action == MapActions.TransitCamp || p.Action == MapActions.Housing));
            var securityDelta = prev == null ? 0 : PlayerText.ShownPoints(security) - PlayerText.ShownPoints(Indicator(prev, "security"));
            m.Report = "Defence Ministry\nReserve call-up " + PlayerText.Percent(s.Mobilization) + " · security " + PlayerText.Percent(security) +
                (prev == null ? "." : " (" + Change(securityDelta, " pts").ToLowerInvariant() + ").") +
                "\n\nAbsorption office\n" + (m.Arrivals > 0 ? Number(m.Arrivals) + " immigrants arrived this turn." : "The next arrival report follows End Turn.") +
                "\n\nFinance Ministry\nNet treasury " + PlayerText.Money(c, "treasury", s.Treasury - s.Debt) + ". " +
                (b.Queued.Count == 0 ? "No orders queued." : b.Queued.Count + " orders await the next turn.");
            m.Goals = new[]
            {
                new Goal("Keep the treasury solvent", PlayerText.Money(c, "treasury", s.Treasury - s.Debt), s.Treasury - s.Debt >= 0),
                new Goal("Hold public support", PlayerText.Percent(approval) + " / " + PlayerText.Percent(EffectsSystem.Approval(c, opening)) + " opening", approval >= EffectsSystem.Approval(c, opening)),
                new Goal("Protect opening security", PlayerText.Percent(security) + " / " + PlayerText.Percent(Indicator(opening, "security")) + " opening", security >= Indicator(opening, "security")),
            };
            m.BriefTitle = m.BriefComplete ? "Five turns complete" : "FIVE-TURN BRIEF  " + Math.Min(5, m.ElapsedTurns) + " / 5";
            var held = m.Goals.Count(g => g.Met);
            m.Summary = m.BriefComplete ? held + " of 3 conditions held. Review the reports or keep playing." : "Hold the three conditions through five turns. They track your state; they grant no extra rewards.";
            string[] next =
            {
                "Read the current decision, then inspect Defence [F3]. Orders resolve together at End Turn.",
                "First dispatch received. Review the resource changes and the current decision before committing again.",
                "Check the budget [F2]. Reserves and civilian services compete for the same treasury.",
                "Review the Cabinet [F1]. The existing appointments and policies remain yours to choose.",
                "One turn to go. Read Why [F4] to see the causes behind the national indicators.",
                "Brief complete. Compare the three conditions with the opening, then continue if you wish.",
            };
            m.NextAction = s.GameOver != null ? "The campaign has ended. Review the final reports or start a new game." : next[Math.Min(5, m.ElapsedTurns)];
            return m;
        }
    }
}
