using System.Collections.Generic;

namespace BookBuddies.Tales
{
    /// <summary>
    /// A villain as it appears in one fight or on the road: the base definition plus its tqVariant roll
    /// (affix, epithet, tint, size, aura). Drawn by FoeArt, made by FoeFactory.
    /// </summary>
    public sealed class FoeVariant
    {
        public FoeDef Def;
        public string Dn;                 // display name with affix and epithet, or null
        public double Hp = 1, Atk = 1;    // multipliers after the variant
        public bool HasHp;                // false keeps the "no hp" grump face for bosses with no epithet
        public string Ax, Tc, Au, Xp;     // affix name, tint colour, aura colour, extra costume part
        public double Vs, Vdf, Vsx;       // size (0 = 1), def multiplier (0 = 1), speed bonus
        public string Sp, Sn, Si;         // special (may be swapped by the affix)
        public BossPhase[] Phases;        // a Book Boss's own rises (v.phz); null: a wild boss rises once
        public FoeVariant[] Team;         // who its summon calls in (v.team); null or empty: random minions
        public string Name => Dn ?? Def.N;
    }

    /// <summary>One rise of a Book Boss (bbPhz): its new name and boast, its twists (enrage shield summon regen haste) and HP/attack multipliers.</summary>
    public sealed class BossPhase
    {
        public string Name, Say;
        public string[] Twists = new string[0];
        public double Hp, Atk;
    }

    /// <summary>Gear totals for a hero (lootSum/lootHero's gx): percentages, perks and power.</summary>
    public sealed class GearSum
    {
        public readonly Dictionary<string, double> Stats = new Dictionary<string, double>(); // hp atk def spd cc cd ls th dg ink rg sh luck
        public readonly Dictionary<string, double> Perks = new Dictionary<string, double>(); // perk -> value
        public readonly Dictionary<string, string> PerkFrom = new Dictionary<string, string>(); // perk -> item name
        public double Power;
        public double this[string k] => Stats.TryGetValue(k, out var v) ? v : 0;
        public double Perk(string k) => Perks.TryGetValue(k, out var v) ? v : 0;
    }

    /// <summary>A pet in battle: everything tqPetHero + lootHero build, plus tactics.</summary>
    public sealed class HeroInfo
    {
        public string Cls, Look, PetKey, Sig, NatureName, NatureIcon;
        public int Rank, Stage, Rn;
        public bool Shiny;
        public int Lvl = 1, Xp;
        public int BaseHp, BaseAtk, BaseDef, BaseSpd;
        public GearSum Gear = new GearSum();
        public double Cc, Cd, Ls, Th, Dg, Rg, Sh; public int GearInk; // the gx numbers used in battle
        public List<string> Kit = new List<string>(), Own = new List<string>(), Known = new List<string>();
        public List<string> Order;          // tactics order (null = Smart)
        public bool AutoUlt = true, WantUlt, PhoenixUsed, PlotUsed;
        public int Dark, Weak;              // literary class counters (dk, wk)
        public int MetaInk;                 // library: Bottomless Inkwell, +1 starting ink per level
        public bool MetaRev;                // library: Second Wind, back up at half HP once a battle
    }

    /// <summary>One side's fighter. Status keys follow the site: stun bleed poison regen expose taunt pow dodge shield crit wind (+ weak).</summary>
    public sealed class BattleUnit
    {
        public string Key, Name;
        public bool IsFoe, Boss, Elite, Ko;
        public int Gen;
        public double Lvl;
        public double Max, Hp;
        public double Atk, Def, Spd;
        public int Ink;
        public char Lane = 'c';
        public int Phase;                   // boss rises so far
        public readonly Dictionary<string, double> St = new Dictionary<string, double>();
        public List<MoveDef> Moves = new List<MoveDef>();  // heroes: known moves; foes: hit + special
        public readonly Dictionary<string, int> Cds = new Dictionary<string, int>();
        public HeroInfo Hero;               // heroes only
        public FoeVariant Foe;              // foes only
        public string BaseName;             // foes: name before "Second Edition"
        public List<(double at, string m)> Tactics; // tale foes (the site's phs): at HP ≤ Max·at, switch to m (summon enrage shield heal), in order; null = none
        public double S(string k) => St.TryGetValue(k, out var v) ? v : 0;
        public bool Alive => !Ko && Hp > 0;
        public double HpFrac => Max <= 0 ? 0 : Hp / Max;
    }

    /// <summary>One thing that happened to one unit inside an event (the site's fx entry).</summary>
    public sealed class BattleHit
    {
        public string Unit;
        public double Damage, Absorbed, Heal, ShieldGained, Hp, Max, Shield;
        public double Effect = 1;           // 1.5 strong, .75 weak (genre)
        public bool Crit, Miss, Ko, Revive, Small;
        public string Buff, Dot, Pop;       // emoji tags and short pop text
    }

    /// <summary>
    /// What the engine says happened, in order. The battle screen animates each one.
    /// Kind: atk heal shield buff dot skip intro talk win lose revive phase rise fate cheer slamw lane.
    /// </summary>
    public sealed class BattleEvent
    {
        public string Kind, Actor, Name, Icon;
        public bool Ult, Aoe, Seq, Foe;
        public string Uv, Anim, Projectile;  // ult cinematic, animation kind (arc beam rain slash quake orbit burst), arc projectile kind
        public readonly List<BattleHit> Fx = new List<BattleHit>();
        public readonly List<KeyValuePair<string, string>> Pops = new List<KeyValuePair<string, string>>(); // unit key (or null) -> text
        public string Line, Speaker, Listener; // talk
        public string Zone;                  // slam lane (l, c, r)
        public int Roll;                     // fate d20
        public string Helper;                // fate NPC
        public string Reply;                 // talk: the pet's answer to Line
        public string Result;                // fate: fumble, meh, good, great or nat (a natural 20)
        public int Bonus;                    // fate: level bonus added to Roll
        public string Sub;                   // rise: what the boss gained ("hits much harder")
    }

    /// <summary>What Bramble Road hands the battle (the site's sp for wildRun).</summary>
    public sealed class BattleSetup
    {
        public string Title, Place;
        public int Lvl = 2;
        public double Mul = 1;
        public bool Guardian;
        public bool BookBoss;                // a town's Book Boss (the site's bb): the top bar says so
        public readonly List<(FoeVariant v, bool boss, bool elite)> Foes = new List<(FoeVariant, bool, bool)>();
        public double HpFrac = 1;
        public int Ink;                      // ink carried in from the road; in a tale, the dungeon's extra starting ink (may be < 0)
        public int Tier = 1;
        public bool Cave;
        public string Look, PetName;         // your buddy (Buddy.Look / Buddy.Name): the engine builds the hero with HeroFactory.Build
        public BattleUnit Hero;              // or a ready-made hero (tests, previews); wins over Look
        public TaleBattle Tale;              // a fight inside a tale (EpicBattle.Setup); null = a wild fight
    }

    /// <summary>
    /// The tale rules for one fight (epStartBattle): who and what the dungeon put in it. With it the engine uses HeroLvl
    /// uncapped, ReadyFoes as built, clamps ink to 0..6 and allows Second Wind only while !Revived.
    /// </summary>
    public sealed class TaleBattle
    {
        public const string Fight = "fight", Elite = "elite", Boss = "boss";
        public string Kind = Fight;          // fight, elite or boss: renown 2/4/8, drops and xp (B.kind)
        public int HeroLvl = 1;              // the run hero's tale level (no 6 cap, no 12 clamp)
        public readonly List<string> Boons = new List<string>(); // boon keys, one per stack (S.boons)
        public string Hazard;                // TQ_HZ key of the land's hazard; null = none or warded (stage 2)
        public string Twist;                 // TQ_TWIST key (tqTwist, already applied to ReadyFoes); null = none (stage 2)
        public string Ally;                  // TQ_NPC key that joins at the intro (E.ally); null = none
        public bool Revived;                 // Second Wind already used this tale (S.revived): no revive in this fight
        public int Ch = 1;                   // the run's chapter (loot item level, story entries)
        public int Sc;                       // the land's scene 0-5 (arena and storybook backdrop)
        public int Gold;                     // the run's ink drops going in (S.gold): all a foe's steal can take
        public readonly List<BattleUnit> ReadyFoes = new List<BattleUnit>(); // foes built by EpicBattle (variants, mods, Tactics)
    }

    /// <summary>How a fight ended, for rewards and the road.</summary>
    public sealed class BattleOutcome
    {
        public bool Won;
        public double HpFrac;
        public int Ink, Rounds;
        public readonly List<BattleUnit> Defeated = new List<BattleUnit>();
        public string BestMove; public double BestHit;
        public readonly List<string> Drops = new List<string>(); // item strings, filled by Loot.ForWin or Loot.ForTale
        public int RenownGained; public bool LevelUp, Evolved; public string LevelNote;
        public BattleUnit Hero;              // your buddy as the fight left it
        public int Lvl;                      // the battle level (life.best)
        public int Nat20s, Helpers;          // fate rolls this fight, for life stats
        public bool Revived;                 // Second Wind brought the party back in this fight (tale: once per tale)
        public (string n, string ab)? Ult;   // the first hero ult: pet name and move name (storybook)
        public int Crits, Heals;             // hero crits and HP healed by heroes this fight (run stats)
        public readonly List<string> Naps = new List<string>();       // names of heroes knocked out (storybook)
        public readonly List<string> HelperKeys = new List<string>(); // TQ_NPC keys of storybook friends who helped
        public int Gold;                     // tale: ink drops won (a gold friend's +20) less those a foe stole in this fight
    }
}
