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

        /// <summary>Milestone 0 has no houses; every player gets a fixed population cap.</summary>
        public const int PopulationCap = 20;

        /// <summary>Standard start stockpile: food, wood, gold, stone.</summary>
        public static readonly Cost StartingResources = new Cost(200, 200, 100, 200);

        public const int TrainQueueLength = 5;

        /// <summary>Ticks between gather increments for a villager standing at a resource.</summary>
        public const int GatherTicks = 8;
        public const int VillagerCarryCapacity = 10;

        /// <summary>Radius in tiles within which a villager looks for another tree when one runs out.</summary>
        public const int ResourceSearchRadius = 10;

        /// <summary>Idle military units look for enemies this often, staggered by entity id.</summary>
        public const int AutoEngageInterval = 5;

        /// <summary>How often a unit chasing a moving target recomputes its path.</summary>
        public const int ChaseRepathInterval = 10;
    }
}
