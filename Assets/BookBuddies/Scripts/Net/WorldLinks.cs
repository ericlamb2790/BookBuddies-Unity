using System;

namespace BookBuddies.Net
{
    /// <summary>Makes the link for a live room's address: offline rooms ("local:…") run inside the game, the rest are WebSockets.</summary>
    public static class WorldLinks
    {
        /// <summary>How an offline room's address starts (BBApi.LiveUrl puts the ticket after it).</summary>
        public const string LocalScheme = "local:";

        /// <summary>A new link, not connected yet, for this address.</summary>
        public static IWorldLink Create(string url) =>
            url.StartsWith(LocalScheme, StringComparison.Ordinal) ? new BookBuddies.Local.LocalLink() : (IWorldLink)new WorldSocket();
    }
}
