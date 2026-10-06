using System.Collections.Generic;

namespace Oduncu.Sim
{
    public enum EntityKind : byte
    {
        None = 0,
        Villager = 1,
        Militia = 2,
        TownCenter = 3,
        Barracks = 4,
        Tree = 5,
    }

    public enum UnitState : byte
    {
        Idle = 0,
        Moving = 1,
        Gathering = 2,
        Returning = 3,
        Building = 4,
        Attacking = 5,
    }

    /// <summary>Static, data-driven stats for one entity kind. Later milestones load these from CSV.</summary>
    public sealed class EntityDef
    {
        public EntityKind Kind;
        public string Name = "";
        public bool IsUnit;
        public bool IsBuilding;
        public bool IsResource;
        public bool IsMilitary;
        public bool CanGather;
        public bool CanBuild;
        public int MaxHp;
        public int Attack;
        public int Armor;
        /// <summary>Distance from the attacker's centre to the target's nearest point.</summary>
        public FP Range;
        public int AttackTicks;
        /// <summary>Tiles per tick.</summary>
        public FP Speed;
        public int LineOfSight;
        /// <summary>Footprint side length in cells (buildings and resources).</summary>
        public int Size = 1;
        public int CostWood;
        public int TrainTicks;
        public int BuildTicks;
        public int Population;
        public int ResourceAmount;
        /// <summary>Which kinds this building can train.</summary>
        public EntityKind[] Trains = System.Array.Empty<EntityKind>();
        public bool IsDropOff;
    }

    public static class EntityDefs
    {
        private static readonly EntityDef[] Table = Build();

        public static EntityDef Get(EntityKind kind) => Table[(int)kind];

        private static EntityDef[] Build()
        {
            var t = new EntityDef[6];
            t[(int)EntityKind.None] = new EntityDef { Kind = EntityKind.None, Name = "None" };
            t[(int)EntityKind.Villager] = new EntityDef
            {
                Kind = EntityKind.Villager, Name = "Villager", IsUnit = true, CanGather = true, CanBuild = true,
                MaxHp = 25, Attack = 3, Armor = 0, Range = FP.Ratio(16, 10), AttackTicks = 15,
                Speed = FP.Ratio(8, 100), LineOfSight = 4, CostWood = 50, TrainTicks = 200, Population = 1,
            };
            t[(int)EntityKind.Militia] = new EntityDef
            {
                Kind = EntityKind.Militia, Name = "Militia", IsUnit = true, IsMilitary = true,
                MaxHp = 40, Attack = 4, Armor = 1, Range = FP.Ratio(16, 10), AttackTicks = 15,
                Speed = FP.Ratio(9, 100), LineOfSight = 6, CostWood = 60, TrainTicks = 210, Population = 1,
            };
            t[(int)EntityKind.TownCenter] = new EntityDef
            {
                Kind = EntityKind.TownCenter, Name = "Town Center", IsBuilding = true, IsDropOff = true,
                MaxHp = 2400, Armor = 3, Size = 3, LineOfSight = 8, CostWood = 275, BuildTicks = 1500,
                Trains = new[] { EntityKind.Villager },
            };
            t[(int)EntityKind.Barracks] = new EntityDef
            {
                Kind = EntityKind.Barracks, Name = "Barracks", IsBuilding = true,
                MaxHp = 1200, Armor = 2, Size = 3, LineOfSight = 6, CostWood = 175, BuildTicks = 350,
                Trains = new[] { EntityKind.Militia },
            };
            t[(int)EntityKind.Tree] = new EntityDef
            {
                Kind = EntityKind.Tree, Name = "Tree", IsResource = true, Size = 1, ResourceAmount = 100,
            };
            return t;
        }
    }

    /// <summary>
    /// One thing on the map: a unit, a building or a resource. Plain mutable data; all
    /// behaviour lives in Simulation so the state can be hashed and serialized trivially.
    /// </summary>
    public sealed class Entity
    {
        public int Id;
        public EntityKind Kind;
        public EntityDef Def;
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
        public List<Cell> Path;
        public int PathIndex;
        public Cell PathGoal;
        public int PathAge;
        public int TargetId;
        public int GatherSourceId;
        public int PreviousGatherSourceId;
        public int BuildSiteId;
        public int Carry;
        public int WorkTimer;
        public int Cooldown;

        // Building state
        public bool UnderConstruction;
        public int BuildProgress;
        public List<EntityKind> TrainQueue;
        public int TrainProgress;

        // Resource state
        public int Amount;

        public bool IsUnit => Def.IsUnit;
        public bool IsBuilding => Def.IsBuilding;
        public bool IsResource => Def.IsResource;

        public CellRect Footprint => new CellRect(Cell.X, Cell.Y, Def.Size);

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
            h.Write(PreviousGatherSourceId);
            h.Write(BuildSiteId);
            h.Write(Carry);
            h.Write(WorkTimer);
            h.Write(Cooldown);
            h.Write(UnderConstruction);
            h.Write(BuildProgress);
            h.Write(TrainProgress);
            h.Write(Amount);
            if (TrainQueue != null)
            {
                h.Write(TrainQueue.Count);
                for (int i = 0; i < TrainQueue.Count; i++) h.Write((int)TrainQueue[i]);
            }
            if (Path != null)
            {
                h.Write(Path.Count);
                for (int i = 0; i < Path.Count; i++) h.Write(Path[i]);
            }
        }
    }

    public sealed class PlayerState
    {
        public int Index;
        public int Wood;
        public bool Alive = true;

        public void WriteState(StateHasher h)
        {
            h.Write(Index);
            h.Write(Wood);
            h.Write(Alive);
        }
    }
}
