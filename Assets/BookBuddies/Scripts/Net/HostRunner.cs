using BookBuddies.Local;
using UnityEngine;

namespace BookBuddies.Net
{
    /// <summary>
    /// Keeps your world open while you host it (Local/LocalHost): every frame it answers your visitors and runs the town
    /// rooms, and (unless it's code only) every couple of seconds it tells the PCs on your network about the world
    /// (LocalBeacon), so it shows in their list. Leaving offline play or quitting closes the world. A world only ever
    /// opens from the title's Worlds card: a new launch never reopens it.
    /// </summary>
    public sealed class HostRunner : MonoBehaviour
    {
        static HostRunner running;
        static bool listed;
        LocalBeacon beacon;

        /// <summary>
        /// Opens your offline world to other PCs under this name (switch to offline play first); "listed" shows it in the
        /// list on nearby PCs, otherwise friends need the join code. False when it couldn't, with LocalHost.Error saying why.
        /// </summary>
        public static bool Open(string name, bool listed = true)
        {
            if (!Settings.IsLocal) return false;
            if (running == null)
            {
                var go = new GameObject("Hosting");
                DontDestroyOnLoad(go);
                running = go.AddComponent<HostRunner>();
            }
            LocalHost.Name = name;
            HostRunner.listed = listed;
            return LocalHost.Start(); // you lead the party everyone who visits joins (Update keeps it so)
        }

        /// <summary>Closes your world: visitors are sent home, what they did is saved, the party ends, and it's no longer announced.</summary>
        public static void Close()
        {
            if (LocalHost.Running) AutoSave.Now("world closed"); // visitors' changes otherwise wait for the 3-minute save
            LocalHost.Stop(); // the party ends with it (LocalParty.Reset)
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
            if (!LocalHost.Running || !listed) StopAnnouncing();
            if (!LocalHost.Running) return;
            string leader = Settings.AccountIdFor(Settings.Local); // you lead it, even when you signed in after it opened
            if (LocalParty.Leader != (leader.Length > 0 ? leader : null))
            {
                LocalParty.Leader = leader;
                Debug.Log(leader.Length > 0 ? "BookBuddies party: your world's party is led by " + leader : "BookBuddies party: hosting while signed out offline, so nobody leads the party yet");
            }
            LocalHost.Pump();
            if (beacon == null && listed) beacon = LocalBeacon.Announce(LocalHost.Info); // read on the beacon's thread: a fresh copy each time
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
