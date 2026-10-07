using BookBuddies.Net;
using BookBuddies.Pets;
using BookBuddies.World;
using UnityEngine;

namespace BookBuddies.Tales
{
    /// <summary>Points the pure-C# Tales code (and the town book) at Unity: data comes from Resources, the saves sit in the game's data folder.</summary>
    public static class TalesBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            TalesData.TextLoader = Art.Text;
            TownBook.TextLoader = Art.Text;
            TalesSave.FilePath = System.IO.Path.Combine(Application.persistentDataPath, "tales.json");
            // each pet has its own progress: the save follows the pet you play, and progress made away from your online
            // account moves to the pet's twin once it's known
            TalesSave.PetKey = PetSync.TalesKey;
            MyPets.ActiveChanged += _ => TalesSave.FollowPet();
            Settings.ServerChanged += TalesSave.FollowPet;
            PetSync.TwinFound += (away, twin) => TalesSave.Current.Rekey("@" + away, twin);
        }
    }
}
