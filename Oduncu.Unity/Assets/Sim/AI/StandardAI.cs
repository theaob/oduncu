using System.Collections.Generic;

namespace Oduncu.Sim.AI
{
    public enum Strategy : byte
    {
        Boom = 0,
        Rush = 1,
        Defend = 2,
        Siege = 3,
    }

    /// <summary>
    /// The Standard AI (design section 11, plan 3.6). Fair: it gets no extra resources and no
    /// map knowledge. It reads its own entities and only the enemy entities its player can see
    /// (<see cref="Simulation.CanSee"/>), and acts only by emitting <see cref="Command"/>s, the
    /// same ones touch input produces, so replays and determinism hold.
    ///
    /// Two layers. A scripted build order (houses, drop-off camps, farms, the buildings each
    /// age needs, production and research) runs every second. A utility layer re-chooses the
    /// strategy (Boom, Rush, Defend, Siege) every 30 seconds from what it has scouted; the
    /// strategy sets the economy preset, the army it builds and when it attacks.
    /// </summary>
    public sealed class StandardAI
    {
        public const int ThinkInterval = 10;
        public const int StrategyInterval = 300;
        /// <summary>From this tick (13 minutes) the AI throws everything at the enemy, so matches end.</summary>
        public const int AllInTick = 7800;

        private const int HomeRadius = 18;

        public readonly int Player;
        public Strategy Strategy { get; private set; }
        /// <summary>Never attack, only defend: for economy tests and a sandbox opponent.</summary>
        public bool Peaceful { get; set; }

        private readonly DeterministicRandom _rng;
        private readonly int[] _count = new int[GameData.EntityKindCount];
        private readonly int[] _building = new int[GameData.EntityKindCount];
        private readonly List<Entity> _villagers = new List<Entity>(80);
        private readonly List<Entity> _army = new List<Entity>(80);
        private readonly List<Entity> _producers = new List<Entity>(16);
        private readonly List<int> _ids = new List<int>(80);

        private Entity _tc;
        private Cell _home;
        private bool _started;
        private int _strategyPeriod;
        private bool _allIn;
        private int _lastTargets = -1;
        private int _underConstruction;
        private int _housesBuilding;
        private int _farmsBuilding;
        private int _scoutWaypoint;
        private int _lastCampTick = -1000;

        // What the AI has learned by looking.
        private int _enemyBuildingId;
        private Cell _enemyBaseGuess;
        private bool _knowsEnemyBase;
        private int _threatId;
        private int _seenCavalry, _seenArchers, _seenInfantry, _seenSpears, _seenSiege;
        private int _enemyArmySeen;
        private bool _attacking;
        private bool _lightThreat;
        /// <summary>What this think's commands already spend, so later ones are not rejected for want of it.</summary>
        private Cost _committed;
        /// <summary>The next age still lacks its buildings: wood is kept for them.</summary>
        private bool _needAgeBuildings;
        private const int AgeBuildingWood = 450;

        public StandardAI(int player, ulong seed)
        {
            Player = player;
            _rng = new DeterministicRandom(seed * 31UL + (ulong)player * 7919UL + 1UL);
            Strategy = (_rng.Next(2) == 0) ? Strategy.Boom : Strategy.Rush;
        }

        /// <summary>Called once per tick before the simulation steps; appends this tick's commands.</summary>
        public void Think(Simulation sim, List<Command> output)
        {
            int tick = sim.CurrentTick + 1;
            if ((tick + Player * 3) % ThinkInterval != 0) return;
            if (!sim.Players[Player].Alive || sim.MatchOver) return;

            Survey(sim);
            _committed = Cost.Zero;
            _allIn = tick >= AllInTick && !Peaceful;
            if (_tc == null && _villagers.Count == 0 && _army.Count == 0) return;
            if (!_started) Start(sim, output);
            if (tick / StrategyInterval != _strategyPeriod)
            {
                _strategyPeriod = tick / StrategyInterval;
                ChooseStrategy(sim, tick);
            }
            string ageWhy = sim.AgeUpRejection(Player);
            _needAgeBuildings = ageWhy != null && ageWhy.StartsWith("needs", System.StringComparison.Ordinal);

            Economy(sim, output);
            Farms(sim, output);
            Trade(sim, output);
            Buildings(sim, output, tick);
            AgeUp(sim, output, tick);
            Research(sim, output);
            Production(sim, output, tick);
            Army(sim, output, tick);
            Scout(sim, output);
        }

        // ------------------------------------------------------------------ looking

        private void Survey(Simulation sim)
        {
            for (int k = 0; k < _count.Length; k++) { _count[k] = 0; _building[k] = 0; }
            _villagers.Clear();
            _army.Clear();
            _producers.Clear();
            _tc = null;
            _underConstruction = 0;
            _housesBuilding = 0;
            _farmsBuilding = 0;
            _threatId = 0;
            FP threatDist = FP.Zero;
            int cav = 0, arch = 0, inf = 0, spear = 0, siege = 0, army = 0, threats = 0;
            bool enemyBuildingAlive = false;

            var entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive) continue;
                if (e.Owner == Player)
                {
                    _count[(int)e.Kind]++;
                    if (e.IsBuilding)
                    {
                        if (e.UnderConstruction)
                        {
                            if (e.Kind != EntityKind.Farm && e.Kind != EntityKind.House) _underConstruction++;
                            if (e.Kind == EntityKind.House) _housesBuilding++;
                            if (e.Kind == EntityKind.Farm) _farmsBuilding++;
                            continue;
                        }
                        if (_building[(int)e.Kind] == 0) _building[(int)e.Kind] = e.Id;
                        if (e.Kind == EntityKind.TownCenter && _tc == null) _tc = e;
                        if (e.Def.Trains.Length > 0 && e.Kind != EntityKind.TownCenter) _producers.Add(e);
                    }
                    else if (e.Kind == EntityKind.Villager) _villagers.Add(e);
                    else if (e.Def.IsMilitary && e.Kind != EntityKind.Scout) _army.Add(e);
                    continue;
                }
                if (e.Owner < 0 || !sim.CanSee(Player, e)) continue;

                // An enemy we can see.
                if (e.IsBuilding)
                {
                    if (e.Def.HasTag(EntityTag.Conquest) && (!_knowsEnemyBase || e.Id == _enemyBuildingId || _enemyBuildingId == 0))
                    {
                        _knowsEnemyBase = true;
                        _enemyBaseGuess = e.Cell;
                        _enemyBuildingId = e.Id;
                    }
                    if (e.Id == _enemyBuildingId) enemyBuildingAlive = true;
                    continue;
                }
                if (!e.Def.IsMilitary) continue;
                army++;
                bool scoutOnly = e.Kind == EntityKind.Scout;
                if (e.Def.HasTag(EntityTag.Cavalry)) cav++;
                else if (e.Def.HasTag(EntityTag.Siege)) siege++;
                else if (e.Def.HasTag(EntityTag.Spear)) spear++;
                else if (e.Def.HasTag(EntityTag.Infantry)) inf++;
                else if (e.Def.HasTag(EntityTag.Archer)) arch++;
                FP d = FPVector2.SqrDistance(e.Position, FPVector2.CellCentre(_home));
                if (_started && !scoutOnly && d <= FP.FromInt(HomeRadius * HomeRadius))
                {
                    threats++;
                    if (_threatId == 0 || d < threatDist)
                    {
                        _threatId = e.Id;
                        threatDist = d;
                    }
                }
            }
            // A lone raider is a nuisance, not an attack: the units at home deal with it without changing plans.
            _lightThreat = threats == 1;
            if (_knowsEnemyBase && !enemyBuildingAlive && _enemyBuildingId != 0)
            {
                // The building we were heading for is gone (or out of memory): look for another.
                _enemyBuildingId = 0;
                _knowsEnemyBase = FindKnownEnemyBuilding(sim);
            }
            // Remember the biggest enemy army of each kind seen at once.
            if (cav > _seenCavalry) _seenCavalry = cav;
            if (arch > _seenArchers) _seenArchers = arch;
            if (inf > _seenInfantry) _seenInfantry = inf;
            if (spear > _seenSpears) _seenSpears = spear;
            if (siege > _seenSiege) _seenSiege = siege;
            if (army > _enemyArmySeen) _enemyArmySeen = army;
        }

        private bool FindKnownEnemyBuilding(Simulation sim)
        {
            Entity best = null;
            FP bestDist = FP.Zero;
            var entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || !e.IsBuilding || e.Owner < 0 || e.Owner == Player || !sim.CanSee(Player, e)) continue;
                if (!e.Def.HasTag(EntityTag.Conquest)) continue;
                FP d = FPVector2.SqrDistance(e.Position, FPVector2.CellCentre(_home));
                if (best == null || d < bestDist) { best = e; bestDist = d; }
            }
            if (best == null) return false;
            _enemyBuildingId = best.Id;
            _enemyBaseGuess = best.Cell;
            return true;
        }

        private void Start(Simulation sim, List<Command> output)
        {
            _started = true;
            _home = _tc != null ? _tc.Cell : _villagers[0].Cell;
            // Without map knowledge the enemy could be anywhere; start the search across the map.
            _enemyBaseGuess = new Cell(sim.Map.Width - 1 - _home.X, sim.Map.Height - 1 - _home.Y);
            if (_tc != null) output.Add(Command.SetAutoQueue(Player, _tc.Id, true));
        }

        // ------------------------------------------------------------------ strategy

        private void ChooseStrategy(Simulation sim, int tick)
        {
            PlayerState me = sim.Players[Player];
            int myArmy = _army.Count;
            if (_threatId != 0 && !_lightThreat && myArmy < _enemyArmySeen) { Strategy = Strategy.Defend; return; }
            if (me.Age >= AgeId.Castle && (_count[(int)EntityKind.SiegeWorkshop] > 0 || tick >= AllInTick))
            {
                Strategy = Strategy.Siege;
                return;
            }
            if (me.Age == AgeId.Feudal && _enemyArmySeen <= 2 && Strategy != Strategy.Boom)
            {
                Strategy = Strategy.Rush;
                return;
            }
            if (Strategy == Strategy.Defend) Strategy = Strategy.Boom;
        }

        private int VillagerTarget()
        {
            // All in: population goes to the army.
            if (_allIn) return 30;
            switch (Strategy)
            {
                case Strategy.Rush: return 32;
                case Strategy.Defend: return 38;
                case Strategy.Siege: return 38;
                default: return 42;
            }
        }

        // ------------------------------------------------------------------ economy

        private void Economy(Simulation sim, List<Command> output)
        {
            PlayerState me = sim.Players[Player];
            EconomyTargets targets = DesiredTargets(sim);
            if (targets.Pack() != _lastTargets)
            {
                _lastTargets = targets.Pack();
                output.Add(Command.SetEconomyTargets(Player, targets));
            }
            Rebalance(sim, output, targets);

            if (_tc == null) return;
            // Once the age-up is the only thing missing, stop villagers at a sensible count and put the food into the age.
            int tick = sim.CurrentTick + 1;
            bool hold = Saving(sim, tick) && _villagers.Count >= ClickUpVillagers(me.Age);
            if (hold)
            {
                if (_tc.AutoQueue) output.Add(Command.SetAutoQueue(Player, _tc.Id, false));
                return;
            }
            // The auto-villager toggle stops at 40 population; past that, queue by hand up to the target.
            if (!_tc.AutoQueue && _villagers.Count < VillagerTarget() && _tc.TrainQueue.Count == 0
                && me.Population < me.PopulationCap && CanSpend(me, me.Stats.Of(EntityKind.Villager).Cost))
            {
                Commit(me.Stats.Of(EntityKind.Villager).Cost);
                output.Add(Command.Train(Player, _tc.Id, EntityKind.Villager));
            }
        }

        private static int ClickUpVillagers(AgeId age)
        {
            switch (age)
            {
                case AgeId.Dark: return 19;
                case AgeId.Feudal: return 27;
                default: return 34;
            }
        }

        private readonly int[] _share = new int[Cost.ResourceCount];
        private readonly int[] _workers = new int[Cost.ResourceCount];

        /// <summary>
        /// The strategy's preset, shifted toward what the next age still lacks and away from
        /// anything piling up unspent. Steps of 5 so small swings do not churn villagers.
        /// </summary>
        private EconomyTargets DesiredTargets(Simulation sim)
        {
            PlayerState me = sim.Players[Player];
            string key = Strategy == Strategy.Rush ? "Rush" : Strategy == Strategy.Siege ? "Siege" : "Boom";
            EconomyPreset preset = GameData.FindPreset(key, me.Age);
            EconomyTargets t = preset != null ? preset.Targets : new EconomyTargets(50, 30, 15, 5);
            for (int r = 0; r < Cost.ResourceCount; r++) _share[r] = t[(ResourceKind)r];

            Cost need = (int)me.Age + 1 < GameData.AgeCount ? GameData.Ages[(int)me.Age + 1].Cost : Cost.Zero;
            if (_needAgeBuildings) need = need + new Cost(0, AgeBuildingWood, 0, 0);
            if (_needAgeBuildings && me.Wood < AgeBuildingWood) Shift((int)ResourceKind.Food, (int)ResourceKind.Wood, 10);
            // The age's gold comes late in the preset; bring it forward once food is covered.
            int gold = (int)ResourceKind.Gold, food = (int)ResourceKind.Food;
            if (need.Gold > me.Gold && me.Food >= need.Food / 2) Shift(food, gold, me.Food >= need.Food ? 20 : 10);

            // A stockpile far beyond what is needed hands villagers to the scarcest resource.
            for (int r = 0; r < (int)ResourceKind.Stone; r++)
            {
                int stock = me.Get((ResourceKind)r);
                if (stock < need[(ResourceKind)r] + 400) continue;
                int to = Scarcest(me, need, r);
                Shift(r, to, stock >= need[(ResourceKind)r] + 1000 ? 20 : 10);
            }
            return new EconomyTargets(_share[0], _share[1], _share[2], _share[3]);
        }

        private bool CanSpend(PlayerState me, Cost cost) => me.CanAfford(cost + _committed);

        private void Commit(Cost cost) => _committed = _committed + cost;

        private void Shift(int from, int to, int amount)
        {
            if (from == to) return;
            // Keep a few villagers on everything that had a share.
            int floor = from == (int)ResourceKind.Stone ? 0 : 10;
            int moved = System.Math.Min(amount, System.Math.Max(0, _share[from] - floor));
            _share[from] -= moved;
            _share[to] += moved;
        }

        /// <summary>The food, wood or gold furthest below what is needed, other than one resource.</summary>
        private static int Scarcest(PlayerState me, Cost need, int except)
        {
            int best = (int)ResourceKind.Gold;
            int bestGap = int.MinValue;
            for (int r = 0; r < (int)ResourceKind.Stone; r++)
            {
                if (r == except) continue;
                int gap = need[(ResourceKind)r] + 300 - me.Get((ResourceKind)r);
                if (gap > bestGap) { bestGap = gap; best = r; }
            }
            return best;
        }

        /// <summary>
        /// The planner only places idle villagers, so when a resource is two or more workers over
        /// its share and another is two under, stop one worker on it; the planner moves them.
        /// </summary>
        private void Rebalance(Simulation sim, List<Command> output, EconomyTargets targets)
        {
            int total = _villagers.Count;
            if (total < 8) return;
            for (int r = 0; r < _workers.Length; r++) _workers[r] = 0;
            for (int i = 0; i < _villagers.Count; i++)
            {
                Entity v = _villagers[i];
                if (v.State != UnitState.Gathering && v.State != UnitState.Returning) continue;
                ResourceKind y = EntityDefs.Get(v.GatherKind).Yields;
                if (y != ResourceKind.None) _workers[(int)y]++;
            }
            int over = -1, under = -1, overBy = 1, underBy = 1;
            for (int r = 0; r < _workers.Length; r++)
            {
                int want = targets[(ResourceKind)r] * total / 100;
                if (_workers[r] - want > overBy) { overBy = _workers[r] - want; over = r; }
                if (want - _workers[r] > underBy) { underBy = want - _workers[r]; under = r; }
            }
            if (over < 0 || under < 0) return;
            for (int i = 0; i < _villagers.Count; i++)
            {
                Entity v = _villagers[i];
                if (v.State != UnitState.Gathering || v.Carry > 0) continue;
                if ((int)EntityDefs.Get(v.GatherKind).Yields != over) continue;
                output.Add(Command.Stop(Player, new[] { v.Id }));
                return;
            }
        }

        // ------------------------------------------------------------------ building

        private void Buildings(Simulation sim, List<Command> output, int tick)
        {
            PlayerState me = sim.Players[Player];
            if (_villagers.Count == 0) return;

            // Houses first: never get housed.
            int headroom = me.PopulationCap - me.Population;
            int wantHousesBuilding = me.Population >= 25 ? 2 : 1;
            if (me.PopulationCap < SimConstants.MaxPopulation && headroom <= 4 + _producers.Count && _housesBuilding < wantHousesBuilding)
            {
                if (TryBuild(sim, output, EntityKind.House, _home, 4, 12)) return;
            }

            if (_underConstruction > 2) return;
            AgeId age = me.Age;

            // Drop-off camps next to where villagers are working, at most one a minute.
            if (tick - _lastCampTick >= 600)
            {
                if (CampFor(sim, output, ResourceKind.Wood, EntityKind.LumberCamp) || CampFor(sim, output, ResourceKind.Food, EntityKind.Mill)
                    || CampFor(sim, output, ResourceKind.Gold, EntityKind.MiningCamp) || CampFor(sim, output, ResourceKind.Stone, EntityKind.MiningCamp))
                {
                    _lastCampTick = tick;
                    return;
                }
            }

            // The buildings each age needs, and the army buildings for the strategy.
            if (_count[(int)EntityKind.Barracks] == 0 && _villagers.Count >= 10 && TryBuild(sim, output, EntityKind.Barracks, _home, 5, 14)) return;
            if (age >= AgeId.Feudal)
            {
                if (_count[(int)EntityKind.ArcheryRange] == 0 && TryBuild(sim, output, EntityKind.ArcheryRange, _home, 5, 14)) return;
                if (_count[(int)EntityKind.Forge] == 0 && TryBuild(sim, output, EntityKind.Forge, _home, 5, 14)) return;
                // Wood goes to the two buildings the next age needs before anything else.
                if (age == AgeId.Feudal && _needAgeBuildings) return;
                if (_count[(int)EntityKind.Market] == 0 && me.Wood >= 250 && TryBuild(sim, output, EntityKind.Market, _home, 6, 16)) return;
                if (_count[(int)EntityKind.Stable] == 0 && (Strategy != Strategy.Defend || _seenSiege > 0) && TryBuild(sim, output, EntityKind.Stable, _home, 5, 14)) return;
                if (Strategy == Strategy.Defend && _count[(int)EntityKind.Tower] < 2 && TryBuild(sim, output, EntityKind.Tower, TowardEnemy(6), 0, 4)) return;
            }
            if (age >= AgeId.Castle)
            {
                if (_count[(int)EntityKind.SiegeWorkshop] == 0 && TryBuild(sim, output, EntityKind.SiegeWorkshop, _home, 5, 16)) return;
                if (_count[(int)EntityKind.Monastery] == 0 && TryBuild(sim, output, EntityKind.Monastery, _home, 6, 16)) return;
                if (_needAgeBuildings) return;
                if (_villagers.Count >= 30 && _count[(int)EntityKind.Barracks] < 2 && TryBuild(sim, output, EntityKind.Barracks, _home, 6, 16)) return;
                if (_villagers.Count >= 30 && _count[(int)EntityKind.ArcheryRange] < 2 && TryBuild(sim, output, EntityKind.ArcheryRange, _home, 6, 16)) return;
                if (_villagers.Count >= 35 && _count[(int)EntityKind.Stable] < 2 && TryBuild(sim, output, EntityKind.Stable, _home, 6, 16)) return;
            }

        }

        /// <summary>
        /// Keep enough farms for the food share of the villagers, minus those still on berries,
        /// sheep and hunt. Farms go up two at a time around the mill (or Town Center).
        /// </summary>
        private void Farms(Simulation sim, List<Command> output)
        {
            int foodWanted = _villagers.Count * sim.Players[Player].EconomyTargets.Food / 100;
            // Sheep, berries and deer near home feed a villager per 150 food left before farms are worth their wood.
            int natural = System.Math.Min(foodWanted, NaturalFoodNearHome(sim) / 150);
            int want = foodWanted - natural;
            if (_count[(int)EntityKind.Farm] >= want || _farmsBuilding >= 2) return;
            Entity mill = sim.Find(_building[(int)EntityKind.Mill]);
            // While the next age still lacks its buildings, those come before new farms.
            TryBuild(sim, output, EntityKind.Farm, mill != null ? mill.Cell : _home, 2, 10, _needAgeBuildings ? 200 : 0);
        }

        /// <summary>Trade surpluses at the market for what the next age (or the army) is short of.</summary>
        private void Trade(Simulation sim, List<Command> output)
        {
            Entity market = sim.Find(_building[(int)EntityKind.Market]);
            if (market == null) return;
            PlayerState me = sim.Players[Player];
            Cost need = (int)me.Age + 1 < GameData.AgeCount ? GameData.Ages[(int)me.Age + 1].Cost : new Cost(400, 0, 400, 0);
            int keep = Saving(sim, sim.CurrentTick + 1) ? 300 : 700;
            if (me.Gold < need.Gold)
            {
                // Selling gives gold: sell whatever is furthest past what we need.
                ResourceKind sell = ResourceKind.None;
                int bestSurplus = keep;
                for (int r = 0; r < Cost.ResourceCount; r++)
                {
                    if (r == (int)ResourceKind.Gold) continue;
                    int surplus = me.Get((ResourceKind)r) - need[(ResourceKind)r] - 100;
                    if (surplus > bestSurplus && sim.SellPrice((ResourceKind)r) >= 25) { bestSurplus = surplus; sell = (ResourceKind)r; }
                }
                if (sell != ResourceKind.None) output.Add(Command.MarketSell(Player, market.Id, sell));
                return;
            }
            if (me.Food < need.Food)
            {
                if (me.Wood > keep + need.Wood && sim.SellPrice(ResourceKind.Wood) >= 30) output.Add(Command.MarketSell(Player, market.Id, ResourceKind.Wood));
                else if (me.Gold > need.Gold + sim.BuyPrice(ResourceKind.Food) + keep - 250) output.Add(Command.MarketBuy(Player, market.Id, ResourceKind.Food));
            }
        }

        private int NaturalFoodNearHome(Simulation sim)
        {
            int total = 0;
            var entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Def.Yields != ResourceKind.Food || e.IsBuilding) continue;
                if (Cell.Chebyshev(e.Cell, _home) > HomeRadius || !sim.CanGather(e, Player, forPlanner: true)) continue;
                total += e.Amount;
            }
            return total;
        }

        /// <summary>Build a camp beside the first resource being worked (in villager id order) with no drop-off near it.</summary>
        private bool CampFor(Simulation sim, List<Command> output, ResourceKind kind, EntityKind camp)
        {
            for (int i = 0; i < _villagers.Count; i++)
            {
                Entity v = _villagers[i];
                if (v.State != UnitState.Gathering && v.State != UnitState.Returning) continue;
                Entity source = sim.Find(v.GatherSourceId);
                if (source == null || source.Def.Yields != kind || source.IsBuilding) continue;
                if (source.IsUnit && kind == ResourceKind.Food) continue; // hunting: carcasses move off quickly
                var filter = new EntityFilter { Owner = OwnerMatch.Owned, Player = Player, Category = EntityCategory.Building, DropOff = true, DropOffKind = kind, Complete = false };
                Entity drop = sim.FindNearest(source.Position, 6, filter);
                if (drop != null) continue;
                if (TryBuild(sim, output, camp, source.Cell, 1, 4)) return true;
            }
            return false;
        }

        private Cell TowardEnemy(int tiles)
        {
            int dx = _enemyBaseGuess.X - _home.X, dy = _enemyBaseGuess.Y - _home.Y;
            int len = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
            if (len == 0) return _home;
            return new Cell(_home.X + dx * tiles / len, _home.Y + dy * tiles / len);
        }

        /// <summary>Find a spot ring by ring around an anchor (one free cell kept around it) and send the nearest villager.</summary>
        private bool TryBuild(Simulation sim, List<Command> output, EntityKind kind, Cell anchor, int minRing, int maxRing, int reserveWood = 0)
        {
            PlayerState me = sim.Players[Player];
            Cost cost = me.Stats.Of(kind).Cost;
            if (!CanSpend(me, cost + new Cost(0, reserveWood, 0, 0))) return false;
            int size = EntityDefs.Get(kind).Size;
            // Scan each ring starting on the side away from the enemy, mirrored per player, so both
            // sides of the mirrored map build alike.
            int sx = _enemyBaseGuess.X >= _home.X ? 1 : -1, sy = _enemyBaseGuess.Y >= _home.Y ? 1 : -1;
            for (int ring = minRing; ring <= maxRing; ring++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != ring) continue;
                        // Origins are top-left corners; mirroring a footprint shifts it by its size.
                        int x = sx > 0 ? anchor.X + dx : anchor.X - dx - (size - 1);
                        int y = sy > 0 ? anchor.Y + dy : anchor.Y - dy - (size - 1);
                        var origin = new Cell(x, y);
                        if (!sim.CanPlace(Player, kind, origin) || !MarginClear(sim, origin, size, kind) || !KeepsOpen(sim, origin, size)) continue;
                        Entity builder = NearestBuilder(origin);
                        if (builder == null) return false;
                        output.Add(Command.Build(Player, new[] { builder.Id }, kind, origin));
                        Commit(cost);
                        _underConstruction++;
                        if (kind == EntityKind.House) _housesBuilding++;
                        if (kind == EntityKind.Farm) _farmsBuilding++;
                        _count[(int)kind]++;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Keep a walkable cell around buildings so the base never walls itself in (farms may touch).</summary>
        private static bool MarginClear(Simulation sim, Cell origin, int size, EntityKind kind)
        {
            if (kind == EntityKind.Farm) return true;
            for (int y = origin.Y - 1; y <= origin.Y + size; y++)
                for (int x = origin.X - 1; x <= origin.X + size; x++)
                {
                    if (x >= origin.X && x < origin.X + size && y >= origin.Y && y < origin.Y + size) continue;
                    if (!sim.Map.InBounds(x, y)) return false;
                    int occupant = sim.Map.OccupantAt(x, y);
                    if (occupant == 0) continue;
                    Entity e = sim.Find(occupant);
                    if (e != null && e.IsBuilding) return false;
                }
            return true;
        }

        private const int OpenWindow = 10;
        private int[] _visit = new int[0];
        private int[] _queue = new int[0];
        private int _visitStamp;

        /// <summary>
        /// Whether placing a building here keeps the free cells around it connected to each
        /// other within a window around the site, so farms and houses never close a pocket
        /// (villagers spawned into one, or a drop-off nobody can reach).
        /// </summary>
        private bool KeepsOpen(Simulation sim, Cell origin, int size)
        {
            MapGrid map = sim.Map;
            int n = map.Width * map.Height;
            if (_visit.Length != n) { _visit = new int[n]; _queue = new int[n]; _visitStamp = 0; }
            _visitStamp++;
            var rect = new CellRect(origin.X, origin.Y, size);
            int x0 = System.Math.Max(0, origin.X - OpenWindow), y0 = System.Math.Max(0, origin.Y - OpenWindow);
            int x1 = System.Math.Min(map.Width - 1, rect.MaxX + OpenWindow), y1 = System.Math.Min(map.Height - 1, rect.MaxY + OpenWindow);

            // Start from the first free cell on the ring around the site.
            int start = -1, ringFree = 0;
            for (int y = rect.Y - 1; y <= rect.MaxY + 1; y++)
                for (int x = rect.X - 1; x <= rect.MaxX + 1; x++)
                {
                    if (rect.Contains(new Cell(x, y)) || !map.IsPassable(x, y, Player)) continue;
                    ringFree++;
                    if (start < 0) start = map.Index(x, y);
                }
            if (start < 0) return true;

            int head = 0, tail = 0, reached = 0;
            _queue[tail++] = start;
            _visit[start] = _visitStamp;
            while (head < tail)
            {
                int i = _queue[head++];
                int cx = i % map.Width, cy = i / map.Width;
                if (cx >= rect.X - 1 && cx <= rect.MaxX + 1 && cy >= rect.Y - 1 && cy <= rect.MaxY + 1) reached++;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = cy + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < x0 || ny < y0 || nx > x1 || ny > y1) continue;
                    if (rect.Contains(new Cell(nx, ny)) || !map.IsPassable(nx, ny, Player)) continue;
                    int j = map.Index(nx, ny);
                    if (_visit[j] == _visitStamp) continue;
                    _visit[j] = _visitStamp;
                    _queue[tail++] = j;
                }
            }
            return reached == ringFree;
        }

        private Entity NearestBuilder(Cell site)
        {
            Entity best = null;
            int bestDist = int.MaxValue;
            for (int i = 0; i < _villagers.Count; i++)
            {
                Entity v = _villagers[i];
                if (v.State == UnitState.Building || v.State == UnitState.Garrisoned || v.State == UnitState.Garrisoning) continue;
                int d = Cell.Chebyshev(v.Cell, site);
                // Prefer villagers not carrying food from far away; ties to the lower id.
                if (d < bestDist) { best = v; bestDist = d; }
            }
            return best;
        }

        // ------------------------------------------------------------------ ages and research

        private bool WantsToAge(Simulation sim, int tick)
        {
            PlayerState me = sim.Players[Player];
            if ((int)me.Age + 1 >= GameData.AgeCount) return false;
            if (_threatId != 0 && !_lightThreat && _army.Count < 3) return false;
            if (Strategy == Strategy.Rush && me.Age == AgeId.Feudal) return _army.Count >= 8 || tick >= 4800;
            return true;
        }

        private void AgeUp(Simulation sim, List<Command> output, int tick)
        {
            if (_tc == null || !WantsToAge(sim, tick)) return;
            PlayerState me = sim.Players[Player];
            if (sim.AgeUpRejection(Player) != null) return;
            for (int i = 0; i < _tc.TrainQueue.Count; i++) if (_tc.TrainQueue[i].IsResearch) return;
            Cost cost = GameData.Ages[(int)me.Age + 1].Cost;
            if (!CanSpend(me, cost)) return;
            Commit(cost);
            // The Town Center can only research with an empty queue slot ahead; clear a waiting villager.
            if (_tc.TrainQueue.Count > 0 && _tc.TrainProgress == 0) output.Add(Command.CancelTrain(Player, _tc.Id, 0));
            output.Add(Command.SetAutoQueue(Player, _tc.Id, false));
            output.Add(Command.AgeUp(Player, _tc.Id));
        }

        /// <summary>Whether to keep resources for the next age rather than spend them.</summary>
        private bool Saving(Simulation sim, int tick)
        {
            PlayerState me = sim.Players[Player];
            if ((_threatId != 0 && !_lightThreat) || tick >= AllInTick || !WantsToAge(sim, tick)) return false;
            if (IsAging()) return false;
            return sim.AgeUpRejection(Player) == null;
        }

        private bool IsAging()
        {
            if (_tc == null) return false;
            for (int i = 0; i < _tc.TrainQueue.Count; i++) if (_tc.TrainQueue[i].IsAgeUp) return true;
            return false;
        }

        private static readonly TechId[] ResearchOrder =
        {
            TechId.Loom, TechId.Woodcutting1, TechId.Farming1, TechId.Wheelbarrow, TechId.GoldMining1,
            TechId.MeleeAttack1, TechId.ArcherAttack1, TechId.Woodcutting2, TechId.Farming2,
            TechId.InfantryArmor1, TechId.CavalryArmor1, TechId.ArcherArmor1, TechId.HandCart,
            TechId.MeleeAttack2, TechId.ArcherAttack2, TechId.GoldMining2, TechId.CavalryArmor2,
            TechId.ArcherArmor2, TechId.InfantryArmor2, TechId.MeleeAttack3, TechId.ArcherAttack3,
            TechId.CavalryArmor3, TechId.ArcherArmor3, TechId.InfantryArmor3,
        };

        private void Research(Simulation sim, List<Command> output)
        {
            PlayerState me = sim.Players[Player];
            int tick = sim.CurrentTick + 1;
            if (Saving(sim, tick)) return;
            for (int i = 0; i < ResearchOrder.Length; i++)
            {
                TechId tech = ResearchOrder[i];
                if (sim.ResearchRejection(Player, tech) != null) continue;
                TechDef def = GameData.Techs[(int)tech];
                Entity at = sim.Find(_building[(int)def.ResearchedAt]);
                if (at == null || at.TrainQueue.Count > 0) continue;
                // Research only with a comfortable surplus, so the army and villagers come first.
                if (!CanSpend(me, def.Cost + new Cost(100, _needAgeBuildings ? AgeBuildingWood : 100, 50, 0))) continue;
                if (at.Kind == EntityKind.TownCenter && _villagers.Count < VillagerTarget() / 2) continue;
                Commit(def.Cost);
                output.Add(Command.Research(Player, at.Id, tech));
                return;
            }
        }

        // ------------------------------------------------------------------ army

        private void Production(Simulation sim, List<Command> output, int tick)
        {
            PlayerState me = sim.Players[Player];
            bool boomQuiet = Strategy == Strategy.Boom && me.Age < AgeId.Castle && _threatId == 0 && tick < AllInTick;
            if (boomQuiet && _army.Count >= 4) return;
            if (Saving(sim, tick) && _threatId == 0) return;
            for (int i = 0; i < _producers.Count; i++)
            {
                Entity b = _producers[i];
                if (b.TrainQueue.Count >= 2) continue;
                EntityKind unit = ChooseUnit(sim, b.Kind, me.Age);
                if (unit == EntityKind.None) continue;
                EntityKind actual = Simulation.LineMemberFor(unit, me.Age);
                if (EntityDefs.Get(actual).MinAge > me.Age) continue;
                if (me.Population >= me.PopulationCap) return;
                Cost unitCost = me.Stats.Of(actual).Cost;
                if (!CanSpend(me, unitCost)) continue;
                if (_needAgeBuildings && unitCost.Wood > 0 && me.Wood < AgeBuildingWood + unitCost.Wood && _threatId == 0) continue;
                Commit(unitCost);
                output.Add(Command.Train(Player, b.Id, unit));
            }
        }

        private EntityKind ChooseUnit(Simulation sim, EntityKind building, AgeId age)
        {
            // Out of gold (mines run dry late on): the units that cost none.
            bool poor = sim.Players[Player].Gold < 150;
            switch (building)
            {
                case EntityKind.Barracks:
                    if (poor || (age >= AgeId.Feudal && _seenCavalry > _seenInfantry + _seenArchers)) return EntityKind.Spearman;
                    return EntityKind.Militia;
                case EntityKind.ArcheryRange:
                    return poor || _seenArchers > _seenInfantry + _seenCavalry ? EntityKind.Skirmisher : EntityKind.Archer;
                case EntityKind.Stable:
                    if (!poor && age >= AgeId.Castle && _seenSpears < 6) return EntityKind.Knight;
                    return EntityKind.Scout;
                case EntityKind.SiegeWorkshop:
                    if (_count[(int)EntityKind.BatteringRam] + _count[(int)EntityKind.CappedRam] < 4) return EntityKind.BatteringRam;
                    if (_count[(int)EntityKind.Mangonel] + _count[(int)EntityKind.Onager] < 2) return EntityKind.Mangonel;
                    return EntityKind.BatteringRam;
                default:
                    return EntityKind.None;
            }
        }

        private int AttackThreshold(Simulation sim, int tick)
        {
            if (Peaceful) return int.MaxValue;
            if (tick >= AllInTick) return 1;
            switch (Strategy)
            {
                case Strategy.Rush: return 7;
                case Strategy.Siege: return 10;
                case Strategy.Defend: return int.MaxValue;
                default: return sim.Players[Player].Age >= AgeId.Castle ? 12 : int.MaxValue;
            }
        }

        private void Army(Simulation sim, List<Command> output, int tick)
        {
            if (_army.Count == 0) return;

            // Defend: everyone at home meets the threat.
            if (_threatId != 0)
            {
                Entity threat = sim.Find(_threatId);
                if (threat != null)
                {
                    CollectIds(onlyIdleOrHome: true);
                    if (_ids.Count > 0) output.Add(Command.AttackMove(Player, _ids, threat.Cell));
                    return;
                }
            }

            int threshold = AttackThreshold(sim, tick);
            if (!_attacking && _army.Count >= threshold) _attacking = true;
            if (_attacking && _army.Count < System.Math.Max(1, threshold / 3) && tick < AllInTick) _attacking = false;

            if (!_attacking)
            {
                // Gather idle units at a muster point between home and the enemy.
                Cell muster = TowardEnemy(7);
                _ids.Clear();
                for (int i = 0; i < _army.Count; i++)
                    if (_army[i].State == UnitState.Idle && Cell.Chebyshev(_army[i].Cell, muster) > 4) _ids.Add(_army[i].Id);
                if (_ids.Count > 0) output.Add(Command.Move(Player, _ids, muster));
                return;
            }

            // Attack: idle soldiers attack-move on the enemy building to take down next, or on the best guess of where the base is.
            Entity goal = ChooseGoal(sim, raid: true);
            Cell target = goal != null ? goal.Cell : _enemyBaseGuess;
            if (goal == null && ArmyNear(target, 4))
            {
                // Nothing here: try the next unexplored part of the map.
                _enemyBaseGuess = NextSearchPoint(sim);
                target = _enemyBaseGuess;
            }
            _ids.Clear();
            for (int i = 0; i < _army.Count; i++)
                if (_army[i].State == UnitState.Idle) _ids.Add(_army[i].Id);
            if (_ids.Count > 0) output.Add(Command.AttackMove(Player, _ids, target));
            if (goal != null && goal.Def.HasTag(EntityTag.Conquest)) FocusBuilding(sim, output, goal);
            SendRams(sim, output);
        }

        /// <summary>Rams only hurt buildings: each goes straight for the nearest building that decides the match.</summary>
        private void SendRams(Simulation sim, List<Command> output)
        {
            for (int i = 0; i < _army.Count; i++)
            {
                Entity ram = _army[i];
                if (!ram.Def.HasTag(EntityTag.TargetsBuildings)) continue;
                if (ram.State == UnitState.Attacking)
                {
                    Entity t = sim.Find(ram.TargetId);
                    if (t != null && t.Def.HasTag(EntityTag.Conquest)) continue;
                }
                Entity best = null;
                int bestDist = int.MaxValue;
                var entities = sim.Entities;
                for (int k = 0; k < entities.Count; k++)
                {
                    Entity e = entities[k];
                    if (!e.Alive || !e.IsBuilding || e.Owner < 0 || e.Owner == Player || !e.Def.HasTag(EntityTag.Conquest) || !sim.CanSee(Player, e)) continue;
                    int d = e.Footprint.DistanceTo(ram.Cell);
                    if (d < bestDist) { best = e; bestDist = d; }
                }
                if (best != null) output.Add(Command.Attack(Player, new[] { ram.Id }, best.Id));
            }
        }

        /// <summary>
        /// The known enemy building that decides the match nearest the army, or with raid set, a
        /// drop-off camp where villagers work, preferred. Town Centers and Castles shoot back, so
        /// they come last unless rams are along.
        /// </summary>
        private Entity ChooseGoal(Simulation sim, bool raid)
        {
            int sx = 0, sy = 0, rams = 0;
            for (int i = 0; i < _army.Count; i++)
            {
                sx += _army[i].Cell.X;
                sy += _army[i].Cell.Y;
                if (_army[i].Def.HasTag(EntityTag.TargetsBuildings)) rams++;
            }
            var centre = new Cell(sx / _army.Count, sy / _army.Count);
            Entity best = null;
            int bestScore = int.MaxValue;
            var entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || !e.IsBuilding || e.Owner < 0 || e.Owner == Player) continue;
                bool camp = raid && (e.Kind == EntityKind.LumberCamp || e.Kind == EntityKind.MiningCamp || e.Kind == EntityKind.Mill);
                if (!camp && !e.Def.HasTag(EntityTag.Conquest)) continue;
                if (!sim.CanSee(Player, e)) continue;
                int score = e.Footprint.DistanceTo(centre);
                if (e.Def.HasTag(EntityTag.Shoots) && rams == 0) score += 24;
                // The villagers working at a camp are worth more than any building.
                if (camp) score -= 14;
                if (score < bestScore || (score == bestScore && e.Id < best.Id)) { best = e; bestScore = score; }
            }
            return best;
        }

        /// <summary>
        /// Soldiers hitting houses, farms and walls, or idle near the goal, go for the building
        /// that decides the match; those fighting units keep fighting.
        /// </summary>
        private void FocusBuilding(Simulation sim, List<Command> output, Entity goal)
        {
            _ids.Clear();
            for (int i = 0; i < _army.Count; i++)
            {
                Entity u = _army[i];
                if (u.State == UnitState.Attacking)
                {
                    if (u.TargetId == goal.Id) continue;
                    Entity t = sim.Find(u.TargetId);
                    if (t == null || t.IsUnit || t.Def.HasTag(EntityTag.Conquest)) continue;
                }
                else if (u.State != UnitState.Idle || goal.Footprint.DistanceTo(u.Cell) > 10) continue;
                _ids.Add(u.Id);
            }
            if (_ids.Count > 0) output.Add(Command.Attack(Player, _ids, goal.Id));
        }

        private bool ArmyNear(Cell c, int within)
        {
            for (int i = 0; i < _army.Count; i++) if (Cell.Chebyshev(_army[i].Cell, c) <= within) return true;
            return false;
        }

        private void CollectIds(bool onlyIdleOrHome)
        {
            _ids.Clear();
            for (int i = 0; i < _army.Count; i++)
            {
                Entity u = _army[i];
                if (u.State == UnitState.Attacking) continue;
                // Soldiers out on the attack keep attacking; those at home (or idle at the muster point) defend.
                if (onlyIdleOrHome && Cell.Chebyshev(u.Cell, _home) > HomeRadius && !(u.State == UnitState.Idle && !_attacking)) continue;
                _ids.Add(u.Id);
            }
        }

        // ------------------------------------------------------------------ scouting

        private Cell NextSearchPoint(Simulation sim)
        {
            int w = sim.Map.Width, h = sim.Map.Height;
            for (int tries = 0; tries < 9; tries++)
            {
                _scoutWaypoint = (_scoutWaypoint + 1) % 9;
                var c = new Cell(w / 6 + (_scoutWaypoint % 3) * w / 3, h / 6 + (_scoutWaypoint / 3) * h / 3);
                // Mirrored for a start in the far half, so both players search alike.
                if (_home.X + _home.Y >= w - 1) c = new Cell(w - 1 - c.X, h - 1 - c.Y);
                if (!sim.Fog.IsExplored(Player, c)) return c;
            }
            return new Cell(_rng.Next(w), _rng.Next(h));
        }

        private void Scout(Simulation sim, List<Command> output)
        {
            var entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Owner != Player || e.Kind != EntityKind.Scout) continue;
                if (e.State != UnitState.Idle) continue;
                Cell next = _knowsEnemyBase ? NextSearchPoint(sim) : _enemyBaseGuess;
                if (!_knowsEnemyBase && Cell.Chebyshev(e.Cell, _enemyBaseGuess) <= 4) next = NextSearchPoint(sim);
                output.Add(Command.Move(Player, new[] { e.Id }, next));
            }
        }
    }
}
