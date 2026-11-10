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

        private const float PingSeconds = 0.45f;
        private const float AttackPulseSeconds = 0.22f;
        private static readonly Color MovePing = new Color(0.4f, 1f, 0.5f, 0.7f);
        private static readonly Color AttackPing = new Color(1f, 0.35f, 0.3f, 0.7f);
        private static readonly Color WorkPing = new Color(1f, 0.9f, 0.4f, 0.7f);

        private struct Ping
        {
            public Transform Transform;
            public Renderer Renderer;
            public float Left;
        }

        private Camera _camera;
        private readonly List<Ping> _pings = new List<Ping>(8);
        private GameObject _flag;
        private bool _overlays = true;

        public void Init(SimRunner runner, Selection selection, Camera cam)
        {
            _runner = runner;
            _selection = selection;
            _camera = cam;
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
            _pings.Clear();
            _flag = null;
            _selectionVersion = -1;
        }

        /// <summary>Flash a shrinking disc on the ground where an order was given.</summary>
        public void ShowOrder(Vector3 world, CommandKind kind)
        {
            if (_root == null) return;
            Color color = kind == CommandKind.Attack || kind == CommandKind.AttackMove ? AttackPing
                : kind == CommandKind.Move || kind == CommandKind.SetRally ? MovePing : WorkPing;
            Material material = PlaceholderArt.UnlitMaterial(color);
            if (material == null) return;
            int slot = -1;
            for (int i = 0; i < _pings.Count; i++)
            {
                if (_pings[i].Left <= 0f) { slot = i; break; }
            }
            Ping ping;
            if (slot < 0)
            {
                GameObject go = PlaceholderArt.BuildPing(_root, color);
                if (go == null) return;
                ping = new Ping { Transform = go.transform, Renderer = go.GetComponent<Renderer>() };
                slot = _pings.Count;
                _pings.Add(ping);
            }
            ping = _pings[slot];
            ping.Left = PingSeconds;
            ping.Renderer.sharedMaterial = material;
            ping.Transform.position = new Vector3(world.x, 0.03f, world.z);
            ping.Transform.gameObject.SetActive(true);
            _pings[slot] = ping;
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
                    view.LastCooldown = e.Cooldown;
                    if (_selection.Contains(e.Id))
                    {
                        view.SelectionRing.SetActive(true);
                        view.Selected = true;
                        _ringed.Add(e.Id);
                    }
                }
                else
                {
                    view.PreviousPosition = view.CurrentPosition;
                    view.CurrentPosition = ToWorld(e);
                }
                view.State = e.State;
                view.Moving = (view.CurrentPosition - view.PreviousPosition).sqrMagnitude > 0.00001f;
                // The cooldown jumps up on the tick a unit or building strikes or shoots.
                if (e.Cooldown > view.LastCooldown && e.State != UnitState.Healing) view.AttackPulse = AttackPulseSeconds;
                view.LastCooldown = e.Cooldown;
                int maxHp = e.Stats.MaxHp;
                view.HpFraction = e.IsResource || maxHp <= 0 ? 1f : Mathf.Clamp01(e.Hp / (float)maxHp);
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
                    if (view.HealthBar != null) view.HealthBar.gameObject.SetActive(false);
                    if (view.Fire != null) view.Fire.SetActive(false);
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
            if (view.CarryPack != null)
            {
                bool loaded = e.Carry > 0;
                GameObject pack = view.CarryPack.gameObject;
                if (pack.activeSelf != loaded) pack.SetActive(loaded);
                if (loaded) view.CarryPack.sharedMaterial = PlaceholderArt.MaterialFor(PlaceholderArt.CarryColor(e.CarryKind));
            }
            if (e.IsBuilding)
            {
                // Buildings under half health burn until repaired.
                bool burning = !e.UnderConstruction && view.HpFraction < 0.5f && e.Kind != EntityKind.Farm;
                if (burning && view.Fire == null) view.Fire = PlaceholderArt.AddFire(view, e.Def.Size);
                if (view.Fire != null && view.Fire.activeSelf != burning) view.Fire.SetActive(burning);

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
                if (step.sqrMagnitude > 0.0001f && view.IsUnit)
                {
                    t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(step, Vector3.up), turn);
                }
                Animate(view);
                UpdateHealthBar(view);
            }

            UpdateDying();
            UpdatePings();
            UpdateSelectionRings();
            UpdateRallyFlag(_runner.Sim);
            UpdateProjectiles(_runner.Sim, a);
        }

        /// <summary>
        /// Placeholder animation from simulation state alone: walkers bob, workers lean into
        /// their swing, attackers lunge (buildings recoil), fires flicker.
        /// </summary>
        private static void Animate(EntityView view)
        {
            Transform model = view.Model;
            if (model == null) return;
            float time = Time.time;
            if (view.AttackPulse > 0f) view.AttackPulse -= Time.deltaTime;
            float pulse = view.AttackPulse > 0f ? Mathf.Sin(Mathf.PI * (1f - view.AttackPulse / AttackPulseSeconds)) : 0f;

            if (!view.IsUnit)
            {
                if (view.Fire != null && view.Fire.activeSelf)
                {
                    Transform fire = view.Fire.transform;
                    for (int i = 0; i < fire.childCount; i++)
                    {
                        Transform flame = fire.GetChild(i);
                        float k = 0.5f + 0.5f * Mathf.Sin(time * (7f + i) + view.Phase + i * 1.7f);
                        Vector3 s = flame.localScale;
                        // Even children are flames (stretch), odd are smoke (swell).
                        if ((i & 1) == 0) s.y = 0.35f + 0.35f * k;
                        else s.x = s.z = 0.28f + 0.14f * k;
                        flame.localScale = s;
                    }
                }
                return;
            }
            if (view.State == UnitState.Carcass) return;

            float y = 0f, z = 0f, lean = 0f;
            if (view.Rigid)
            {
                // Siege engines kick back when they fire.
                z = -0.12f * pulse;
            }
            else
            {
                if (view.Moving) y = Mathf.Abs(Mathf.Sin(time * 11f + view.Phase)) * 0.07f;
                bool working = view.State == UnitState.Gathering || view.State == UnitState.Building || view.State == UnitState.Repairing;
                if (working && !view.Moving) lean = 22f * Mathf.Max(0f, Mathf.Sin(time * 7f + view.Phase));
                z = 0.18f * pulse;
                lean += 14f * pulse;
            }
            model.localPosition = new Vector3(0f, y, z);
            model.localRotation = Quaternion.Euler(lean, 0f, 0f);
        }

        /// <summary>A bar over anything of a player's that is selected or hurt, turned to face the camera.</summary>
        private void UpdateHealthBar(EntityView view)
        {
            bool show = _overlays && view.Owner >= 0 && view.State != UnitState.Carcass
                && (view.HpFraction < 0.999f || (view.Selected && view.IsUnit));
            if (!show)
            {
                if (view.HealthBar != null && view.HealthBar.gameObject.activeSelf) view.HealthBar.gameObject.SetActive(false);
                return;
            }
            if (view.HealthBar == null && !PlaceholderArt.AddHealthBar(view))
            {
                _overlays = false;
                return;
            }
            if (!view.HealthBar.gameObject.activeSelf) view.HealthBar.gameObject.SetActive(true);
            if (_camera != null) view.HealthBar.rotation = _camera.transform.rotation;
            float f = Mathf.Max(0.02f, view.HpFraction);
            Vector3 s = view.HealthFill.localScale;
            if (Mathf.Abs(s.x - view.BarWidth * f) > 0.001f)
            {
                s.x = view.BarWidth * f;
                view.HealthFill.localScale = s;
                view.HealthFill.localPosition = new Vector3(-view.BarWidth * (1f - f) * 0.5f, 0f, -0.01f);
                Color c = f > 0.6f ? PlaceholderArt.HealthHigh : f > 0.3f ? PlaceholderArt.HealthMid : PlaceholderArt.HealthLow;
                Material m = PlaceholderArt.UnlitMaterial(c);
                if (m != null) view.HealthFillRenderer.sharedMaterial = m;
            }
        }

        private void UpdatePings()
        {
            for (int i = 0; i < _pings.Count; i++)
            {
                Ping ping = _pings[i];
                if (ping.Left <= 0f) continue;
                ping.Left -= Time.deltaTime;
                if (ping.Left <= 0f)
                {
                    ping.Transform.gameObject.SetActive(false);
                }
                else
                {
                    // Start wide and close in on the point.
                    float d = 0.3f + 1.3f * (ping.Left / PingSeconds);
                    ping.Transform.localScale = new Vector3(d, 0.01f, d);
                }
                _pings[i] = ping;
            }
        }

        /// <summary>A flag at the rally point of the selected building, while it is selected.</summary>
        private void UpdateRallyFlag(Simulation sim)
        {
            Entity b = _selection.Count == 1 ? _selection.Primary(sim) : null;
            bool show = b != null && b.IsBuilding && b.Owner == _runner.LocalPlayer && b.HasRally;
            if (!show)
            {
                if (_flag != null && _flag.activeSelf) _flag.SetActive(false);
                return;
            }
            if (_flag == null) _flag = PlaceholderArt.BuildFlag(_root, PlaceholderArt.PlayerColor(b.Owner));
            if (!_flag.activeSelf) _flag.SetActive(true);
            Vector3 at = new Vector3(b.RallyCell.X + 0.5f, 0f, b.RallyCell.Y + 0.5f);
            Entity target = b.RallyTargetId != 0 ? sim.Find(b.RallyTargetId) : null;
            if (target != null && target.Alive) at = ToWorld(target);
            _flag.transform.position = at;
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
                if (_views.TryGetValue(_ringed[i], out old))
                {
                    old.Selected = false;
                    if (old.SelectionRing != null) old.SelectionRing.SetActive(false);
                }
            }
            _ringed.Clear();
            IReadOnlyList<int> ids = _selection.Ids;
            for (int i = 0; i < ids.Count; i++)
            {
                EntityView view;
                if (!_views.TryGetValue(ids[i], out view) || view.SelectionRing == null) continue;
                view.SelectionRing.SetActive(true);
                view.Selected = true;
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
