using System.Collections.Generic;

namespace Oduncu.Sim
{
    public enum UnitState : byte
    {
        Idle = 0,
        Moving = 1,
        Gathering = 2,
        Returning = 3,
        Building = 4,
        Attacking = 5,
        Repairing = 6,
        /// <summary>A dead animal left on the map as food. Not a valid attack target.</summary>
        Carcass = 7,
        /// <summary>Inside a building: off the map, out of the spatial index, safe from attack.</summary>
        Garrisoned = 8,
        /// <summary>Walking to a building to garrison in it (TargetId).</summary>
        Garrisoning = 9,
        /// <summary>A monk healing a friendly unit (TargetId).</summary>
        Healing = 10,
    }

    /// <summary>One slot of a production queue: what is being made and what was paid for it.</summary>
    public struct QueueItem
    {
        public EntityKind Unit;
        /// <summary>The exact price charged when queued, refunded in full on cancel.</summary>
        public Cost Paid;
    }

    /// <summary>
    /// One thing on the map: a unit, a building or a resource. Plain mutable data; all
    /// behaviour lives in Simulation so the state can be hashed and serialized trivially.
    /// Entity objects are pooled and reused after death, so hold ids rather than references
    /// across ticks; Simulation.Find returns null for an id that has died.
    /// </summary>
    public sealed class Entity
    {
        public const int PathCapacity = 48;

        public int Id;
        public EntityKind Kind;
        /// <summary>Static data for the kind (size, tags, what it trains).</summary>
        public EntityDef Def;
        /// <summary>The owner's resolved stats for this kind, including researched upgrades.</summary>
        public UnitStats Stats;
        public int Owner = SimConstants.NeutralOwner;
        public bool Alive = true;
        public int Hp;

        /// <summary>Centre for units; for buildings and resources the centre of the footprint.</summary>
        public FPVector2 Position;
        /// <summary>Cell the unit stands in, or the origin (min corner) of a building footprint.</summary>
        public Cell Cell;

        // Unit state
        public UnitState State;
        public Cell MoveTarget;
        /// <summary>Reused path buffer; only meaningful while HasPath is true.</summary>
        public readonly List<Cell> Path = new List<Cell>(PathCapacity);
        public bool HasPath;
        public int PathIndex;
        public Cell PathGoal;
        public int PathAge;
        public int TargetId;
        public int GatherSourceId;
        /// <summary>Kind of resource last gathered, used to find the next one when it runs out.</summary>
        public EntityKind GatherKind;
        public int PreviousGatherSourceId;
        public int BuildSiteId;
        public int Carry;
        public ResourceKind CarryKind = ResourceKind.None;
        /// <summary>Fraction of the next resource unit gathered so far.</summary>
        public FP GatherProgress;
        public int Cooldown;
        /// <summary>Last entity that damaged this one; boar retaliate against it.</summary>
        public int LastAttackerId;
        /// <summary>Hold-ground stance: only attack what is already in range and never chase.</summary>
        public bool HoldGround;
        /// <summary>The current target was picked by auto-engage rather than ordered.</summary>
        public bool AutoTarget;
        /// <summary>Building this unit is garrisoned in, or 0.</summary>
        public int GarrisonedIn;
        /// <summary>Ticks a monk has spent converting its current target.</summary>
        public int ChannelTicks;
        /// <summary>Group move speed limit (the slowest unit's speed), or zero for none.</summary>
        public FP SpeedCap;
        /// <summary>Follow the shared flow field toward FlowGoal until close, then A* to MoveTarget.</summary>
        public bool UsesFlow;
        public Cell FlowGoal;

        // Building state
        public bool UnderConstruction;
        public int BuildProgress;
        public readonly List<QueueItem> TrainQueue = new List<QueueItem>(SimConstants.TrainQueueLength);
        public int TrainProgress;
        /// <summary>Keep one villager queued while on (Town Center auto-villager toggle).</summary>
        public bool AutoQueue;
        public bool HasRally;
        public Cell RallyCell;
        /// <summary>Entity the rally point is on (a resource to gather, or a building), or 0 for a plain cell.</summary>
        public int RallyTargetId;
        /// <summary>Hit points repaired since the building was last at full health; repair is charged from this.</summary>
        public int RepairedHp;
        /// <summary>For farms: the villager working it, so the planner sends one farmer per farm.</summary>
        public int FarmerId;
        /// <summary>Units inside this building.</summary>
        public int GarrisonCount;

        // Resource state
        public int Amount;

        /// <summary>Bucket in the spatial index, or -1 when not indexed. Not part of the hashed state.</summary>
        internal int IndexBucket = -1;

        public bool IsUnit => Def.IsUnit;
        public bool IsBuilding => Def.IsBuilding;
        public bool IsResource => Def.IsResource;

        public CellRect Footprint => new CellRect(Cell.X, Cell.Y, Def.Size);

        /// <summary>Clear every field for reuse from the pool.</summary>
        internal void Reset(int id, EntityKind kind, EntityDef def, int owner, UnitStats stats)
        {
            Id = id;
            Kind = kind;
            Def = def;
            Stats = stats;
            Owner = owner;
            Alive = true;
            Hp = stats.MaxHp;
            Position = FPVector2.Zero;
            Cell = default;
            State = UnitState.Idle;
            MoveTarget = default;
            Path.Clear();
            HasPath = false;
            PathIndex = 0;
            PathGoal = default;
            PathAge = 0;
            TargetId = 0;
            GatherSourceId = 0;
            GatherKind = EntityKind.None;
            PreviousGatherSourceId = 0;
            BuildSiteId = 0;
            Carry = 0;
            CarryKind = ResourceKind.None;
            GatherProgress = FP.Zero;
            Cooldown = 0;
            LastAttackerId = 0;
            HoldGround = false;
            AutoTarget = false;
            GarrisonedIn = 0;
            ChannelTicks = 0;
            SpeedCap = FP.Zero;
            UsesFlow = false;
            FlowGoal = default;
            GarrisonCount = 0;
            UnderConstruction = false;
            BuildProgress = 0;
            TrainQueue.Clear();
            TrainProgress = 0;
            AutoQueue = false;
            HasRally = false;
            RallyCell = default;
            RallyTargetId = 0;
            RepairedHp = 0;
            FarmerId = 0;
            Amount = 0;
            IndexBucket = -1;
        }

        public void WriteState(StateHasher h)
        {
            h.Write(Id);
            h.Write((int)Kind);
            h.Write(Owner);
            h.Write(Alive);
            h.Write(Hp);
            h.Write(Position);
            h.Write(Cell);
            h.Write((int)State);
            h.Write(MoveTarget);
            h.Write(PathIndex);
            h.Write(TargetId);
            h.Write(GatherSourceId);
            h.Write((int)GatherKind);
            h.Write(PreviousGatherSourceId);
            h.Write(BuildSiteId);
            h.Write(Carry);
            h.Write((int)CarryKind);
            h.Write(GatherProgress);
            h.Write(Cooldown);
            h.Write(LastAttackerId);
            h.Write(HoldGround);
            h.Write(AutoTarget);
            h.Write(GarrisonedIn);
            h.Write(ChannelTicks);
            h.Write(SpeedCap);
            h.Write(UsesFlow);
            h.Write(FlowGoal);
            h.Write(GarrisonCount);
            h.Write(UnderConstruction);
            h.Write(BuildProgress);
            h.Write(TrainProgress);
            h.Write(AutoQueue);
            h.Write(HasRally);
            h.Write(RallyCell);
            h.Write(RallyTargetId);
            h.Write(RepairedHp);
            h.Write(FarmerId);
            h.Write(Amount);
            h.Write(TrainQueue.Count);
            for (int i = 0; i < TrainQueue.Count; i++)
            {
                h.Write((int)TrainQueue[i].Unit);
                TrainQueue[i].Paid.WriteState(h);
            }
            h.Write(HasPath);
            if (HasPath)
            {
                h.Write(Path.Count);
                for (int i = 0; i < Path.Count; i++) h.Write(Path[i]);
            }
        }
    }

    public sealed class PlayerState
    {
        private static readonly string[] NotEnough = { "not enough food", "not enough wood", "not enough gold", "not enough stone" };

        public int Index;
        public int Food;
        public int Wood;
        public int Gold;
        public int Stone;
        public bool Alive = true;
        /// <summary>Units alive plus units in production, recounted every tick.</summary>
        public int Population;
        /// <summary>Housing from finished buildings, capped at SimConstants.MaxPopulation.</summary>
        public int PopulationCap;
        /// <summary>Set once the player has finished a Town Center, Castle or military production building.</summary>
        public bool HadConquestBuilding;
        public EconomyTargets EconomyTargets = EconomyTargets.Off;
        /// <summary>Stats for every entity kind with this player's researched upgrades applied.</summary>
        public readonly PlayerStats Stats = new PlayerStats();

        public int Get(ResourceKind kind)
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

        public void Add(ResourceKind kind, int amount)
        {
            switch (kind)
            {
                case ResourceKind.Food: Food += amount; break;
                case ResourceKind.Wood: Wood += amount; break;
                case ResourceKind.Gold: Gold += amount; break;
                case ResourceKind.Stone: Stone += amount; break;
            }
        }

        public void Set(Cost amounts)
        {
            Food = amounts.Food;
            Wood = amounts.Wood;
            Gold = amounts.Gold;
            Stone = amounts.Stone;
        }

        public bool CanAfford(Cost cost) => Food >= cost.Food && Wood >= cost.Wood && Gold >= cost.Gold && Stone >= cost.Stone;

        /// <summary>The rejection message for the first resource (food, wood, gold, stone) that is short, or null.</summary>
        public string ShortageMessage(Cost cost)
        {
            if (Food < cost.Food) return NotEnough[0];
            if (Wood < cost.Wood) return NotEnough[1];
            if (Gold < cost.Gold) return NotEnough[2];
            if (Stone < cost.Stone) return NotEnough[3];
            return null;
        }

        public void Pay(Cost cost)
        {
            Food -= cost.Food;
            Wood -= cost.Wood;
            Gold -= cost.Gold;
            Stone -= cost.Stone;
        }

        public void Refund(Cost cost)
        {
            Food += cost.Food;
            Wood += cost.Wood;
            Gold += cost.Gold;
            Stone += cost.Stone;
        }

        public void WriteState(StateHasher h)
        {
            h.Write(Index);
            h.Write(Food);
            h.Write(Wood);
            h.Write(Gold);
            h.Write(Stone);
            h.Write(Alive);
            h.Write(Population);
            h.Write(PopulationCap);
            h.Write(HadConquestBuilding);
            EconomyTargets.WriteState(h);
            Stats.WriteState(h);
        }
    }
}
