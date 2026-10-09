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
                default: Reject("unknown command"); break;
            }
        }

        private void Reject(string reason) => LastRejection = reason;

        private Entity OwnedUnit(Command c, int id)
        {
            Entity e = Find(id);
            if (e == null || !e.IsUnit || e.Owner != c.Player) return null;
            return e;
        }

        private void ApplyMove(Command c)
        {
            if (!Map.InBounds(c.Cell)) { Reject("move target out of bounds"); return; }
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u == null) continue;
                u.State = UnitState.Moving;
                u.MoveTarget = c.Cell;
                u.HasPath = false;
                u.TargetId = 0;
                u.PreviousGatherSourceId = 0;
            }
        }

        private void ApplyGather(Command c)
        {
            Entity source = Find(c.Target);
            if (source == null || !source.IsResource) { Reject("gather target is not a resource"); return; }
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u == null || !u.Def.CanGather) continue;
                u.State = UnitState.Gathering;
                u.GatherSourceId = source.Id;
                u.GatherKind = source.Kind;
                u.PreviousGatherSourceId = 0;
                u.TargetId = 0;
                u.HasPath = false;
                u.WorkTimer = 0;
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
            EntityDef def = EntityDefs.Get(c.EntityType);
            if (CountUnits(c.Player) + QueuedUnits(c.Player) + def.Population > SimConstants.PopulationCap) { Reject("population cap"); return; }
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
            if (target == null || target.IsResource || target.Owner == c.Player) { Reject("invalid attack target"); return; }
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u == null) continue;
                u.State = UnitState.Attacking;
                u.TargetId = target.Id;
                u.HasPath = false;
                u.PreviousGatherSourceId = 0;
            }
        }

        private void ApplyStop(Command c)
        {
            for (int i = 0; i < c.Units.Length; i++)
            {
                Entity u = OwnedUnit(c, c.Units[i]);
                if (u == null) continue;
                u.State = UnitState.Idle;
                u.HasPath = false;
                u.TargetId = 0;
                u.PreviousGatherSourceId = 0;
            }
        }
    }
}
