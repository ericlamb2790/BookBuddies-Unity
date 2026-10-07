// The client's end of a town room offline: what Net/WorldSocket is to the Worker's rooms, LocalLink is to LocalTowns.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookBuddies.Net;

namespace BookBuddies.Local
{
    /// <summary>
    /// The live town's link when playing offline: joins a room running inside the game (LocalTowns) instead of opening a
    /// WebSocket, and speaks the same town.js messages. What the room sends waits here until Poll hands it over, as a
    /// socket's messages would, and Poll keeps the rooms ticking, so they only run while someone is in town.
    /// </summary>
    public sealed class LocalLink : IWorldLink
    {
        /// <summary>One message from the room (JSON text), raised from Poll.</summary>
        public event Action<string> OnMessage;
        /// <summary>Raised once from Poll when the link ends: 4000 joined elsewhere, 4003 sent home, 1006 couldn't join.</summary>
        public event Action<int> OnClosed;

        readonly Queue<string> inbox = new Queue<string>();
        LocalTown room;
        string id; // our connection id in the room
        int closeCode = -1;
        bool closeReported;

        /// <summary>In the room: sending works.</summary>
        public bool Open => room != null;

        /// <summary>
        /// Joins the room a "local:" address names (BBApi.LiveUrl: the escaped ticket "&lt;pid&gt;|&lt;town&gt;:&lt;shard&gt;").
        /// An unknown player or a full room closes the link with 1006 and a player on a break with 4003, on the next Poll.
        /// </summary>
        public Task Connect(string url)
        {
            string address = url.StartsWith(WorldLinks.LocalScheme, StringComparison.Ordinal) ? url.Substring(WorldLinks.LocalScheme.Length) : url;
            string ticket = Uri.UnescapeDataString(address);
            int bar = ticket.LastIndexOf('|');
            var to = bar > 0 ? LocalTowns.Room(ticket.Substring(bar + 1)) : null;
            id = to?.Join(this, ticket.Substring(0, bar));
            if (id != null) room = to;
            else Drop(1006); // keeps 4003 when the room sent a player on a break home
            return Task.CompletedTask;
        }

        /// <summary>Call every frame: runs the rooms' due turns, hands over the waiting messages, then the close notice.</summary>
        public void Poll()
        {
            LocalTowns.Tick(LocalServer.Now);
            // only what waited when Poll began: replies to what the handlers send now come next frame, as over a socket
            for (int n = inbox.Count; n > 0 && inbox.Count > 0; n--) OnMessage?.Invoke(inbox.Dequeue());
            if (closeCode >= 0 && !closeReported)
            {
                closeReported = true;
                OnClosed?.Invoke(closeCode);
            }
        }

        /// <summary>Sends one message (JSON text) to the room; dropped while not Open.</summary>
        public void Send(string json) => room?.Receive(id, json);

        /// <summary>Leaves the room (the others see "bye"); nothing more is delivered.</summary>
        public void Dispose()
        {
            room?.Leave(id);
            room = null;
            inbox.Clear();
        }

        // the room's end: a message for this player, and the room closing the link
        internal void Deliver(string json) => inbox.Enqueue(json);

        internal void Drop(int code)
        {
            room = null;
            if (closeCode < 0) closeCode = code;
        }
    }
}
