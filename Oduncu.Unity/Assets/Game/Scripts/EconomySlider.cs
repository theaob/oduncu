using System;
using Oduncu.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Oduncu.Game
{
    /// <summary>
    /// The economy planner's four-segment slider (design section 4.3): Food, Wood, Gold and
    /// Stone shares side by side, adding up to 100. Dragging near a boundary moves it in steps
    /// of 5; letting go reports the new targets.
    /// </summary>
    public sealed class EconomySlider : VisualElement
    {
        public const int Step = 5;

        public static readonly Color[] SegmentColors =
        {
            new Color(0.85f, 0.3f, 0.3f),
            new Color(0.55f, 0.38f, 0.2f),
            new Color(0.95f, 0.78f, 0.2f),
            new Color(0.6f, 0.6f, 0.62f),
        };

        private static readonly string[] Names = { "Food", "Wood", "Gold", "Stone" };

        private readonly int[] _values = { 25, 25, 25, 25 };
        private readonly VisualElement[] _segments = new VisualElement[4];
        private readonly Label[] _labels = new Label[4];
        private int _dragging = -1;

        /// <summary>Raised when the player lets go after changing the split.</summary>
        public event Action<EconomyTargets> Changed;

        public EconomySlider()
        {
            style.flexDirection = FlexDirection.Row;
            style.height = 56;
            style.minWidth = 280;
            for (int i = 0; i < 4; i++)
            {
                var seg = new VisualElement { pickingMode = PickingMode.Ignore };
                seg.style.backgroundColor = SegmentColors[i];
                seg.style.justifyContent = Justify.Center;
                seg.style.alignItems = Align.Center;
                seg.style.overflow = Overflow.Hidden;
                seg.style.borderRightWidth = i < 3 ? 3 : 0;
                seg.style.borderRightColor = Color.white;
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.style.color = Color.black;
                label.style.fontSize = 13;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                seg.Add(label);
                _segments[i] = seg;
                _labels[i] = label;
                Add(seg);
            }
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            Refresh();
        }

        public EconomyTargets Value => new EconomyTargets(_values[0], _values[1], _values[2], _values[3]);

        public void SetValue(EconomyTargets t)
        {
            if (_dragging >= 0) return;
            if (t.IsOff) t = new EconomyTargets(25, 25, 25, 25);
            _values[0] = t.Food;
            _values[1] = t.Wood;
            _values[2] = t.Gold;
            _values[3] = t.Stone;
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < 4; i++)
            {
                _segments[i].style.flexGrow = 0;
                _segments[i].style.flexShrink = 0;
                _segments[i].style.width = new Length(_values[i], LengthUnit.Percent);
                _labels[i].text = _values[i] >= 10 ? Names[i] + "\n" + _values[i] : _values[i] > 0 ? _values[i].ToString() : "";
            }
        }

        private int Boundary(int i)
        {
            int sum = 0;
            for (int k = 0; k <= i; k++) sum += _values[k];
            return sum;
        }

        private float Percent(Vector3 local)
        {
            float w = resolvedStyle.width;
            return w > 0f ? Mathf.Clamp(local.x / w * 100f, 0f, 100f) : 0f;
        }

        private void OnDown(PointerDownEvent evt)
        {
            float at = Percent(evt.localPosition);
            int best = -1;
            float bestDist = 12f;
            for (int i = 0; i < 3; i++)
            {
                float d = Mathf.Abs(Boundary(i) - at);
                // Ties go to the right-most boundary so a zero-width segment can still be opened.
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            if (best < 0) return;
            _dragging = best;
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnMove(PointerMoveEvent evt)
        {
            if (_dragging < 0 || !this.HasPointerCapture(evt.pointerId)) return;
            int i = _dragging;
            int lo = i == 0 ? 0 : Boundary(i - 1);
            int hi = Boundary(i + 1);
            int at = Mathf.Clamp(Mathf.RoundToInt(Percent(evt.localPosition) / Step) * Step, lo, hi);
            _values[i] = at - lo;
            _values[i + 1] = hi - at;
            Refresh();
            evt.StopPropagation();
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (_dragging < 0) return;
            _dragging = -1;
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
            Changed?.Invoke(Value);
            evt.StopPropagation();
        }
    }
}
