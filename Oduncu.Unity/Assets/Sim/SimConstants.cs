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
    }
}
