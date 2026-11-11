using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Oduncu.Game
{
    /// <summary>
    /// The battlefield gestures from design section 7, on the Input System's EnhancedTouch
    /// (plan section 4): tap, double-tap, one-finger drag pans, long-press then drag
    /// box-selects, two-finger drag pans, pinch snaps between the three zoom levels.
    /// Touches that start on the HUD belong to the HUD. In the editor and on desktop the
    /// mouse stands in for one finger, and the scroll wheel zooms.
    /// </summary>
    public sealed class GestureInput : MonoBehaviour
    {
        /// <summary>Movement, in dp, below which a press is still a tap.</summary>
        public float TapSlopDp = 10f;
        public float LongPressSeconds = 0.4f;
        public float DoubleTapSeconds = 0.3f;
        /// <summary>Pinch ratio that steps one zoom level.</summary>
        public float PinchStep = 1.3f;

        private enum Gesture
        {
            None,
            Pending,
            Pan,
            Box,
            Multi,
        }

        private struct Track
        {
            public Vector2 Start;
            public Vector2 Last;
            public float StartTime;
            public bool OverUi;
        }

        private CameraRig _rig;
        private PlayerController _player;
        private Func<Vector2, bool> _isOverUi;
        private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
        private readonly List<Touch> _active = new List<Touch>(4);
        private Gesture _gesture;
        private int _primaryId = -1;
        private Vector2 _multiCentre;
        private float _pinchStart;
        private float _lastTapTime = -10f;
        private Vector2 _lastTapPos;
        private float _scrollCooldown;

        /// <summary>The box-select rectangle in screen pixels while dragging, or null.</summary>
        public Rect? Box { get; private set; }

        /// <summary>Whether a press is being held long enough to start a box select.</summary>
        public bool LongPressArmed { get; private set; }

        public void Init(CameraRig rig, PlayerController player, Func<Vector2, bool> isOverUi)
        {
            _rig = rig;
            _player = player;
            _isOverUi = isOverUi;
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            if (!Application.isMobilePlatform) TouchSimulation.Enable();
        }

        private void OnDisable()
        {
            if (!Application.isMobilePlatform) TouchSimulation.Disable();
            EnhancedTouchSupport.Disable();
            Cancel();
        }

        /// <summary>Drop any gesture in progress (menus opening, match ending).</summary>
        public void Cancel()
        {
            _gesture = Gesture.None;
            _primaryId = -1;
            Box = null;
            LongPressArmed = false;
            _tracks.Clear();
        }

        private float Slop => TapSlopDp * (Screen.dpi > 0f ? Screen.dpi : 160f) / 160f;

        private void Update()
        {
            if (_rig == null || _rig.Camera == null) return;
            float now = Time.unscaledTime;

            _active.Clear();
            foreach (Touch t in Touch.activeTouches)
            {
                if (t.phase == TouchPhase.Began || !_tracks.ContainsKey(t.touchId))
                {
                    _tracks[t.touchId] = new Track { Start = t.screenPosition, Last = t.screenPosition, StartTime = now, OverUi = _isOverUi(t.screenPosition) };
                }
                if (!_tracks[t.touchId].OverUi) _active.Add(t);
            }

            int down = 0;
            for (int i = 0; i < _active.Count; i++)
            {
                if (!IsEnd(_active[i].phase)) down++;
            }

            if (down >= 2 || (_gesture == Gesture.Multi && down > 0))
            {
                UpdateMulti();
            }
            else if (_gesture != Gesture.Multi)
            {
                UpdateSingle(now);
            }
            else if (down == 0)
            {
                _gesture = Gesture.None;
                _primaryId = -1;
            }

            for (int i = 0; i < _active.Count; i++)
            {
                Touch t = _active[i];
                Track tr = _tracks[t.touchId];
                tr.Last = t.screenPosition;
                _tracks[t.touchId] = tr;
            }
            foreach (Touch t in Touch.activeTouches)
            {
                if (IsEnd(t.phase)) _tracks.Remove(t.touchId);
            }

            UpdateScrollZoom();
        }

        private static bool IsEnd(TouchPhase phase) => phase == TouchPhase.Ended || phase == TouchPhase.Canceled;

        private void UpdateSingle(float now)
        {
            if (_active.Count == 0) return;
            Touch t = _active[0];
            if (_gesture == Gesture.None)
            {
                if (IsEnd(t.phase) && t.phase == TouchPhase.Canceled) return;
                _gesture = Gesture.Pending;
                _primaryId = t.touchId;
            }
            if (t.touchId != _primaryId) return;

            Track tr = _tracks[t.touchId];
            Vector2 pos = t.screenPosition;
            bool held = now - tr.StartTime >= LongPressSeconds;

            switch (_gesture)
            {
                case Gesture.Pending:
                    LongPressArmed = held;
                    if ((pos - tr.Start).magnitude > Slop)
                    {
                        _gesture = held ? Gesture.Box : Gesture.Pan;
                        LongPressArmed = false;
                        if (_gesture == Gesture.Pan) PanBetween(tr.Last, pos);
                    }
                    break;
                case Gesture.Pan:
                    PanBetween(tr.Last, pos);
                    break;
                case Gesture.Box:
                    Box = RectBetween(tr.Start, pos);
                    break;
            }

            if (!IsEnd(t.phase)) return;

            if (_gesture == Gesture.Pending && t.phase == TouchPhase.Ended)
            {
                bool isDouble = now - _lastTapTime <= DoubleTapSeconds && (pos - _lastTapPos).magnitude <= Slop * 3f;
                if (isDouble)
                {
                    _player.DoubleTap(pos);
                    _lastTapTime = -10f;
                }
                else
                {
                    _player.Tap(pos);
                    _lastTapTime = now;
                    _lastTapPos = pos;
                }
            }
            else if (_gesture == Gesture.Box && Box.HasValue)
            {
                _player.BoxSelect(Box.Value);
            }
            _gesture = Gesture.None;
            _primaryId = -1;
            Box = null;
            LongPressArmed = false;
        }

        private void UpdateMulti()
        {
            Vector2 centre = Vector2.zero;
            int n = 0;
            Vector2 a = Vector2.zero, b = Vector2.zero;
            for (int i = 0; i < _active.Count; i++)
            {
                if (IsEnd(_active[i].phase)) continue;
                Vector2 p = _active[i].screenPosition;
                if (n == 0) a = p;
                else if (n == 1) b = p;
                centre += p;
                n++;
            }
            if (n == 0) return;
            centre /= n;
            float spread = n >= 2 ? (a - b).magnitude : 0f;

            if (_gesture != Gesture.Multi)
            {
                // A second finger turns whatever the first was doing into a camera gesture.
                _gesture = Gesture.Multi;
                Box = null;
                LongPressArmed = false;
                _multiCentre = centre;
                _pinchStart = spread;
                return;
            }

            PanBetween(_multiCentre, centre);
            _multiCentre = centre;

            if (n >= 2 && _pinchStart > 1f)
            {
                float ratio = spread / _pinchStart;
                if (ratio >= PinchStep)
                {
                    _rig.ZoomIn();
                    _pinchStart = spread;
                }
                else if (ratio <= 1f / PinchStep)
                {
                    _rig.ZoomOut();
                    _pinchStart = spread;
                }
            }
            else if (n >= 2)
            {
                _pinchStart = spread;
            }
        }

        private void PanBetween(Vector2 from, Vector2 to)
        {
            if (from == to) return;
            Vector3 a = _rig.ScreenToGround(from);
            Vector3 b = _rig.ScreenToGround(to);
            _rig.Pan(a - b);
        }

        private void UpdateScrollZoom()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            _scrollCooldown -= Time.unscaledDeltaTime;
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f || _scrollCooldown > 0f) return;
            if (_isOverUi(mouse.position.ReadValue())) return;
            if (scroll > 0f) _rig.ZoomIn();
            else _rig.ZoomOut();
            _scrollCooldown = 0.2f;
        }

        private static Rect RectBetween(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
    }
}
