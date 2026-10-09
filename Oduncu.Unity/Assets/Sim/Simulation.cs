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
        /// <summary>Free entity objects kept ready so spawning on the tick path does not allocate.</summary>
        public const int EntityPoolReserve = 64;
        private const int EntityPoolLowWater = 16;

        public readonly MapGrid Map;
        public readonly PlayerState[] Players;
        public readonly DeterministicRandom Rng;
        public readonly SpatialIndex Index;
        public int CurrentTick { get; private set; }

        private readonly List<Entity> _entities = new List<Entity>(1024);
        private readonly Dictionary<int, Entity> _byId = new Dictionary<int, Entity>(1024);
        private readonly Stack<Entity> _pool = new Stack<Entity>(EntityPoolReserve * 2);
        private readonly Pathfinder _pathfinder;
        private readonly StateHasher _hasher = new StateHasher();
        private readonly PlayerStats _neutralStats = new PlayerStats();
        private readonly List<Entity> _scratchUnits = new List<Entity>(64);
        private readonly int[] _scratchMaxHp = new int[GameData.EntityKindCount];
        private int _nextId = 1;

        /// <summary>Alive entities in ascending id order. Do not mutate.</summary>
        public IReadOnlyList<Entity> Entities => _entities;

        public Simulation(int width, int height, int playerCount, ulong seed)
        {
            if (playerCount < 1 || playerCount > SimConstants.MaxPlayers) throw new ArgumentOutOfRangeException(nameof(playerCount));
            Map = new MapGrid(width, height);
            Index = new SpatialIndex(width, height);
            Players = new PlayerState[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                Players[i] = new PlayerState { Index = i };
                Players[i].Set(SimConstants.StartingResources);
            }
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
            TopUpPool();
            if (commands != null)
            {
                for (int i = 0; i < commands.Count; i++) ApplyCommand(commands[i]);
            }

            RecountPopulation();
            for (int p = 0; p < Players.Length; p++) RunEconomyPlanner(p);

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

        /// <summary>Stats for an entity kind as its owner currently has them. Neutral entities use base stats.</summary>
        public UnitStats StatsFor(int owner, EntityKind kind)
        {
            return owner >= 0 && owner < Players.Length ? Players[owner].Stats.Of(kind) : _neutralStats.Of(kind);
        }

        // ------------------------------------------------------------------ spawning

        public Entity SpawnUnit(EntityKind kind, int owner, Cell at)
        {
            var def = EntityDefs.Get(kind);
            if (!def.IsUnit) throw new ArgumentException("not a unit: " + kind);
            var e = NewEntity(kind, def, owner);
            e.Cell = Map.Clamp(at);
            e.Position = FPVector2.CellCentre(e.Cell);
            e.State = UnitState.Idle;
            e.Amount = def.ResourceAmount;
            Index.Add(e);
            return e;
        }

        /// <summary>Place a building or resource. Returns null if the footprint is out of bounds or blocked.</summary>
        public Entity SpawnStructure(EntityKind kind, int owner, Cell origin, bool underConstruction)
        {
            var def = EntityDefs.Get(kind);
            if (!def.IsBuilding && !def.IsResource) throw new ArgumentException("not a structure: " + kind);
            var rect = new CellRect(origin.X, origin.Y, def.Size);
            if (!Map.IsRectInBounds(rect) || !Map.IsRectFree(rect)) return null;
            var e = NewEntity(kind, def, owner);
            e.Cell = origin;
            e.Position = rect.Centre;
            e.Amount = def.ResourceAmount;
            if (def.IsBuilding)
            {
                e.UnderConstruction = underConstruction;
                if (underConstruction) e.Hp = 1;
            }
            Map.Occupy(rect, e.Id);
            Index.Add(e);
            return e;
        }

        private Entity NewEntity(EntityKind kind, EntityDef def, int owner)
        {
            Entity e = _pool.Count > 0 ? _pool.Pop() : new Entity();
            e.Reset(_nextId++, kind, def, owner, StatsFor(owner, kind));
            _entities.Add(e);
            _byId.Add(e.Id, e);
            return e;
        }

        /// <summary>Allocate pooled entities in one batch when the reserve runs low, never one per spawn.</summary>
        private void TopUpPool()
        {
            if (_pool.Count >= EntityPoolLowWater) return;
            while (_pool.Count < EntityPoolReserve) _pool.Push(new Entity());
        }

        private void Kill(Entity e)
        {
            if (!e.Alive) return;
            e.Alive = false;
            if (e.IsBuilding || e.IsResource) Map.Clear(e.Footprint);
            Index.Remove(e);
        }

        /// <summary>Compact the entity list in place and return dead entities to the pool.</summary>
        private void RemoveDead()
        {
            int write = 0;
            for (int read = 0; read < _entities.Count; read++)
            {
                Entity e = _entities[read];
                if (e.Alive)
                {
                    _entities[write++] = e;
                    continue;
                }
                _byId.Remove(e.Id);
                _pool.Push(e);
            }
            if (write < _entities.Count) _entities.RemoveRange(write, _entities.Count - write);
        }

        private void UpdatePlayersAlive()
        {
            for (int p = 0; p < Players.Length; p++)
            {
                bool alive = false;
                for (int i = 0; i < _entities.Count && !alive; i++)
                {
                    Entity e = _entities[i];
                    if (e.Owner == p && ((e.IsUnit && !e.Def.IsAnimal) || e.Kind == EntityKind.TownCenter)) alive = true;
                }
                Players[p].Alive = alive;
            }
        }

        // ------------------------------------------------------------------ research

        /// <summary>
        /// Apply a finished tech to a player: rebuild their stat table and raise the hit points
        /// of existing entities whose maximum changed. Production queues call this when research
        /// completes; tests and scenario setup may call it directly. Returns false if the tech
        /// was already researched.
        /// </summary>
        public bool CompleteResearch(int player, TechId tech)
        {
            PlayerStats stats = Players[player].Stats;
            if (tech == TechId.None || stats.HasResearched(tech)) return false;
            for (int k = 0; k < _scratchMaxHp.Length; k++) _scratchMaxHp[k] = stats.Of((EntityKind)k).MaxHp;
            stats.Research(tech);
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive || e.Owner != player || e.UnderConstruction) continue;
                int delta = e.Stats.MaxHp - _scratchMaxHp[(int)e.Kind];
                if (delta != 0) e.Hp = Math.Max(1, e.Hp + delta);
            }
            return true;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Living units of a player, not counting animals they herd.</summary>
        public int CountUnits(int owner)
        {
            int n = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (e.Alive && e.Owner == owner && e.IsUnit && !e.Def.IsAnimal) n++;
            }
            return n;
        }

        public int QueuedUnits(int owner)
        {
            int n = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (e.Alive && e.Owner == owner && e.IsBuilding) n += e.TrainQueue.Count;
            }
            return n;
        }

        public bool IsEnemy(Entity a, Entity b) => a.Owner != b.Owner && a.Owner >= 0 && b.Owner >= 0;

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
    }
}
