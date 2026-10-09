using System.Collections.Generic;
using Oduncu.Sim;

namespace Oduncu.Game
{
    /// <summary>
    /// What the local player has selected, and the three control groups (design section 7).
    /// Holds entity ids only; anything that died or went inside is pruned after each tick.
    /// </summary>
    public sealed class Selection
    {
        public const int GroupCount = 3;

        private readonly List<int> _ids = new List<int>(64);
        private readonly List<int>[] _groups = { new List<int>(), new List<int>(), new List<int>() };

        public IReadOnlyList<int> Ids => _ids;
        public int Count => _ids.Count;

        /// <summary>Bumped on every change, so views redraw only when something changed.</summary>
        public int Version { get; private set; }

        public bool Contains(int id) => _ids.Contains(id);

        public void Clear()
        {
            if (_ids.Count == 0) return;
            _ids.Clear();
            Version++;
        }

        public void Set(int id)
        {
            _ids.Clear();
            if (id != 0) _ids.Add(id);
            Version++;
        }

        public void Set(List<int> ids)
        {
            _ids.Clear();
            _ids.AddRange(ids);
            Version++;
        }

        /// <summary>The first selected entity that still exists, or null.</summary>
        public Entity Primary(Simulation sim)
        {
            for (int i = 0; i < _ids.Count; i++)
            {
                Entity e = sim.Find(_ids[i]);
                if (e != null && e.Alive) return e;
            }
            return null;
        }

        /// <summary>Selected units of the player, for a command's unit list.</summary>
        public List<int> OwnedUnits(Simulation sim, int player, List<int> into)
        {
            into.Clear();
            for (int i = 0; i < _ids.Count; i++)
            {
                Entity e = sim.Find(_ids[i]);
                if (e != null && e.Alive && e.IsUnit && e.Owner == player) into.Add(e.Id);
            }
            return into;
        }

        /// <summary>Drop ids that died, went inside a building or were never seen again.</summary>
        public void Prune(Simulation sim, int player)
        {
            bool changed = false;
            for (int i = _ids.Count - 1; i >= 0; i--)
            {
                if (!Keep(sim, player, _ids[i]))
                {
                    _ids.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed) Version++;
            for (int g = 0; g < _groups.Length; g++)
            {
                List<int> group = _groups[g];
                for (int i = group.Count - 1; i >= 0; i--)
                {
                    Entity e = sim.Find(group[i]);
                    if (e == null || !e.Alive || e.Owner != player) group.RemoveAt(i);
                }
            }
        }

        private static bool Keep(Simulation sim, int player, int id)
        {
            Entity e = sim.Find(id);
            if (e == null || !e.Alive || e.State == UnitState.Garrisoned) return false;
            return sim.CanSee(player, e);
        }

        public void AssignGroup(int group)
        {
            _groups[group].Clear();
            _groups[group].AddRange(_ids);
        }

        public int GroupSize(int group) => _groups[group].Count;

        public bool SelectGroup(int group)
        {
            if (_groups[group].Count == 0) return false;
            Set(_groups[group]);
            return true;
        }

        public void ClearGroups()
        {
            for (int g = 0; g < _groups.Length; g++) _groups[g].Clear();
        }
    }
}
