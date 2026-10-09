using System.Collections.Generic;
using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// Draws the simulation: one placeholder view per entity the local player may see,
    /// interpolated between ticks, plus selection rings, construction progress, projectiles
    /// and a short death effect. Enemy units in the fog are hidden; buildings and resources
    /// stay once seen, as the simulation's CanSee says.
    /// </summary>
    public sealed class EntityPresenter : MonoBehaviour
    {
        private const float DeathSeconds = 0.7f;
        private const float TurnSpeed = 10f;
        private const float ProjectileArc = 0.6f;

        private SimRunner _runner;
        private Selection _selection;
        private Transform _root;
        private readonly Dictionary<int, EntityView> _views = new Dictionary<int, EntityView>();
        private readonly List<int> _toRemove = new List<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<EntityView> _dying = new List<EntityView>();
        private readonly List<int> _ringed = new List<int>();
        private readonly List<Transform> _projectiles = new List<Transform>();
        private int _selectionVersion = -1;

        public void Init(SimRunner runner, Selection selection)
        {
            _runner = runner;
            _selection = selection;
            _runner.Ticked += OnTicked;
            _runner.MatchStarted += OnMatchStarted;
        }

        private void OnDestroy()
        {
            if (_runner == null) return;
            _runner.Ticked -= OnTicked;
            _runner.MatchStarted -= OnMatchStarted;
        }

        public bool TryGetView(int id, out EntityView view) => _views.TryGetValue(id, out view);

        public void Clear()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _views.Clear();
            _dying.Clear();
            _ringed.Clear();
            _projectiles.Clear();
            _selectionVersion = -1;
        }

        private void OnMatchStarted(Simulation sim)
        {
            Clear();
            _root = new GameObject("Entities").transform;
            _root.SetParent(transform, false);
            OnTicked(sim);
            foreach (EntityView view in _views.Values) view.PreviousPosition = view.CurrentPosition;
        }

        private void OnTicked(Simulation sim)
        {
            int local = _runner.LocalPlayer;
            _seen.Clear();
            IReadOnlyList<Entity> entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                // Garrisoned units are inside a building: their view goes away and comes back on eject.
                if (!e.Alive || e.State == UnitState.Garrisoned) continue;
                if (!sim.CanSee(local, e)) continue;
                _seen.Add(e.Id);
                EntityView view;
                if (!_views.TryGetValue(e.Id, out view))
                {
                    view = PlaceholderArt.Build(e, _root);
                    view.EntityId = e.Id;
                    _views.Add(e.Id, view);
                    view.PreviousPosition = view.CurrentPosition = ToWorld(e);
                    view.transform.position = view.CurrentPosition;
                    if (_selection.Contains(e.Id))
                    {
                        view.SelectionRing.SetActive(true);
                        _ringed.Add(e.Id);
                    }
                }
                else
                {
                    view.PreviousPosition = view.CurrentPosition;
                    view.CurrentPosition = ToWorld(e);
                }
                UpdateShape(e, view);
            }

            _toRemove.Clear();
            foreach (KeyValuePair<int, EntityView> kv in _views)
            {
                if (!_seen.Contains(kv.Key)) _toRemove.Add(kv.Key);
            }
            for (int i = 0; i < _toRemove.Count; i++)
            {
                int id = _toRemove[i];
                EntityView view = _views[id];
                _views.Remove(id);
                Entity e = sim.Find(id);
                bool died = (e == null || !e.Alive) && !EntityDefs.Get(view.Kind).IsResource;
                if (died)
                {
                    view.Dying = DeathSeconds;
                    if (view.SelectionRing != null) view.SelectionRing.SetActive(false);
                    _dying.Add(view);
                }
                else
                {
                    Destroy(view.gameObject);
                }
            }
        }

        private static void UpdateShape(Entity e, EntityView view)
        {
            if (view.Model == null) return;
            if (e.IsBuilding)
            {
                int buildTicks = e.Stats.BuildTicks;
                float progress = e.UnderConstruction && buildTicks > 0 ? Mathf.Clamp01(e.BuildProgress / (float)buildTicks) : 1f;
                Vector3 s = view.Model.localScale;
                s.y = e.UnderConstruction ? 0.15f + 0.85f * progress : 1f;
                view.Model.localScale = s;
            }
            else if (e.State == UnitState.Carcass)
            {
                view.Model.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        private void LateUpdate()
        {
            if (_runner == null || !_runner.HasMatch) return;
            float a = _runner.InterpolationAlpha;
            float turn = 1f - Mathf.Exp(-TurnSpeed * Time.deltaTime);
            foreach (EntityView view in _views.Values)
            {
                Vector3 p = Vector3.Lerp(view.PreviousPosition, view.CurrentPosition, a);
                Vector3 step = view.CurrentPosition - view.PreviousPosition;
                Transform t = view.transform;
                t.position = p;
                if (step.sqrMagnitude > 0.0001f && EntityDefs.Get(view.Kind).IsUnit)
                {
                    t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(step, Vector3.up), turn);
                }
            }

            UpdateDying();
            UpdateSelectionRings();
            UpdateProjectiles(_runner.Sim, a);
        }

        private void UpdateDying()
        {
            for (int i = _dying.Count - 1; i >= 0; i--)
            {
                EntityView view = _dying[i];
                view.Dying -= Time.deltaTime;
                if (view.Dying <= 0f || view.Model == null)
                {
                    Destroy(view.gameObject);
                    _dying.RemoveAt(i);
                    continue;
                }
                float k = view.Dying / DeathSeconds;
                // Units topple and sink; buildings collapse into the ground.
                if (EntityDefs.Get(view.Kind).IsUnit) view.Model.localRotation = Quaternion.Euler(0f, 0f, 90f * (1f - k));
                Vector3 s = view.Model.localScale;
                s.y = Mathf.Max(0.01f, s.y * (0.9f + 0.1f * k));
                view.Model.localScale = s;
                view.transform.position = view.CurrentPosition + Vector3.down * (1f - k) * 0.5f;
            }
        }

        private void UpdateSelectionRings()
        {
            if (_selection.Version == _selectionVersion) return;
            _selectionVersion = _selection.Version;
            for (int i = 0; i < _ringed.Count; i++)
            {
                EntityView old;
                if (_views.TryGetValue(_ringed[i], out old) && old.SelectionRing != null) old.SelectionRing.SetActive(false);
            }
            _ringed.Clear();
            IReadOnlyList<int> ids = _selection.Ids;
            for (int i = 0; i < ids.Count; i++)
            {
                EntityView view;
                if (!_views.TryGetValue(ids[i], out view) || view.SelectionRing == null) continue;
                view.SelectionRing.SetActive(true);
                _ringed.Add(ids[i]);
            }
        }

        private void UpdateProjectiles(Simulation sim, float alpha)
        {
            IReadOnlyList<Projectile> shots = sim.Projectiles;
            int local = _runner.LocalPlayer;
            int used = 0;
            float now = sim.CurrentTick + alpha;
            for (int i = 0; i < shots.Count; i++)
            {
                Projectile p = shots[i];
                Cell from = p.From.ToCell();
                Cell to = p.Impact.ToCell();
                if (!sim.Fog.IsVisible(local, from) && !sim.Fog.IsVisible(local, to)) continue;
                int flight = Mathf.Max(1, p.LandTick - p.FireTick);
                float t = Mathf.Clamp01((now - p.FireTick) / flight);
                var a = new Vector3(p.From.X.AsFloat, 0.8f, p.From.Y.AsFloat);
                var b = new Vector3(p.Impact.X.AsFloat, 0.4f, p.Impact.Y.AsFloat);
                Vector3 pos = Vector3.Lerp(a, b, t) + Vector3.up * (ProjectileArc * 4f * t * (1f - t) * Mathf.Max(1f, Vector3.Distance(a, b) * 0.25f));
                Transform shot = ProjectileView(used++, p.Splash > FP.Zero);
                shot.position = pos;
                if (b != a) shot.rotation = Quaternion.LookRotation(b - a, Vector3.up);
            }
            for (int i = used; i < _projectiles.Count; i++)
            {
                if (_projectiles[i].gameObject.activeSelf) _projectiles[i].gameObject.SetActive(false);
            }
        }

        private Transform ProjectileView(int index, bool heavy)
        {
            while (_projectiles.Count <= index)
            {
                GameObject go = PlaceholderArt.AddPrimitive(PrimitiveType.Cube, _root, Vector3.zero, new Vector3(0.05f, 0.05f, 0.4f),
                    Quaternion.identity, new Color(0.2f, 0.15f, 0.1f));
                go.name = "Projectile";
                _projectiles.Add(go.transform);
            }
            Transform t = _projectiles[index];
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t.localScale = heavy ? new Vector3(0.3f, 0.3f, 0.3f) : new Vector3(0.05f, 0.05f, 0.4f);
            return t;
        }

        public static Vector3 ToWorld(Entity e)
        {
            return new Vector3(e.Position.X.AsFloat, 0f, e.Position.Y.AsFloat);
        }
    }
}
