using System.Collections.Generic;

namespace Oduncu.Sim
{
    public sealed partial class Simulation
    {
        /// <summary>Last rejected command reason, for debugging and UI feedback. Not part of the hashed state.</summary>
        public string LastRejection { get; private set; }

        private void ApplyCommand(Command c)
        {
            if (c == null) return;
            if (c.Player < 0 || c.Player >= Players.Length) { Reject("bad player"); return; }
            switch (c.Kind)
            {
                case CommandKind.Move: ApplyMove(c); break;
                case CommandKind.Gather: ApplyGather(c); break;
                case CommandKind.Build: ApplyBuild(c); break;
                case CommandKind.Train: ApplyTrain(c); break;
                case CommandKind.Attack: ApplyAttack(c); break;
                case CommandKind.Stop: ApplyStop(c); break;
                case CommandKind.CancelTrain: ApplyCancelTrain(c); break;
                case CommandKind.SetRally: ApplySetRally(c); break;
                case CommandKind.Repair: ApplyRepair(c); break;
                case CommandKind.SetAutoQueue: ApplySetAutoQueue(c); break;
                case CommandKind.SetEconomyTargets: ApplySetEconomyTargets(c); break;
                case CommandKind.Garrison: ApplyGarrison(c); break;
                case CommandKind.Ungarrison: ApplyUngarrison(c); break;
                case CommandKind.SetStance: ApplySetStance(c); break;
                default: Reject("unknown command"); break;
            }
        }

        private void Reject(string reason) => LastRejection = reason;

        private Entity OwnedUnit(Command c, int id)
        {
            Entity e = Find(id);
            if (e == null || !e.IsUnit || e.Owner != c.Player || e.State == UnitState.Garrisoned) return null;
            return e;
        }

        private void ApplyMove(Command c)
        {
            if (!Map.InBounds(c.Cell)) { Reject("move target out of bounds"); return; }
            List<Entity> group = _scratchUnits;
            group.Clear();
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u != null) group.Add(u);
            }
            OrderGroupMove(group, c.Cell);
            group.Clear();
        }

        private void ApplyGather(Command c)
        {
            Entity source = Find(c.Target);
            if (!CanGather(source, c.Player)) { Reject("cannot gather that"); return; }
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u == null || !u.Def.CanGather) continue;
                StartGathering(u, source);
            }
        }

        private void ApplyBuild(Command c)
        {
            EntityDef def = EntityDefs.Get(c.EntityType);
            if (!def.IsBuilding || c.EntityType == EntityKind.TownCenter) { Reject("cannot build that"); return; }
            var rect = new CellRect(c.Cell.X, c.Cell.Y, def.Size);
            if (!Map.IsRectInBounds(rect)) { Reject("footprint out of bounds"); return; }
            if (!Map.IsRectFree(rect)) { Reject("footprint blocked"); return; }
            if (AnyUnitInside(rect)) { Reject("unit standing in footprint"); return; }
            PlayerState player = Players[c.Player];
            Cost cost = player.Stats.Of(c.EntityType).Cost;
            if (!player.CanAfford(cost)) { Reject(player.ShortageMessage(cost)); return; }

            List<Entity> builders = _scratchUnits;
            builders.Clear();
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u != null && u.Def.CanBuild) builders.Add(u);
            }
            if (builders.Count == 0) { Reject("no builder"); return; }

            player.Pay(cost);
            Entity site = SpawnStructure(c.EntityType, c.Player, c.Cell, underConstruction: true);
            for (int i = 0; i < builders.Count; i++)
            {
                Entity u = builders[i];
                if (u.State == UnitState.Gathering || u.State == UnitState.Returning) u.PreviousGatherSourceId = u.GatherSourceId;
                u.State = UnitState.Building;
                u.BuildSiteId = site.Id;
                u.TargetId = 0;
                u.HasPath = false;
            }
            builders.Clear();
        }

        private void ApplyTrain(Command c)
        {
            Entity b = Find(c.Target);
            if (b == null || !b.IsBuilding || b.Owner != c.Player) { Reject("not your building"); return; }
            if (b.UnderConstruction) { Reject("building under construction"); return; }
            bool allowed = false;
            for (int i = 0; i < b.Def.Trains.Length; i++) if (b.Def.Trains[i] == c.EntityType) allowed = true;
            if (!allowed) { Reject("building cannot train that"); return; }
            if (b.TrainQueue.Count >= SimConstants.TrainQueueLength) { Reject("queue full"); return; }
            // No population check here: a housed queue is held, not rejected (see UpdateBuilding).
            PlayerState player = Players[c.Player];
            Cost cost = player.Stats.Of(c.EntityType).Cost;
            if (!player.CanAfford(cost)) { Reject(player.ShortageMessage(cost)); return; }
            player.Pay(cost);
            b.TrainQueue.Add(new QueueItem { Unit = c.EntityType, Paid = cost });
        }

        /// <summary>Remove one queue slot (Arg is the slot index) and refund exactly what was paid for it.</summary>
        private void ApplyCancelTrain(Command c)
        {
            Entity b = Find(c.Target);
            if (b == null || !b.IsBuilding || b.Owner != c.Player) { Reject("not your building"); return; }
            if (c.Arg < 0 || c.Arg >= b.TrainQueue.Count) { Reject("no such queue slot"); return; }
            Players[c.Player].Refund(b.TrainQueue[c.Arg].Paid);
            b.TrainQueue.RemoveAt(c.Arg);
            if (c.Arg == 0) b.TrainProgress = 0;
        }

        private void ApplyAttack(Command c)
        {
            Entity target = Find(c.Target);
            if (!IsAttackable(target) || target.Owner == c.Player) { Reject("invalid attack target"); return; }
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u == null || u.Def.IsAnimal) continue;
                if (u.Def.HasTag(EntityTag.TargetsBuildings) && !target.IsBuilding) { Reject("can only attack buildings"); continue; }
                if (u.Def.HasTag(EntityTag.Monk) && (!target.IsUnit || target.Def.IsAnimal)) { Reject("monks convert units only"); continue; }
                SetIdle(u);
                u.State = UnitState.Attacking;
                u.TargetId = target.Id;
                u.PreviousGatherSourceId = 0;
            }
        }

        private void ApplyStop(Command c)
        {
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u == null) continue;
                SetIdle(u);
                u.PreviousGatherSourceId = 0;
            }
        }
    
        private Entity OwnedBuilding(Command c)
        {
            Entity b = Find(c.Target);
            if (b == null || !b.IsBuilding || b.Owner != c.Player) return null;
            return b;
        }

        private void ApplySetRally(Command c)
        {
            Entity b = OwnedBuilding(c);
            if (b == null) { Reject("not your building"); return; }
            if (b.Def.Trains.Length == 0) { Reject("building trains nothing"); return; }
            Entity target = c.Arg != 0 ? Find(c.Arg) : null;
            if (c.Arg != 0 && target == null) { Reject("rally target is gone"); return; }
            Cell cell = target != null ? target.Cell : c.Cell;
            if (!Map.InBounds(cell)) { Reject("rally point out of bounds"); return; }
            b.HasRally = true;
            b.RallyCell = cell;
            b.RallyTargetId = target != null ? target.Id : 0;
        }

        private void ApplyRepair(Command c)
        {
            Entity b = OwnedBuilding(c);
            if (b == null) { Reject("not your building"); return; }
            if (!b.UnderConstruction && b.Hp >= b.Stats.MaxHp) { Reject("nothing to repair"); return; }
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u == null || !u.Def.CanBuild) continue;
                if (u.State == UnitState.Gathering || u.State == UnitState.Returning) u.PreviousGatherSourceId = u.GatherSourceId;
                u.HasPath = false;
                if (b.UnderConstruction)
                {
                    u.State = UnitState.Building;
                    u.BuildSiteId = b.Id;
                    u.TargetId = 0;
                }
                else
                {
                    u.State = UnitState.Repairing;
                    u.TargetId = b.Id;
                }
            }
        }

        private void ApplySetAutoQueue(Command c)
        {
            Entity b = OwnedBuilding(c);
            if (b == null) { Reject("not your building"); return; }
            if (System.Array.IndexOf(b.Def.Trains, EntityKind.Villager) < 0) { Reject("building does not train villagers"); return; }
            b.AutoQueue = c.Arg != 0;
        }

        private void ApplySetEconomyTargets(Command c)
        {
            EconomyTargets targets = EconomyTargets.Unpack(c.Arg);
            if (!targets.IsValid) { Reject("economy targets must add up to 100"); return; }
            Players[c.Player].EconomyTargets = targets;
        }

        private void ApplyGarrison(Command c)
        {
            Entity b = OwnedBuilding(c);
            if (b == null) { Reject("not your building"); return; }
            if (b.UnderConstruction || b.Def.GarrisonCapacity == 0) { Reject("cannot garrison there"); return; }
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u == null || !CanGarrisonIn(u, b)) continue;
                SetIdle(u);
                u.PreviousGatherSourceId = 0;
                u.State = UnitState.Garrisoning;
                u.TargetId = b.Id;
            }
        }

        private void ApplyUngarrison(Command c)
        {
            Entity b = OwnedBuilding(c);
            if (b == null) { Reject("not your building"); return; }
            Eject(b);
        }

        private void ApplySetStance(Command c)
        {
            if (c.Arg != (int)Stance.Aggressive && c.Arg != (int)Stance.HoldGround) { Reject("unknown stance"); return; }
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u != null) u.HoldGround = c.Arg == (int)Stance.HoldGround;
            }
        }
    }
}
