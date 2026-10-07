using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Pets;
using UnityEngine;

namespace BookBuddies.Net
{
    /// <summary>
    /// Pet changes made away from your online account (offline on this PC, or visiting a friend's world) reach it too, so
    /// your pets are the same everywhere: a pet hatched there is hatched online, a rename or reroll changes its online
    /// twin, and switching pets switches online. Each pet there is matched to its twin by id once known, else by the look
    /// it had before it changed, and only what changed there since the last push is sent, so changes made on the site
    /// meanwhile stay. Runs at save points (AutoSave), when you leave that server, and (for offline play, which is on this
    /// PC) whenever the online server turns up, once it answers its health check; until then the changes wait. The pets
    /// are read from the server they're on, with your sign-in there. Main thread only.
    /// </summary>
    public static class PetSync
    {
        const float Every = 10;            // seconds between pushes at save points (saves meanwhile fold into the next)
        const string SaveKey = "bb.petsync"; // per online account: {map: {"<server>|<id>": twin id}, was: {"<server>|<id>": {name, look}}, "active|<server>": id}

        static Task pushing;
        static float pushedAt = -99;
        static bool hurry;
        static readonly List<string> owed = new List<string>(); // the servers whose changes go up at the next push

        static bool Away => Settings.SignedIn && Settings.Server != Settings.OnlineServer;

        /// <summary>MyPets: a pet is about to change away from your online account. Keeps how it was (the first time), to match and diff it later.</summary>
        public static void Before(MyPets.Pet pet)
        {
            if (!Away || pet == null) return;
            var s = Load();
            if (s.Obj("was").ContainsKey(KeyOf(pet.Id))) return;
            s.Obj("was")[KeyOf(pet.Id)] = Was(pet.Name, pet.Look);
            Save(s);
        }

        /// <summary>MyPets: a pet was just hatched away from your online account; the next push hatches its twin online.</summary>
        public static void Hatched(MyPets.Pet pet)
        {
            if (!Away || pet == null) return;
            var s = Load();
            var w = Was(pet.Name, pet.Look);
            w["new"] = true;
            s.Obj("was")[KeyOf(pet.Id)] = w;
            Save(s);
        }

        /// <summary>MyPets: the active pet is about to change (a switch or a hatch). Keeps which one it was, so the switch goes online.</summary>
        public static void BeforeSwitch()
        {
            if (!Away || MyPets.Active == null) return;
            var s = Load();
            string slot = "active|" + Settings.Server;
            if (s.Str(slot).Length > 0) return;
            s[slot] = MyPets.Active.Id;
            Save(s);
        }

        /// <summary>
        /// Sends the pet changes made on a server away from your online account (from: the one you're on unless named) to
        /// your online account. One push at a time, at most every 10 s unless now: saves that come meanwhile fold into one
        /// more push of your pets as they are then. Returns the push under way, if any. Never throws.
        /// </summary>
        public static Task Push(bool now = false, string from = null)
        {
            from ??= Settings.Server;
            if (!CanPush(from)) return pushing ?? Task.CompletedTask;
            if (!owed.Contains(from)) owed.Add(from);
            hurry |= now;
            if (pushing == null || pushing.IsCompleted) pushing = Run();
            return pushing;
        }

        // offline play or a friend's world you're signed in to, with an online account to push to
        static bool CanPush(string from) =>
            (from == Settings.Local || Settings.WorldNameOf(from).Length > 0) && Settings.TokenFor(from).Length > 0 && CoinBank.HasBank;

        static async Task Run()
        {
            while (owed.Count > 0)
            {
                while (!hurry && Time.realtimeSinceStartup < pushedAt + Every) await Task.Delay(250);
                hurry = false;
                pushedAt = Time.realtimeSinceStartup;
                var from = new List<string>(owed);
                owed.Clear();
                foreach (var here in from) if (CanPush(here)) await PushNow(here);
            }
        }

        static async Task PushNow(string here)
        {
            string online = Settings.OnlineServer, token = Settings.TokenFor(online), account = Settings.AccountIdFor(online);
            try
            {
                if (!await BBApi.Up(online)) return;
                var theirs = await BBApi.PetsOn(here); // as that server has them (MyPets may only hold a stand-in after a switch)
                var list = await BBApi.SendTo(online, token, "GET", "/me/pets", null);
                if (Settings.AccountIdFor(online) != account) return; // signed in as someone else meanwhile
                var s = Load();
                var map = s.Obj("map");
                var was = s.Obj("was");
                bool hatched = false; // an online hatch makes that pet active there
                foreach (var pet in Pets(theirs))
                {
                    string k = here + "|" + pet.Str("id"), name = pet.Str("name"), look = pet.Str("look");
                    if (look.Length == 0) continue;
                    var before = was.Obj(k);
                    var twin = Twin(list, map, here, k, before?.Str("look") ?? look) ?? Twin(list, map, here, k, look);
                    if (twin == null)
                    {
                        // only a pet hatched here gets a twin: one changed on the site since it came here just isn't matched
                        if (before == null || !before.Truthy("new") || list.Arr("pets").Count >= MyPets.Max) continue;
                        var known = Ids(list);
                        try { list = await BBApi.SendTo(online, token, "POST", "/me/pets", new Dictionary<string, object> { ["name"] = name, ["look"] = look }); }
                        catch (BBApi.ApiError e) when (e.Status >= 400 && e.Status < 500) { Pushed(k, null, name, look); continue; } // refused: it stays here
                        twin = Pets(list).Find(p => !known.Contains(p.Str("id")));
                        hatched = true;
                    }
                    else
                    {
                        var body = new Dictionary<string, object>();
                        // with no record nothing changed here since it came (or was last pushed): only recorded changes go
                        string oldName = before?.Str("name") ?? name, oldLook = before?.Str("look") ?? look;
                        if (name != oldName && name != twin.Str("name")) body["name"] = name;
                        if (look != oldLook && look != twin.Str("look")) body["look"] = look;
                        if (body.Count > 0)
                        {
                            list = await Patch(online, token, twin.Str("id"), body, list);
                            twin = Pets(list).Find(p => p.Str("id") == twin.Str("id")) ?? twin;
                        }
                    }
                    if (twin != null) map[k] = twin.Str("id");
                    Pushed(k, twin?.Str("id"), name, look);
                }
                // a switch is a change of this server's own active pet since its last push
                string slot = "active|" + here, last = s.Str(slot), active = theirs.Str("active");
                if (active.Length > 0 && (last != active || hatched))
                {
                    string twin = map.Str(here + "|" + active);
                    if ((last.Length > 0 || hatched) && twin.Length > 0 && list.Str("active") != twin)
                        await BBApi.SendTo(online, token, "POST", $"/me/pets/{Uri.EscapeDataString(twin)}/active", new Dictionary<string, object>());
                    var now = Load();
                    now[slot] = active;
                    Save(now);
                }
            }
            catch (BBApi.ApiError) { } // not now: the changes wait for the next save
            catch (Exception e) { Debug.LogException(e); }
        }

        // what was pushed for one pet: its twin, and how it is now (the next push sends only what changes after this);
        // read and written at once, so a change MyPets notes meanwhile isn't lost
        static void Pushed(string k, string twin, string name, string look)
        {
            var s = Load();
            if (twin != null) s.Obj("map")[k] = twin;
            s.Obj("was")[k] = Was(name, look);
            Save(s);
        }

        // a rename or reroll the online server refuses (a bad name, say) isn't tried again; anything else waits for the next save
        static async Task<Dictionary<string, object>> Patch(string online, string token, string id, Dictionary<string, object> body, Dictionary<string, object> list)
        {
            try { return await BBApi.SendTo(online, token, "PATCH", "/me/pets/" + Uri.EscapeDataString(id), body); }
            catch (BBApi.ApiError e) when (e.Status >= 400 && e.Status < 500) { return list; }
        }

        // this pet's online twin: the one it was matched to, else one with this look that no other pet on this server has
        // (the same pet on another server shares its twin)
        static Dictionary<string, object> Twin(Dictionary<string, object> list, Dictionary<string, object> map, string here, string k, string look)
        {
            var pets = Pets(list);
            string id = map.Str(k);
            if (id.Length > 0) return pets.Find(p => p.Str("id") == id);
            var taken = new HashSet<string>();
            foreach (var kv in map) if (kv.Key.StartsWith(here + "|", StringComparison.Ordinal) && kv.Value is string t) taken.Add(t);
            return pets.Find(p => p.Str("look") == look && !taken.Contains(p.Str("id")));
        }

        static List<Dictionary<string, object>> Pets(Dictionary<string, object> list)
        {
            var pets = new List<Dictionary<string, object>>();
            foreach (var o in list.Arr("pets")) if (o is Dictionary<string, object> p) pets.Add(p);
            return pets;
        }

        static HashSet<string> Ids(Dictionary<string, object> list) => new HashSet<string>(Pets(list).ConvertAll(p => p.Str("id")));

        static Dictionary<string, object> Was(string name, string look) => new Dictionary<string, object> { ["name"] = name, ["look"] = look };

        static string KeyOf(string id) => Settings.Server + "|" + id;

        static string StoreKey => SaveKey + "|" + Settings.AccountIdFor(Settings.OnlineServer);

        static Dictionary<string, object> Load()
        {
            Dictionary<string, object> s = null;
            try { s = Json.ParseObject(PlayerPrefs.GetString(StoreKey, "")); } catch (FormatException) { }
            s ??= new Dictionary<string, object>();
            if (s.Obj("map") == null) s["map"] = new Dictionary<string, object>();
            if (s.Obj("was") == null) s["was"] = new Dictionary<string, object>();
            return s;
        }

        static void Save(Dictionary<string, object> s)
        {
            PlayerPrefs.SetString(StoreKey, Json.Write(s));
            PlayerPrefs.Save();
        }
    }
}
