using BookBuddies.Pets;

namespace BookBuddies.Tales
{
    /// <summary>
    /// Evolution in Tales (site build 532+, tqPetHero): each of the pet's three forms adds Might (attack),
    /// Toughness (defense), Vigor (health) and Agility (speed), leaning toward what its body, mane, tail, fur and
    /// personality favour (petEvoBuffs). An egg gets nothing.
    /// </summary>
    public static class HeroEvo
    {
        /// <summary>The shared pet parts (evolution tables live in Data/pet_parts.json).</summary>
        static PetParts Parts
        {
            get
            {
                if (PetParts.TextLoader == null) PetParts.TextLoader = TalesData.TextLoader;
                return PetParts.Current;
            }
        }

        /// <summary>petEvoBuffs: % added to Might, Toughness, Vigor and Agility for this look (its "ev" form) and personality.</summary>
        public static (double mig, double tuf, double vig, double agi) Buffs(string lookJson, string personality)
        {
            var parts = Parts;
            var look = PetLook.Parse(string.IsNullOrEmpty(lookJson) ? "{}" : lookJson, parts) ?? new PetLook();
            int evo = look.Stage > 0 ? look.Evo : 0;
            var b = parts.Evolution.BuffsAt(parts.Evolution.Points(look, personality, parts), evo);
            return (b["mig"], b["tuf"], b["vig"], b["agi"]);
        }

        /// <summary>
        /// tqPetHero's base stats with the evolution buffs folded in (each stat is rounded once, like the site):
        /// hp, attack and defense from the class, mult and meta upgrades; speed from stage and rank.
        /// </summary>
        public static void SetBase(HeroInfo info, ClassDef c, double mult, HeroSeed s)
        {
            var b = Buffs(s.Look, s.Personality);
            info.BaseHp = JsMath.RoundI(c.Hp * mult * (1 + .06 * s.MetaVit) * (1 + b.vig / 100));
            info.BaseAtk = JsMath.RoundI(c.Atk * mult * (1 + .05 * s.MetaAtk) * (1 + b.mig / 100));
            info.BaseDef = JsMath.RoundI(c.Def * mult * (1 + b.tuf / 100));
            info.BaseSpd = JsMath.RoundI(c.Spd * (1 + .04 * s.Stage) * (1 + .05 * s.Rank) * (1 + b.agi / 100));
        }
    }
}
