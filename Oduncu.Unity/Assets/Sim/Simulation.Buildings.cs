namespace Oduncu.Sim
{
    public sealed partial class Simulation
    {
        private void UpdateBuilding(Entity b)
        {
            if (b.UnderConstruction || b.TrainQueue == null || b.TrainQueue.Count == 0) return;

            EntityDef def = EntityDefs.Get(b.TrainQueue[0]);
            b.TrainProgress++;
            if (b.TrainProgress < def.TrainTicks) return;

            Cell spawnCell;
            if (!FindSpawnCell(b.Footprint, out spawnCell)) return; // blocked in; keep waiting

            b.TrainProgress = 0;
            EntityKind kind = b.TrainQueue[0];
            b.TrainQueue.RemoveAt(0);
            SpawnUnit(kind, b.Owner, spawnCell);
        }
    }
}
