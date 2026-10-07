using BookBuddies.Local;
using UnityEngine;

namespace BookBuddies.Net
{
    /// <summary>
    /// Keeps your world open while you host it (Local/LocalHost): every frame it answers your visitors and runs the town
    /// rooms, and every couple of seconds it tells the PCs on your network about the world (LocalBeacon), so it shows in
    /// their list. Leaving offline play or quitting closes the world. A world only ever opens from the title's Worlds
    /// card: a new launch never reopens it.
    /// </summary>
    public sealed class HostRunner : MonoBehaviour
    {
        static HostRunner running;
        LocalBeacon beacon;

        /// <summary>
        /// Opens your offline world to other PCs under this name (switch to offline play first). False when it couldn't,
        /// with LocalHost.Error saying why in plain words.
        /// </summary>
        public static bool Open(string name)
        {
            if (!Settings.IsLocal) return false;
            if (running == null)
            {
                var go = new GameObject("Hosting");
                DontDestroyOnLoad(go);
                running = go.AddComponent<HostRunner>();
            }
            LocalHost.Name = name;
            return LocalHost.Start();
        }

        /// <summary>Closes your world: visitors are sent home, what they did is saved, and it's no longer announced.</summary>
        public static void Close()
        {
            if (LocalHost.Running) AutoSave.Now("world closed"); // visitors' changes otherwise wait for the 3-minute save
            LocalHost.Stop();
            if (running != null) running.StopAnnouncing();
        }

        void OnEnable()
        {
            Settings.ServerChanged += LeftOffline;
            LocalHost.Failed += Debug.LogException; // a visitor's request tripped over a bug here: worth a look
        }

        void OnDisable()
        {
            Settings.ServerChanged -= LeftOffline;
            LocalHost.Failed -= Debug.LogException;
        }

        // your world is your offline one, so it closes when you play online or visit someone else's
        void LeftOffline()
        {
            if (!Settings.IsLocal && LocalHost.Running) Close();
        }

        void Update()
        {
            if (!LocalHost.Running) { StopAnnouncing(); return; }
            LocalHost.Pump();
            if (beacon == null) beacon = LocalBeacon.Announce(LocalHost.Info); // read on the beacon's thread: a fresh copy each time
        }

        void StopAnnouncing()
        {
            beacon?.Dispose();
            beacon = null;
        }

        void OnApplicationQuit() => Close();

        void OnDestroy()
        {
            Close();
            if (running == this) running = null;
        }
    }
}
