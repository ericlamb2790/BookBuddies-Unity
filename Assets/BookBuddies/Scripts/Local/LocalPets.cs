// Ports Server/src/pets.js:1-116: MAX_PETS, cleanPet, GET/POST /me/pets, PATCH /me/pets/<id>, POST /me/pets/<id>/active,
// petList, activeLook, firstPet and petName. The active pet's look is also the player's "pet", as on the Worker.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BookBuddies.Local
{
    /// <summary>A player's pets, up to six: each has an id, a name and a look, and one of them is active.</summary>
    public static class LocalPets
    {
        /// <summary>A full nest.</summary>
        public const int MaxPets = 6;

        // the website's pet look (cleanPet, build 551)
        static readonly string[] Slots = { "hat", "eye", "top", "dress", "bottom", "shoes", "neck", "hand" };
        static readonly string[] Looks = { "sh", "f", "e", "pt", "bg", "mn", "tl", "sk" };
        static readonly Regex PartId = new Regex("^[a-z0-9_]{1,16}\\z");
        static readonly Regex Mood = new Regex("^[a-z]{1,12}\\z");
        static readonly Regex NameJunk = new Regex(@"[<>""&\x00-\x1f]");
        static readonly Regex PetPath = new Regex("^/me/pets/([a-z0-9]{1,24})(/active)?\\z");

        /// <summary>
        /// A pet look as tidy JSON text (known keys and simple ids only, at most 500 characters), or null when it can't be
        /// read: hue h 0-359, stage s 0-5, outfit o {slot: id}, body/face/ears/pattern/background/mane/tail/skin ids, size z,
        /// rank r, shiny sy, form ev, mood m, state st, name n. Takes the look as JSON text or as a parsed object.
        /// </summary>
        public static string CleanPet(object v)
        {
            Dictionary<string, object> p;
            try { p = (v is string s ? Json.Parse(s) : v) as Dictionary<string, object>; }
            catch (Exception) { return null; } // JSON.parse failed
            if (p == null) return null;
            var o = new Dictionary<string, object>();
            var outfit = Js.Get(p, "o") as Dictionary<string, object>;
            foreach (string k in Slots)
                if (Js.Get(outfit, k) is string id && PartId.IsMatch(id)) o[k] = id;
            var look = new Dictionary<string, object> { ["h"] = Clamp(p, "h", 0, 359), ["s"] = Clamp(p, "s", 0, 5), ["o"] = o };
            foreach (string k in Looks)
                if (Js.Get(p, k) is string id && PartId.IsMatch(id)) look[k] = id;
            if (p.ContainsKey("z")) look["z"] = Clamp(p, "z", 0, 2);
            if (p.ContainsKey("r")) look["r"] = Clamp(p, "r", 0, 9999);
            if (Js.Truthy(Js.Get(p, "sy"))) look["sy"] = 1;
            if (p.ContainsKey("ev")) look["ev"] = Clamp(p, "ev", 1, 3, 1);
            if (Js.Get(p, "m") is string m && Mood.IsMatch(m)) look["m"] = m;
            if (p.ContainsKey("st")) look["st"] = Clamp(p, "st", 0, 6);
            if (Js.Get(p, "n") is string n)
            {
                n = Js.Trim(NameJunk.Replace(n, ""));
                if (n.Length > 14) n = n.Substring(0, 14);
                if (n.Length > 0) look["n"] = n;
            }
            string text = Stringify(look);
            return text.Length <= 500 ? text : null;
        }

        // int(v, lo, hi, d) in pets.js: parseInt, with d for NaN and 0, kept between lo and hi
        static int Clamp(Dictionary<string, object> p, string key, int lo, int hi, int fallback = 0)
        {
            double n = Js.ParseInt(Js.Get(p, key));
            if (double.IsNaN(n) || n == 0) n = fallback;
            return (int)Math.Max(lo, Math.Min(hi, n));
        }

        // JSON.stringify: as Json.Write, except that a lone surrogate (a name cut mid-emoji) is written as \uXXXX
        static string Stringify(object o)
        {
            string s = Json.Write(o);
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool pair = char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
                if (pair) sb.Append(c).Append(s[++i]);
                else if (char.IsSurrogate(c)) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>GET, POST /me/pets, PATCH /me/pets/&lt;id&gt; and POST /me/pets/&lt;id&gt;/active. Every reply is the whole list.</summary>
        internal static Dictionary<string, object> Route(string method, string path, Dictionary<string, object> body, LocalPlayer me)
        {
            if (path == "/me/pets" && method == "GET") return Reply(me);
            if (path == "/me/pets" && method == "POST") return Hatch(body, me);
            var m = PetPath.Match(path);
            if (!m.Success || method != (m.Groups[2].Success ? "POST" : "PATCH")) throw new LocalProblem("Not found", 404);
            FirstPet(me);
            var pet = me.Pets.Find(x => x.Id == m.Groups[1].Value) ?? throw new LocalProblem("That pet isn’t one of yours.", 404);
            return m.Groups[2].Success ? MakeActive(me, pet) : Change(body, me, pet);
        }

        /// <summary>The player's pets, oldest first, and the active one's id: {pets: [{id, name, look}], active}.</summary>
        internal static Dictionary<string, object> List(LocalPlayer me)
        {
            FirstPet(me);
            var pets = new List<object>();
            string active = "";
            foreach (var p in Sorted(me))
            {
                pets.Add(new Dictionary<string, object> { ["id"] = p.Id, ["name"] = p.Name, ["look"] = p.Look });
                if (p.Active && active.Length == 0) active = p.Id;
            }
            return new Dictionary<string, object> { ["pets"] = pets, ["active"] = active };
        }

        /// <summary>Keeps the active pet's look in step when PATCH /me changes the player's "pet".</summary>
        internal static void ActiveLook(LocalPlayer me, string look)
        {
            foreach (var p in me.Pets) if (p.Active) p.Look = look;
            LocalServer.Store.Touch();
        }

        /// <summary>
        /// Seeds a new offline profile with pets brought from the online account ([{name, look, active}]): each is cleaned
        /// like a hatch (one that isn't valid is left out), at most MaxPets, and exactly one is active (the first marked
        /// active, else the first). Returns the active look, or null when none came through.
        /// </summary>
        internal static string Seed(LocalPlayer me, object pets)
        {
            if (!(pets is List<object> list)) return null;
            foreach (var x in list)
            {
                if (me.Pets.Count >= MaxPets) break;
                var p = x as Dictionary<string, object>;
                string look = CleanPet(Js.Get(p, "look")), name = LocalSafety.CleanText(Js.Get(p, "name"), 14);
                if (look == null || name.Length < 2 || LocalSafety.TextProblem(name) != null) continue;
                me.Pets.Add(new LocalPet { Id = "p" + (me.Pets.Count + 1), Name = name, Look = look, Active = Js.Truthy(Js.Get(p, "active")), Born = me.Created });
            }
            if (me.Pets.Count == 0) return null;
            var active = me.Pets.Find(p => p.Active) ?? me.Pets[0];
            foreach (var p in me.Pets) p.Active = p == active;
            LocalServer.Store.Touch();
            return active.Look;
        }

        // oldest first (born, then id)
        static List<LocalPet> Sorted(LocalPlayer me)
        {
            var list = new List<LocalPet>(me.Pets);
            list.Sort((a, b) => a.Born != b.Born ? a.Born.CompareTo(b.Born) : string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        // the pet you hatched with becomes the first on the list (once; nothing happens when the list has pets already)
        static void FirstPet(LocalPlayer me)
        {
            if (string.IsNullOrEmpty(me.Pet) || me.Pets.Count > 0) return;
            me.Pets.Add(new LocalPet { Id = "p1", Name = me.Name, Look = me.Pet, Active = true, Born = me.Created != 0 ? me.Created : LocalServer.Now });
            LocalServer.Store.Touch();
        }

        // a new pet from an egg; it becomes the active one, like hatching on the website
        static Dictionary<string, object> Hatch(Dictionary<string, object> body, LocalPlayer me)
        {
            string name = PetName(Js.Get(body, "name"));
            string look = CleanPet(Js.Get(body, "look")) ?? throw new LocalProblem("That pet look couldn’t be read.");
            FirstPet(me);
            if (me.Pets.Count >= MaxPets) throw new LocalProblem($"You have {MaxPets} pets already. That’s a full nest!");
            // born a millisecond after the last pet at least, so the list keeps the hatching order
            long born = me.Pets.Count == 0 ? LocalServer.Now : Math.Max(LocalServer.Now, me.Pets.Max(p => p.Born) + 1);
            foreach (var p in me.Pets) p.Active = false;
            me.Pets.Add(new LocalPet { Id = "p" + LocalAccounts.Hex(LocalAccounts.RandomBytes(5)), Name = name, Look = look, Active = true, Born = born });
            me.Pet = look;
            LocalServer.Store.Touch();
            return Reply(me);
        }

        // a DNA reroll (a new look) or a new name
        static Dictionary<string, object> Change(Dictionary<string, object> body, LocalPlayer me, LocalPet pet)
        {
            string name = body.ContainsKey("name") ? PetName(body["name"]) : pet.Name;
            string look = body.ContainsKey("look") ? (CleanPet(body["look"]) ?? throw new LocalProblem("That pet look couldn’t be read.")) : pet.Look;
            pet.Name = name;
            pet.Look = look;
            if (pet.Active) me.Pet = look;
            LocalServer.Store.Touch();
            return Reply(me);
        }

        static Dictionary<string, object> MakeActive(LocalPlayer me, LocalPet pet)
        {
            foreach (var p in me.Pets) p.Active = p == pet;
            me.Pet = pet.Look;
            LocalServer.Store.Touch();
            return Reply(me);
        }

        static string PetName(object raw)
        {
            string name = LocalSafety.CleanText(raw, 14);
            if (name.Length < 2) throw new LocalProblem("Pick a name with at least 2 letters.");
            if (LocalSafety.TextProblem(name) != null) throw new LocalProblem("Please pick a kinder name.");
            return name;
        }

        static Dictionary<string, object> Reply(LocalPlayer me)
        {
            var reply = List(me);
            reply["pet"] = me.Pet ?? "";
            return reply;
        }
    }
}
