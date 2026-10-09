namespace Oduncu.Sim
{
    public sealed partial class Simulation
    {
        private void UpdateBuilding(Entity b)
        {
            if (b.UnderConstruction || b.TrainQueue.Count == 0) return;

            EntityKind kind = b.TrainQueue[0].Unit;
            b.TrainProgress++;
            if (b.TrainProgress < StatsFor(b.Owner, kind).TrainTicks) return;

            Cell spawnCell;
            if (!FindSpawnCell(b.Footprint, out spawnCell)) return; // blocked in; keep waiting

            b.TrainProgress = 0;
            b.TrainQueue.RemoveAt(0);
            SpawnUnit(kind, b.Owner, spawnCell);
        }
    }
}
