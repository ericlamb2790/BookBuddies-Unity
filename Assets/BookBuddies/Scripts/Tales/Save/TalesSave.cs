using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BookBuddies.Tales
{
    /// <summary>Your buddy's Tales profile, like the site's pg().quest.pets[key].</summary>
    public sealed class PetProfile
    {
        public string Cls;                                   // chosen class; null = natural class from personality
        public double Rxp;                                   // renown XP (Pet Lv = renown level + 1)
        public readonly Dictionary<string, List<string>> Own = new Dictionary<string, List<string>>(); // bought moves per class
        public readonly Dictionary<string, List<string>> Kit = new Dictionary<string, List<string>>(); // equipped moves per class
        public readonly Dictionary<string, double> Life = new Dictionary<string, double>();         // tales, wins, bosses, foes, best, n20, helpers, falls
        public readonly Dictionary<string, string> Gear = new Dictionary<string, string>();          // slot w/a/h/c -> item string
        public readonly Dictionary<string, double> ClassRxp = new Dictionary<string, double>();     // renown put aside for each other class played
        public List<string> Order;                           // tactics: my move order (null = Smart)
        public bool AutoUlt = true;
        public string Lane;                                  // "l", "c" or "r"; null = default

        public void Count(string stat, double by = 1) => Life[stat] = (Life.TryGetValue(stat, out var v) ? v : 0) + by;
        public double Stat(string stat) => Life.TryGetValue(stat, out var v) ? v : 0;
    }

    /// <summary>
    /// Everything Tales keeps on this device for one account. Each pet has its own progress: its hero (level, class, moves
    /// and their order, renown, gear), bag and dust, HP and ink between fights, and its road and towns (the towns it walked
    /// to, its lore stones, the Book Bosses it beat). The account's is shared by every pet: the codex, what the shops sold
    /// (library upgrades, unlocked classes, keepsakes; the server's ledger keeps those too, see Economy/Shop.TakeOwned) and
    /// the daily limits (Bramble Road's finds, the cave guardian, Recall). The pet fields hold the active pet's (Pet);
    /// UsePet switches them.
    /// On disk it's nested in saves/ next to the game's other data (TalesSave.FilePath): &lt;account&gt;/account.json, then
    /// pets/&lt;pet&gt;/pet.json and towns.json for each pet, with that pet's tales and storybooks (TaleStore) beside them.
    /// Another account's save waits in its own folder, and saves/backup.json keeps the one from before the last replace.
    /// AutoSave flushes it at good moments. Pure C#: Unity sets FilePath and PetKey at startup.
    /// </summary>
    public sealed class TalesSave
    {
        public const int Version = 2;
        /// <summary>The pet of a save from before each pet had its own: the active pet's, once one is known (UsePet).</summary>
        public const string Unkeyed = "_";
        /// <summary>tales.json in the game's data folder: saves/ goes beside it (a tales.json from before moves in).</summary>
        public static string FilePath;
        /// <summary>The key of the pet being played (TalesBoot sets it); the save follows it when loaded or replaced.</summary>
        public static Func<string> PetKey;
        /// <summary>Now in ms, for At (tests move it).</summary>
        public static Func<double> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        static TalesSave current;
        public static event Action Changed;

        public static TalesSave Current
        {
            get
            {
                if (current != null) return current;
                current = Load();
                current.UsePet(PetKey?.Invoke());
                return current;
            }
        }

        static readonly object writing = new object();
        static long snapshots, written;   // snapshots made, and the newest one on disk
        static volatile string writtenText; // the save as the last write had it
        static readonly Dictionary<string, string> onDisk = new Dictionary<string, string>(); // each file's text as last written
        static string inUse;               // the folder of the save in use, as saves/in-use.txt names it

        // ---- the account's ----
        public string Owner = "";                            // the account this save belongs to; "" = not tied to one yet
        public double At;                                    // when the content last changed (ms)
        public double SiteIn;                                // the site_at of the website save taken in (0 = never)
        string stamped, petStamped;                          // the content At and PetAt date; a loaded or replaced save starts with its own
        public readonly List<string> Seen = new List<string>(); // gear codex: base keys seen
        public readonly List<string> Met = new List<string>();  // bestiary: foe base names met
        public double LairAt;                                    // when the cave guardian was last beaten (ms)
        public bool ChestReady;                                  // the Guardian's Chest is waiting
        public readonly Dictionary<string, string> RoadDays = new Dictionary<string, string>(); // daily finds: "g"/"s" + link (pw_rd), "b" + town (a Book Boss's gift) -> yyyy-MM-dd
        public string FindDay = "";                              // gear found in towns and at shrines: the day and how many (5 a day)
        public int FindCount;
        public double RecallAt;                                  // when Recall was last used (ms)
        public readonly Dictionary<string, int> Meta = new Dictionary<string, int>(); // library upgrade levels (quest.meta)
        public readonly List<string> ClassesUnlocked = new List<string>();  // classes opened past the base seven (quest.clsU)
        public readonly List<string> Keepsakes = new List<string>();        // town keepsakes bought (decor ids)
        public readonly HashSet<string> Unseen = new HashSet<string>();     // unlocks not looked at yet ("move:<cls>:<k>", "cls:<k>")

        // ---- the active pet's ----
        public string Pet = Unkeyed;                         // whose progress this is: the pet's id on the owner's account
        public double PetAt;                                 // when this pet's progress last changed (ms)
        public string Personality;                           // the site's pers; picked once for a Unity-hatched buddy
        public PetProfile Me { get; private set; } = new PetProfile();
        public readonly List<string> Bag = new List<string>(); // item strings, newest last (LT_BAG cap)
        public readonly HashSet<string> Fresh = new HashSet<string>(); // items not looked at yet (the "new" dot)
        public int Dust;
        public double HpFrac = 1, InkSaved, VitalsAt;           // carried between wild fights (pw_vit)
        public int Link = 1;                                     // last Bramble Road link
        public readonly List<string> TownsSeen = new List<string>(); // towns walked into (pw_seen)
        public string HomeStone = "", LastStone = "";            // lore stones (pg().stone home and last)
        public readonly Dictionary<string, double> Gyms = new Dictionary<string, double>(); // Book Bosses beaten: town -> last win (ms) (pg().gyms)

        readonly Dictionary<string, Dictionary<string, object>> others = new Dictionary<string, Dictionary<string, object>>(); // the other pets', as PetObject has them

        /// <summary>The active pet has been adventuring: renown, a chosen class, a tale or gear on.</summary>
        public bool HasHero => Me.Rxp > 0 || Me.Cls != null || Me.Stat("tales") > 0 || Me.Gear.Count > 0;

        /// <summary>Any pet in the save has been adventuring.</summary>
        public bool AnyHero
        {
            get
            {
                if (HasHero) return true;
                foreach (var p in others.Values) if (IsHero(p)) return true;
                return false;
            }
        }

        /// <summary>HasHero for a pet as PetObject has it.</summary>
        public static bool IsHero(Dictionary<string, object> pet)
        {
            var me = pet.Obj("me");
            return me.Num("rxp") > 0 || me.Str("cls", null) != null || me.Obj("life").Num("tales") > 0 || me.Obj("gear")?.Count > 0;
        }

        /// <summary>The pets with progress here, the active one first.</summary>
        public List<string> Pets
        {
            get
            {
                var keys = new List<string> { Pet };
                keys.AddRange(others.Keys);
                return keys;
            }
        }

        /// <summary>One pet's progress as PetObject has it; null when there's none.</summary>
        public Dictionary<string, object> PetOf(string key) => key == Pet ? PetObject() : others.TryGetValue(key, out var p) ? Copy(p) : null;

        /// <summary>
        /// One pet's progress as a save of its own, with the account's shared parts (a pet with none starts fresh), dated when
        /// that pet last played (a pet never dated as its save).
        /// </summary>
        public TalesSave PetSave(string key)
        {
            var o = AccountObject();
            var pet = PetOf(key) ?? new Dictionary<string, object>();
            o["pet"] = key;
            Lay(o, pet);
            if (pet.Num("at") > 0) o["at"] = pet.Num("at");
            return FromObject(o);
        }

        /// <summary>Sets one pet's progress (PetObject's shape): the active pet's or another's.</summary>
        public void PutPet(string key, Dictionary<string, object> pet)
        {
            if (key == Pet) FillPet(pet);
            else others[key] = pet;
        }

        public void Touch() { Save(); Changed?.Invoke(); }

        /// <summary>The save in use follows the pet being played (PetKey): switching pets, a save loaded or replaced.</summary>
        public static void FollowPet() => Current.UsePet(PetKey?.Invoke());

        /// <summary>
        /// Makes key's progress the one in the pet fields: the active pet's goes aside and key's comes back, or a pet with none
        /// starts fresh. A save from before pets had their own (Unkeyed) becomes key's. The save in use is saved.
        /// </summary>
        public void UsePet(string key)
        {
            if (string.IsNullOrEmpty(key) || key == Pet) return;
            if (Pet == Unkeyed) { Rekey(Unkeyed, key); return; }
            if (this == current) TaleStore.Flush(); // a tale waiting to be saved goes to the pet it's for
            SetPets(AllPets(), key);
            if (this != current) return;
            TaleStore.Reload();
            Touch();
        }

        /// <summary>
        /// Files from's progress under to (its id there learned, or changed by a merge). When to has progress too the two
        /// become one (TalesMerge.JoinPet, the newer staying whole). On the save in use the pet's folder moves too.
        /// </summary>
        public void Rekey(string from, string to)
        {
            if (string.IsNullOrEmpty(to) || from == to || (from != Pet && !others.ContainsKey(from))) return;
            if (this == current) TaleStore.Flush();
            var all = AllPets();
            var a = all[from];
            all.Remove(from);
            all[to] = all.TryGetValue(to, out var b) ? TalesMerge.JoinPet(a, b, a.Num("at") >= b.Num("at")) : a;
            SetPets(all, Pet == from ? to : Pet);
            if (this != current || string.IsNullOrEmpty(FilePath)) return;
            lock (writing)
            {
                try { MoveFolder(PetFolder(from), PetFolder(to)); }
                catch (Exception) { /* its tales stay where they were */ }
                onDisk.Clear();
            }
            TaleStore.Reload();
            Touch();
        }

        // every pet's progress, the active pet's first
        Dictionary<string, Dictionary<string, object>> AllPets()
        {
            var all = new Dictionary<string, Dictionary<string, object>> { [Pet] = PetObject() };
            foreach (var kv in others) all[kv.Key] = kv.Value;
            return all;
        }

        // the pets, with active's in the fields; switching isn't playing, so PetAt stays the pet's own
        void SetPets(Dictionary<string, Dictionary<string, object>> all, string active)
        {
            others.Clear();
            foreach (var kv in all) if (kv.Key != active) others[kv.Key] = kv.Value;
            Pet = active;
            FillPet(all.TryGetValue(active, out var p) ? p : null);
            petStamped = PetContent();
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            string text = Stamped();
            Write(ToFiles(), text, ++snapshots);
        }

        /// <summary>Writes the loaded save now if it changed since it was last written (nothing before it's loaded).</summary>
        public static void Flush() => Snapshot()?.Invoke();

        /// <summary>Flush, with only the JSON made on the caller's thread: the files are written on a background thread.</summary>
        public static Task FlushAsync() => Snapshot() is Action write ? Task.Run(write) : Task.CompletedTask;

        /// <summary>Clearing the device: the save is forgotten (the next use reads the disk again) and a write already on its way is dropped.</summary>
        public static void Forget()
        {
            lock (writing)
            {
                written = snapshots;
                writtenText = null;
                current = null;
                onDisk.Clear();
                inUse = null;
            }
        }

        // the loaded save as text now, and the write that puts it on disk; null when there's nothing new to write
        static Action Snapshot()
        {
            if (current == null || string.IsNullOrEmpty(FilePath)) return null;
            string text = current.Stamped();
            if (text == writtenText) return null;
            var files = current.ToFiles();
            long n = ++snapshots;
            return () => Write(files, text, n);
        }

        // one write at a time; a snapshot older than the one on disk is dropped. When the save in use moved to another
        // folder (claimed or replaced), saves/in-use.txt names the new one, and a guest's folder moves in with its tales.
        static void Write(Files files, string text, long n)
        {
            lock (writing)
            {
                if (n <= written) return;
                try
                {
                    if (files.Dir != inUse)
                    {
                        if (inUse != null && Path.GetFileName(inUse) == Unkeyed) MoveFolder(inUse, files.Dir);
                        WriteAtomic(Pointer, Path.GetFileName(files.Dir));
                        inUse = files.Dir;
                        onDisk.Clear();
                    }
                    WriteFiles(files);
                    written = n;
                    writtenText = text;
                }
                catch (Exception) { /* a full disk shouldn't stop the game */ }
            }
        }

        // the files whose text changed since they were last written
        static void WriteFiles(Files files)
        {
            foreach (var (path, text) in files.List)
                if (!onDisk.TryGetValue(path, out var was) || was != text)
                {
                    WriteAtomic(path, text);
                    onDisk[path] = text;
                }
        }

        /// <summary>Writes a file whole: a temp file first, then swapped in, so a crash mid-write never leaves half of it.</summary>
        internal static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, null); }
            catch (Exception e) when (e is IOException || e is PlatformNotSupportedException)
            {
                File.Delete(path);
                File.Move(tmp, path);
            }
        }

        /// <summary>
        /// Makes s the save in use and writes it. The save it replaces is kept as saves/backup.json, and another save its
        /// account's folder held as saves/backup-&lt;account&gt;.json.
        /// </summary>
        public static void Replace(TalesSave s)
        {
            TaleStore.Flush(); // a tale waiting to be saved goes to the pet it's for
            Flush();
            if (!string.IsNullOrEmpty(FilePath))
                lock (writing)
                {
                    try
                    {
                        WriteAtomic(Path.Combine(Root, "backup.json"), Current.ToText());
                        string home = Dir(s.Owner);
                        if (home != inUse && ReadFolder(home) is TalesSave there && there.ToText() != s.ToText())
                            WriteAtomic(Path.Combine(Root, "backup-" + Path.GetFileName(home) + ".json"), there.ToText());
                    }
                    catch (Exception) { /* the backups are only for support */ }
                    onDisk.Clear();
                }
            current = s;
            s.stamped = Content(s);
            s.petStamped = s.PetContent();
            if (PetKey?.Invoke() is string key && key.Length > 0 && key != s.Pet) s.UsePet(key);
            s.Save();
            TaleStore.Reload();
            Changed?.Invoke();
        }

        /// <summary>Puts the save aside (an account's stays in its folder; an unowned one only goes to the backup) and starts an empty one.</summary>
        public static void Stash() => Replace(new TalesSave());

        /// <summary>An account's save put aside on this device; null when there's none.</summary>
        public static TalesSave Stashed(string owner)
        {
            if (string.IsNullOrEmpty(FilePath) || string.IsNullOrEmpty(owner)) return null;
            lock (writing) return ReadFolder(Dir(owner));
        }

        // saves/<account> (a guest's is "_") and its pets' folders
        static string Root => Path.Combine(Path.GetDirectoryName(FilePath), "saves");
        static string Pointer => Path.Combine(Root, "in-use.txt");
        static string Dir(string owner) => Path.Combine(Root, owner.Length == 0 ? Unkeyed : Safe(owner));

        /// <summary>The save in use's folder, for the tales every pet shares (the daily tale); null without FilePath.</summary>
        public static string AccountFolder => string.IsNullOrEmpty(FilePath) ? null : Dir(Current.Owner);

        /// <summary>The active pet's folder, for its tales and storybooks; null without FilePath.</summary>
        public static string ActivePetFolder => string.IsNullOrEmpty(FilePath) ? null : Current.PetFolder(Current.Pet);

        string PetFolder(string key) => Path.Combine(Dir(Owner), "pets", key == Unkeyed ? Unkeyed : Safe(key));

        // a name that's safe as a folder (ids and server addresses): letters, digits, dots and dashes
        static string Safe(string name)
        {
            var c = name.ToCharArray();
            for (int i = 0; i < c.Length; i++) if (!char.IsLetterOrDigit(c[i]) && c[i] != '.' && c[i] != '-') c[i] = '-';
            string safe = new string(c).TrimStart('.');
            return safe.Length > 0 ? safe : "-";
        }

        // moves a folder's files into another (the save's own files aside, and any already there), then deletes it
        static void MoveFolder(string from, string to)
        {
            if (from == to || !Directory.Exists(from)) return;
            if (!Directory.Exists(to))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                Directory.Move(from, to);
                return;
            }
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file), dest = Path.Combine(to, file.Substring(from.Length + 1));
                if (name == "account.json" || name == "pet.json" || name == "towns.json" || File.Exists(dest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Move(file, dest);
            }
            Directory.Delete(from, true);
        }

        // what At dates: everything but at, and the owner (claiming a save isn't playing it)
        static string Content(TalesSave s)
        {
            var o = s.ToObject(false);
            o.Remove("owner");
            o.Remove("petAt");
            return Json.Write(o);
        }

        string PetContent()
        {
            var p = PetObject();
            p.Remove("at");
            return Json.Write(p);
        }

        // the text to write, with At (and the active pet's PetAt) moved to now when the content changed since it was last stamped
        string Stamped()
        {
            string content = Content(this);
            if (content != stamped)
            {
                stamped = content;
                At = Clock();
                string pet = PetContent();
                if (pet != petStamped) { petStamped = pet; PetAt = At; }
            }
            return ToText();
        }

        /// <summary>The save as the server stores it.</summary>
        public string ToText() => Json.Write(ToObject());

        /// <summary>
        /// The save as one JSON object (without at when withAt is false): the account's, and the active pet's at the top as
        /// in a save from before pets had their own (the website reads those), with pet and petAt; the other pets' are in pets.
        /// </summary>
        public Dictionary<string, object> ToObject(bool withAt = true)
        {
            var o = AccountObject();
            Lay(o, PetObject());
            var pets = new Dictionary<string, object>();
            foreach (var kv in others) pets[kv.Key] = Copy(kv.Value);
            o["pets"] = pets;
            if (!withAt) o.Remove("at");
            return o;
        }

        Dictionary<string, object> AccountObject() => new Dictionary<string, object>
        {
            ["v"] = (double)Version, ["owner"] = Owner, ["at"] = At, ["siteIn"] = SiteIn, ["pet"] = Pet,
            ["seen"] = Strs(Seen), ["met"] = Strs(Met), ["lair"] = LairAt, ["chest"] = ChestReady, ["rd"] = Texts(RoadDays), ["fd"] = FindDay, ["fn"] = (double)FindCount,
            ["stone"] = new Dictionary<string, object> { ["rt"] = RecallAt },
            ["meta"] = Ints(Meta), ["clsU"] = Strs(ClassesUnlocked), ["keep"] = Strs(Keepsakes), ["new"] = Strs(Unseen),
        };

        /// <summary>The active pet's progress: at, pers, me, bag, fresh, dust, hp, ink, vt, and its road and towns (link, towns, gyms, stone).</summary>
        public Dictionary<string, object> PetObject() => new Dictionary<string, object>
        {
            ["at"] = PetAt, ["pers"] = Personality, ["bag"] = Strs(Bag), ["fresh"] = Strs(Fresh), ["dust"] = (double)Dust,
            ["hp"] = HpFrac, ["ink"] = InkSaved, ["vt"] = VitalsAt,
            ["me"] = new Dictionary<string, object>
            {
                ["cls"] = Me.Cls, ["rxp"] = Me.Rxp, ["own"] = Lists(Me.Own), ["kit"] = Lists(Me.Kit), ["life"] = Nums(Me.Life), ["crxp"] = Nums(Me.ClassRxp),
                ["gear"] = Texts(Me.Gear), ["order"] = Me.Order == null ? null : Strs(Me.Order), ["ua"] = Me.AutoUlt, ["lane"] = Me.Lane,
            },
            ["link"] = (double)Link, ["towns"] = Strs(TownsSeen), ["gyms"] = Nums(Gyms),
            ["stone"] = new Dictionary<string, object> { ["home"] = HomeStone, ["last"] = LastStone },
        };

        static readonly string[] TownKeys = { "link", "towns", "gyms", "stone" }; // a pet's road and towns: towns.json

        // a pet's progress laid over a save object: at becomes petAt, and its lore stones join the account's Recall
        static void Lay(Dictionary<string, object> o, Dictionary<string, object> pet)
        {
            foreach (var kv in pet)
                if (kv.Key == "at") o["petAt"] = kv.Value;
                else if (kv.Key == "stone" && o.Obj("stone") is Dictionary<string, object> stone && kv.Value is Dictionary<string, object> st)
                    foreach (var s in st) stone[s.Key] = s.Value;
                else o[kv.Key] = kv.Value;
        }

        static Dictionary<string, object> Copy(Dictionary<string, object> o) => Json.ParseObject(Json.Write(o));

        // the save as files: each pet's pet.json and towns.json, then account.json, which names the pets
        sealed class Files { public string Dir; public readonly List<(string path, string text)> List = new List<(string, string)>(); }

        Files ToFiles()
        {
            var f = new Files { Dir = Dir(Owner) };
            var keys = new List<object>();
            foreach (var kv in AllPets())
            {
                keys.Add(kv.Key);
                var pet = new Dictionary<string, object>(kv.Value);
                var towns = new Dictionary<string, object>();
                foreach (string k in TownKeys) if (pet.TryGetValue(k, out var v)) { towns[k] = v; pet.Remove(k); }
                string dir = PetFolder(kv.Key);
                f.List.Add((Path.Combine(dir, "pet.json"), Json.Write(pet)));
                f.List.Add((Path.Combine(dir, "towns.json"), Json.Write(towns)));
            }
            var account = AccountObject();
            account["pets"] = keys;
            f.List.Add((Path.Combine(f.Dir, "account.json"), Json.Write(account)));
            return f;
        }

        /// <summary>Reads the save in use (an empty one when there's none), moving a tales.json from before into saves/ first.</summary>
        public static TalesSave Load()
        {
            TalesSave s = null;
            if (!string.IsNullOrEmpty(FilePath))
                lock (writing)
                {
                    try
                    {
                        if (!Directory.Exists(Root)) Migrate();
                        string name = File.Exists(Pointer) ? Path.GetFileName(File.ReadAllText(Pointer).Trim()) : "";
                        if (name.Length > 0 && name != "..") s = ReadFolder(inUse = Path.Combine(Root, name));
                    }
                    catch (Exception) { /* an empty save, as on a new device */ }
                }
            s ??= new TalesSave();
            s.stamped = Content(s);
            s.petStamped = s.PetContent();
            return s;
        }

        // a save's folder, or null when it has none or it can't be read
        static TalesSave ReadFolder(string dir)
        {
            var o = ReadObject(Path.Combine(dir, "account.json"));
            if (o == null) return null;
            var pets = new Dictionary<string, object>();
            foreach (var x in o.Arr("pets"))
                if (x is string k)
                {
                    string folder = Path.Combine(dir, "pets", k == Unkeyed ? Unkeyed : Safe(k));
                    var pet = ReadObject(Path.Combine(folder, "pet.json")) ?? new Dictionary<string, object>();
                    var towns = ReadObject(Path.Combine(folder, "towns.json"));
                    if (towns != null) foreach (var kv in towns) pet[kv.Key] = kv.Value;
                    pets[k] = pet;
                }
            if (pets.TryGetValue(o.Str("pet", Unkeyed), out var active))
            {
                Lay(o, (Dictionary<string, object>)active);
                pets.Remove(o.Str("pet", Unkeyed));
            }
            o["pets"] = pets;
            return FromObject(o);
        }

        static Dictionary<string, object> ReadObject(string path)
        {
            try { return File.Exists(path) ? Json.ParseObject(File.ReadAllText(path)) : null; }
            catch (Exception) { return null; }
        }

        // a device from before pets had their own: tales.json and other accounts' tales.<id>.json move into their accounts'
        // folders as the active pet's progress (Unkeyed until UsePet), the solo tale and storybooks go with tales.json's pet
        // and the daily tale with its account, and the old files are kept in saves/v1
        static void Migrate()
        {
            string dir = Path.GetDirectoryName(FilePath), old = Path.Combine(Root, "v1");
            var files = new List<string>(Directory.GetFiles(dir, "tales*.json"));
            files.Sort((a, b) => (Path.GetFileName(a) == "tales.json").CompareTo(Path.GetFileName(b) == "tales.json")); // the one in use last: it wins its folder
            Directory.CreateDirectory(old);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (name != "tales.bak.json" && Read(file) is TalesSave s)
                {
                    var f = s.ToFiles();
                    WriteFiles(f);
                    if (name == "tales.json")
                    {
                        WriteAtomic(Pointer, Path.GetFileName(f.Dir));
                        foreach (string tale in new[] { "tale_run.json", "tale_books.json", "tale_daily.json" })
                        {
                            string from = Path.Combine(dir, tale), to = tale == "tale_daily.json" ? Path.Combine(f.Dir, tale) : Path.Combine(s.PetFolder(Unkeyed), tale);
                            if (File.Exists(from) && !File.Exists(to)) File.Move(from, to);
                        }
                    }
                }
                File.Move(file, Path.Combine(old, name));
            }
            onDisk.Clear();
        }

        // a save file from before (one JSON object), or null when it's missing or unreadable; one from before accounts is
        // dated by its write time
        static TalesSave Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var o = Json.ParseObject(File.ReadAllText(path));
                if (o == null) return null;
                var s = FromObject(o);
                if (!o.ContainsKey("at")) s.At = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
                return s;
            }
            catch (Exception) { return null; }
        }

        /// <summary>A save from its JSON object (ToObject's shape, or one from before pets had their own); an empty save for null.</summary>
        public static TalesSave FromObject(Dictionary<string, object> o)
        {
            var s = new TalesSave();
            if (o == null) return s;
            s.Owner = o.Str("owner"); s.At = o.Num("at"); s.SiteIn = o.Num("siteIn");
            foreach (var x in o.Arr("seen")) if (x is string b) s.Seen.Add(b);
            foreach (var x in o.Arr("met")) if (x is string b) s.Met.Add(b);
            s.LairAt = o.Num("lair"); s.ChestReady = o.Truthy("chest");
            var rd = o.Obj("rd");
            if (rd == null) { s.RoadDays["g1"] = o.Str("stash"); s.RoadDays["s1"] = o.Str("shrine"); } // older saves: one stash and one shrine
            else foreach (var kv in rd) if (kv.Value is string day) s.RoadDays[kv.Key] = day;
            s.FindDay = o.Str("fd"); s.FindCount = o.Int("fn");
            s.RecallAt = o.Obj("stone").Num("rt");
            var meta = o.Obj("meta"); if (meta != null) foreach (var kv in meta) if (kv.Value is double d) s.Meta[kv.Key] = (int)d;
            foreach (var x in o.Arr("clsU")) if (x is string c) s.ClassesUnlocked.Add(c);
            foreach (var x in o.Arr("keep")) if (x is string k) s.Keepsakes.Add(k);
            foreach (var x in o.Arr("new")) if (x is string n) s.Unseen.Add(n);
            string pet = o.Str("pet", Unkeyed);
            s.Pet = pet.Length > 0 ? pet : Unkeyed;
            s.FillPet(o);
            s.PetAt = o.Has("petAt") ? o.Num("petAt") : s.At;
            var pets = o.Obj("pets");
            if (pets != null) foreach (var kv in pets) if (kv.Key != s.Pet && kv.Value is Dictionary<string, object> p) s.others[kv.Key] = Copy(p);
            return s;
        }

        // the pet fields from a pet's progress (PetObject's shape; a save object's top works too); null starts fresh
        void FillPet(Dictionary<string, object> o)
        {
            PetAt = o.Num("at");
            Personality = o.Str("pers", null);
            Bag.Clear(); foreach (var x in o.Arr("bag")) if (x is string b) Bag.Add(b);
            Fresh.Clear(); foreach (var x in o.Arr("fresh")) if (x is string b) Fresh.Add(b);
            Dust = o.Int("dust");
            HpFrac = o.Num("hp", 1); InkSaved = o.Num("ink"); VitalsAt = o.Num("vt"); Link = Math.Max(1, o.Int("link", 1));
            TownsSeen.Clear(); foreach (var x in o.Arr("towns")) if (x is string t) TownsSeen.Add(t);
            Gyms.Clear(); var gyms = o.Obj("gyms"); if (gyms != null) foreach (var kv in gyms) if (kv.Value is double d) Gyms[kv.Key] = d;
            var stone = o.Obj("stone");
            HomeStone = stone.Str("home"); LastStone = stone.Str("last");
            Me = new PetProfile();
            var me = o.Obj("me");
            if (me == null) return;
            Me.Cls = me.Str("cls", null); Me.Rxp = me.Num("rxp");
            ReadLists(me.Obj("own"), Me.Own); ReadLists(me.Obj("kit"), Me.Kit);
            var life = me.Obj("life"); if (life != null) foreach (var kv in life) if (kv.Value is double d) Me.Life[kv.Key] = d;
            var crxp = me.Obj("crxp"); if (crxp != null) foreach (var kv in crxp) if (kv.Value is double d) Me.ClassRxp[kv.Key] = d;
            var gear = me.Obj("gear"); if (gear != null) foreach (var kv in gear) if (kv.Value is string g) Me.Gear[kv.Key] = g;
            if (me.Has("order")) { Me.Order = new List<string>(); foreach (var x in me.Arr("order")) if (x is string k) Me.Order.Add(k); }
            Me.AutoUlt = !me.Has("ua") || me.Truthy("ua"); Me.Lane = me.Str("lane", null);
        }

        internal static List<object> Strs(IEnumerable<string> a) { var l = new List<object>(); foreach (var x in a) l.Add(x); return l; }
        static Dictionary<string, object> Lists(Dictionary<string, List<string>> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = Strs(kv.Value); return o; }
        internal static Dictionary<string, object> Nums(Dictionary<string, double> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = kv.Value; return o; }
        static Dictionary<string, object> Ints(Dictionary<string, int> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = (double)kv.Value; return o; }
        internal static Dictionary<string, object> Texts(Dictionary<string, string> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = kv.Value; return o; }
        static void ReadLists(Dictionary<string, object> from, Dictionary<string, List<string>> to)
        {
            if (from == null) return;
            foreach (var kv in from)
            {
                var l = new List<string>();
                if (kv.Value is List<object> a) foreach (var x in a) if (x is string s) l.Add(s);
                to[kv.Key] = l;
            }
        }
    }
}
