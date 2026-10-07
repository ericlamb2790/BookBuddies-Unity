using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Net;
using UnityEngine;

namespace BookBuddies.Pets
{
    /// <summary>
    /// Your pets: a nest of up to six, like the site, each with an id, a name and a look, and one of them active. The active
    /// pet's look is Buddy.Look, so the town, Tales and the title screen all show it. Signed in to the Unity server, the list
    /// lives there (/me/pets) and every change answers with the whole list; signed out, or on the website's server (which
    /// keeps extra pets in its own save), it lives on this device. Offline the backend on this PC keeps them the same way, and
    /// switching between online and offline loads that account's list (the list is saved with the account it belongs to).
    /// Pets that don't fit in the nest (after joining accounts) rest at the Pet Inn and can swap back in.
    /// </summary>
    public static class MyPets
    {
        public const int Max = 6;
        const string SaveKey = "bb.pets";

        public sealed class Pet { public string Id, Name, Look; }

        /// <summary>Raised with the new look when the active pet changes (switch, hatch, reroll or a server update).</summary>
        public static event Action<string> ActiveChanged;

        static List<Pet> pets, resting;
        static string activeId, owner;
        static (string server, bool keeps) petServer; // whether the server we're signed in to keeps pets

        public static IReadOnlyList<Pet> All => Current();
        public static bool Full => Current().Count >= Max;

        /// <summary>Pets napping at the Pet Inn: kept, just not in the nest (none on a server or a device without the inn).</summary>
        public static IReadOnlyList<Pet> Resting
        {
            get { Current(); return resting; }
        }

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
            if (await OnServer()) Apply(await BBApi.Pets());
        }

        /// <summary>A new pet from an egg; it becomes the active one, like hatching on the site. Null when you have six.</summary>
        public static async Task<Pet> Hatch(string name, string look)
        {
            if (Full) return null;
            if (await OnServer())
            {
                PetSync.BeforeSwitch();
                Apply(await BBApi.HatchPet(name, look));
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
                Apply(await BBApi.SetActivePet(pet.Id));
                return;
            }
            activeId = pet.Id;
            Saved();
        }

        /// <summary>Lets a nest pet nap at the Pet Inn (never the active one, and one always stays in the nest).</summary>
        public static async Task Rest(Pet pet) => Apply(await BBApi.RestPet(pet.Id, true));

        /// <summary>Brings a pet home from the Pet Inn, when the nest has room.</summary>
        public static async Task Wake(Pet pet) => Apply(await BBApi.RestPet(pet.Id, false));

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
                Apply(await BBApi.UpdatePet(pet.Id, look, name));
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

        /// <summary>A server reply with the whole list ({pets: [{id, name, look}], active, resting}) becomes your pets.</summary>
        public static void Apply(Dictionary<string, object> reply)
        {
            var list = Read(reply.Arr("pets"));
            if (list.Count == 0) return;
            pets = list;
            resting = Read(reply.Arr("resting"));
            owner = Settings.AccountId;
            activeId = reply.Str("active", list[0].Id);
            Saved();
        }

        static List<Pet> Read(List<object> list)
        {
            var read = new List<Pet>();
            foreach (var o in list)
                if (o is Dictionary<string, object> p && p.Str("look").Length > 0) read.Add(new Pet { Id = p.Str("id"), Name = p.Str("name"), Look = p.Str("look") });
            return read;
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
            if (!Buddy.Hatched) { if (pets.Count + resting.Count > 0) { pets.Clear(); resting.Clear(); Write(); } return; }
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
            resting = new List<Pet>();
            activeId = null;
            Dictionary<string, object> saved = null;
            try { saved = Json.ParseObject(PlayerPrefs.GetString(SaveKey, "")); } catch (FormatException) { }
            if (saved == null || saved.Str("owner") != owner) return;
            activeId = saved.Str("active", null);
            pets = Read(saved.Arr("pets"));
            resting = Read(saved.Arr("resting"));
        }

        static void Write()
        {
            PlayerPrefs.SetString(SaveKey, Json.Write(new Dictionary<string, object> { ["owner"] = owner, ["active"] = activeId, ["pets"] = Rows(pets), ["resting"] = Rows(resting) }));
            PlayerPrefs.Save();
        }

        static List<object> Rows(List<Pet> list) => list.ConvertAll(p => (object)new Dictionary<string, object> { ["id"] = p.Id, ["name"] = p.Name, ["look"] = p.Look });
    }
}
