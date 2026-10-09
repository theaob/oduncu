namespace Oduncu.Sim
{
    public sealed partial class Simulation
    {
        private void UpdateUnit(Entity u)
        {
            if (u.Cooldown > 0) u.Cooldown--;

            switch (u.State)
            {
                case UnitState.Idle:
                    if (u.Def.IsMilitary) AutoEngage(u);
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
                case UnitState.Attacking:
                    UpdateAttacking(u);
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

        // ------------------------------------------------------------------ economy

        /// <summary>Another resource of the kind the villager was gathering, when its source runs out.</summary>
        private Entity FindNextResource(Entity u)
        {
            if (u.GatherKind == EntityKind.None) return null;
            var filter = new EntityFilter { Kind = u.GatherKind, WithAmount = true };
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
            if (source == null || !source.IsResource || source.Amount <= 0)
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
            }
            u.GatherKind = source.Kind;

            if (!IsAdjacent(u, source))
            {
                if (!EnsurePathTo(u, source)) { SetIdle(u); return; }
                StepAlongPath(u);
                return;
            }

            u.HasPath = false;
            ResourceKind yields = source.Def.Yields;
            if (u.CarryKind != yields)
            {
                // Switching resource drops whatever was carried, as in AoE2.
                u.Carry = 0;
                u.CarryKind = yields;
            }
            u.WorkTimer++;
            if (u.WorkTimer < SimConstants.GatherTicks) return;
            u.WorkTimer = 0;
            u.Carry++;
            source.Amount--;
            if (source.Amount <= 0) Kill(source);
            if (u.Carry >= SimConstants.VillagerCarryCapacity)
            {
                u.State = UnitState.Returning;
                u.HasPath = false;
            }
        }

        private void UpdateReturning(Entity u)
        {
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
            }
        }

        private void ResumePreviousJob(Entity u)
        {
            u.BuildSiteId = 0;
            if (u.PreviousGatherSourceId != 0 || u.Carry > 0)
            {
                u.GatherSourceId = u.PreviousGatherSourceId;
                u.PreviousGatherSourceId = 0;
                u.State = UnitState.Gathering;
                u.HasPath = false;
                u.WorkTimer = 0;
                return;
            }
            SetIdle(u);
        }

        // ------------------------------------------------------------------ combat

        private void AutoEngage(Entity u)
        {
            if ((CurrentTick + u.Id) % SimConstants.AutoEngageInterval != 0) return;
            var filter = new EntityFilter { Owner = OwnerMatch.Enemy, Player = u.Owner };
            Entity target = FindNearest(u.Position, u.Stats.LineOfSight, ref filter);
            if (target == null) return;
            u.State = UnitState.Attacking;
            u.TargetId = target.Id;
            u.HasPath = false;
        }

        private bool InRange(Entity u, Entity target)
        {
            FP d = FPVector2.Distance(u.Position, NearestPointOf(target, u.Position));
            return d <= u.Stats.Range;
        }

        private void UpdateAttacking(Entity u)
        {
            Entity target = Find(u.TargetId);
            if (target == null || target.IsResource) { SetIdle(u); return; }

            if (InRange(u, target))
            {
                u.HasPath = false;
                if (u.Cooldown > 0) return;
                u.Cooldown = u.Stats.AttackTicks;
                int damage = u.Stats.Attack - target.Stats.MeleeArmor;
                if (damage < 1) damage = 1;
                target.Hp -= damage;
                if (target.Hp <= 0) Kill(target);
                return;
            }

            if (!EnsurePathTo(u, target)) { SetIdle(u); return; }
            StepAlongPath(u);
        }
    }
}
