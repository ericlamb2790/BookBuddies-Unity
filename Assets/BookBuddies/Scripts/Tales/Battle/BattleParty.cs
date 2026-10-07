using System.Collections.Generic;

namespace BookBuddies.Tales
{
    // A shared party fight (Setup.Party): foes made tougher for the party, each player's inputs, and the state the
    // captain's game sends after every step so the other games stay in step (BattleWire puts it all on the wire)
    public sealed partial class BattleEngine
    {
        /// <summary>How much tougher foes are for a party of n pets: ×hp to their HP and ×atk to their attack (1 and 1 alone).</summary>
        public static (double hp, double atk) PartyScale(int n) => (1 + .75 * (n - 1), 1 + .2 * (n - 1));

        // a foe joins the fight; with 2+ pets it is scaled for the party whenever it comes in (a Book Boss's team, a summon)
        void AddFoe(BattleUnit f)
        {
            if (Heroes.Count > 1)
            {
                var (hp, atk) = PartyScale(Heroes.Count);
                f.Max = JsMath.Round(f.Max * hp);
                f.Hp = JsMath.Round(f.Hp * hp);
                f.Atk = JsMath.Round(f.Atk * atk);
            }
            Foes.Add(f);
        }

        /// <summary>One player's input for its pet: "ult" asks for the Ultimate, "lane" moves it, "cheer" cheers. Returns the cheer's event, or null.</summary>
        public BattleEvent Input(BattleInput i)
        {
            var h = Heroes.Find(u => u.Key == i.Hero);
            if (h == null) return null;
            switch (i.Act)
            {
                case "ult": RequestUlt(h); break;
                case "lane": MoveLane(h, i.Lane); break;
                case "cheer": return Cheer(h);
            }
            return null;
        }

        /// <summary>Every unit as it stands: [key, hp, max, ink, ko, lane, phase, {statuses}, {cooldowns}, wantUlt] (flags 0 or 1), ready for the wire.</summary>
        public List<object> Snapshot()
        {
            var snap = new List<object>();
            var units = new List<BattleUnit>(Heroes);
            units.AddRange(Foes);
            foreach (var u in units)
            {
                var cds = new Dictionary<string, object>();
                foreach (var kv in u.Cds) cds[kv.Key] = (double)kv.Value;
                snap.Add(new List<object>
                {
                    u.Key, BattleWire.Num(u.Hp), BattleWire.Num(u.Max), (double)u.Ink, u.Ko ? 1.0 : 0, u.Lane.ToString(), (double)u.Phase,
                    BattleWire.Nums(u.St), cds, u.Hero?.WantUlt == true ? 1.0 : 0,
                });
            }
            return snap;
        }

        /// <summary>Puts every unit this fight knows back as a Snapshot has it (units it doesn't know are skipped).</summary>
        public void Apply(List<object> snap)
        {
            foreach (var o in snap)
            {
                if (!(o is List<object> x) || x.Count < 10) continue;
                var u = Find(x[0] as string);
                if (u == null) continue;
                u.Hp = BattleWire.Dbl(x[1]);
                u.Max = BattleWire.Dbl(x[2]);
                u.Ink = (int)BattleWire.Dbl(x[3]);
                u.Ko = BattleWire.Dbl(x[4]) != 0;
                if (x[5] is string lane && lane.Length > 0) u.Lane = lane[0];
                u.Phase = (int)BattleWire.Dbl(x[6]);
                u.St.Clear();
                BattleWire.Fill(u.St, x[7] as Dictionary<string, object>);
                u.Cds.Clear();
                if (x[8] is Dictionary<string, object> cds) foreach (var kv in cds) u.Cds[kv.Key] = (int)BattleWire.Dbl(kv.Value);
                if (u.Hero != null) u.Hero.WantUlt = BattleWire.Dbl(x[9]) != 0;
            }
        }
    }
}
