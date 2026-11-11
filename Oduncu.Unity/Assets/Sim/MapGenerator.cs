namespace Oduncu.Sim
{
    /// <summary>
    /// Milestone 0 map: two Town Centers in opposite corners, three villagers each, a
    /// guaranteed grove next to each base, and seeded forest blobs elsewhere.
    /// </summary>
    public static class MapGenerator
    {
        public const int DefaultSize = 48;

        public static Simulation CreateDefault(int seed)
        {
            var sim = new Simulation(DefaultSize, DefaultSize, 2, (ulong)seed);
            for (int p = 0; p < sim.Players.Length; p++) sim.SetCivilization(p, CivId.Woodlanders);
            var rng = new DeterministicRandom((ulong)seed * 7919UL + 17UL);

            PlaceBase(sim, 0, new Cell(4, 4), mirror: false);
            PlaceBase(sim, 1, new Cell(DefaultSize - 7, DefaultSize - 7), mirror: true);

            // Guaranteed groves so every scripted scenario has wood in reach.
            PlaceGrove(sim, 4, 11, 5, 4);
            PlaceGrove(sim, DefaultSize - 9, DefaultSize - 15, 5, 4);

            // Random forest blobs away from both bases.
            for (int blob = 0; blob < 7; blob++)
            {
                int cx = rng.Next(6, DefaultSize - 6);
                int cy = rng.Next(6, DefaultSize - 6);
                if (Cell.Chebyshev(new Cell(cx, cy), new Cell(6, 6)) < 12) continue;
                if (Cell.Chebyshev(new Cell(cx, cy), new Cell(DefaultSize - 7, DefaultSize - 7)) < 12) continue;
                int radius = rng.Next(2, 5);
                for (int y = cy - radius; y <= cy + radius; y++)
                    for (int x = cx - radius; x <= cx + radius; x++)
                        if (rng.Chance(75)) sim.SpawnStructure(EntityKind.Tree, SimConstants.NeutralOwner, new Cell(x, y), false);
            }
            return sim;
        }

        private static void PlaceBase(Simulation sim, int player, Cell tcOrigin, bool mirror)
        {
            sim.SpawnStructure(EntityKind.TownCenter, player, tcOrigin, underConstruction: false);
            int vx = mirror ? tcOrigin.X - 1 : tcOrigin.X + 3;
            for (int i = 0; i < 3; i++) sim.SpawnUnit(EntityKind.Villager, player, new Cell(vx, tcOrigin.Y + i));
        }

        private static void PlaceGrove(Simulation sim, int x0, int y0, int w, int h)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    sim.SpawnStructure(EntityKind.Tree, SimConstants.NeutralOwner, new Cell(x, y), false);
        }
    }
}
