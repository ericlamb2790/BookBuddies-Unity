using UnityEngine;

namespace BookBuddies.Tales
{
    /// <summary>Points the pure-C# Tales code at Unity: data comes from Resources, the save file sits in the game's data folder.</summary>
    public static class TalesBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            TalesData.TextLoader = Art.Text;
            TalesSave.FilePath = System.IO.Path.Combine(Application.persistentDataPath, "tales.json");
        }
    }
}
