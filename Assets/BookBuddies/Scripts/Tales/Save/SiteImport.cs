using System;
using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The website's Tales progress as a game save, taken in once when a website account first plays the game. The hero is
    /// the one of the pet the website shows (its charPet, whose look the game already has); its bag, the codex, library
    /// levels, classes, Book Bosses, lore stones and keepsakes come too. Anything the game doesn't know is left out. The
    /// website's per-pet Plaza keys live in the browser, so they can't come in. Pure.
    /// </summary>
    public static class SiteImport
    {
        // quest.meta's library upgrades and their top levels (economy.json shop.meta)
        static readonly Dictionary<string, int> MetaMax = new Dictionary<string, int> { ["vit"] = 10, ["atk"] = 10, ["ink"] = 10, ["luck"] = 1, ["rev"] = 1, ["shop"] = 10 };

        /// <summary>The game save made from a website pet save (players.petsave), owned by owner and dated siteAt; null when there's nothing to bring.</summary>
        public static TalesSave FromPetsave(string petsave, string owner, double siteAt)
        {
            Dictionary<string, object> pg;
            try { pg = Json.ParseObject(petsave ?? ""); } catch (Exception) { return null; }
            var pet = CharPet(pg);
            string k = KeyOf(pet);
            if (k == null) return null;

            // the website's shapes moved into the game save's (TalesSave.ToObject), then read like a save
            var q = pg.Obj("quest");
            var hero = q.Obj("pets").Obj(k);
            var me = new Dictionary<string, object>();
            if (hero != null)
                foreach (var key in new[] { "cls", "rxp", "own", "kit", "life", "gear" }) if (hero.TryGetValue(key, out var v)) me[key] = v;
            // the bag is per pet now; an old shared one belongs to legacyChar, or to whoever plays when there's none
            var c = q.Obj("chars").Obj(k) ?? (!q.Has("legacyChar") || q.Str("legacyChar") == k ? q : null);
            var gyms = new Dictionary<string, object>();
            var won = pg.Obj("gyms").Obj(k);
            if (won != null) foreach (var kv in won) if (won.Truthy(kv.Key)) gyms[kv.Key] = kv.Value is double ms ? ms : 1.0;
            var keep = new List<object>();
            foreach (var x in pg.Arr("owned")) if (x is string r && r.StartsWith("decor:")) keep.Add(r.Substring(6));
            var s = TalesSave.FromObject(new Dictionary<string, object>
            {
                ["me"] = me, ["meta"] = q.Obj("meta"), ["clsU"] = q.Arr("clsU"), ["seen"] = q.Arr("seen"), ["met"] = q.Arr("met"),
                ["bag"] = c.Arr("bag"), ["dust"] = c.Num("dust"), ["fresh"] = c.Arr("nw"), ["fd"] = c.Obj("fd").Str("d"), ["fn"] = c.Obj("fd").Num("n"),
                ["gyms"] = gyms, ["stone"] = pg.Obj("stone"), ["keep"] = keep, ["rd"] = new Dictionary<string, object>(),
            });

            // only what the game knows
            var d = TalesData.Current;
            s.Bag.RemoveAll(i => Loot.Get(i) == null);
            s.Fresh.RemoveWhere(i => Loot.Get(i) == null);
            foreach (var slot in new List<string>(s.Me.Gear.Keys)) if (Loot.Get(s.Me.Gear[slot]) == null) s.Me.Gear.Remove(slot);
            if (s.Me.Cls != null && !d.Classes.ContainsKey(s.Me.Cls)) s.Me.Cls = null;
            foreach (var moves in new[] { s.Me.Own, s.Me.Kit })
                foreach (var cls in new List<string>(moves.Keys))
                    if (!d.Classes.ContainsKey(cls)) moves.Remove(cls);
                    else moves[cls].RemoveAll(m => HeroFactory.Move(cls, m) == null);
            s.ClassesUnlocked.RemoveAll(cls => !d.Classes.ContainsKey(cls));
            foreach (var key in new List<string>(s.Meta.Keys))
                if (MetaMax.TryGetValue(key, out int max) && s.Meta[key] > 0) s.Meta[key] = Math.Min(s.Meta[key], max);
                else s.Meta.Remove(key);

            if (Json.Write(s.ToObject()) == Json.Write(new TalesSave().ToObject())) return null;
            string pers = pet.Str("pers", null);
            if (pers != null && HeroFactory.Natures.ContainsKey(pers)) s.Personality = pers;
            s.Owner = owner;
            s.At = s.SiteIn = siteAt;
            return s;
        }

        /// <summary>The website's charKey(charPet()): the key its Tales progress for the shown pet is filed under; null with no pets.</summary>
        internal static string CharKey(Dictionary<string, object> pg) => KeyOf(CharPet(pg));

        // charPet: the active pet once hatched, else the hatched one being played, the first hatched one, or the active egg
        static Dictionary<string, object> CharPet(Dictionary<string, object> pg)
        {
            if (pg == null) return null;
            var pets = pg.Arr("pets").ConvertAll(p => p as Dictionary<string, object>);
            var active = int.TryParse(pg.Str("active"), out int a) && a >= 0 && a < pets.Count ? pets[a] : null;
            if (active.Truthy("hatched")) return active;
            string play = Text(pg.TryGetValue("play", out var pl) ? pl : null);
            return pets.Find(p => p.Truthy("hatched") && Text(p.TryGetValue("id", out var id) ? id : null) == play)
                ?? pets.Find(p => p.Truthy("hatched")) ?? active;
        }

        // charKey: String(p.id || p.hatched || p.name)
        static string KeyOf(Dictionary<string, object> p)
        {
            if (p == null) return null;
            foreach (var key in new[] { "id", "hatched" }) if (p.Truthy(key)) return Text(p[key]);
            return Text(p.TryGetValue("name", out var n) ? n : null);
        }

        // String(v) for a JSON value: numbers the way JS writes them
        static string Text(object v) => v is double n ? JsMath.Num(n) : v is bool b ? (b ? "true" : "false") : v as string;
    }
}
