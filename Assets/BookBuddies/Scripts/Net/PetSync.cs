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
    /// meanwhile stay. Runs at save points (AutoSave) once the online server answers its health check; until then the
    /// changes wait. Main thread only.
    /// </summary>
    public static class PetSync
    {
        const float Every = 10;            // seconds between pushes at save points (saves meanwhile fold into the next)
        const string SaveKey = "bb.petsync"; // per online account: {map: {"<server>|<id>": twin id}, was: {"<server>|<id>": {name, look}}, active}

        static Task pushing;
        static float pushedAt = -99;
        static bool owed, hurry;

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
            if (!Away) return;
            var s = Load();
            if (s.Str("active").Length > 0 || MyPets.Active == null) return;
            s["active"] = KeyOf(MyPets.Active.Id);
            Save(s);
        }

        /// <summary>
        /// Sends the pet changes made here to your online account. One push at a time, at most every 10 s unless now: saves
        /// that come meanwhile fold into one more push of your pets as they are then. Never throws.
        /// </summary>
        public static Task Push(bool now = false)
        {
            if (!Away || !CoinBank.HasBank || !Buddy.Hatched) return Task.CompletedTask;
            owed = true;
            hurry |= now;
            if (pushing == null || pushing.IsCompleted) pushing = Run();
            return pushing;
        }

        static async Task Run()
        {
            while (owed)
            {
                while (!hurry && Time.realtimeSinceStartup < pushedAt + Every) await Task.Delay(250);
                owed = hurry = false;
                if (!Away || !Buddy.Hatched) return;
                pushedAt = Time.realtimeSinceStartup;
                await PushNow(Settings.Server, new List<MyPets.Pet>(MyPets.All), MyPets.Active?.Id);
            }
        }

        static async Task PushNow(string here, List<MyPets.Pet> mine, string activeHere)
        {
            string online = Settings.OnlineServer, token = Settings.TokenFor(online), account = Settings.AccountIdFor(online);
            try
            {
                if (!await BBApi.Up(online)) return;
                var list = await BBApi.SendTo(online, token, "GET", "/me/pets", null);
                if (Settings.AccountIdFor(online) != account) return; // signed in as someone else meanwhile
                var s = Load();
                var map = s.Obj("map");
                var was = s.Obj("was");
                bool hatched = false; // an online hatch makes that pet active there
                foreach (var pet in mine)
                {
                    string k = here + "|" + pet.Id;
                    var before = was.Obj(k);
                    var twin = Twin(list, map, k, before?.Str("look") ?? pet.Look) ?? Twin(list, map, k, pet.Look);
                    if (twin == null)
                    {
                        // only a pet hatched here gets a twin: one changed on the site since it came here just isn't matched
                        if (before == null || !before.Truthy("new") || list.Arr("pets").Count >= MyPets.Max) continue;
                        var known = Ids(list);
                        try { list = await BBApi.SendTo(online, token, "POST", "/me/pets", new Dictionary<string, object> { ["name"] = pet.Name, ["look"] = pet.Look }); }
                        catch (BBApi.ApiError e) when (e.Status >= 400 && e.Status < 500) { was[k] = Was(pet.Name, pet.Look); Save(s); continue; } // refused: it stays here
                        twin = Pets(list).Find(p => !known.Contains(p.Str("id")));
                        hatched = true;
                    }
                    else
                    {
                        var body = new Dictionary<string, object>();
                        // with no record nothing changed here: the twin as it is now is the baseline
                        string name = before?.Str("name") ?? twin.Str("name"), look = before?.Str("look") ?? twin.Str("look");
                        if (pet.Name != name && pet.Name != twin.Str("name")) body["name"] = pet.Name;
                        if (pet.Look != look && pet.Look != twin.Str("look")) body["look"] = pet.Look;
                        if (body.Count > 0)
                        {
                            list = await Patch(online, token, twin.Str("id"), body, list);
                            twin = Pets(list).Find(p => p.Str("id") == twin.Str("id")) ?? twin;
                        }
                    }
                    if (twin != null) map[k] = twin.Str("id");
                    was[k] = Was(pet.Name, pet.Look);
                    Save(s);
                }
                string ak = here + "|" + activeHere;
                if (activeHere != null && (s.Str("active") != ak || hatched))
                {
                    bool switched = s.Str("active").Length > 0 && s.Str("active") != ak;
                    if ((switched || hatched) && map.Str(ak).Length > 0 && list.Str("active") != map.Str(ak))
                        await BBApi.SendTo(online, token, "POST", $"/me/pets/{Uri.EscapeDataString(map.Str(ak))}/active", new Dictionary<string, object>());
                    s["active"] = ak;
                    Save(s);
                }
            }
            catch (BBApi.ApiError) { } // not now: the changes wait for the next save
            catch (Exception e) { Debug.LogException(e); }
        }

        // a rename or reroll the online server refuses (a bad name, say) isn't tried again; anything else waits for the next save
        static async Task<Dictionary<string, object>> Patch(string online, string token, string id, Dictionary<string, object> body, Dictionary<string, object> list)
        {
            try { return await BBApi.SendTo(online, token, "PATCH", "/me/pets/" + Uri.EscapeDataString(id), body); }
            catch (BBApi.ApiError e) when (e.Status >= 400 && e.Status < 500) { return list; }
        }

        // this pet's online twin: the one it was matched to, else an unmatched one with this look
        static Dictionary<string, object> Twin(Dictionary<string, object> list, Dictionary<string, object> map, string k, string look)
        {
            var pets = Pets(list);
            string id = map.Str(k);
            if (id.Length > 0) return pets.Find(p => p.Str("id") == id);
            var taken = new HashSet<string>();
            foreach (var v in map.Values) if (v is string t) taken.Add(t);
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
