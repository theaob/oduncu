using System.Collections.Generic;
using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// Milestone 0 input: tap to select one of your units or buildings, tap again to
    /// command it (move, gather, attack, build). One-finger drag pans the camera.
    /// Uses the legacy Input API because it needs no project settings; the Input System
    /// package replaces it in milestone 1 together with the real command card.
    /// </summary>
    [RequireComponent(typeof(SimRunner), typeof(EntityPresenter))]
    public sealed class TouchInput : MonoBehaviour
    {
        public Camera Camera;
        public float TapMaxPixels = 24f;
        public float TapMaxSeconds = 0.35f;
        public float PanSpeed = 1f;

        private SimRunner _runner;
        private EntityPresenter _presenter;
        private int _selectedId;
        private bool _placingBarracks;
        private bool _pressed;
        private Vector2 _pressStart;
        private Vector3 _lastGround;
        private float _pressTime;
        private string _status = "Tap a villager, then tap a tree.";

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _presenter = GetComponent<EntityPresenter>();
            if (Camera == null) Camera = Camera.main;
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _pressed = true;
                _pressStart = Input.mousePosition;
                _pressTime = Time.unscaledTime;
                _lastGround = GroundPoint(Input.mousePosition);
            }
            else if (_pressed && Input.GetMouseButton(0))
            {
                if (((Vector2)Input.mousePosition - _pressStart).magnitude > TapMaxPixels)
                {
                    Vector3 now = GroundPoint(Input.mousePosition);
                    Vector3 delta = _lastGround - now;
                    delta.y = 0f;
                    Camera.transform.position += delta * PanSpeed;
                    _lastGround = GroundPoint(Input.mousePosition);
                }
            }
            else if (_pressed && Input.GetMouseButtonUp(0))
            {
                _pressed = false;
                bool isTap = ((Vector2)Input.mousePosition - _pressStart).magnitude <= TapMaxPixels
                             && Time.unscaledTime - _pressTime <= TapMaxSeconds;
                if (isTap && !PointerOverGui(Input.mousePosition)) HandleTap(Input.mousePosition);
            }
        }

        private static bool PointerOverGui(Vector2 screen)
        {
            // IMGUI buttons live in the bottom 70 px; ignore taps there.
            return screen.y < 70f;
        }

        private Vector3 GroundPoint(Vector2 screen)
        {
            Ray ray = Camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, Vector3.zero);
            float d;
            return plane.Raycast(ray, out d) ? ray.GetPoint(d) : Vector3.zero;
        }

        private void HandleTap(Vector2 screen)
        {
            Simulation sim = _runner.Sim;
            int player = _runner.LocalPlayer;
            Entity hit = EntityUnder(screen);
            Entity selected = sim.Find(_selectedId);

            if (_placingBarracks)
            {
                _placingBarracks = false;
                if (selected != null && selected.Def.CanBuild)
                {
                    Vector3 g = GroundPoint(screen);
                    var origin = new Cell(Mathf.FloorToInt(g.x) - 1, Mathf.FloorToInt(g.z) - 1);
                    _runner.Enqueue(Command.Build(player, new[] { selected.Id }, EntityKind.Barracks, origin));
                    _status = "Building barracks.";
                }
                return;
            }

            if (hit != null && hit.Owner == player)
            {
                Select(hit.Id);
                _status = "Selected " + hit.Def.Name + ".";
                return;
            }

            if (selected == null || !selected.IsUnit)
            {
                if (hit == null) Select(0);
                return;
            }

            var units = new[] { selected.Id };
            if (hit == null)
            {
                Vector3 g = GroundPoint(screen);
                _runner.Enqueue(Command.Move(player, units, new Cell(Mathf.FloorToInt(g.x), Mathf.FloorToInt(g.z))));
                _status = "Moving.";
            }
            else if (hit.IsResource && selected.Def.CanGather)
            {
                _runner.Enqueue(Command.Gather(player, units, hit.Id));
                _status = "Gathering wood.";
            }
            else if (hit.Owner >= 0 && hit.Owner != player)
            {
                _runner.Enqueue(Command.Attack(player, units, hit.Id));
                _status = "Attacking " + hit.Def.Name + ".";
            }
        }

        private Entity EntityUnder(Vector2 screen)
        {
            RaycastHit hitInfo;
            if (!Physics.Raycast(Camera.ScreenPointToRay(screen), out hitInfo, 500f)) return null;
            var view = hitInfo.collider.GetComponentInParent<EntityView>();
            return view == null ? null : _runner.Sim.Find(view.EntityId);
        }

        private void Select(int id)
        {
            _selectedId = id;
            _presenter.SetSelected(id);
        }

        private void OnGUI()
        {
            Simulation sim = _runner.Sim;
            int player = _runner.LocalPlayer;
            GUI.skin.button.fontSize = 28;
            GUI.skin.label.fontSize = 28;
            GUI.Label(new Rect(16, 8, 900, 40), "Wood " + sim.Players[player].Wood + "   Pop " + sim.CountUnits(player) + "/" + SimConstants.PopulationCap + "   Tick " + sim.CurrentTick);
            GUI.Label(new Rect(16, 48, 1200, 40), _status + (sim.LastRejection != null ? "   (last rejection: " + sim.LastRejection + ")" : ""));

            Entity selected = sim.Find(_selectedId);
            float y = Screen.height - 64f;
            float x = 16f;
            if (selected != null && selected.IsBuilding && !selected.UnderConstruction)
            {
                foreach (EntityKind kind in selected.Def.Trains)
                {
                    if (GUI.Button(new Rect(x, y, 320, 56), "Train " + EntityDefs.Get(kind).Name + " (" + EntityDefs.Get(kind).CostWood + ")"))
                    {
                        _runner.Enqueue(Command.Train(player, selected.Id, kind));
                    }
                    x += 336f;
                }
                GUI.Label(new Rect(x, y, 400, 56), "Queue " + selected.TrainQueue.Count);
            }
            else if (selected != null && selected.Def.CanBuild)
            {
                if (GUI.Button(new Rect(x, y, 360, 56), _placingBarracks ? "Tap ground to place" : "Build Barracks (175)"))
                {
                    _placingBarracks = !_placingBarracks;
                }
                x += 376f;
                if (GUI.Button(new Rect(x, y, 200, 56), "Stop"))
                {
                    _runner.Enqueue(Command.Stop(player, new[] { selected.Id }));
                }
            }
        }
    }
}
