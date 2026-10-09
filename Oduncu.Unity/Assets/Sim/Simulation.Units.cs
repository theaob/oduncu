namespace Oduncu.Sim
{
    public sealed partial class Simulation
    {
        private void UpdateUnit(Entity u)
        {
            if (u.Cooldown > 0) u.Cooldown--;
            if (u.State != UnitState.Carcass && u.Def.HasTag(EntityTag.Herdable)) UpdateHerdOwnership(u);

            switch (u.State)
            {
                case UnitState.Idle:
                    if (u.Def.IsMilitary) AutoEngage(u);
                    else if (u.Def.IsAnimal) UpdateIdleAnimal(u);
                    break;
                case UnitState.Moving:
                    UpdateMoving(u);
                    break;
                case UnitState.Gathering:
                    UpdateGathering(u);
                    break;
                case UnitState.Returning:
                    UpdateReturning(u);
                    break;
                case UnitState.Building:
                    UpdateBuildingWork(u);
                    break;
                case UnitState.Repairing:
                    UpdateRepairing(u);
                    break;
                case UnitState.Attacking:
                    UpdateAttacking(u);
                    break;
                case UnitState.Carcass:
                    break;
            }
        }

        // ------------------------------------------------------------------ movement

        private void UpdateMoving(Entity u)
        {
            if (!u.HasPath)
            {
                if (!_pathfinder.FindPathToCell(u.Cell, u.MoveTarget, u.Path)) { SetIdle(u); return; }
                u.HasPath = true;
                u.PathIndex = 0;
            }
            if (StepAlongPath(u)) SetIdle(u);
        }

        /// <summary>Move along the current path. Returns true when the path is finished or lost.</summary>
        private bool StepAlongPath(Entity u)
        {
            if (!u.HasPath || u.PathIndex >= u.Path.Count) return true;
            Cell next = u.Path[u.PathIndex];
            if (!Map.IsFree(next))
            {
                // Something was built across the path; drop it so the owner state recomputes.
                u.HasPath = false;
                return false;
            }
            FPVector2 target = FPVector2.CellCentre(next);
            FPVector2 delta = target - u.Position;
            FP dist = delta.Magnitude;
            FP speed = u.Stats.Speed;
            if (dist <= speed)
            {
                u.Position = target;
                SetUnitCell(u, next);
                u.PathIndex++;
                return u.PathIndex >= u.Path.Count;
            }
            u.Position = u.Position + delta.Normalized * speed;
            SetUnitCell(u, u.Position.ToCell());
            return false;
        }

        private bool IsAdjacent(Entity u, Entity target)
        {
            return target.Footprint.DistanceTo(u.Cell) <= 1;
        }

        /// <summary>Ensure a path to a cell touching the target exists, recomputing when the target moved.</summary>
        private bool EnsurePathTo(Entity u, Entity target)
        {
            Cell goal = target.Cell;
            bool stale = !u.HasPath
                || goal != u.PathGoal
                || (target.IsUnit && CurrentTick - u.PathAge >= SimConstants.ChaseRepathInterval);
            if (!stale) return true;
            u.HasPath = true;
            u.PathGoal = goal;
            u.PathAge = CurrentTick;
            u.PathIndex = 0;
            return _pathfinder.FindPathAdjacentToRect(u.Cell, target.Footprint, u.Path);
        }

        private void SetIdle(Entity u)
        {
            u.State = UnitState.Idle;
            u.HasPath = false;
            u.TargetId = 0;
        }

        private void MoveTo(Entity u, Cell cell)
        {
            u.State = UnitState.Moving;
            u.MoveTarget = Map.Clamp(cell);
            u.HasPath = false;
            u.TargetId = 0;
        }

        // ------------------------------------------------------------------ combat

        private void AutoEngage(Entity u)
        {
            if ((CurrentTick + u.Id) % SimConstants.AutoEngageInterval != 0) return;
            var filter = new EntityFilter { Owner = OwnerMatch.Enemy, Player = u.Owner, ExcludeTags = EntityTag.Animal };
            Entity target = FindNearest(u.Position, u.Stats.LineOfSight, ref filter);
            if (target == null) return;
            u.State = UnitState.Attacking;
            u.TargetId = target.Id;
            u.HasPath = false;
        }

        private bool InRange(Entity u, Entity target, FP range)
        {
            FP d = FPVector2.Distance(u.Position, NearestPointOf(target, u.Position));
            return d <= range;
        }

        /// <summary>Whether a unit can be hit at all: alive and not already a carcass.</summary>
        private static bool IsAttackable(Entity target)
        {
            return target != null && !target.IsResource && target.State != UnitState.Carcass;
        }

        private void UpdateAttacking(Entity u)
        {
            Entity target = Find(u.TargetId);
            if (!IsAttackable(target)) { SetIdle(u); return; }

            if (InRange(u, target, u.Stats.Range))
            {
                u.HasPath = false;
                if (u.Cooldown > 0) return;
                u.Cooldown = u.Stats.AttackTicks;
                int damage = u.Stats.Attack - target.Stats.MeleeArmor;
                if (damage < 1) damage = 1;
                DealDamage(u, target, damage);
                return;
            }

            if (!EnsurePathTo(u, target)) { SetIdle(u); return; }
            StepAlongPath(u);
        }

        /// <summary>Apply damage, remember who did it, and kill or (for animals) turn into a carcass.</summary>
        private void DealDamage(Entity attacker, Entity target, int damage)
        {
            target.Hp -= damage;
            target.LastAttackerId = attacker.Id;
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

        // ------------------------------------------------------------------ animals

        /// <summary>Deer run a short way from any unit that comes close; boar turn on whatever hurt them.</summary>
        private void UpdateIdleAnimal(Entity a)
        {
            if ((CurrentTick + a.Id) % SimConstants.AnimalCheckInterval != 0) return;

            if (a.Def.HasTag(EntityTag.Retaliates))
            {
                Entity attacker = Find(a.LastAttackerId);
                if (IsAttackable(attacker) && FPVector2.SqrDistance(a.Position, attacker.Position) <= FP.FromInt(a.Stats.LineOfSight * a.Stats.LineOfSight))
                {
                    a.State = UnitState.Attacking;
                    a.TargetId = attacker.Id;
                    a.HasPath = false;
                }
                return;
            }

            if (a.Def.HasTag(EntityTag.Flees))
            {
                var filter = new EntityFilter { Category = EntityCategory.Unit, ExcludeTags = EntityTag.Animal };
                Entity threat = FindNearest(a.Position, SimConstants.FleeTriggerRange, ref filter);
                if (threat == null) return;
                FPVector2 away = (a.Position - threat.Position).Normalized;
                if (away == FPVector2.Zero) away = new FPVector2(FP.One, FP.Zero);
                FPVector2 to = a.Position + away * FP.FromInt(SimConstants.FleeDistance);
                MoveTo(a, to.ToCell());
            }
        }
    }
}
