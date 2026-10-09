using System.Collections.Generic;

namespace Oduncu.Sim
{
    /// <summary>
    /// A shot in flight. Projectiles always hit (design section 5.3): the damage lands after a
    /// delay fixed at fire time, and is wasted if the target has died or gone inside by then.
    /// </summary>
    public struct Projectile
    {
        public int AttackerId;
        public int Owner;
        public EntityKind AttackerKind;
        public int Attack;
        public AttackType Type;
        public int TargetId;
        /// <summary>Where the target was when the shot was fired; splash is centred here.</summary>
        public FPVector2 Impact;
        public FPVector2 From;
        public FP Splash;
        public int FireTick;
        public int LandTick;

        public void WriteState(StateHasher h)
        {
            h.Write(AttackerId);
            h.Write(Owner);
            h.Write((int)AttackerKind);
            h.Write(Attack);
            h.Write((int)Type);
            h.Write(TargetId);
            h.Write(Impact);
            h.Write(From);
            h.Write(Splash);
            h.Write(FireTick);
            h.Write(LandTick);
        }
    }

    /// <summary>Own damaged units a monk may heal.</summary>
    internal struct DamagedFriendFilter : IEntityFilter
    {
        public int Player;
        public int Self;

        public bool Matches(Entity e)
        {
            return e.IsUnit && e.Owner == Player && e.Id != Self && !e.Def.IsAnimal
                && e.State != UnitState.Carcass && e.Hp < e.Stats.MaxHp;
        }
    }

    public sealed partial class Simulation
    {
        private readonly List<Projectile> _projectiles = new List<Projectile>(SimConstants.MaxProjectiles);
        private readonly List<Entity> _scratchHits = new List<Entity>(64);

        /// <summary>Shots in flight, in fire order. For presentation; do not mutate.</summary>
        public IReadOnlyList<Projectile> Projectiles => _projectiles;

        /// <summary>The player who won by conquest, or -1 (no winner yet, or everyone lost on the same tick).</summary>
        public int Winner { get; private set; } = -1;
        /// <summary>Tick on which the match was decided, or 0 while it is still going.</summary>
        public int MatchEndTick { get; private set; }
        public bool MatchOver => MatchEndTick != 0;

        // ------------------------------------------------------------------ damage

        /// <summary>
        /// Damage of one hit: attack minus the target's armour against that attack type, at
        /// least 1, plus every bonus whose tag the target carries (design section 5.1).
        /// </summary>
        public int DamageAgainst(EntityKind attackerKind, int attack, AttackType type, Entity target)
        {
            int armour = type == AttackType.Melee ? target.Stats.MeleeArmor : target.Stats.PierceArmor;
            int damage = attack - armour;
            if (damage < 1) damage = 1;
            BonusDamage[] bonuses = EntityDefs.Get(attackerKind).Bonuses;
            for (int i = 0; i < bonuses.Length; i++)
            {
                if ((target.Def.Tags & bonuses[i].Against) != 0) damage += bonuses[i].Amount;
            }
            return damage;
        }

        /// <summary>Whether an entity can be hit at all: alive, not a resource or carcass, not inside a building.</summary>
        private static bool IsAttackable(Entity target)
        {
            return target != null && !target.IsResource
                && target.State != UnitState.Carcass && target.State != UnitState.Garrisoned;
        }

        /// <summary>Melee hits land now; ranged attacks launch a projectile.</summary>
        private void Fire(Entity attacker, Entity target)
        {
            if (!attacker.Def.IsRanged)
            {
                DealDamage(attacker.Id, target, DamageAgainst(attacker.Kind, attacker.Stats.Attack, attacker.Def.AttackType, target));
                return;
            }
            Launch(attacker, target);
        }

        private void Launch(Entity attacker, Entity target)
        {
            FPVector2 from = attacker.Position;
            FP distance = FPVector2.Distance(from, NearestPointOf(target, from));
            int delay = (distance / attacker.Def.ProjectileSpeed).CeilToInt();
            if (delay < 1) delay = 1;
            var p = new Projectile
            {
                AttackerId = attacker.Id,
                Owner = attacker.Owner,
                AttackerKind = attacker.Kind,
                Attack = attacker.Stats.Attack,
                Type = attacker.Def.AttackType,
                TargetId = target.Id,
                Impact = target.Position,
                From = from,
                Splash = attacker.Def.SplashRadius,
                FireTick = CurrentTick,
                LandTick = CurrentTick + delay,
            };
            if (_projectiles.Count >= SimConstants.MaxProjectiles)
            {
                // Out of slots: resolve at once rather than grow the list on the tick path.
                Land(ref p);
                return;
            }
            _projectiles.Add(p);
        }

        /// <summary>Land every projectile due this tick, in fire order, and compact the list in place.</summary>
        private void UpdateProjectiles()
        {
            int write = 0;
            for (int read = 0; read < _projectiles.Count; read++)
            {
                Projectile p = _projectiles[read];
                if (p.LandTick > CurrentTick)
                {
                    _projectiles[write++] = p;
                    continue;
                }
                Land(ref p);
            }
            if (write < _projectiles.Count) _projectiles.RemoveRange(write, _projectiles.Count - write);
        }

        private void Land(ref Projectile p)
        {
            if (p.Splash <= FP.Zero)
            {
                Entity target = Find(p.TargetId);
                if (IsAttackable(target)) DealDamage(p.AttackerId, target, DamageAgainst(p.AttackerKind, p.Attack, p.Type, target));
                return;
            }

            // Area damage hits everything in the radius once, friend or foe, in id order.
            List<Entity> hits = _scratchHits;
            CollectInRadius(p.Impact, p.Splash, hits);
            for (int i = 0; i < hits.Count; i++)
            {
                Entity e = hits[i];
                if (e.Alive && IsAttackable(e)) DealDamage(p.AttackerId, e, DamageAgainst(p.AttackerKind, p.Attack, p.Type, e));
            }
            hits.Clear();
        }

        /// <summary>Units and buildings whose nearest point is within radius of a position, sorted by id.</summary>
        private void CollectInRadius(FPVector2 at, FP radius, List<Entity> into)
        {
            into.Clear();
            int r = radius.CeilToInt();
            FP r2 = radius * radius;
            int bx0 = Index.BucketXOf(at.X.FloorToInt() - r - MaxStructureSize), bx1 = Index.BucketXOf(at.X.FloorToInt() + r);
            int by0 = Index.BucketYOf(at.Y.FloorToInt() - r - MaxStructureSize), by1 = Index.BucketYOf(at.Y.FloorToInt() + r);
            for (int by = by0; by <= by1; by++)
            {
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    var bucket = Index.Bucket(bx, by);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        Entity e = bucket[i];
                        if (!e.Alive || e.IsResource) continue;
                        if (FPVector2.SqrDistance(at, NearestPointOf(e, at)) > r2) continue;
                        // Insertion sort by id: lists are short and this never allocates.
                        int j = into.Count;
                        into.Add(e);
                        while (j > 0 && into[j - 1].Id > e.Id)
                        {
                            into[j] = into[j - 1];
                            j--;
                        }
                        into[j] = e;
                    }
                }
            }
        }

        /// <summary>Apply damage, remember who did it, and kill or (for animals) turn into a carcass.</summary>
        private void DealDamage(int attackerId, Entity target, int damage)
        {
            target.Hp -= damage;
            target.LastAttackerId = attackerId;
            if (target.Hp > 0) return;
            if (target.IsUnit && target.Def.IsAnimal) MakeCarcass(target);
            else Kill(target);
        }

        /// <summary>A dead animal stays on the map as neutral food until it is gathered out.</summary>
        private void MakeCarcass(Entity animal)
        {
            animal.Hp = 0;
            animal.State = UnitState.Carcass;
            animal.Owner = SimConstants.NeutralOwner;
            animal.Stats = StatsFor(animal.Owner, animal.Kind);
            animal.HasPath = false;
            animal.TargetId = 0;
        }

        // ------------------------------------------------------------------ targeting

        /// <summary>
        /// Best enemy for a unit or shooting building to attack within maxRange of it: military
        /// units first, then other units, then buildings; ties by distance, then id (plan 3.4).
        /// Respects minimum range and units that only attack buildings. Animals are never picked.
        /// </summary>
        public Entity FindAttackTarget(Entity seeker, FP maxRange)
        {
            bool buildingsOnly = seeker.Def.HasTag(EntityTag.TargetsBuildings);
            bool unitsOnly = seeker.IsBuilding;
            FPVector2 from = seeker.Position;
            FP max2 = maxRange * maxRange;
            FP min = seeker.Def.MinRange;
            FP min2 = min * min;
            int r = maxRange.CeilToInt();

            Entity best = null;
            int bestClass = int.MaxValue;
            FP bestDist = FP.Zero;
            int bx0 = Index.BucketXOf(from.X.FloorToInt() - r - MaxStructureSize), bx1 = Index.BucketXOf(from.X.FloorToInt() + r);
            int by0 = Index.BucketYOf(from.Y.FloorToInt() - r - MaxStructureSize), by1 = Index.BucketYOf(from.Y.FloorToInt() + r);
            for (int by = by0; by <= by1; by++)
            {
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    var bucket = Index.Bucket(bx, by);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        Entity e = bucket[i];
                        if (!e.Alive || e.Owner < 0 || e.Owner == seeker.Owner || !IsAttackable(e) || e.Def.IsAnimal) continue;
                        if (buildingsOnly && !e.IsBuilding) continue;
                        if (unitsOnly && !e.IsUnit) continue;
                        FP d = FPVector2.SqrDistance(from, NearestPointOf(e, from));
                        if (d > max2 || (min > FP.Zero && d < min2)) continue;
                        int cls = e.IsBuilding ? 2 : (e.Def.IsMilitary ? 0 : 1);
                        if (cls < bestClass || (cls == bestClass && (d < bestDist || (d == bestDist && e.Id < best.Id))))
                        {
                            best = e;
                            bestClass = cls;
                            bestDist = d;
                        }
                    }
                }
            }
            return best;
        }

        private bool InRange(Entity u, Entity target, FP range)
        {
            return FPVector2.Distance(u.Position, NearestPointOf(target, u.Position)) <= range;
        }

        private void AutoEngage(Entity u)
        {
            if ((CurrentTick + u.Id) % SimConstants.AutoEngageInterval != 0) return;
            FP reach = u.HoldGround ? u.Stats.Range : FP.FromInt(u.Stats.LineOfSight);
            Entity target = FindAttackTarget(u, reach);
            if (target == null) return;
            u.State = UnitState.Attacking;
            u.TargetId = target.Id;
            u.AutoTarget = true;
            u.HasPath = false;
        }

        private void UpdateAttacking(Entity u)
        {
            Entity target = Find(u.TargetId);
            // The owner check catches targets converted to our side mid-fight.
            if (!IsAttackable(target) || target.Owner == u.Owner) { SetIdle(u); return; }
            if (u.Def.HasTag(EntityTag.Monk)) { UpdateConverting(u, target); return; }

            FP d = FPVector2.Distance(u.Position, NearestPointOf(target, u.Position));
            if (u.Def.MinRange > FP.Zero && d < u.Def.MinRange) { SetIdle(u); return; }
            if (d <= u.Stats.Range)
            {
                u.HasPath = false;
                if (u.Cooldown > 0) return;
                u.Cooldown = u.Stats.AttackTicks;
                Fire(u, target);
                return;
            }
            // Hold ground: never chase something the unit picked for itself.
            if (u.HoldGround && u.AutoTarget) { SetIdle(u); return; }
            if (!EnsurePathTo(u, target)) { SetIdle(u); return; }
            // Standing next to the target's cell but still out of reach (units are pushed
            // off cell centres): close the last bit in a straight line.
            if (StepAlongPath(u)) StepDirect(u, NearestPointOf(target, u.Position));
        }

        /// <summary>Move straight toward a point by one tick of speed, unless that ends in a blocked cell.</summary>
        private void StepDirect(Entity u, FPVector2 point)
        {
            FPVector2 delta = point - u.Position;
            FP dist = delta.Magnitude;
            if (dist == FP.Zero) return;
            FPVector2 to = dist <= u.Stats.Speed ? point : u.Position + delta.Normalized * u.Stats.Speed;
            if (!Map.IsFree(to.ToCell())) return;
            u.Position = to;
            SetUnitCell(u, to.ToCell());
        }

        // ------------------------------------------------------------------ shooting buildings

        /// <summary>Arrows per volley: the building's own plus one per garrisoned unit.</summary>
        public int ArrowsFor(Entity building) => building.Def.Arrows + building.GarrisonCount;

        private void UpdateShooting(Entity b)
        {
            if (b.Cooldown > 0) { b.Cooldown--; return; }
            Entity target = FindAttackTarget(b, b.Stats.Range);
            if (target == null) return;
            b.Cooldown = b.Stats.AttackTicks;
            int arrows = ArrowsFor(b);
            for (int i = 0; i < arrows; i++) Launch(b, target);
        }

        // ------------------------------------------------------------------ garrison

        private void UpdateGarrisoning(Entity u)
        {
            Entity b = Find(u.TargetId);
            if (b == null || !CanGarrisonIn(u, b)) { SetIdle(u); return; }
            if (!IsAdjacent(u, b))
            {
                if (!EnsurePathTo(u, b)) { SetIdle(u); return; }
                StepAlongPath(u);
                return;
            }
            if (b.GarrisonCount >= b.Def.GarrisonCapacity) { SetIdle(u); return; }
            Index.Remove(u);
            u.State = UnitState.Garrisoned;
            u.GarrisonedIn = b.Id;
            u.HasPath = false;
            u.TargetId = 0;
            u.Cell = b.Cell;
            u.Position = b.Position;
            b.GarrisonCount++;
        }

        private static bool CanGarrisonIn(Entity u, Entity b)
        {
            return b.IsBuilding && b.Owner == u.Owner && !b.UnderConstruction && b.Def.GarrisonCapacity > 0
                && !u.Def.IsAnimal && !u.Def.HasTag(EntityTag.Siege);
        }

        /// <summary>Put every unit inside a building back on free cells around it, in id order.</summary>
        private void Eject(Entity b)
        {
            if (b.GarrisonCount == 0) return;
            int placed = 0, stuck = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity u = _entities[i];
                if (!u.Alive || u.State != UnitState.Garrisoned || u.GarrisonedIn != b.Id) continue;
                Cell cell;
                if (!FindSpawnCell(b.Footprint, placed, out cell))
                {
                    // Walled in: a standing building keeps the unit; a destroyed one frees its footprint.
                    if (b.Alive) { stuck++; continue; }
                    cell = b.Cell;
                }
                placed++;
                u.State = UnitState.Idle;
                u.GarrisonedIn = 0;
                u.Cell = cell;
                u.Position = FPVector2.CellCentre(cell);
                Index.Add(u);
            }
            b.GarrisonCount = stuck;
        }

        // ------------------------------------------------------------------ monks

        private void UpdateConverting(Entity monk, Entity target)
        {
            if (!target.IsUnit || target.Def.IsAnimal) { SetIdle(monk); return; }
            FP d = FPVector2.Distance(monk.Position, target.Position);
            if (d <= monk.Stats.Range)
            {
                monk.HasPath = false;
                monk.ChannelTicks++;
                if (monk.ChannelTicks < SimConstants.ConversionTicks) return;
                Convert(target, monk.Owner);
                SetIdle(monk);
                return;
            }
            monk.ChannelTicks = 0;
            if (!EnsurePathTo(monk, target)) { SetIdle(monk); return; }
            StepAlongPath(monk);
        }

        /// <summary>Hand a unit to another player. It drops whatever it was doing and stands idle.</summary>
        private void Convert(Entity u, int newOwner)
        {
            u.Owner = newOwner;
            u.Stats = StatsFor(newOwner, u.Kind);
            if (u.Hp > u.Stats.MaxHp) u.Hp = u.Stats.MaxHp;
            SetIdle(u);
            u.GatherSourceId = 0;
            u.PreviousGatherSourceId = 0;
            u.BuildSiteId = 0;
            u.HoldGround = false;
        }

        private void FindHealTarget(Entity monk)
        {
            if ((CurrentTick + monk.Id) % SimConstants.AutoEngageInterval != 0) return;
            var filter = new DamagedFriendFilter { Player = monk.Owner, Self = monk.Id };
            Entity target = FindNearest(monk.Position, monk.Stats.LineOfSight, ref filter);
            if (target == null) return;
            monk.State = UnitState.Healing;
            monk.TargetId = target.Id;
            monk.HasPath = false;
        }

        private void UpdateHealing(Entity monk)
        {
            Entity target = Find(monk.TargetId);
            if (target == null || target.Owner != monk.Owner || !IsAttackable(target) || target.Hp >= target.Stats.MaxHp)
            {
                SetIdle(monk);
                return;
            }
            if (FPVector2.Distance(monk.Position, target.Position) <= monk.Stats.Range)
            {
                monk.HasPath = false;
                if (monk.Cooldown > 0) return;
                monk.Cooldown = SimConstants.HealInterval;
                target.Hp++;
                return;
            }
            if (!EnsurePathTo(monk, target)) { SetIdle(monk); return; }
            StepAlongPath(monk);
        }

        // ------------------------------------------------------------------ conquest

        /// <summary>
        /// A player is in the game while they own a finished Town Center, Castle or military
        /// production building (tag conquest). Players who never had one (test scenarios,
        /// skirmishes of units only) stay in while they have units. The match ends on the tick
        /// one player or none is left.
        /// </summary>
        private void UpdatePlayersAlive()
        {
            for (int p = 0; p < Players.Length; p++)
            {
                bool conquest = false, units = false;
                for (int i = 0; i < _entities.Count; i++)
                {
                    Entity e = _entities[i];
                    if (!e.Alive || e.Owner != p) continue;
                    if (e.IsBuilding && !e.UnderConstruction && e.Def.HasTag(EntityTag.Conquest)) { conquest = true; break; }
                    if (e.IsUnit && !e.Def.IsAnimal) units = true;
                }
                PlayerState player = Players[p];
                if (conquest) player.HadConquestBuilding = true;
                player.Alive = conquest || (!player.HadConquestBuilding && units);
            }

            if (MatchOver || Players.Length < 2) return;
            int alive = 0, last = -1;
            for (int p = 0; p < Players.Length; p++)
            {
                if (!Players[p].Alive) continue;
                alive++;
                last = p;
            }
            if (alive > 1) return;
            MatchEndTick = CurrentTick;
            Winner = last;
        }
    }
}
