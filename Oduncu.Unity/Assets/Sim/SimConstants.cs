namespace Oduncu.Sim
{
    public static class SimConstants
    {
        /// <summary>Simulation ticks per second. Presentation interpolates between ticks.</summary>
        public const int TicksPerSecond = 10;

        /// <summary>Upper bound on ticks the presentation may run in one frame when catching up.</summary>
        public const int MaxCatchUpTicks = 5;

        public const int MaxPlayers = 4;
        public const int NeutralOwner = -1;

        /// <summary>Hard population cap (design section 4.4). Housing below this comes from Town Centers and houses.</summary>
        public const int MaxPopulation = 75;

        /// <summary>The Town Center's auto-villager toggle switches itself off at this population (section 4.2).</summary>
        public const int AutoVillagerStopPopulation = 40;

        /// <summary>Standard start stockpile: food, wood, gold, stone.</summary>
        public static readonly Cost StartingResources = new Cost(200, 200, 100, 200);

        public const int TrainQueueLength = 5;

        public const int VillagerCarryCapacity = 10;

        /// <summary>Villagers shoot animals from this range with this damage (design: hunting attack on animals only).</summary>
        public static readonly FP VillagerHuntRange = FP.FromInt(3);
        public const int VillagerHuntDamage = 4;

        /// <summary>Herdables change owner when only another player's units are within this many tiles.</summary>
        public const int HerdRange = 4;
        public const int HerdCheckInterval = 5;

        /// <summary>Deer flee this far when a unit comes within FleeTriggerRange.</summary>
        public const int FleeTriggerRange = 3;
        public const int FleeDistance = 5;
        public const int AnimalCheckInterval = 5;

        /// <summary>Repairing a building from 1 HP to full costs 1/RepairCostDivisor of its price.</summary>
        public const int RepairCostDivisor = 2;

        /// <summary>The economy planner runs for each player this often, staggered by player index.</summary>
        public const int PlannerInterval = 10;
        public const int PlannerSearchRadius = 40;

        /// <summary>Radius in tiles within which a villager looks for another tree when one runs out.</summary>
        public const int ResourceSearchRadius = 10;

        /// <summary>Idle military units look for enemies this often, staggered by entity id.</summary>
        public const int AutoEngageInterval = 5;

        /// <summary>How often a unit chasing a moving target recomputes its path.</summary>
        public const int ChaseRepathInterval = 10;

        // ------------------------------------------------------------------ combat (plan 3.4)

        /// <summary>Preallocated projectile slots; more in flight than this is a bug, not a balance case.</summary>
        public const int MaxProjectiles = 1024;

        /// <summary>Monks convert a unit after channelling this long in range.</summary>
        public const int ConversionTicks = 40;
        /// <summary>Monks heal one hit point this often.</summary>
        public const int HealInterval = 5;

        /// <summary>Unit centres closer than this push apart.</summary>
        public static readonly FP SeparationRadius = FP.Ratio(6, 10);
        /// <summary>Most a unit is pushed in one tick, in tiles.</summary>
        public static readonly FP MaxSeparationPush = FP.Ratio(1, 10);

        /// <summary>Group moves with more units than this share a flow field.</summary>
        public const int FlowFieldGroupThreshold = 8;
        /// <summary>Cached flow fields; the least recently used is recomputed for a new goal.</summary>
        public const int FlowFieldPoolSize = 8;
        /// <summary>Units following a flow field switch to A* to their own slot this close to the goal.</summary>
        public const int FlowHandoffDistance = 6;
    }
}
