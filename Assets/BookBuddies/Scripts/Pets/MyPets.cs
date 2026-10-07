using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Net;
using UnityEngine;

namespace BookBuddies.Pets
{
    /// <summary>
    /// Your pets: up to six, like the site, each with an id, a name and a look, and one of them active. The active pet's look
    /// is Buddy.Look, so the town, Tales and the title screen all show it. Signed in to the Unity server, the list lives there
    /// (/me/pets) and every change answers with the whole list; signed out, or on the website's server (which keeps extra pets
    /// in its own save), it lives on this device. Offline the backend on this PC keeps them the same way, and switching
    /// between online and offline loads that account's list (the list is saved with the account it belongs to).
    /// </summary>
    public static class MyPets
    {
        public const int Max = 6;
        const string SaveKey = "bb.pets";

        public sealed class Pet { public string Id, Name, Look; }

        /// <summary>Raised with the new look when the active pet changes (switch, hatch, reroll or a server update).</summary>
        public static event Action<string> ActiveChanged;

        static List<Pet> pets;
        static string activeId, owner;
        static (string server, bool keeps) petServer; // whether the server we're signed in to keeps pets

        public static IReadOnlyList<Pet> All => Current();
        public static bool Full => Current().Count >= Max;

        /// <summary>The active pet (null before your egg hatches).</summary>
        public static Pet Active
        {
            get { Current(); return Find(); }
        }

        /// <summary>The active pet's name, or "Your buddy".</summary>
        public static string ActiveName => Active is Pet p && p.Name.Length > 0 ? p.Name : "Your buddy";

        /// <summary>A name for a new pet that none of yours has yet (petNameRoll).</summary>
        public static string NewName()
        {
            var taken = new HashSet<string>(Current().ConvertAll(p => p.Name.ToLowerInvariant()));
            return PetSprites.Parts.Dna.RollName(Buddy.Roll, taken);
        }

        /// <summary>Gets the list from the server when it keeps pets (nothing to do otherwise).</summary>
        public static async Task Refresh()
        {
            if (await OnServer()) Take(await BBApi.Pets());
        }

        /// <summary>A new pet from an egg; it becomes the active one, like hatching on the site. Null when you have six.</summary>
        public static async Task<Pet> Hatch(string name, string look)
        {
            if (Full) return null;
            if (await OnServer())
            {
                PetSync.BeforeSwitch();
                Take(await BBApi.HatchPet(name, look));
                PetSync.Hatched(Active);
                return Active;
            }
            var pet = new Pet { Id = "p" + DateTime.UtcNow.Ticks.ToString("x"), Name = name, Look = look };
            Current().Add(pet);
            activeId = pet.Id;
            Saved();
            return pet;
        }

        /// <summary>The site's DNA reroll: a new random body, parts and colour (outfit, stage, form and shine stay).</summary>
        public static Task Reroll(Pet pet) => Change(pet, PetSprites.Parts.Dna.Reroll(pet.Look, Buddy.Roll), null);

        public static Task Rename(Pet pet, string name) => Change(pet, null, name);

        public static async Task MakeActive(Pet pet)
        {
            if (await OnServer())
            {
                PetSync.BeforeSwitch();
                Take(await BBApi.SetActivePet(pet.Id));
                return;
            }
            activeId = pet.Id;
            Saved();
        }

        /// <summary>Forgets the pets on this device (Buddy.Forget: signing out or starting over).</summary>
        public static void Forget()
        {
            pets = null;
            PlayerPrefs.DeleteKey(SaveKey);
        }

        static async Task Change(Pet pet, string look, string name)
        {
            if (await OnServer())
            {
                PetSync.Before(pet);
                Take(await BBApi.UpdatePet(pet.Id, look, name));
                return;
            }
            pet.Look = look ?? pet.Look;
            pet.Name = name ?? pet.Name;
            Saved();
        }

        // the Unity server keeps pets (its /health says it makes accounts), and so does the offline one; asked once per server
        static async Task<bool> OnServer()
        {
            if (!Settings.SignedIn) return false;
            if (Settings.IsLocal) return true;
            if (petServer.server != Settings.Server) petServer = (Settings.Server, await BBApi.CanHatch());
            return petServer.keeps;
        }

        // a server reply: {pets: [{id, name, look}], active}
        static void Take(Dictionary<string, object> reply)
        {
            var list = new List<Pet>();
            foreach (var o in reply.Arr("pets"))
                if (o is Dictionary<string, object> p) list.Add(new Pet { Id = p.Str("id"), Name = p.Str("name"), Look = p.Str("look") });
            if (list.Count == 0) return;
            pets = list;
            owner = Settings.AccountId;
            activeId = reply.Str("active", list[0].Id);
            Saved();
        }

        static List<Pet> Current()
        {
            if (pets == null || owner != Settings.AccountId) Load();
            Follow();
            return pets;
        }

        // Buddy.Look is what the rest of the game saves (signing in, hatching on the title screen), so the list follows it:
        // the pet with that look becomes active, or the active pet takes that look. No buddy, no pets.
        static void Follow()
        {
            if (!Buddy.Hatched) { if (pets.Count > 0) { pets.Clear(); Write(); } return; }
            var active = pets.Find(p => p.Id == activeId);
            if (active != null && active.Look == Buddy.Look) return;
            var same = pets.Find(p => p.Look == Buddy.Look);
            if (same == null && pets.Count == 0) pets.Add(same = new Pet { Id = "p1", Name = Buddy.Name.Length > 0 ? Buddy.Name : "Buddy", Look = Buddy.Look });
            if (same == null) { same = active ?? pets[0]; same.Look = Buddy.Look; }
            activeId = same.Id;
            Write();
        }

        // a change was made: keep it, and the active pet's look becomes Buddy.Look
        static void Saved()
        {
            Write();
            string look = Find()?.Look;
            if (string.IsNullOrEmpty(look) || look == Buddy.Look) return;
            Buddy.Save(null, look);
            ActiveChanged?.Invoke(look);
        }

        static Pet Find() => pets.Find(p => p.Id == activeId) ?? (pets.Count > 0 ? pets[0] : null);

        // saved with the account it belongs to, so signing in as someone else starts from their pets
        static void Load()
        {
            owner = Settings.AccountId;
            pets = new List<Pet>();
            activeId = null;
            Dictionary<string, object> saved = null;
            try { saved = Json.ParseObject(PlayerPrefs.GetString(SaveKey, "")); } catch (FormatException) { }
            if (saved == null || saved.Str("owner") != owner) return;
            activeId = saved.Str("active", null);
            foreach (var o in saved.Arr("pets"))
                if (o is Dictionary<string, object> p && p.Str("look").Length > 0) pets.Add(new Pet { Id = p.Str("id"), Name = p.Str("name"), Look = p.Str("look") });
        }

        static void Write()
        {
            var list = pets.ConvertAll(p => (object)new Dictionary<string, object> { ["id"] = p.Id, ["name"] = p.Name, ["look"] = p.Look });
            PlayerPrefs.SetString(SaveKey, Json.Write(new Dictionary<string, object> { ["owner"] = owner, ["active"] = activeId, ["pets"] = list }));
            PlayerPrefs.Save();
        }
    }
}
