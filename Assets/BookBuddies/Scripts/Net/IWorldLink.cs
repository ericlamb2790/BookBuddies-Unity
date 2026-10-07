using System;
using System.Threading.Tasks;

namespace BookBuddies.Net
{
    /// <summary>
    /// A connection to a live town room: a WebSocket to the server's room (WorldSocket), or offline the room running
    /// inside the game (Local/LocalLink). Both speak town.js's JSON messages, so the town never has to know which.
    /// </summary>
    public interface IWorldLink : IDisposable
    {
        /// <summary>One message from the room (JSON text), raised from Poll on the main thread.</summary>
        event Action<string> OnMessage;

        /// <summary>Raised once from Poll when the link ends: 4000 joined elsewhere, 4003 sent home, 1006 couldn't connect, 1000 normal.</summary>
        event Action<int> OnClosed;

        /// <summary>Connected: sending works.</summary>
        bool Open { get; }

        /// <summary>Joins the room at this address. Never throws: a failure closes the link with 1006.</summary>
        Task Connect(string url);

        /// <summary>Call every frame: delivers waiting messages, then the close notice if the link ended.</summary>
        void Poll();

        /// <summary>Sends one message (JSON text); dropped while not Open.</summary>
        void Send(string json);
    }
}
