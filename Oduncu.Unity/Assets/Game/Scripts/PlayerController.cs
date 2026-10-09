using System;
using System.Collections.Generic;
using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>What the next tap on the battlefield means.</summary>
    public enum TapMode
    {
        Normal,
        PlaceBuilding,
        PlaceWall,
        AttackMove,
        Rally,
        Garrison,
    }

    /// <summary>
    /// Turns the local player's gestures and button presses into selection changes and
    /// Commands (design section 7 gesture table). Everything it changes in the game goes
    /// through SimRunner.Enqueue; it only reads the simulation.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        /// <summary>Screen distance, in dp, within which a tap hits a unit.</summary>
        private const float UnitTapDp = 30f;

        private SimRunner _runner;
        private CameraRig _rig;
        private PlacementPreview _preview;

        public Selection Selection { get; } = new Selection();
        public TapMode Mode { get; private set; }
        public EntityKind PlacingKind { get; private set; }

        /// <summary>Short feedback: a rejected command, or what the current mode expects.</summary>
        public event Action<string> Toast;

        /// <summary>Raised when the player's own building or unit loses hit points (for alerts).</summary>
        public event Action<Entity> OwnDamaged;

        private readonly List<int> _scratchIds = new List<int>(64);
        private readonly List<int> _builders = new List<int>(16);
        private readonly List<Cell> _wallCells = new List<Cell>(Simulation.MaxWallSegments);
        private readonly Dictionary<int, int> _lastHp = new Dictionary<int, int>();
        private Cell _placeOrigin;
        private bool _wallStarted;
        private Cell _wallFrom;
        private Cell _wallTo;
        private int _idleCursor;
        private int _townCenterCursor;

        public int LocalPlayer => _runner.LocalPlayer;
        public Simulation Sim => _runner.Sim;

        public void Init(SimRunner runner, CameraRig rig, PlacementPreview preview)
        {
            _runner = runner;
            _rig = rig;
            _preview = preview;
            _runner.MatchStarted += OnMatchStarted;
            _runner.Ticked += OnTicked;
        }

        private void OnDestroy()
        {
            if (_runner == null) return;
            _runner.MatchStarted -= OnMatchStarted;
            _runner.Ticked -= OnTicked;
        }

        private void OnMatchStarted(Simulation sim)
        {
            Selection.Clear();
            Selection.ClearGroups();
            CancelMode();
            _lastHp.Clear();
            sim.CommandRejected += OnRejected;
            Entity tc = FirstOwned(sim, LocalPlayer, EntityKind.TownCenter);
            if (tc != null) _rig.JumpTo(EntityPresenter.ToWorld(tc));
        }

        private void OnRejected(Command c, string why)
        {
            if (c.Player == LocalPlayer) Toast?.Invoke(Capitalise(why) + ".");
        }

        private void OnTicked(Simulation sim)
        {
            Selection.Prune(sim, LocalPlayer);
            if (Mode == TapMode.PlaceBuilding || Mode == TapMode.PlaceWall)
            {
                _builders.RemoveAll(id => !IsOwnBuilder(sim, id));
                if (_builders.Count == 0) CancelMode();
            }
            TrackDamage(sim);
        }

        private void TrackDamage(Simulation sim)
        {
            IReadOnlyList<Entity> entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Owner != LocalPlayer || e.IsResource) continue;
                int last;
                if (_lastHp.TryGetValue(e.Id, out last) && e.Hp < last && !e.UnderConstruction) OwnDamaged?.Invoke(e);
                _lastHp[e.Id] = e.Hp;
            }
        }

        private void Update()
        {
            if (!_runner.HasMatch) return;
            Simulation sim = Sim;
            if (Mode == TapMode.PlaceBuilding) _preview.ShowBuilding(sim, LocalPlayer, PlacingKind, _placeOrigin);
            else if (Mode == TapMode.PlaceWall && _wallStarted) _preview.ShowWall(sim, LocalPlayer, PlacingKind, _wallFrom, _wallTo, _wallCells);
            else if (Mode == TapMode.PlaceWall) _preview.Hide();
        }

        // ------------------------------------------------------------------ gestures

        public void Tap(Vector2 screen)
        {
            if (!_runner.HasMatch) return;
            Simulation sim = Sim;
            Vector3 ground = _rig.ScreenToGround(screen);
            Cell cell = ToCell(ground);
            Entity hit = Pick(screen);

            switch (Mode)
            {
                case TapMode.PlaceBuilding:
                {
                    EntityDef def = EntityDefs.Get(PlacingKind);
                    Cell origin = new Cell(cell.X - (def.Size - 1) / 2, cell.Y - (def.Size - 1) / 2);
                    // Tapping the footprint already shown places it; tapping elsewhere moves it.
                    if (Inside(cell, _placeOrigin, def.Size)) ConfirmPlacement();
                    else _placeOrigin = origin;
                    return;
                }
                case TapMode.PlaceWall:
                    if (!_wallStarted)
                    {
                        _wallStarted = true;
                        _wallFrom = _wallTo = cell;
                        Toast?.Invoke("Tap where the wall ends, then Confirm.");
                    }
                    else if (cell == _wallTo && _wallTo != _wallFrom)
                    {
                        ConfirmPlacement();
                    }
                    else
                    {
                        _wallTo = cell;
                    }
                    return;
                case TapMode.AttackMove:
                    Selection.OwnedUnits(sim, LocalPlayer, _scratchIds);
                    if (_scratchIds.Count > 0)
                    {
                        if (hit != null && IsEnemy(hit)) _runner.Enqueue(Command.Attack(LocalPlayer, _scratchIds.ToArray(), hit.Id));
                        else _runner.Enqueue(Command.AttackMove(LocalPlayer, _scratchIds.ToArray(), cell));
                    }
                    CancelMode();
                    return;
                case TapMode.Rally:
                {
                    Entity building = Selection.Primary(sim);
                    if (building != null && building.IsBuilding && building.Owner == LocalPlayer)
                    {
                        _runner.Enqueue(Command.SetRally(LocalPlayer, building.Id, cell, hit != null && hit.Id != building.Id ? hit.Id : 0));
                        Toast?.Invoke("Rally point set.");
                    }
                    CancelMode();
                    return;
                }
                case TapMode.Garrison:
                    Selection.OwnedUnits(sim, LocalPlayer, _scratchIds);
                    if (hit != null && hit.IsBuilding && hit.Owner == LocalPlayer && _scratchIds.Count > 0)
                    {
                        _runner.Enqueue(Command.Garrison(LocalPlayer, _scratchIds.ToArray(), hit.Id));
                    }
                    else
                    {
                        Toast?.Invoke("Tap one of your buildings to garrison.");
                    }
                    CancelMode();
                    return;
            }

            Selection.OwnedUnits(sim, LocalPlayer, _scratchIds);
            if (_scratchIds.Count == 0)
            {
                // Nothing of ours to command: the tap selects (enemies and resources for inspection).
                if (hit != null) Selection.Set(hit.Id);
                else Selection.Clear();
                return;
            }

            if (hit != null && hit.Owner == LocalPlayer && hit.IsUnit)
            {
                Selection.Set(hit.Id);
                return;
            }
            Command order = SmartCommand(sim, hit, cell);
            if (order != null) _runner.Enqueue(order);
            else if (hit != null) Selection.Set(hit.Id);
        }

        /// <summary>The design's context command for a tap with own units selected, or null to select instead.</summary>
        private Command SmartCommand(Simulation sim, Entity hit, Cell cell)
        {
            int[] units = _scratchIds.ToArray();
            if (hit == null) return Command.Move(LocalPlayer, units, cell);
            if (IsEnemy(hit)) return Command.Attack(LocalPlayer, units, hit.Id);

            bool villagers = AllVillagers(sim, units);
            if (hit.Owner == LocalPlayer && hit.IsBuilding)
            {
                if (!villagers) return null;
                if (hit.UnderConstruction || hit.Hp < hit.Stats.MaxHp) return Command.Repair(LocalPlayer, units, hit.Id);
                if (hit.Def.IsGatherable) return Command.Gather(LocalPlayer, units, hit.Id);
                return null;
            }
            if (hit.IsResource || hit.Def.IsAnimal)
            {
                if (villagers) return Command.Gather(LocalPlayer, units, hit.Id);
                if (hit.Def.IsAnimal && !hit.Def.HasTag(EntityTag.Herdable) && hit.State != UnitState.Carcass) return Command.Attack(LocalPlayer, units, hit.Id);
                return Command.Move(LocalPlayer, units, cell);
            }
            return Command.Move(LocalPlayer, units, cell);
        }

        /// <summary>Double-tap a unit or building: select every one of that kind on screen.</summary>
        public void DoubleTap(Vector2 screen)
        {
            if (!_runner.HasMatch || Mode != TapMode.Normal) return;
            Entity hit = Pick(screen);
            if (hit == null || hit.Owner != LocalPlayer) return;
            _scratchIds.Clear();
            IReadOnlyList<Entity> entities = Sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Owner != LocalPlayer || e.Kind != hit.Kind || e.State == UnitState.Garrisoned) continue;
                if (_rig.IsOnScreen(EntityPresenter.ToWorld(e))) _scratchIds.Add(e.Id);
            }
            Selection.Set(_scratchIds);
        }

        /// <summary>Long-press and drag: select own units inside a screen rectangle.</summary>
        public void BoxSelect(Rect screenRect)
        {
            if (!_runner.HasMatch) return;
            _scratchIds.Clear();
            IReadOnlyList<Entity> entities = Sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || !e.IsUnit || e.Owner != LocalPlayer || e.State == UnitState.Garrisoned) continue;
                Vector2 p = _rig.WorldToScreen(EntityPresenter.ToWorld(e) + Vector3.up * 0.5f);
                if (screenRect.Contains(p)) _scratchIds.Add(e.Id);
            }
            if (_scratchIds.Count > 0) Selection.Set(_scratchIds);
        }

        // ------------------------------------------------------------------ picking

        /// <summary>
        /// The visible entity under a screen point: the nearest unit body within a finger's
        /// width, else the building or resource whose footprint is under the point.
        /// </summary>
        public Entity Pick(Vector2 screen)
        {
            Simulation sim = Sim;
            int local = LocalPlayer;
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            float pixelsPerWorld = Screen.height / (2f * _rig.Camera.orthographicSize);
            float reach = Mathf.Max(UnitTapDp * dpi / 160f, 0.45f * PlaceholderArt.UnitScale * pixelsPerWorld);
            float best = reach * reach;
            Entity bestUnit = null;
            Entity structure = null;
            Vector3 raised = GroundAt(screen, 0.6f);
            IReadOnlyList<Entity> entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.State == UnitState.Garrisoned || !sim.CanSee(local, e)) continue;
                if (e.IsUnit)
                {
                    Vector2 p = _rig.WorldToScreen(EntityPresenter.ToWorld(e) + Vector3.up * 0.45f * PlaceholderArt.UnitScale);
                    float d = (p - screen).sqrMagnitude;
                    // Prefer own units over others at the same distance, so tapping into a melee selects yours.
                    if (e.Owner != local) d *= 1.2f;
                    if (d < best)
                    {
                        best = d;
                        bestUnit = e;
                    }
                }
                else if (structure == null)
                {
                    CellRect r = e.Footprint;
                    if (raised.x >= r.X - 0.1f && raised.x <= r.X + r.Size + 0.1f && raised.z >= r.Y - 0.1f && raised.z <= r.Y + r.Size + 0.1f) structure = e;
                }
            }
            return bestUnit ?? structure;
        }

        private Vector3 GroundAt(Vector2 screen, float height)
        {
            Ray ray = _rig.Camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            float d;
            return plane.Raycast(ray, out d) ? ray.GetPoint(d) : Vector3.zero;
        }

        // ------------------------------------------------------------------ modes

        public bool CanPlaceNow => Mode == TapMode.PlaceBuilding || (Mode == TapMode.PlaceWall && _wallStarted);

        public void BeginPlacement(EntityKind kind)
        {
            if (!CollectBuilders()) return;
            EntityDef def = EntityDefs.Get(kind);
            PlacingKind = kind;
            Vector3 centre = _rig.Focus;
            if (def.HasTag(EntityTag.Wall) && kind != EntityKind.Gate)
            {
                Mode = TapMode.PlaceWall;
                _wallStarted = false;
                Toast?.Invoke("Tap where the wall starts.");
                return;
            }
            Mode = TapMode.PlaceBuilding;
            Cell c = ToCell(centre);
            _placeOrigin = new Cell(c.X - (def.Size - 1) / 2, c.Y - (def.Size - 1) / 2);
            Toast?.Invoke("Tap to move the " + def.Name + ", tap it again or Confirm to build.");
        }

        public void BeginAttackMove()
        {
            Mode = TapMode.AttackMove;
            Toast?.Invoke("Tap where to attack-move.");
        }

        public void BeginRally()
        {
            Mode = TapMode.Rally;
            Toast?.Invoke("Tap the rally point.");
        }

        public void BeginGarrison()
        {
            Mode = TapMode.Garrison;
            Toast?.Invoke("Tap a building to garrison in.");
        }

        public void CancelMode()
        {
            Mode = TapMode.Normal;
            _wallStarted = false;
            if (_preview != null) _preview.Hide();
        }

        public void ConfirmPlacement()
        {
            if (!_runner.HasMatch) return;
            if (Mode == TapMode.PlaceBuilding)
            {
                _runner.Enqueue(Command.Build(LocalPlayer, _builders.ToArray(), PlacingKind, _placeOrigin));
            }
            else if (Mode == TapMode.PlaceWall && _wallStarted)
            {
                _runner.Enqueue(Command.BuildWall(LocalPlayer, _builders.ToArray(), PlacingKind, _wallFrom, _wallTo));
            }
            CancelMode();
        }

        private bool CollectBuilders()
        {
            _builders.Clear();
            IReadOnlyList<int> ids = Selection.Ids;
            for (int i = 0; i < ids.Count; i++)
            {
                if (IsOwnBuilder(Sim, ids[i])) _builders.Add(ids[i]);
            }
            return _builders.Count > 0;
        }

        private bool IsOwnBuilder(Simulation sim, int id)
        {
            Entity e = sim.Find(id);
            return e != null && e.Alive && e.Owner == LocalPlayer && e.Def.CanBuild && e.State != UnitState.Garrisoned;
        }

        // ------------------------------------------------------------------ buttons

        public void Stop()
        {
            Selection.OwnedUnits(Sim, LocalPlayer, _scratchIds);
            if (_scratchIds.Count > 0) _runner.Enqueue(Command.Stop(LocalPlayer, _scratchIds.ToArray()));
        }

        public void SetStance(Stance stance)
        {
            Selection.OwnedUnits(Sim, LocalPlayer, _scratchIds);
            if (_scratchIds.Count > 0) _runner.Enqueue(Command.SetStance(LocalPlayer, _scratchIds.ToArray(), stance));
        }

        /// <summary>Queue a unit at the selected building of that kind with the shortest queue.</summary>
        public void Train(EntityKind unit)
        {
            Entity b = BestSelectedBuilding();
            if (b != null) _runner.Enqueue(Command.Train(LocalPlayer, b.Id, unit));
        }

        public void Research(TechId tech)
        {
            Entity b = BestSelectedBuilding();
            if (b != null) _runner.Enqueue(Command.Research(LocalPlayer, b.Id, tech));
        }

        public void AgeUp()
        {
            Entity b = BestSelectedBuilding();
            if (b != null) _runner.Enqueue(Command.AgeUp(LocalPlayer, b.Id));
        }

        public void CancelQueueSlot(int slot)
        {
            Entity b = Selection.Primary(Sim);
            if (b != null && b.IsBuilding && b.Owner == LocalPlayer) _runner.Enqueue(Command.CancelTrain(LocalPlayer, b.Id, slot));
        }

        public void Ungarrison()
        {
            Entity b = Selection.Primary(Sim);
            if (b != null && b.IsBuilding && b.Owner == LocalPlayer) _runner.Enqueue(Command.Ungarrison(LocalPlayer, b.Id));
        }

        public void SetAutoQueue(bool on)
        {
            Entity b = Selection.Primary(Sim);
            if (b != null && b.IsBuilding && b.Owner == LocalPlayer) _runner.Enqueue(Command.SetAutoQueue(LocalPlayer, b.Id, on));
        }

        public void Market(ResourceKind kind, bool buy)
        {
            Entity b = Selection.Primary(Sim);
            if (b == null || b.Kind != EntityKind.Market || b.Owner != LocalPlayer) return;
            _runner.Enqueue(buy ? Command.MarketBuy(LocalPlayer, b.Id, kind) : Command.MarketSell(LocalPlayer, b.Id, kind));
        }

        public void SetEconomyTargets(EconomyTargets targets)
        {
            if (_runner.HasMatch) _runner.Enqueue(Command.SetEconomyTargets(LocalPlayer, targets));
        }

        private Entity BestSelectedBuilding()
        {
            Entity primary = Selection.Primary(Sim);
            if (primary == null || !primary.IsBuilding || primary.Owner != LocalPlayer) return null;
            Entity best = null;
            IReadOnlyList<int> ids = Selection.Ids;
            for (int i = 0; i < ids.Count; i++)
            {
                Entity e = Sim.Find(ids[i]);
                if (e == null || !e.Alive || e.Kind != primary.Kind || e.Owner != LocalPlayer || e.UnderConstruction) continue;
                if (best == null || e.TrainQueue.Count < best.TrainQueue.Count) best = e;
            }
            return best;
        }

        // ------------------------------------------------------------------ quick select

        public int IdleVillagerCount()
        {
            if (!_runner.HasMatch) return 0;
            int n = 0;
            IReadOnlyList<Entity> entities = Sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (e.Alive && e.Owner == LocalPlayer && e.Kind == EntityKind.Villager && e.State == UnitState.Idle) n++;
            }
            return n;
        }

        /// <summary>Select the next idle villager and centre the camera on it.</summary>
        public void SelectIdleVillager()
        {
            Entity found = NextOwned(ref _idleCursor, e => e.Kind == EntityKind.Villager && e.State == UnitState.Idle);
            if (found == null)
            {
                Toast?.Invoke("No idle villagers.");
                return;
            }
            Selection.Set(found.Id);
            _rig.JumpTo(EntityPresenter.ToWorld(found));
        }

        /// <summary>Select every military unit the player has on the map.</summary>
        public void SelectArmy()
        {
            _scratchIds.Clear();
            IReadOnlyList<Entity> entities = Sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (e.Alive && e.Owner == LocalPlayer && e.IsUnit && (e.Def.IsMilitary || e.Def.HasTag(EntityTag.Monk)) && e.State != UnitState.Garrisoned) _scratchIds.Add(e.Id);
            }
            if (_scratchIds.Count == 0)
            {
                Toast?.Invoke("No army yet.");
                return;
            }
            Selection.Set(_scratchIds);
        }

        public void SelectTownCenter()
        {
            Entity tc = NextOwned(ref _townCenterCursor, e => e.Kind == EntityKind.TownCenter);
            if (tc == null) return;
            Selection.Set(tc.Id);
            _rig.JumpTo(EntityPresenter.ToWorld(tc));
        }

        public void SelectGroup(int group)
        {
            if (!Selection.SelectGroup(group)) return;
            Entity first = Selection.Primary(Sim);
            if (first != null && !_rig.IsOnScreen(EntityPresenter.ToWorld(first))) _rig.JumpTo(EntityPresenter.ToWorld(first));
        }

        public void AssignGroup(int group)
        {
            Selection.AssignGroup(group);
            Toast?.Invoke("Group " + (group + 1) + " set.");
        }

        private Entity NextOwned(ref int cursor, Func<Entity, bool> match)
        {
            IReadOnlyList<Entity> entities = Sim.Entities;
            int n = entities.Count;
            for (int k = 1; k <= n; k++)
            {
                int i = (cursor + k) % n;
                Entity e = entities[i];
                if (e.Alive && e.Owner == LocalPlayer && match(e))
                {
                    cursor = i;
                    return e;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ helpers

        private bool IsEnemy(Entity e) => e.Owner >= 0 && e.Owner != LocalPlayer;

        private static bool AllVillagers(Simulation sim, int[] units)
        {
            for (int i = 0; i < units.Length; i++)
            {
                Entity e = sim.Find(units[i]);
                if (e == null || !e.Def.CanGather) return false;
            }
            return true;
        }

        private static Entity FirstOwned(Simulation sim, int player, EntityKind kind)
        {
            IReadOnlyList<Entity> entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (e.Alive && e.Kind == kind && e.Owner == player) return e;
            }
            return null;
        }

        private static bool Inside(Cell c, Cell origin, int size) => c.X >= origin.X && c.Y >= origin.Y && c.X < origin.X + size && c.Y < origin.Y + size;

        public static Cell ToCell(Vector3 world) => new Cell(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.z));

        private static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
