using System.Collections.Generic;

namespace Oduncu.Sim
{
    public enum RevealMode : byte
    {
        Normal = 0,
        AllVisible = 1,
    }

    public sealed partial class Simulation
    {
        /// <summary>Longest wall one BuildWall command places.</summary>
        public const int MaxWallSegments = 40;
        /// <summary>Builders who finish a wall segment move on to unfinished segments this close.</summary>
        private const int WallContinueRadius = 6;

        private bool _fogDirty = true;

        /// <summary>
        /// Match setting, as AoE2's "reveal map": Normal fog, or AllVisible, which lets every
        /// player target anything. Set before the first tick. Tests that are not about fog use AllVisible.
        /// </summary>
        public RevealMode Reveal { get; set; } = RevealMode.Normal;

        // ------------------------------------------------------------------ fog of war

        /// <summary>
        /// Recompute what every player sees from their entities' line of sight. Runs at the end
        /// of every tick, and before commands when entities were added from outside the tick
        /// (match setup, tests), so commands are always checked against current sight.
        /// </summary>
        private void UpdateFog()
        {
            _fogDirty = false;
            Fog.BeginUpdate();
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive || e.Owner < 0 || e.Owner >= Players.Length) continue;
                if (e.State == UnitState.Garrisoned || e.State == UnitState.Carcass || e.IsResource) continue;
                int radius = e.Stats.LineOfSight;
                Cell centre = e.Cell;
                if (e.IsBuilding)
                {
                    radius += e.Def.Size / 2;
                    centre = e.Position.ToCell();
                }
                Fog.Reveal(e.Owner, centre, radius);
            }

            // Buildings and resources stay known once seen (no peeking at what was built later in explored ground).
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive || e.IsUnit) continue;
                for (int p = 0; p < Players.Length; p++)
                {
                    int bit = 1 << p;
                    if ((e.SeenBy & bit) != 0 || e.Owner == p) continue;
                    if (AnyCellVisible(p, e.Footprint)) e.SeenBy |= bit;
                }
            }
        }

        private bool AnyCellVisible(int player, CellRect r)
        {
            for (int y = r.Y; y <= r.MaxY; y++)
                for (int x = r.X; x <= r.MaxX; x++)
                    if (Fog.IsVisible(player, x, y)) return true;
            return false;
        }

        /// <summary>
        /// Whether a player can currently target an entity: their own always; other units while
        /// visible; buildings and resources once the player has seen them.
        /// </summary>
        public bool CanSee(int player, Entity e)
        {
            if (e == null) return false;
            if (Reveal == RevealMode.AllVisible || (e.Owner == player && player >= 0)) return true;
            if (e.IsUnit) return Fog.IsVisible(player, e.Cell);
            return player >= 0 && player < Players.Length && (e.SeenBy & (1 << player)) != 0;
        }

        /// <summary>
        /// Whether a Build command for this kind at this origin would be accepted, apart from
        /// cost: in bounds, free, nobody standing there, age reached, and explored by the player.
        /// </summary>
        public bool CanPlace(int player, EntityKind kind, Cell origin)
        {
            EntityDef def = EntityDefs.Get(kind);
            if (!def.IsBuilding || kind == EntityKind.TownCenter || def.MinAge > Players[player].Age) return false;
            var rect = new CellRect(origin.X, origin.Y, def.Size);
            if (!Map.IsRectInBounds(rect) || !Map.IsRectFree(rect) || AnyUnitInside(rect)) return false;
            for (int y = rect.Y; y <= rect.MaxY; y++)
                for (int x = rect.X; x <= rect.MaxX; x++)
                    if (!Fog.IsExplored(player, x, y)) return false;
            return true;
        }

        // ------------------------------------------------------------------ walls

        /// <summary>Cells of a straight wall from one cell to another: 8-way steps along the longer axis.</summary>
        public static void WallLine(Cell from, Cell to, List<Cell> into)
        {
            into.Clear();
            int dx = to.X - from.X, dy = to.Y - from.Y;
            int steps = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
            if (steps >= MaxWallSegments) steps = MaxWallSegments - 1;
            for (int i = 0; i <= steps; i++)
            {
                int total = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
                // Integer interpolation rounded half away from zero, so the line is symmetric.
                int x = from.X + (total == 0 ? 0 : RoundDiv(dx * i, total));
                int y = from.Y + (total == 0 ? 0 : RoundDiv(dy * i, total));
                into.Add(new Cell(x, y));
            }
        }

        private static int RoundDiv(int a, int b) => a >= 0 ? (2 * a + b) / (2 * b) : -((-2 * a + b) / (2 * b));

        private readonly List<Cell> _scratchCells = new List<Cell>(MaxWallSegments);

        /// <summary>
        /// Place a straight line of wall foundations (section 7's drag gesture). Each segment is
        /// charged as it is placed; blocked cells and cells with units on them are skipped, and the
        /// line stops when the player runs out. The villagers build the segments nearest first.
        /// </summary>
        private void ApplyBuildWall(Command c)
        {
            EntityDef def = EntityDefs.Get(c.EntityType);
            if (!def.IsBuilding || !def.HasTag(EntityTag.Wall) || def.Size != 1) { Reject("not a wall"); return; }
            PlayerState player = Players[c.Player];
            if (def.MinAge > player.Age) { Reject(RequiresAge(def.MinAge)); return; }
            if (!Map.InBounds(c.Cell) || !Map.InBounds(c.Cell2)) { Reject("wall out of bounds"); return; }

            List<Entity> builders = _scratchUnits;
            builders.Clear();
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u != null && u.Def.CanBuild) builders.Add(u);
            }
            if (builders.Count == 0) { Reject("no builder"); return; }

            Cost cost = player.Stats.Of(c.EntityType).Cost;
            List<Cell> line = _scratchCells;
            WallLine(c.Cell, c.Cell2, line);
            Entity first = null;
            bool shortage = false;
            for (int i = 0; i < line.Count; i++)
            {
                var rect = new CellRect(line[i].X, line[i].Y, 1);
                if (!Map.IsRectFree(rect) || AnyUnitInside(rect)) continue;
                if (!player.CanAfford(cost))
                {
                    // Partial walls are fine: place what the stockpile pays for and say why it stopped.
                    Reject(player.ShortageMessage(cost));
                    shortage = true;
                    break;
                }
                player.Pay(cost);
                Entity site = SpawnStructure(c.EntityType, c.Player, line[i], underConstruction: true);
                if (first == null) first = site;
            }
            line.Clear();
            if (first == null)
            {
                if (!shortage) Reject("nowhere to build");
                builders.Clear();
                return;
            }
            for (int i = 0; i < builders.Count; i++)
            {
                Entity u = builders[i];
                if (u.State == UnitState.Gathering || u.State == UnitState.Returning) u.PreviousGatherSourceId = u.GatherSourceId;
                SetIdle(u);
                u.State = UnitState.Building;
                u.BuildSiteId = first.Id;
            }
            builders.Clear();
        }

        /// <summary>The player's nearest unfinished wall segment near a unit, for builders moving down the line.</summary>
        private Entity NextWallSite(Entity u)
        {
            var filter = new EntityFilter { Owner = OwnerMatch.Owned, Player = u.Owner, Category = EntityCategory.Building, Tags = EntityTag.Wall };
            Entity best = null;
            FP bestDist = FP.Zero;
            // A plain filter cannot ask for "under construction", so scan the few candidates by hand.
            int r = WallContinueRadius;
            int bx0 = Index.BucketXOf(u.Cell.X - r - MaxStructureSize), bx1 = Index.BucketXOf(u.Cell.X + r);
            int by0 = Index.BucketYOf(u.Cell.Y - r - MaxStructureSize), by1 = Index.BucketYOf(u.Cell.Y + r);
            for (int by = by0; by <= by1; by++)
            {
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    var bucket = Index.Bucket(bx, by);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        Entity e = bucket[i];
                        if (!e.Alive || !e.UnderConstruction || !filter.Matches(e)) continue;
                        FP d = FPVector2.SqrDistance(u.Position, e.Position);
                        if (d > FP.FromInt(r * r)) continue;
                        if (best == null || d < bestDist || (d == bestDist && e.Id < best.Id))
                        {
                            best = e;
                            bestDist = d;
                        }
                    }
                }
            }
            return best;
        }
    }
}
