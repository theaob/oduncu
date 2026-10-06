using System;
using System.Collections.Generic;

namespace Oduncu.Sim
{
    /// <summary>
    /// The deterministic game state and its tick function. No engine types, no floats,
    /// no wall-clock time. Given the same seed and the same command stream, two
    /// Simulation instances produce the same state hash on every tick.
    /// </summary>
    public sealed partial class Simulation
    {
        public readonly MapGrid Map;
        public readonly PlayerState[] Players;
        public readonly DeterministicRandom Rng;
        public int CurrentTick { get; private set; }

        private readonly List<Entity> _entities = new List<Entity>();
        private readonly Dictionary<int, Entity> _byId = new Dictionary<int, Entity>();
        private readonly Pathfinder _pathfinder;
        private readonly StateHasher _hasher = new StateHasher();
        private readonly List<Entity> _spawnedThisTick = new List<Entity>();
        private int _nextId = 1;

        /// <summary>Alive entities in ascending id order. Do not mutate.</summary>
        public IReadOnlyList<Entity> Entities => _entities;

        public Simulation(int width, int height, int playerCount, ulong seed)
        {
            if (playerCount < 1 || playerCount > SimConstants.MaxPlayers) throw new ArgumentOutOfRangeException(nameof(playerCount));
            Map = new MapGrid(width, height);
            Players = new PlayerState[playerCount];
            for (int i = 0; i < playerCount; i++) Players[i] = new PlayerState { Index = i, Wood = SimConstants.StartingWood };
            Rng = new DeterministicRandom(seed);
            _pathfinder = new Pathfinder(Map);
        }

        public Entity Find(int id)
        {
            Entity e;
            return id != 0 && _byId.TryGetValue(id, out e) && e.Alive ? e : null;
        }

        /// <summary>Advance one tick. Commands are applied in list order before anything moves.</summary>
        public void Step(IReadOnlyList<Command> commands)
        {
            CurrentTick++;
            if (commands != null)
            {
                for (int i = 0; i < commands.Count; i++) ApplyCommand(commands[i]);
            }

            // Iterate by index over a snapshot count: entities spawned this tick are appended
            // and get their first update next tick, which keeps ordering deterministic.
            int count = _entities.Count;
            for (int i = 0; i < count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive) continue;
                if (e.IsUnit) UpdateUnit(e);
                else if (e.IsBuilding) UpdateBuilding(e);
            }

            RemoveDead();
            UpdatePlayersAlive();
        }

        public ulong ComputeHash()
        {
            _hasher.Reset();
            _hasher.Write(CurrentTick);
            _hasher.Write(Rng.State);
            _hasher.Write(_nextId);
            for (int i = 0; i < Players.Length; i++) Players[i].WriteState(_hasher);
            for (int i = 0; i < _entities.Count; i++) _entities[i].WriteState(_hasher);
            Map.WriteState(_hasher);
            return _hasher.Value;
        }

        // ------------------------------------------------------------------ spawning

        public Entity SpawnUnit(EntityKind kind, int owner, Cell at)
        {
            var def = EntityDefs.Get(kind);
            if (!def.IsUnit) throw new ArgumentException(kind + " is not a unit");
            var e = NewEntity(kind, def, owner);
            e.Cell = Map.Clamp(at);
            e.Position = FPVector2.CellCentre(e.Cell);
            e.State = UnitState.Idle;
            return e;
        }

        /// <summary>Place a building or resource. Returns null if the footprint is out of bounds or blocked.</summary>
        public Entity SpawnStructure(EntityKind kind, int owner, Cell origin, bool underConstruction)
        {
            var def = EntityDefs.Get(kind);
            if (!def.IsBuilding && !def.IsResource) throw new ArgumentException(kind + " is not a structure");
            var rect = new CellRect(origin.X, origin.Y, def.Size);
            if (!Map.IsRectInBounds(rect) || !Map.IsRectFree(rect)) return null;
            var e = NewEntity(kind, def, owner);
            e.Cell = origin;
            e.Position = rect.Centre;
            e.Amount = def.ResourceAmount;
            if (def.IsBuilding)
            {
                e.TrainQueue = new List<EntityKind>();
                e.UnderConstruction = underConstruction;
                if (underConstruction) e.Hp = 1;
            }
            Map.Occupy(rect, e.Id);
            return e;
        }

        private Entity NewEntity(EntityKind kind, EntityDef def, int owner)
        {
            var e = new Entity { Id = _nextId++, Kind = kind, Def = def, Owner = owner, Hp = def.MaxHp };
            _entities.Add(e);
            _byId.Add(e.Id, e);
            return e;
        }

        private void Kill(Entity e)
        {
            if (!e.Alive) return;
            e.Alive = false;
            if (e.IsBuilding || e.IsResource) Map.Clear(e.Footprint);
        }

        private void RemoveDead()
        {
            bool any = false;
            for (int i = 0; i < _entities.Count; i++) if (!_entities[i].Alive) { any = true; break; }
            if (!any) return;
            for (int i = 0; i < _entities.Count; i++) if (!_entities[i].Alive) _byId.Remove(_entities[i].Id);
            _entities.RemoveAll(e => !e.Alive);
        }

        private void UpdatePlayersAlive()
        {
            for (int p = 0; p < Players.Length; p++)
            {
                bool alive = false;
                for (int i = 0; i < _entities.Count && !alive; i++)
                {
                    Entity e = _entities[i];
                    if (e.Owner == p && (e.IsUnit || e.Kind == EntityKind.TownCenter)) alive = true;
                }
                Players[p].Alive = alive;
            }
        }

        // ------------------------------------------------------------------ queries

        public int CountUnits(int owner)
        {
            int n = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (e.Alive && e.Owner == owner && e.IsUnit) n++;
            }
            return n;
        }

        public int QueuedUnits(int owner)
        {
            int n = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (e.Alive && e.Owner == owner && e.IsBuilding && e.TrainQueue != null) n += e.TrainQueue.Count;
            }
            return n;
        }

        public bool IsEnemy(Entity a, Entity b) => a.Owner != b.Owner && a.Owner >= 0 && b.Owner >= 0;

        /// <summary>Nearest entity matching the predicate within radius tiles of a position. Ties go to the lower id.</summary>
        public Entity FindNearest(FPVector2 from, int radius, Func<Entity, bool> predicate)
        {
            Entity best = null;
            FP bestDist = FP.FromInt(radius * radius);
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive || !predicate(e)) continue;
                FP d = FPVector2.SqrDistance(from, NearestPointOf(e, from));
                if (d < bestDist || (best == null && d == bestDist))
                {
                    best = e;
                    bestDist = d;
                }
            }
            return best;
        }

        /// <summary>Closest point of an entity to a position: its centre for units, its rect boundary otherwise.</summary>
        public static FPVector2 NearestPointOf(Entity e, FPVector2 from)
        {
            return e.IsUnit ? e.Position : e.Footprint.NearestPoint(from);
        }

        /// <summary>A free cell near a footprint for a trained unit to appear on, searching outward rings.</summary>
        private bool FindSpawnCell(CellRect around, out Cell cell)
        {
            for (int ring = 1; ring <= 4; ring++)
            {
                int x0 = around.X - ring, y0 = around.Y - ring;
                int x1 = around.MaxX + ring, y1 = around.MaxY + ring;
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        bool onRing = x == x0 || x == x1 || y == y0 || y == y1;
                        if (!onRing) continue;
                        if (Map.IsFree(x, y)) { cell = new Cell(x, y); return true; }
                    }
                }
            }
            cell = default;
            return false;
        }

        private bool AnyUnitInside(CellRect rect)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (e.Alive && e.IsUnit && rect.Contains(e.Cell)) return true;
            }
            return false;
        }
    }
}
