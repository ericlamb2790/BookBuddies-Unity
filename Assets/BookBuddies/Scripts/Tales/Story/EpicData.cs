using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>A land of the dungeon (TQ_REGIONS row, or the lair): its look, hazard, named villain, places and intro lines.</summary>
    public sealed class Land
    {
        public string K, N, I, Hz, Bn;       // key, name, icon, TQ_HZ key (null = none), named villain (TQ_BOSS name, null = none)
        public int Sc;                       // scene 0-5 (arena and storybook backdrop)
        public bool Soft;                    // act I lands: no hazard, no named villain
        public string Color, Ink;            // map ground and its dark ink
        public string[] Deco = new string[0], Places = new string[0], Lines = new string[0];
    }

    /// <summary>A tabletop scene (TQ_ENC, TQ_DANGER, TQ_CHEST, TQ_DM): icon, title, narration and 2-3 options.</summary>
    public sealed class EncTpl
    {
        public string I, T, Text;
        public readonly List<EncOpt> Opts = new List<EncOpt>();
    }

    /// <summary>One option of a scene: label, the check it rolls (null = safe) against Dc, effects and texts for success and failure.</summary>
    public sealed class EncOpt
    {
        public string I, N, Chk, OkFx, FailFx, OkText, FailText;
        public int Dc;
    }

    /// <summary>A relic, curse or Fate card row (TQ_ART, TQ_CUR, TQ_FATE in key order): key, icon, name, what it does.</summary>
    public sealed class EpicRow
    {
        public string Key, I, N, D;
    }

    /// <summary>
    /// The dungeon's tables, exported from the website (Resources/BookBuddies/Data/epic.json, made by tools/export_epic.js).
    /// Pure C#: loads through TalesData.TextLoader on first use.
    /// </summary>
    public sealed class EpicData
    {
        static EpicData current;
        public static EpicData Current => current ?? (current = Load(TalesData.TextLoader("Data/epic")));

        public readonly List<Land> Lands = new List<Land>();                     // TQ_REGIONS in order: 4 soft lands (act I), 12 hard
        public readonly List<EncTpl> Encounters = new List<EncTpl>(), Dangers = new List<EncTpl>(), Chests = new List<EncTpl>();
        public readonly List<EncTpl> Twists = new List<EncTpl>();                 // TQ_DM: the Story Master's scenes (view tp.s = twist, used key "t" + index)
        public readonly List<EpicRow> Relics = new List<EpicRow>(), Curses = new List<EpicRow>(); // TQ_ART, TQ_CUR
        public readonly List<EpicRow> Fates = new List<EpicRow>();                 // TQ_FATE in key order: f_insp, f_wind, f_lantern, f_armor, f_ink, f_ally
        public readonly Dictionary<string, string[]> PhaseLines = new Dictionary<string, string[]>(); // TQ_PHASE: a boss's lines by tactic (summon, enrage, shield, heal)
        public readonly Dictionary<string, string> PhaseNames = new Dictionary<string, string>();     // TQ_PHASEN: the phase banner by tactic ("is enraged!")
        public string[] RelicNames = new string[0];                               // TQ_RELIC: what the Dark Author stole
        public readonly List<(string i, string n)> Threads = new List<(string, string)>(); // TQ_THR side quests
        public string[] CampTales = new string[0];                                // TQ_TALES: told at the campfire
        public readonly Dictionary<string, (string i, string n)> Nodes = new Dictionary<string, (string, string)>();  // TQ_EPN by node type
        public readonly Dictionary<string, (string i, string n)> Checks = new Dictionary<string, (string, string)>(); // TQ_CHK
        public readonly Dictionary<string, Dictionary<string, int>> CheckBonus = new Dictionary<string, Dictionary<string, int>>(); // TQ_CHKB by class
        public readonly Dictionary<string, (string i, string n, string d)> Hazards = new Dictionary<string, (string, string, string)>(); // TQ_HZ
        public string[] Titles = new string[0];                                   // TQ_EPIC_T with {relic} {dark} {lair}
        public string[] CampLines = new string[0];                                // tqFlvPool('camp'), {p} = the pet

        public Land Land(string k) => Lands.Find(l => l.K == k);
        public EpicRow Relic(string k) => Relics.Find(r => r.Key == k);
        public EpicRow Curse(string k) => Curses.Find(r => r.Key == k);
        public EpicRow Fate(string k) => Fates.Find(r => r.Key == k);

        /// <summary>A check's bonus for a class (TQ_CHKB, sleuth when the class has none).</summary>
        public int Bonus(string cls, string chk)
        {
            if (cls == null || !CheckBonus.TryGetValue(cls, out var b)) b = CheckBonus["sleuth"];
            return b.TryGetValue(chk, out int n) ? n : 0;
        }

        public static EpicData Load(string json)
        {
            var j = Json.ParseObject(json);
            var d = new EpicData();
            foreach (Dictionary<string, object> r in j.Arr("TQ_REGIONS"))
                d.Lands.Add(new Land
                {
                    K = r.Str("k"), N = r.Str("n"), I = r.Str("i"), Sc = r.Int("sc"), Soft = r.Truthy("soft"), Hz = r.Str("hz", null), Bn = r.Str("bn", null),
                    Color = r.Str("land"), Ink = r.Str("ink"), Deco = TalesData.Strings(r.Arr("deco")), Places = TalesData.Strings(r.Arr("pl")), Lines = TalesData.Strings(r.Arr("x")),
                });
            ReadScenes(j.Arr("TQ_ENC"), d.Encounters);
            ReadScenes(j.Arr("TQ_DANGER"), d.Dangers);
            ReadScenes(j.Arr("TQ_CHEST"), d.Chests);
            ReadScenes(j.Arr("TQ_DM"), d.Twists);
            ReadRows(j.Arr("TQ_ART"), d.Relics);
            ReadRows(j.Arr("TQ_CUR"), d.Curses);
            ReadRows(j.Arr("TQ_FATE"), d.Fates);
            d.RelicNames = TalesData.Strings(j.Arr("TQ_RELIC"));
            foreach (List<object> t in j.Arr("TQ_THR")) d.Threads.Add(((string)t[0], (string)t[1]));
            d.CampTales = TalesData.Strings(j.Arr("TQ_TALES"));
            foreach (var kv in j.Obj("TQ_EPN")) { var a = (List<object>)kv.Value; d.Nodes[kv.Key] = ((string)a[0], (string)a[1]); }
            foreach (var kv in j.Obj("TQ_CHK")) { var a = (List<object>)kv.Value; d.Checks[kv.Key] = ((string)a[0], (string)a[1]); }
            foreach (var kv in j.Obj("TQ_CHKB"))
            {
                var b = new Dictionary<string, int>();
                foreach (var c in (Dictionary<string, object>)kv.Value) b[c.Key] = (int)(double)c.Value;
                d.CheckBonus[kv.Key] = b;
            }
            foreach (var kv in j.Obj("TQ_HZ")) { var h = (Dictionary<string, object>)kv.Value; d.Hazards[kv.Key] = (h.Str("i"), h.Str("n"), h.Str("d")); }
            foreach (var kv in j.Obj("TQ_PHASE")) d.PhaseLines[kv.Key] = TalesData.Strings((List<object>)kv.Value);
            foreach (var kv in j.Obj("TQ_PHASEN")) d.PhaseNames[kv.Key] = (string)kv.Value;
            d.Titles = TalesData.Strings(j.Arr("TQ_EPIC_T"));
            d.CampLines = TalesData.Strings(j.Arr("FLV_CAMP"));
            return d;
        }

        // [icon, title, narration, [[icon, label, check, DC, okFx, failFx, okText, failText]…]]
        static void ReadScenes(List<object> from, List<EncTpl> to)
        {
            foreach (List<object> s in from)
            {
                var t = new EncTpl { I = (string)s[0], T = (string)s[1], Text = (string)s[2] };
                foreach (List<object> o in (List<object>)s[3])
                    t.Opts.Add(new EncOpt
                    {
                        I = (string)o[0], N = (string)o[1], Chk = (string)o[2] == "" ? null : (string)o[2], Dc = (int)(double)o[3],
                        OkFx = (string)o[4], FailFx = (string)o[5], OkText = (string)o[6], FailText = o.Count > 7 ? (string)o[7] : "",
                    });
                to.Add(t);
            }
        }

        static void ReadRows(List<object> from, List<EpicRow> to)
        {
            foreach (List<object> r in from) to.Add(new EpicRow { Key = (string)r[0], I = (string)r[1], N = (string)r[2], D = (string)r[3] });
        }
    }
}
