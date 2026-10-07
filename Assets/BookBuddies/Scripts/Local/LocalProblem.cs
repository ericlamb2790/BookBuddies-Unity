// Ports Server/src/db.js:118-121 (Problem): an error the player should see, with the HTTP status the Worker would send.

using System;

namespace BookBuddies.Local
{
    /// <summary>A refusal from the offline server, worded and numbered like the Worker's {error} replies.</summary>
    public sealed class LocalProblem : Exception
    {
        /// <summary>The HTTP status the online server answers with (400, 401, 404, 409…).</summary>
        public readonly int Status;

        public LocalProblem(string message, int status = 400) : base(message) { Status = status; }

        /// <summary>A 500 for a bug in the offline server; "inner" is what actually went wrong.</summary>
        public LocalProblem(string message, int status, Exception inner) : base(message, inner) { Status = status; }
    }
}
