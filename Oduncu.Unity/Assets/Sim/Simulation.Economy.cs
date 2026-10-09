namespace Oduncu.Sim
{
    /// <summary>Gathering, drop-off, construction, repair, herding, population and the economy planner.</summary>
    public sealed partial class Simulation
    {
        /// <summary>Gatherable entities of one kind or one resource, as a specific player sees them.</summary>
        private struct GatherableFilter : IEntityFilter
        {
            public Simulation Sim;
            public int Player;
            public EntityKind Kind;
            public ResourceKind Yield;
            /// <summary>Planner rules: no boar, no farm someone else works, no farm that cannot be reseeded.</summary>
            public bool ForPlanner;
            public Entity Asker;

            public bool Matches(Entity e)
            {
                if (Kind != EntityKind.None && e.Kind != Kind) return false;
                if (Yield != ResourceKind.None && e.Def.Yields != Yield) return false;
                return Sim.CanGather(e, Player, ForPlanner, Asker);
            }
        }

        /// <summary>
        /// Whether a player's villagers may gather an entity: resources with something left, their own
        /// finished farms, carcasses, wild animals (hunting) and their own sheep.
        /// </summary>
        public bool CanGather(Entity e, int player, bool forPlanner = false, Entity asker = null)
        {
            if (e == null || !e.Alive || !e.Def.IsGatherable) return false;
            if (e.IsResource) return e.Amount > 0;
            if (e.IsBuilding)
            {
                if (e.Owner != player || e.UnderConstruction) return false;
                if (!forPlanner) return true;
                if (!IsFarmFree(e, asker)) return false;
                return e.Amount > 0 || Players[player].CanAfford(e.Stats.Cost);
            }
            // Animals
            if (e.State == UnitState.Carcass) return e.Amount > 0;
            if (e.Def.HasTag(EntityTag.Herdable)) return e.Owner == player;
            if (forPlanner && e.Def.HasTag(EntityTag.Retaliates)) return false;
            return e.Owner < 0;
        }

        private bool IsFarmFree(Entity farm, Entity asker)
        {
            Entity farmer = Find(farm.FarmerId);
            if (farmer == null || farmer == asker || farmer.GatherSourceId != farm.Id) return true;
            return farmer.State != UnitState.Gathering && farmer.State != UnitState.Returning;
        }

        private void StartGathering(Entity u, Entity source)
        {
            u.State = UnitState.Gathering;
            u.GatherSourceId = source.Id;
            u.GatherKind = source.Kind;
            u.PreviousGatherSourceId = 0;
            u.BuildSiteId = 0;
            u.TargetId = 0;
            u.HasPath = false;
            u.GatherProgress = FP.Zero;
            if (source.IsBuilding) source.FarmerId = u.Id;
        }

        // ------------------------------------------------------------------ gathering

        /// <summary>Another source of the kind the villager was gathering, when its source runs out.</summary>
        private Entity FindNextResource(Entity u)
        {
            if (u.GatherKind == EntityKind.None) return null;
            var filter = new GatherableFilter { Sim = this, Player = u.Owner, Kind = u.GatherKind, Yield = ResourceKind.None, ForPlanner = true, Asker = u };
            return FindNearest(u.Position, SimConstants.ResourceSearchRadius, ref filter);
        }

        private Entity FindNearestDropOff(Entity u)
        {
            var filter = new EntityFilter
            {
                Owner = OwnerMatch.Owned, Player = u.Owner, Complete = true,
                DropOff = true, DropOffKind = u.CarryKind,
            };
            return FindNearest(u.Position, Map.Width + Map.Height, ref filter);
        }

        private void UpdateGathering(Entity u)
        {
            Entity source = Find(u.GatherSourceId);
            if (!CanGather(source, u.Owner))
            {
                source = FindNextResource(u);
                if (source == null)
                {
                    if (u.Carry > 0) { u.State = UnitState.Returning; u.HasPath = false; }
                    else SetIdle(u);
                    return;
                }
                u.GatherSourceId = source.Id;
                u.HasPath = false;
                if (source.IsBuilding) source.FarmerId = u.Id;
            }
            u.GatherKind = source.Kind;

            if (source.IsUnit && source.State != UnitState.Carcass)
            {
                Hunt(u, source);
                return;
            }

            if (!IsAdjacent(u, source))
            {
                if (!EnsurePathTo(u, source)) { SetIdle(u); return; }
                StepAlongPath(u);
                return;
            }
            u.HasPath = false;

            if (source.IsBuilding && source.Amount <= 0 && !TryReseed(source))
            {
                SetIdle(u);
                return;
            }

            ResourceKind yields = source.Def.Yields;
            if (u.CarryKind != yields)
            {
                // Switching resource drops whatever was carried, as in AoE2.
                u.Carry = 0;
                u.CarryKind = yields;
            }
            u.GatherProgress += StatsFor(u.Owner, source.Kind).GatherRate;
            if (u.GatherProgress < FP.One) return;
            u.GatherProgress -= FP.One;
            u.Carry++;
            source.Amount--;
            if (source.Amount <= 0 && !source.IsBuilding) Kill(source);
            if (u.Carry >= SimConstants.VillagerCarryCapacity)
            {
                u.State = UnitState.Returning;
                u.HasPath = false;
            }
        }

        /// <summary>Kill a live animal with the villager's hunting attack before it can be gathered.</summary>
        private void Hunt(Entity u, Entity animal)
        {
            if (InRange(u, animal, SimConstants.VillagerHuntRange))
            {
                u.HasPath = false;
                if (u.Cooldown > 0) return;
                u.Cooldown = u.Stats.AttackTicks;
                DealDamage(u.Id, animal, SimConstants.VillagerHuntDamage);
                return;
            }
            if (!EnsurePathTo(u, animal)) { SetIdle(u); return; }
            StepAlongPath(u);
        }

        /// <summary>An exhausted farm refills for its price in wood. Returns false if the owner cannot pay.</summary>
        private bool TryReseed(Entity farm)
        {
            PlayerState owner = Players[farm.Owner];
            Cost cost = farm.Stats.Cost;
            if (!owner.CanAfford(cost)) return false;
            owner.Pay(cost);
            farm.Amount = farm.Def.ResourceAmount;
            return true;
        }

        private void UpdateReturning(Entity u)
        {
            if (u.Carry == 0)
            {
                u.State = UnitState.Gathering;
                u.HasPath = false;
                return;
            }
            Entity dropOff = FindNearestDropOff(u);
            if (dropOff == null) { SetIdle(u); return; }
            if (!IsAdjacent(u, dropOff))
            {
                if (!EnsurePathTo(u, dropOff)) { SetIdle(u); return; }
                StepAlongPath(u);
                return;
            }
            Players[u.Owner].Add(u.CarryKind, u.Carry);
            u.Carry = 0;
            u.State = UnitState.Gathering;
            u.HasPath = false;
        }

        // ------------------------------------------------------------------ construction and repair

        private void UpdateBuildingWork(Entity u)
        {
            Entity site = Find(u.BuildSiteId);
            if (site == null || !site.IsBuilding || !site.UnderConstruction)
            {
                ResumePreviousJob(u);
                return;
            }
            if (!IsAdjacent(u, site))
            {
                if (!EnsurePathTo(u, site)) { SetIdle(u); return; }
                StepAlongPath(u);
                return;
            }
            u.HasPath = false;
            site.BuildProgress++;
            int max = site.Stats.MaxHp;
            int buildTicks = site.Stats.BuildTicks;
            site.Hp = 1 + (int)((long)(max - 1) * site.BuildProgress / buildTicks);
            if (site.BuildProgress >= buildTicks)
            {
                site.UnderConstruction = false;
                site.Hp = max;
                // Whoever finishes a farm starts working it.
                if (site.Def.IsGatherable && u.Def.CanGather) StartGathering(u, site);
            }
        }

        private void UpdateRepairing(Entity u)
        {
            Entity b = Find(u.TargetId);
            if (b == null || !b.IsBuilding || b.Owner != u.Owner)
            {
                ResumePreviousJob(u);
                return;
            }
            if (b.UnderConstruction)
            {
                u.State = UnitState.Building;
                u.BuildSiteId = b.Id;
                u.TargetId = 0;
                return;
            }
            if (b.Hp >= b.Stats.MaxHp)
            {
                b.RepairedHp = 0;
                ResumePreviousJob(u);
                return;
            }
            if (!IsAdjacent(u, b))
            {
                if (!EnsurePathTo(u, b)) { SetIdle(u); return; }
                StepAlongPath(u);
                return;
            }
            u.HasPath = false;

            int max = b.Stats.MaxHp;
            int heal = (max + b.Stats.BuildTicks - 1) / b.Stats.BuildTicks;
            if (heal > max - b.Hp) heal = max - b.Hp;
            int total = b.RepairedHp + heal;
            Cost price = b.Stats.Cost;
            var due = new Cost(
                RepairCharge(price.Food, total, max) - RepairCharge(price.Food, b.RepairedHp, max),
                RepairCharge(price.Wood, total, max) - RepairCharge(price.Wood, b.RepairedHp, max),
                RepairCharge(price.Gold, total, max) - RepairCharge(price.Gold, b.RepairedHp, max),
                RepairCharge(price.Stone, total, max) - RepairCharge(price.Stone, b.RepairedHp, max));
            PlayerState owner = Players[u.Owner];
            if (!owner.CanAfford(due))
            {
                SetIdle(u);
                return;
            }
            owner.Pay(due);
            b.Hp += heal;
            b.RepairedHp = b.Hp >= max ? 0 : total;
        }

        /// <summary>Resources charged in total for repairing this many hit points of a building of this price.</summary>
        private static int RepairCharge(int price, int repairedHp, int maxHp)
        {
            return (int)((long)price * repairedHp / ((long)maxHp * SimConstants.RepairCostDivisor));
        }

        private void ResumePreviousJob(Entity u)
        {
            u.BuildSiteId = 0;
            u.TargetId = 0;
            if (u.PreviousGatherSourceId != 0 || u.Carry > 0)
            {
                u.GatherSourceId = u.PreviousGatherSourceId;
                u.PreviousGatherSourceId = 0;
                u.State = UnitState.Gathering;
                u.HasPath = false;
                u.GatherProgress = FP.Zero;
                return;
            }
            SetIdle(u);
        }

        // ------------------------------------------------------------------ herding

        /// <summary>Sheep belong to whoever has units near them; another player takes them when only their units are near.</summary>
        private void UpdateHerdOwnership(Entity sheep)
        {
            if ((CurrentTick + sheep.Id) % SimConstants.HerdCheckInterval != 0) return;
            if (sheep.Owner >= 0)
            {
                var own = new EntityFilter { Category = EntityCategory.Unit, ExcludeTags = EntityTag.Animal, Owner = OwnerMatch.Owned, Player = sheep.Owner };
                if (FindNearest(sheep.Position, SimConstants.HerdRange, ref own) != null) return;
            }
            var other = new EntityFilter { Category = EntityCategory.Unit, ExcludeTags = EntityTag.Animal, Owner = OwnerMatch.Enemy, Player = sheep.Owner };
            Entity taker = FindNearest(sheep.Position, SimConstants.HerdRange, ref other);
            if (taker == null) return;
            sheep.Owner = taker.Owner;
            sheep.Stats = StatsFor(sheep.Owner, sheep.Kind);
            SetIdle(sheep);
        }

        // ------------------------------------------------------------------ population

        private void RecountPopulation()
        {
            for (int p = 0; p < Players.Length; p++)
            {
                Players[p].Population = 0;
                Players[p].PopulationCap = 0;
            }
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive || e.Owner < 0) continue;
                PlayerState p = Players[e.Owner];
                if (e.IsUnit)
                {
                    if (!e.Def.IsAnimal) p.Population += e.Def.Population;
                }
                else if (e.IsBuilding && !e.UnderConstruction)
                {
                    p.PopulationCap += e.Def.Housing;
                    if (e.TrainQueue.Count > 0 && e.TrainProgress > 0) p.Population += EntityDefs.Get(e.TrainQueue[0].Unit).Population;
                }
            }
            for (int p = 0; p < Players.Length; p++)
            {
                if (Players[p].PopulationCap > SimConstants.MaxPopulation) Players[p].PopulationCap = SimConstants.MaxPopulation;
            }
        }

        // ------------------------------------------------------------------ production buildings

        private void UpdateBuilding(Entity b)
        {
            if (b.UnderConstruction || b.Owner < 0) return;
            if (b.Def.HasTag(EntityTag.Shoots)) UpdateShooting(b);
            PlayerState owner = Players[b.Owner];
            if (b.AutoQueue) AutoQueueVillager(b, owner);
            if (b.TrainQueue.Count == 0) return;

            EntityKind kind = b.TrainQueue[0].Unit;
            if (b.TrainProgress == 0)
            {
                // Housed: hold the queue (never reject) until there is room.
                int need = EntityDefs.Get(kind).Population;
                if (owner.Population + need > owner.PopulationCap) return;
                owner.Population += need;
            }
            b.TrainProgress++;
            if (b.TrainProgress < StatsFor(b.Owner, kind).TrainTicks) return;

            Cell spawnCell;
            if (!FindSpawnCell(b.Footprint, out spawnCell)) return; // blocked in; keep waiting

            b.TrainProgress = 0;
            b.TrainQueue.RemoveAt(0);
            Entity unit = SpawnUnit(kind, b.Owner, spawnCell);
            SendToRally(b, unit);
        }

        private void AutoQueueVillager(Entity b, PlayerState owner)
        {
            if (owner.Population >= SimConstants.AutoVillagerStopPopulation)
            {
                b.AutoQueue = false;
                return;
            }
            if (b.TrainQueue.Count > 0) return;
            Cost cost = owner.Stats.Of(EntityKind.Villager).Cost;
            if (!owner.CanAfford(cost)) return;
            owner.Pay(cost);
            b.TrainQueue.Add(new QueueItem { Unit = EntityKind.Villager, Paid = cost });
        }

        private void SendToRally(Entity b, Entity unit)
        {
            if (!b.HasRally) return;
            Entity target = Find(b.RallyTargetId);
            if (target != null && unit.Def.CanGather && CanGather(target, unit.Owner)) StartGathering(unit, target);
            else if (target != null && target.IsBuilding) MoveTo(unit, target.Cell);
            else MoveTo(unit, b.RallyCell);
        }

        // ------------------------------------------------------------------ economy planner

        /// <summary>
        /// Every PlannerInterval ticks per player: each idle villager, in id order, goes to the
        /// resource whose villager count is furthest below its target share (ties: food, wood,
        /// gold, stone). Busy villagers, including those on manual orders, are never moved.
        /// </summary>
        private void RunEconomyPlanner(int player)
        {
            PlayerState p = Players[player];
            if (p.EconomyTargets.IsOff || (CurrentTick + player) % SimConstants.PlannerInterval != 0) return;

            int total = 0, food = 0, wood = 0, gold = 0, stone = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive || e.Owner != player || !e.IsUnit || !e.Def.CanGather) continue;
                total++;
                if (e.State != UnitState.Gathering && e.State != UnitState.Returning) continue;
                switch (EntityDefs.Get(e.GatherKind).Yields)
                {
                    case ResourceKind.Food: food++; break;
                    case ResourceKind.Wood: wood++; break;
                    case ResourceKind.Gold: gold++; break;
                    case ResourceKind.Stone: stone++; break;
                }
            }

            for (int i = 0; i < _entities.Count; i++)
            {
                Entity v = _entities[i];
                if (!v.Alive || v.Owner != player || !v.IsUnit || !v.Def.CanGather || v.State != UnitState.Idle) continue;

                int tried = 0;
                for (int attempt = 0; attempt < Cost.ResourceCount; attempt++)
                {
                    int best = -1;
                    long bestGap = long.MinValue;
                    for (int r = 0; r < Cost.ResourceCount; r++)
                    {
                        if ((tried & (1 << r)) != 0 || p.EconomyTargets[(ResourceKind)r] == 0) continue;
                        int have = r == 0 ? food : r == 1 ? wood : r == 2 ? gold : stone;
                        long gap = (long)p.EconomyTargets[(ResourceKind)r] * total - 100L * have;
                        if (gap > bestGap) { bestGap = gap; best = r; }
                    }
                    if (best < 0) break;
                    tried |= 1 << best;

                    var filter = new GatherableFilter { Sim = this, Player = player, Kind = EntityKind.None, Yield = (ResourceKind)best, ForPlanner = true, Asker = v };
                    Entity source = FindNearest(v.Position, SimConstants.PlannerSearchRadius, ref filter);
                    if (source == null) continue;
                    StartGathering(v, source);
                    if (best == 0) food++;
                    else if (best == 1) wood++;
                    else if (best == 2) gold++;
                    else stone++;
                    break;
                }
            }
        }
    }
}
