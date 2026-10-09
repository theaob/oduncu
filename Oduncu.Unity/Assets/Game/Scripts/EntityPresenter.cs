using System.Collections.Generic;
using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// Milestone 0 presentation: one primitive per entity, coloured by owner, interpolated
    /// between simulation ticks. Replaced by real models and animation in milestone 1.
    /// </summary>
    [RequireComponent(typeof(SimRunner))]
    public sealed class EntityPresenter : MonoBehaviour
    {
        public static readonly Color[] PlayerColors = { new Color(0.2f, 0.4f, 1f), new Color(1f, 0.25f, 0.2f), Color.green, Color.yellow };
        public static readonly Color NeutralColor = new Color(0.1f, 0.5f, 0.15f);

        private SimRunner _runner;
        private readonly Dictionary<int, EntityView> _views = new Dictionary<int, EntityView>();
        private readonly List<int> _toRemove = new List<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private int _selectedId;

        public IReadOnlyDictionary<int, EntityView> Views => _views;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _runner.Ticked += OnTicked;
        }

        private void Start()
        {
            OnTicked(_runner.Sim);
            foreach (var view in _views.Values) view.PreviousPosition = view.CurrentPosition;
        }

        private void OnDestroy()
        {
            if (_runner != null) _runner.Ticked -= OnTicked;
        }

        public void SetSelected(int entityId)
        {
            if (_selectedId == entityId) return;
            EntityView old;
            if (_views.TryGetValue(_selectedId, out old)) old.transform.localScale = BaseScale(_runner.Sim.Find(_selectedId));
            _selectedId = entityId;
            EntityView now;
            if (_views.TryGetValue(_selectedId, out now)) now.transform.localScale = BaseScale(_runner.Sim.Find(_selectedId)) * 1.25f;
        }

        private void OnTicked(Simulation sim)
        {
            _seen.Clear();
            var entities = sim.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                // Garrisoned units are inside a building: their view goes away and comes back on eject.
                if (!e.Alive || e.State == UnitState.Garrisoned) continue;
                _seen.Add(e.Id);
                EntityView view;
                if (!_views.TryGetValue(e.Id, out view))
                {
                    view = CreateView(e);
                    _views.Add(e.Id, view);
                    view.PreviousPosition = view.CurrentPosition = ToWorld(e);
                }
                else
                {
                    view.PreviousPosition = view.CurrentPosition;
                    view.CurrentPosition = ToWorld(e);
                }
            }

            _toRemove.Clear();
            foreach (var kv in _views) if (!_seen.Contains(kv.Key)) _toRemove.Add(kv.Key);
            for (int i = 0; i < _toRemove.Count; i++)
            {
                Destroy(_views[_toRemove[i]].gameObject);
                _views.Remove(_toRemove[i]);
            }
        }

        private void LateUpdate()
        {
            float a = _runner.InterpolationAlpha;
            foreach (var view in _views.Values)
            {
                view.transform.position = Vector3.Lerp(view.PreviousPosition, view.CurrentPosition, a);
            }
        }

        public static Vector3 ToWorld(Entity e)
        {
            float height = e.IsUnit ? 0.5f : (e.IsResource ? 0.75f : 1f);
            return new Vector3(e.Position.X.AsFloat, height, e.Position.Y.AsFloat);
        }

        private static Vector3 BaseScale(Entity e)
        {
            if (e == null) return Vector3.one;
            if (e.IsUnit) return e.Kind == EntityKind.Militia ? new Vector3(0.5f, 0.5f, 0.5f) : new Vector3(0.4f, 0.4f, 0.4f);
            if (e.IsResource) return new Vector3(0.7f, 1.5f, 0.7f);
            return new Vector3(e.Def.Size * 0.95f, 2f, e.Def.Size * 0.95f);
        }

        private EntityView CreateView(Entity e)
        {
            PrimitiveType type = e.IsUnit ? PrimitiveType.Capsule : (e.IsResource ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = e.Def.Name + " #" + e.Id;
            go.transform.localScale = BaseScale(e);
            var renderer = go.GetComponent<Renderer>();
            renderer.material.color = e.Owner >= 0 ? PlayerColors[e.Owner % PlayerColors.Length] : NeutralColor;
            var view = go.AddComponent<EntityView>();
            view.EntityId = e.Id;
            return view;
        }
    }
}
