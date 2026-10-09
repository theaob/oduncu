using System;
using Oduncu.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oduncu.Game
{
    /// <summary>
    /// Builds the game scene at runtime and runs the match flow (plan section 4): main menu,
    /// "Skirmish vs Standard AI" on the Open map, pause, victory and defeat. The scene only
    /// needs this component; everything else is created here so no hand-made scene or prefab
    /// has to live in the repository.
    /// </summary>
    [RequireComponent(typeof(SimRunner))]
    public sealed class GameRoot : MonoBehaviour
    {
        private static readonly Color Sky = new Color(0.16f, 0.2f, 0.14f);
        private static readonly Color GroundColor = new Color(0.42f, 0.58f, 0.32f);

        private SimRunner _runner;
        private CameraRig _rig;
        private EntityPresenter _presenter;
        private PlayerController _player;
        private GestureInput _gestures;
        private Hud _hud;
        private bool _playing;
        private bool _ended;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            _runner = GetComponent<SimRunner>();

            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Sky;
            _rig = cam.gameObject.AddComponent<CameraRig>();
            _rig.Init(cam, OpenMap.Size);

            if (FindFirstObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
                sun.intensity = 1.1f;
            }

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(transform, false);
            ground.transform.position = new Vector3(OpenMap.Size / 2f, 0f, OpenMap.Size / 2f);
            ground.transform.localScale = new Vector3(OpenMap.Size / 10f, 1f, OpenMap.Size / 10f);
            ground.GetComponent<Renderer>().sharedMaterial = PlaceholderArt.MaterialFor(GroundColor);

            var preview = new GameObject("Placement").AddComponent<PlacementPreview>();
            preview.transform.SetParent(transform, false);

            _presenter = gameObject.AddComponent<EntityPresenter>();
            _player = gameObject.AddComponent<PlayerController>();
            _player.Init(_runner, _rig, preview);
            _presenter.Init(_runner, _player.Selection);
            gameObject.AddComponent<FogOverlay>().Init(_runner, cam);

            _gestures = gameObject.AddComponent<GestureInput>();
            _hud = gameObject.AddComponent<Hud>();
            _hud.Init(this, _runner, _player, _rig, _gestures);
            _gestures.Init(_rig, _player, _hud.IsOverUi);
            _gestures.enabled = false;
        }

        private void Update()
        {
            // Escape on desktop, the back button on Android.
            Keyboard keyboard = Keyboard.current;
            if (_playing && !_ended && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_runner.Paused) Resume();
                else Pause();
            }
            if (!_playing || _ended || !_runner.HasMatch || !_runner.Sim.MatchOver) return;
            _ended = true;
            _gestures.enabled = false;
            _player.CancelMode();
            Simulation sim = _runner.Sim;
            _hud.ShowEnd(sim.Winner == _runner.LocalPlayer, sim.MatchEndTick);
        }

        private void OnApplicationPause(bool paused)
        {
            // Backgrounding the app pauses a single player match.
            if (paused && _playing && !_ended && !_runner.Paused) Pause();
        }

        public void StartSkirmish()
        {
            StartMatch(Environment.TickCount & 0x7fffffff);
        }

        public void Restart()
        {
            StartMatch(_runner.Seed);
        }

        private void StartMatch(int seed)
        {
            _runner.StartMatch(seed);
            _playing = true;
            _ended = false;
            _hud.ShowMatch();
            _gestures.enabled = true;
        }

        public void Pause()
        {
            if (!_playing) return;
            _runner.Paused = true;
            _gestures.enabled = false;
            _player.CancelMode();
            _hud.ShowPause();
        }

        public void Resume()
        {
            _runner.Paused = false;
            _gestures.enabled = !_ended;
            _hud.ShowMatch();
        }

        public void QuitToMenu()
        {
            _playing = false;
            _ended = false;
            _gestures.enabled = false;
            _player.CancelMode();
            _player.Selection.Clear();
            _runner.EndMatch();
            _presenter.Clear();
            _hud.ShowMenu();
        }
    }
}
