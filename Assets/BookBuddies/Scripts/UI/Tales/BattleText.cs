using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BookBuddies.Tales
{
    /// <summary>The battle screen's words: lane names, the site's prose fix, and the fight log's lines.</summary>
    public static class BattleText
    {
        static readonly Regex MidThe = new Regex(@"([a-z,;’”)])\sThe\s(?=[A-Z])");

        /// <summary>tqProse: "beat The Big Bad Wolf" reads "beat the Big Bad Wolf".</summary>
        public static string Prose(string s) => s == null || s.IndexOf(" The ", System.StringComparison.Ordinal) < 0 ? s ?? "" : MidThe.Replace(s, "$1 the ");

        /// <summary>The lanes are rows on this screen: l is the top one, c the middle, r the bottom.</summary>
        public static string Lane(char lane) => lane == 'l' ? "Top" : lane == 'r' ? "Bottom" : "Middle";

        /// <summary>Who is on which side of a log line: the party, the foes, or the story itself.</summary>
        public enum Side { Story, Pet, Foe }

        /// <summary>One fight log row for an event (the site's flogAdd): an icon, the text (rich text) and whose it is.</summary>
        public static (string icon, string text, Side side) LogLine(BattleEvent e, BattleEngine engine)
        {
            string who = Name(engine, e.Actor);
            var side = e.Foe ? Side.Foe : Side.Pet;
            switch (e.Kind)
            {
                case "intro": return ("⚔️", $"Fight starts: {Prose(engine.Setup.Title)}", Side.Story);
                case "skip": return ("💫", $"{who} is too dizzy to move", side);
                case "dot": return (e.Fx.Exists(f => f.Damage > 0) ? "🩸" : "💚", $"{Bold(who)} {Targets(e, engine)}", side);
                case "slamw": return ("⚠️", $"{Bold(who)} winds up a slam on the {Lane(e.Zone?[0] ?? 'c')} lane", Side.Foe);
                case "fate" when e.Result == "ally": return ("🤝", Prose(e.Line), Side.Story);
                case "fate": return ("🎲", $"{e.Name} rolled {e.Roll}{(e.Bonus > 0 ? " + " + e.Bonus : "")}: {Prose(e.Line)}", Side.Story);
                case "rise": return ("👑", $"{Bold(Prose(e.Name))} rises again: {e.Sub}", Side.Foe);
                case "talk": return ("💬", $"{Bold(who)}: “{e.Line}” {Bold(Name(engine, e.Listener))}: “{e.Reply}”", Side.Foe);
                case "cheer": return ("📣", $"{Bold(who)} got a cheer: +1 ink", Side.Pet);
                case "win": return ("🏆", "Victory!", Side.Story);
                case "lose": return ("💤", "The party fell asleep", Side.Story);
            }
            string move = UI.UiKit.SplitEmoji(e.Name, out _);
            string targets = Targets(e, engine);
            return (e.Icon, $"{Bold(who)} {(e.Ult ? "ultimate " : "")}{Prose(move)}{(targets.Length > 0 ? " → " + targets : "")}", side);
        }

        static string Targets(BattleEvent e, BattleEngine engine)
        {
            var parts = new List<string>();
            foreach (var f in e.Fx)
            {
                string n = Name(engine, f.Unit);
                if (f.Miss) parts.Add($"{n} miss");
                else if (f.Damage > 0) parts.Add($"{n} −{f.Damage}{(f.Crit ? " crit" : "")}{(f.Ko ? " (out)" : "")}");
                else if (f.Absorbed > 0) parts.Add($"{n} shield −{f.Absorbed}");
                else if (f.Heal > 0) parts.Add($"{n} +{f.Heal}");
                else if (f.ShieldGained > 0) parts.Add($"{n} +{f.ShieldGained} shield");
                else if (f.Revive) parts.Add($"{n} back up");
                else if (f.Ko) parts.Add($"{n} is out");
            }
            return string.Join(" · ", parts);
        }

        static string Name(BattleEngine engine, string key) => Prose(engine.Find(key)?.Name ?? "");

        static string Bold(string s) => $"<b>{s}</b>";
    }
}
