using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The tale files next to the Tales save (lobby.md step 4, storybook.md step 5): tale_run.json holds the one solo tale
    /// (the site's private small read), tale_daily.json the daily tale ({day, over, score, label, state}), tale_books.json
    /// the kept storybooks, newest first, at most BookCap. Writes go to a temp file first and the newest wins, also when
    /// AutoSave writes on a background thread (FlushAsync). Pure C#.
    /// </summary>
    public static class TaleStore
    {
        /// <summary>The most storybooks kept (the server's cap on the site).</summary>
        public const int BookCap = 40;
        const double SaveDelay = 3; // seconds: saveSoon's debounce

        /// <summary>Where the files go; null = the folder of TalesSave.FilePath.</summary>
        public static string Folder;

        static TaleRun pending;
        static DateTime dueAt;
        static List<Dictionary<string, object>> books;
        static long snapshots; // writes asked for (on the main thread)
        static readonly Dictionary<string, long> written = new Dictionary<string, long>(); // each file's newest write on disk (lock it)

        static string PathOf(string file)
        {
            string dir = Folder ?? (string.IsNullOrEmpty(TalesSave.FilePath) ? null : Path.GetDirectoryName(TalesSave.FilePath));
            return dir == null ? null : Path.Combine(dir, file);
        }

        // ---- the solo tale ----

        /// <summary>The saved solo tale (going or ended); null when there is none.</summary>
        public static TaleRun Load()
        {
            var o = Read("tale_run.json") as Dictionary<string, object>;
            try { return TaleRun.FromJson(o); }
            catch (Exception) { return null; } // a damaged file is a tale lost, not a crash
        }

        /// <summary>Saves the tale now (leaving, ending, after each room); a daily tale goes to the daily file (saveDaily).</summary>
        public static void Save(TaleRun run)
        {
            pending = null;
            Writer(run)?.Invoke();
        }

        // the file a tale goes in and the write that puts it there, its JSON made now
        static Action Writer(TaleRun run)
        {
            if (run.Daily == null) return Writer("tale_run.json", run.ToJson());
            return Writer("tale_daily.json", new Dictionary<string, object>
            {
                ["day"] = run.Daily, ["over"] = run.Over, ["score"] = (double)TaleLife.Score(run), ["label"] = TaleLife.Label(run),
                ["state"] = run.Over ? null : run.ToJson(), // a finished daily keeps only its result: that day is done
            });
        }

        /// <summary>Saves the tale a few seconds from now; later calls push the save back (saveSoon). Tick does the save.</summary>
        public static void SaveSoon(TaleRun run)
        {
            pending = run;
            dueAt = DateTime.UtcNow.AddSeconds(SaveDelay);
        }

        /// <summary>Saves a SaveSoon tale once it is due; call every frame while a tale is open.</summary>
        public static void Tick()
        {
            if (pending != null && DateTime.UtcNow >= dueAt) Save(pending);
        }

        /// <summary>Clearing the device: a tale waiting to be saved and the books read from disk are dropped.</summary>
        public static void Forget()
        {
            pending = null;
            books = null;
        }

        /// <summary>Saves a SaveSoon tale now instead of when it's due (before a fight, when the game closes).</summary>
        public static void Flush()
        {
            if (pending != null) Save(pending);
        }

        /// <summary>Flush, with only the JSON made on the caller's thread: the file is written on a background thread.</summary>
        public static Task FlushAsync()
        {
            if (pending == null) return Task.CompletedTask;
            var write = Writer(pending);
            pending = null;
            return write == null ? Task.CompletedTask : Task.Run(write);
        }

        /// <summary>Forgets the solo tale (closed from the lobby, or left after its end).</summary>
        public static void Clear()
        {
            pending = null;
            Writer("tale_run.json", null)?.Invoke();
        }

        // ---- the daily tale ----

        /// <summary>The daily tale saved for that day (going or finished); null when it hasn't been played (or the file is another day's).</summary>
        public static DailySave LoadDaily(string day)
        {
            var o = Read("tale_daily.json") as Dictionary<string, object>;
            if (o == null || o.Str("day", null) != day) return null;
            var d = new DailySave { Day = day, Over = o.Truthy("over"), Score = o.Int("score"), Label = o.Str("label") };
            try { if (!d.Over) d.Run = TaleRun.FromJson(o.Obj("state")); }
            catch (Exception) { /* a damaged run: the day's try is still spent */ }
            return d;
        }

        // ---- storybooks ----

        /// <summary>The kept storybooks, newest first (StoryBook.Make's objects, each with its "key").</summary>
        public static List<Dictionary<string, object>> Books
        {
            get
            {
                if (books != null) return books;
                books = new List<Dictionary<string, object>>();
                if (Read("tale_books.json") is List<object> list)
                    foreach (var b in list) if (b is Dictionary<string, object> d && d.Arr("pages") != null) books.Add(d);
                return books;
            }
        }

        /// <summary>The key a tale's book is kept under: one book per tale and volume.</summary>
        public static string KeyOf(TaleRun run) => $"{run.Id}:{run.Vol}";

        /// <summary>True when this tale's book is on the shelf.</summary>
        public static bool IsKept(TaleRun run) => Books.Exists(b => b.Str("key") == KeyOf(run));

        /// <summary>Puts the tale's book on the shelf (newest first; the oldest past BookCap go).</summary>
        public static void Keep(TaleRun run)
        {
            if (run.Book == null || IsKept(run)) return;
            run.Book["key"] = KeyOf(run);
            Books.Insert(0, run.Book);
            if (books.Count > BookCap) books.RemoveRange(BookCap, books.Count - BookCap);
            SaveBooks();
        }

        /// <summary>Takes a book off the shelf.</summary>
        public static void Delete(Dictionary<string, object> book)
        {
            if (Books.Remove(book)) SaveBooks();
        }

        static void SaveBooks() => Write("tale_books.json", new List<object>(books));

        /// <summary>Drops what is loaded so the next read comes from disk (tests, a new player).</summary>
        public static void Reload() { books = null; pending = null; }

        // ---- files ----

        static object Read(string file)
        {
            string path = PathOf(file);
            if (path == null || !File.Exists(path)) return null;
            try { return Json.Parse(File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }

        static void Write(string file, object value) => Writer(file, value)?.Invoke();

        // the write of a file (null deletes it), its JSON made now; null when there's no folder to write in
        static Action Writer(string file, object value)
        {
            string path = PathOf(file);
            if (path == null) return null;
            string text = value == null ? null : Json.Write(value);
            long n = ++snapshots;
            return () => Put(path, text, n);
        }

        // one write at a time, written whole to a temp file first so a crash mid-write never leaves half a tale;
        // a write older than the file's newest is dropped
        static void Put(string path, string text, long n)
        {
            lock (written)
            {
                if (written.TryGetValue(path, out long last) && n <= last) return;
                written[path] = n;
                try
                {
                    if (text != null) TalesSave.WriteAtomic(path, text);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception) { /* a full disk shouldn't stop the game */ }
            }
        }
    }

    /// <summary>A day's daily tale as saved: its result (score, label, over) and, while it is going, the run to continue.</summary>
    public sealed class DailySave
    {
        public string Day, Label;
        public int Score;
        public bool Over;
        public TaleRun Run;

        /// <summary>True when that day's try is spent: finished, or its run can't be read back.</summary>
        public bool Done => Over || Run == null;
    }
}
