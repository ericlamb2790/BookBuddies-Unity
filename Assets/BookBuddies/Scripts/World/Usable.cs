using UnityEngine;

namespace BookBuddies.World
{
    /// <summary>
    /// Something in the world you can use: a pet, a seat, a place or a foe. The hover ring and its little label,
    /// and the "E  Sit" prompt when you stand next to it, all describe one of these.
    /// </summary>
    public sealed class Usable
    {
        public enum Kinds { Pet, Seat, Spot, Foe }

        public Kinds Kind;
        public object Thing;   // the PetActor, TownMap.Seat, TownMap.Spot or road foe
        public Vector2 At;     // map point on the ground under it: the ring's centre
        public Vector2 Ring;   // ring radii in tiles
        public Vector3 Top;    // world point just above it, for the label and the prompt
        public string Icon, Label, Verb;

        public bool Danger => Kind == Kinds.Foe;

        /// <summary>True when both describe the same thing.</summary>
        public bool Same(Usable other) => other != null && other.Kind == Kind && Equals(other.Thing, Thing);
    }
}
