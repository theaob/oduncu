using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Oduncu.Game
{
    /// <summary>
    /// A HUD button for touch: at least 48 dp (design section 7), a title and an optional
    /// second line, tap and long-press callbacks, and a pressed tint. Used instead of the
    /// theme's Button so every HUD button looks and behaves the same.
    /// </summary>
    public sealed class TapPad : VisualElement
    {
        public const float MinSize = 48f;
        public const float LongPressSeconds = 0.5f;

        public static readonly Color Normal = new Color(0.12f, 0.12f, 0.14f, 0.82f);
        public static readonly Color Pressed = new Color(0.32f, 0.32f, 0.36f, 0.95f);
        public static readonly Color AlertTint = new Color(0.7f, 0.45f, 0.1f, 0.9f);
        public static readonly Color ShortColor = new Color(1f, 0.45f, 0.4f);
        public static readonly Color DetailColor = new Color(0.8f, 0.8f, 0.8f);

        public readonly Label Title;
        public readonly Label Detail;
        public Action Tapped;
        public Action LongPressed;

        private float _downAt = -1f;
        private bool _alert;
        private bool _active = true;

        public TapPad(string title, float width = 64f, float height = 56f)
        {
            AddToClassList(Hud.HitClass);
            style.width = Mathf.Max(width, MinSize);
            style.height = Mathf.Max(height, MinSize);
            style.marginLeft = style.marginRight = style.marginTop = style.marginBottom = 2;
            style.backgroundColor = Normal;
            SetRadius(this, 6);
            style.justifyContent = Justify.Center;
            style.alignItems = Align.Center;
            style.paddingLeft = style.paddingRight = 2;

            Title = new Label(title) { pickingMode = PickingMode.Ignore };
            Title.style.color = Color.white;
            Title.style.fontSize = 13;
            Title.style.unityTextAlign = TextAnchor.MiddleCenter;
            Title.style.whiteSpace = WhiteSpace.Normal;
            Add(Title);

            Detail = new Label { pickingMode = PickingMode.Ignore };
            Detail.style.color = DetailColor;
            Detail.style.fontSize = 10;
            Detail.style.unityTextAlign = TextAnchor.MiddleCenter;
            Detail.style.whiteSpace = WhiteSpace.Normal;
            Detail.style.display = DisplayStyle.None;
            Add(Detail);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerLeaveEvent>(_ => Unpress());
        }

        public static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
        }

        public void Set(string title, string detail, bool active, bool shortOfCost, bool alert)
        {
            if (Title.text != title) Title.text = title;
            bool hasDetail = !string.IsNullOrEmpty(detail);
            Detail.style.display = hasDetail ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasDetail && Detail.text != detail) Detail.text = detail;
            Detail.style.color = shortOfCost ? ShortColor : DetailColor;
            _active = active;
            _alert = alert;
            style.opacity = active ? 1f : 0.45f;
            if (_downAt < 0f) style.backgroundColor = Rest();
        }

        /// <summary>Called every frame by the HUD so alert buttons flash.</summary>
        public void Tick()
        {
            if (_downAt >= 0f) return;
            style.backgroundColor = Rest();
        }

        private Color Rest()
        {
            if (_alert && Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f) return AlertTint;
            return Normal;
        }

        private void OnDown(PointerDownEvent evt)
        {
            _downAt = Time.unscaledTime;
            style.backgroundColor = Pressed;
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
            if (_downAt < 0f) return;
            float held = Time.unscaledTime - _downAt;
            bool inside = ContainsPoint(evt.localPosition);
            Unpress();
            evt.StopPropagation();
            if (!inside || !_active) return;
            if (held >= LongPressSeconds && LongPressed != null) LongPressed();
            else Tapped?.Invoke();
        }

        private void Unpress()
        {
            if (_downAt < 0f) return;
            _downAt = -1f;
            style.backgroundColor = Rest();
        }
    }
}
