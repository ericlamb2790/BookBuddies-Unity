using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>A shared fight's line to the other games (PartyBattle implements it). Null: a fight of your own.</summary>
    public interface IBattleLink
    {
        /// <summary>This game runs the fight.</summary>
        bool Captain { get; }
        /// <summary>Follower: the captain went quiet (8 s): carry on alone.</summary>
        bool Lost { get; }
        /// <summary>Follower: steps received but not yet played (speed up when more than 1).</summary>
        int Behind { get; }
        /// <summary>Captain: hold step n while a live follower is 2+ steps behind (gives up on one after 3 s).</summary>
        bool Waiting(int n);
        /// <summary>Captain: everyone's inputs since the last step, in arrival order.</summary>
        List<BattleInput> TakeInputs();
        /// <summary>Captain: a fresh seed.</summary>
        uint NewSeed();
        /// <summary>Captain: step n done, tell the others (over: who won once it's over, else null).</summary>
        void Sent(int n, uint seed, List<BattleInput> ins, List<object> snap, bool? over);
        /// <summary>Follower: step n from the captain, if it has come.</summary>
        bool Next(int n, out uint seed, out List<BattleInput> ins);
        /// <summary>Follower: the captain's state after step n, if it has come.</summary>
        bool After(int n, out List<object> snap, out bool? over);
        /// <summary>Follower: step n played (an ack for pacing).</summary>
        void Played(int n);
        /// <summary>Your own input: the captain queues it; a follower sends it to the captain.</summary>
        void Ask(BattleInput i);
        /// <summary>Your input sent but not applied yet (the dock shows a queued Ultimate).</summary>
        bool Pending(string act);
        /// <summary>The player behind a hero (for its plate), or null.</summary>
        string NameOf(string heroKey);
        /// <summary>The screen is gone (stop listening).</summary>
        void Closed();
    }
}
