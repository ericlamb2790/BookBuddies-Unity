using System;
using System.Collections.Generic;
using System.IO;

namespace BookBuddies.Tales
{
    /// <summary>
    /// The tale files next to the Tales save (lobby.md step 4, storybook.md step 5): tale_run.json holds the one solo tale
    /// (the site's private small read), tale_books.json the kept storybooks, newest first, at most BookCap. Pure C#.
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

        /// <summary>Saves the tale now (leaving, ending, after each room).</summary>
        public static void Save(TaleRun run)
        {
            pending = null;
            Write("tale_run.json", run.ToJson());
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

        /// <summary>Forgets the solo tale (closed from the lobby, or left after its end).</summary>
        public static void Clear()
        {
            pending = null;
            string path = PathOf("tale_run.json");
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (Exception) { /* nothing else to do */ }
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

        // written whole to a temp file first, so a crash mid-write never leaves half a tale
        static void Write(string file, object value)
        {
            string path = PathOf(file);
            if (path == null) return;
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Json.Write(value));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception) { /* a full disk shouldn't stop the game */ }
        }
    }
}
