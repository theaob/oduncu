namespace Oduncu.Sim
{
    public enum ResourceKind : byte
    {
        Food = 0,
        Wood = 1,
        Gold = 2,
        Stone = 3,
        None = 255,
    }

    public enum EntityCategory : byte
    {
        None = 0,
        Unit = 1,
        Building = 2,
        Resource = 3,
    }

    /// <summary>A price in all four resources.</summary>
    public readonly struct Cost
    {
        public const int ResourceCount = 4;

        public readonly int Food;
        public readonly int Wood;
        public readonly int Gold;
        public readonly int Stone;

        public Cost(int food, int wood, int gold, int stone)
        {
            Food = food;
            Wood = wood;
            Gold = gold;
            Stone = stone;
        }

        public static readonly Cost Zero = new Cost(0, 0, 0, 0);

        public int this[ResourceKind kind]
        {
            get
            {
                switch (kind)
                {
                    case ResourceKind.Food: return Food;
                    case ResourceKind.Wood: return Wood;
                    case ResourceKind.Gold: return Gold;
                    case ResourceKind.Stone: return Stone;
                    default: return 0;
                }
            }
        }

        public bool IsZero => Food == 0 && Wood == 0 && Gold == 0 && Stone == 0;

        public static Cost operator +(Cost a, Cost b) => new Cost(a.Food + b.Food, a.Wood + b.Wood, a.Gold + b.Gold, a.Stone + b.Stone);
        public static bool operator ==(Cost a, Cost b) => a.Food == b.Food && a.Wood == b.Wood && a.Gold == b.Gold && a.Stone == b.Stone;
        public static bool operator !=(Cost a, Cost b) => !(a == b);
        public override bool Equals(object obj) => obj is Cost c && c == this;
        public override int GetHashCode() => unchecked(((Food * 31 + Wood) * 31 + Gold) * 31 + Stone);
        public override string ToString() => Food + "F " + Wood + "W " + Gold + "G " + Stone + "S";

        public void WriteState(StateHasher h)
        {
            h.Write(Food);
            h.Write(Wood);
            h.Write(Gold);
            h.Write(Stone);
        }
    }

    /// <summary>Base stats for one entity kind, generated from Assets/Data/entities.csv.</summary>
    public sealed class EntityDef
    {
        public EntityKind Kind;
        public string Key = "";
        public string Name = "";
        public EntityCategory Category;
        public EntityTag Tags;
        public AgeId MinAge;
        public int MaxHp;
        public int Attack;
        public int MeleeArmor;
        public int PierceArmor;
        /// <summary>Distance from the attacker's centre to the target's nearest point.</summary>
        public FP Range;
        public int AttackTicks;
        /// <summary>Tiles per tick.</summary>
        public FP Speed;
        public int LineOfSight;
        /// <summary>Footprint side length in cells (buildings and resources).</summary>
        public int Size = 1;
        public Cost Cost;
        public int TrainTicks;
        public int BuildTicks;
        public int Population;
        public int ResourceAmount;
        /// <summary>What a resource gives when gathered; None for everything else.</summary>
        public ResourceKind Yields = ResourceKind.None;
        /// <summary>Which kinds this building can train.</summary>
        public EntityKind[] Trains = System.Array.Empty<EntityKind>();

        public bool IsUnit => Category == EntityCategory.Unit;
        public bool IsBuilding => Category == EntityCategory.Building;
        public bool IsResource => Category == EntityCategory.Resource;
        public bool IsMilitary => (Tags & EntityTag.Military) != 0;
        public bool CanGather => (Tags & EntityTag.Gatherer) != 0;
        public bool CanBuild => (Tags & EntityTag.Builder) != 0;
        public bool IsDropOff => (Tags & (EntityTag.DropoffFood | EntityTag.DropoffWood | EntityTag.DropoffGold | EntityTag.DropoffStone)) != 0;

        public bool HasTag(EntityTag tag) => (Tags & tag) == tag;

        public bool AcceptsDropOff(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Food: return (Tags & EntityTag.DropoffFood) != 0;
                case ResourceKind.Wood: return (Tags & EntityTag.DropoffWood) != 0;
                case ResourceKind.Gold: return (Tags & EntityTag.DropoffGold) != 0;
                case ResourceKind.Stone: return (Tags & EntityTag.DropoffStone) != 0;
                default: return false;
            }
        }
    }

    public sealed class TechDef
    {
        public TechId Id;
        public string Key = "";
        public string Name = "";
        public string Description = "";
        public EntityKind ResearchedAt;
        public AgeId MinAge;
        public Cost Cost;
        public int ResearchTicks;
        public TechId Requires;
    }

    public enum StatId : byte
    {
        MaxHp,
        Attack,
        MeleeArmor,
        PierceArmor,
        Range,
        Reload,
        Speed,
        LineOfSight,
        TrainTime,
        BuildTime,
        CostFood,
        CostWood,
        CostGold,
        CostStone,
    }

    public enum EffectOp : byte
    {
        Add,
        Multiply,
    }

    /// <summary>
    /// One stat change applied by a tech (or a civilization bonus). Targets one entity kind,
    /// every kind carrying a tag, or every kind when both are None.
    /// </summary>
    public readonly struct TechEffect
    {
        public readonly TechId Tech;
        public readonly EntityKind TargetKind;
        public readonly EntityTag TargetTag;
        public readonly StatId Stat;
        public readonly EffectOp Op;
        /// <summary>Amount to add (in the stat's own unit: ticks, tiles per tick, hit points) or the factor to multiply by.</summary>
        public readonly FP Value;

        public TechEffect(TechId tech, EntityKind targetKind, EntityTag targetTag, StatId stat, EffectOp op, FP value)
        {
            Tech = tech;
            TargetKind = targetKind;
            TargetTag = targetTag;
            Stat = stat;
            Op = op;
            Value = value;
        }

        public bool Applies(EntityDef def)
        {
            if (TargetKind != EntityKind.None) return def.Kind == TargetKind;
            if (TargetTag != EntityTag.None) return (def.Tags & TargetTag) != 0;
            return def.Kind != EntityKind.None;
        }
    }

    public sealed class AgeDef
    {
        public AgeId Id;
        public string Key = "";
        public string Name = "";
        public Cost Cost;
        public int ResearchTicks;
        /// <summary>How many finished buildings of RequiredBuildingAge (or later) the player needs.</summary>
        public int RequiredBuildings;
        public AgeId RequiredBuildingAge;
        /// <summary>A building that satisfies the requirement on its own, or None.</summary>
        public EntityKind OrBuilding;
    }

    /// <summary>The generated balance tables. See tools/DataGen and Assets/Data.</summary>
    public static partial class GameData
    {
        public static readonly EntityDef[] Entities = CreateEntities();
        public static readonly TechDef[] Techs = CreateTechs();
        public static readonly TechEffect[] TechEffects = CreateTechEffects();
        public static readonly AgeDef[] Ages = CreateAges();
    }

    public static class EntityDefs
    {
        public static EntityDef Get(EntityKind kind) => GameData.Entities[(int)kind];
    }
}
