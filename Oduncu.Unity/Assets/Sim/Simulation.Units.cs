using System.Collections.Generic;

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
                    else if (u.Def.HasTag(EntityTag.Monk)) FindHealTarget(u);
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
                case UnitState.Garrisoning:
                    UpdateGarrisoning(u);
                    break;
                case UnitState.Healing:
                    UpdateHealing(u);
                    break;
                case UnitState.Carcass:
                case UnitState.Garrisoned:
                    break;
            }
        }

        // ------------------------------------------------------------------ movement

        private void UpdateMoving(Entity u)
        {
            FP speed = u.SpeedCap > FP.Zero ? FP.Min(u.Stats.Speed, u.SpeedCap) : u.Stats.Speed;
            if (u.UsesFlow)
            {
                // Large groups share one flow field until close, then each unit paths to its own slot.
                Cell next;
                if (Cell.Chebyshev(u.Cell, u.FlowGoal) > SimConstants.FlowHandoffDistance && _flowFields.NextCell(u.FlowGoal, u.Cell, u.Owner, out next))
                {
                    StepToward(u, next, speed);
                    return;
                }
                u.UsesFlow = false;
                u.HasPath = false;
            }
            if (!u.HasPath)
            {
                if (!_pathfinder.FindPathToCell(u.Cell, u.MoveTarget, u.Path, u.Owner)) { SetIdle(u); return; }
                u.HasPath = true;
                u.PathIndex = 0;
            }
            if (StepAlongPath(u, speed)) SetIdle(u);
        }

        /// <summary>Move toward a neighbouring cell's centre. Returns true on arrival.</summary>
        private bool StepToward(Entity u, Cell cell, FP speed)
        {
            FPVector2 target = FPVector2.CellCentre(cell);
            FPVector2 delta = target - u.Position;
            FP dist = delta.Magnitude;
            if (dist <= speed)
            {
                u.Position = target;
                SetUnitCell(u, cell);
                return true;
            }
            u.Position = u.Position + delta.Normalized * speed;
            SetUnitCell(u, u.Position.ToCell());
            return false;
        }

        private bool StepAlongPath(Entity u) => StepAlongPath(u, u.Stats.Speed);

        /// <summary>Move along the current path. Returns true when the path is finished or lost.</summary>
        private bool StepAlongPath(Entity u, FP speed)
        {
            if (!u.HasPath || u.PathIndex >= u.Path.Count) return true;
            Cell next = u.Path[u.PathIndex];
            if (!Map.IsPassable(next, u.Owner))
            {
                // Something was built across the path; drop it so the owner state recomputes.
                u.HasPath = false;
                return false;
            }
            if (!StepToward(u, next, speed)) return false;
            u.PathIndex++;
            return u.PathIndex >= u.Path.Count;
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
            return _pathfinder.FindPathAdjacentToRect(u.Cell, target.Footprint, u.Path, u.Owner);
        }

        private void SetIdle(Entity u)
        {
            u.State = UnitState.Idle;
            u.HasPath = false;
            u.TargetId = 0;
            u.AutoTarget = false;
            u.ChannelTicks = 0;
            u.SpeedCap = FP.Zero;
            u.UsesFlow = false;
        }

        private void MoveTo(Entity u, Cell cell)
        {
            u.State = UnitState.Moving;
            u.MoveTarget = Map.Clamp(cell);
            u.HasPath = false;
            u.TargetId = 0;
            u.AutoTarget = false;
            u.ChannelTicks = 0;
            u.SpeedCap = FP.Zero;
            u.UsesFlow = false;
        }

        // ------------------------------------------------------------------ separation

        private static readonly FPVector2[] SeparationDirections =
        {
            new FPVector2(FP.One, FP.Zero), new FPVector2(FP.Zero, FP.One), new FPVector2(-FP.One, FP.Zero), new FPVector2(FP.Zero, -FP.One),
            new FPVector2(FP.Ratio(7071, 10000), FP.Ratio(7071, 10000)), new FPVector2(-FP.Ratio(7071, 10000), FP.Ratio(7071, 10000)),
            new FPVector2(-FP.Ratio(7071, 10000), -FP.Ratio(7071, 10000)), new FPVector2(FP.Ratio(7071, 10000), -FP.Ratio(7071, 10000)),
        };

        /// <summary>Units at work hold their spot: they push others but are not pushed.</summary>
        private static bool IsAnchored(Entity u)
        {
            switch (u.State)
            {
                case UnitState.Gathering:
                case UnitState.Building:
                case UnitState.Repairing:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Soft collision (plan 3.4): each unit, in id order, moves away from units closer than
        /// SeparationRadius by up to MaxSeparationPush per tick. A push that would end in a
        /// blocked cell is tried along each axis alone and dropped if both are blocked. Units
        /// on exactly the same spot split along a direction picked from their ids.
        /// </summary>
        private void SeparateUnits()
        {
            FP radius = SimConstants.SeparationRadius;
            FP radius2 = radius * radius;
            FP maxPush = SimConstants.MaxSeparationPush;
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity u = _entities[i];
                if (!u.Alive || !u.IsUnit || u.IndexBucket < 0 || u.State == UnitState.Carcass || IsAnchored(u)) continue;

                FPVector2 push = FPVector2.Zero;
                int bx0 = Index.BucketXOf(u.Cell.X - 1), bx1 = Index.BucketXOf(u.Cell.X + 1);
                int by0 = Index.BucketYOf(u.Cell.Y - 1), by1 = Index.BucketYOf(u.Cell.Y + 1);
                for (int by = by0; by <= by1; by++)
                {
                    for (int bx = bx0; bx <= bx1; bx++)
                    {
                        var bucket = Index.Bucket(bx, by);
                        for (int k = 0; k < bucket.Count; k++)
                        {
                            Entity v = bucket[k];
                            if (v == u || !v.Alive || !v.IsUnit || v.State == UnitState.Carcass) continue;
                            FPVector2 diff = u.Position - v.Position;
                            FP d2 = diff.SqrMagnitude;
                            if (d2 >= radius2) continue;
                            if (d2 == FP.Zero)
                            {
                                int lo = u.Id < v.Id ? u.Id : v.Id, hi = u.Id < v.Id ? v.Id : u.Id;
                                FPVector2 dir = SeparationDirections[(lo * 31 + hi) & 7];
                                push = push + (u.Id == lo ? dir : FPVector2.Zero - dir) * (radius / 2);
                                continue;
                            }
                            FP d = FP.Sqrt(d2);
                            push = push + diff * ((radius - d) / (d * 2));
                        }
                    }
                }
                if (push == FPVector2.Zero) continue;
                if (push.SqrMagnitude > maxPush * maxPush) push = push.Normalized * maxPush;

                FPVector2 to = u.Position + push;
                if (!Map.IsPassable(to.ToCell(), u.Owner))
                {
                    to = new FPVector2(u.Position.X + push.X, u.Position.Y);
                    if (!Map.IsPassable(to.ToCell(), u.Owner))
                    {
                        to = new FPVector2(u.Position.X, u.Position.Y + push.Y);
                        if (!Map.IsPassable(to.ToCell(), u.Owner)) continue;
                    }
                }
                u.Position = to;
                SetUnitCell(u, to.ToCell());
            }
        }

        // ------------------------------------------------------------------ formations

        /// <summary>
        /// Group move (plan 3.4): units, in id order, take slots on a square grid centred on
        /// the target, one cell apart, and the group moves at its slowest unit's speed. Groups
        /// larger than FlowFieldGroupThreshold share a flow field to the target.
        /// </summary>
        private void OrderGroupMove(List<Entity> units, Cell target)
        {
            int n = units.Count;
            if (n == 0) return;
            FP cap = FP.Zero;
            if (n > 1)
            {
                cap = units[0].Stats.Speed;
                for (int i = 1; i < n; i++) cap = FP.Min(cap, units[i].Stats.Speed);
            }
            int cols = (int)FP.IntegerSqrt((ulong)n);
            if (cols * cols < n) cols++;
            int rows = (n + cols - 1) / cols;
            bool flow = n > SimConstants.FlowFieldGroupThreshold;
            for (int i = 0; i < n; i++)
            {
                Entity u = units[i];
                var slot = new Cell(target.X + i % cols - (cols - 1) / 2, target.Y + i / cols - (rows - 1) / 2);
                MoveTo(u, slot);
                u.PreviousGatherSourceId = 0;
                u.SpeedCap = cap;
                u.UsesFlow = flow;
                u.FlowGoal = target;
            }
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
