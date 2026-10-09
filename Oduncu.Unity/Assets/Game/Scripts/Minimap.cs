using System.Collections.Generic;
using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// The minimap texture (design section 7): one texel per cell, terrain and resources as
    /// explored, units and buildings as the local player may see them, fog, the camera's view,
    /// and a flashing marker where something of the player's is under attack.
    /// </summary>
    public sealed class Minimap
    {
        private static readonly Color32 Unexplored = new Color32(12, 12, 14, 255);
        private static readonly Color32 Grass = new Color32(92, 128, 70, 255);
        private static readonly Color32 TreeColor = new Color32(30, 75, 34, 255);
        private static readonly Color32 GoldColor = new Color32(240, 200, 50, 255);
        private static readonly Color32 StoneColor = new Color32(170, 170, 165, 255);
        private static readonly Color32 FoodColor = new Color32(200, 70, 90, 255);
        private static readonly Color32 ViewColor = new Color32(255, 255, 255, 255);
        private static readonly Color32 AlertColor = new Color32(255, 40, 40, 255);

        public Texture2D Texture { get; private set; }

        private Color32[] _pixels;
        private int _w;
        private int _h;
        private Vector2Int _alertCell;
        private float _alertUntil = -1f;

        public void EnsureSize(int w, int h)
        {
            if (Texture != null && _w == w && _h == h) return;
            _w = w;
            _h = h;
            Texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Minimap" };
            _pixels = new Color32[w * h];
        }

        /// <summary>Flash a cell for a few seconds ("under attack" flashes the minimap region).</summary>
        public void Flash(Cell cell, float seconds)
        {
            _alertCell = new Vector2Int(cell.X, cell.Y);
            _alertUntil = Time.unscaledTime + seconds;
        }

        public void Redraw(Simulation sim, int player, CameraRig rig)
        {
            EnsureSize(sim.Fog.Width, sim.Fog.Height);
            FogOfWar fog = sim.Fog;
            bool all = sim.Reveal == RevealMode.AllVisible;
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    _pixels[y * _w + x] = all || fog.IsExplored(player, x, y) ? Grass : Unexplored;
                }
            }

            IReadOnlyList<Entity> entities = sim.Entities;
            // Resources and buildings first, units on top.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    Entity e = entities[i];
                    if (!e.Alive || e.State == UnitState.Garrisoned || e.IsUnit != (pass == 1)) continue;
                    if (!sim.CanSee(player, e)) continue;
                    Color32 c = ColorOf(e);
                    if (e.IsUnit)
                    {
                        Plot(e.Cell.X, e.Cell.Y, c);
                        continue;
                    }
                    CellRect r = e.Footprint;
                    for (int y = r.Y; y <= r.MaxY; y++)
                        for (int x = r.X; x <= r.MaxX; x++)
                            Plot(x, y, c);
                }
            }

            // Explored but not currently visible ground is dimmed, as on the battlefield.
            if (!all)
            {
                for (int y = 0; y < _h; y++)
                {
                    for (int x = 0; x < _w; x++)
                    {
                        if (fog.IsVisible(player, x, y) || !fog.IsExplored(player, x, y)) continue;
                        Color32 c = _pixels[y * _w + x];
                        _pixels[y * _w + x] = new Color32((byte)(c.r * 3 / 5), (byte)(c.g * 3 / 5), (byte)(c.b * 3 / 5), 255);
                    }
                }
            }

            DrawView(rig);
            if (Time.unscaledTime < _alertUntil && Mathf.Repeat(Time.unscaledTime, 0.5f) < 0.3f)
            {
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (Mathf.Abs(dx) == 2 || Mathf.Abs(dy) == 2) Plot(_alertCell.x + dx, _alertCell.y + dy, AlertColor);
            }

            Texture.SetPixels32(_pixels);
            Texture.Apply(false);
        }

        private static Color32 ColorOf(Entity e)
        {
            if (e.Owner >= 0)
            {
                Color c = PlaceholderArt.PlayerColor(e.Owner);
                if (e.IsBuilding) c *= 0.8f;
                c.a = 1f;
                return c;
            }
            switch (e.Kind)
            {
                case EntityKind.Tree: return TreeColor;
                case EntityKind.GoldMine: return GoldColor;
                case EntityKind.StoneMine: return StoneColor;
                default: return FoodColor;
            }
        }

        /// <summary>The outline of what the camera shows, from the ground under the screen corners.</summary>
        private void DrawView(CameraRig rig)
        {
            if (rig == null || rig.Camera == null) return;
            Vector3 a = rig.ScreenToGround(new Vector2(0f, 0f));
            Vector3 b = rig.ScreenToGround(new Vector2(Screen.width, 0f));
            Vector3 c = rig.ScreenToGround(new Vector2(Screen.width, Screen.height));
            Vector3 d = rig.ScreenToGround(new Vector2(0f, Screen.height));
            Line(a, b);
            Line(b, c);
            Line(c, d);
            Line(d, a);
        }

        private void Line(Vector3 from, Vector3 to)
        {
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.z - from.z)));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                Plot(Mathf.FloorToInt(Mathf.Lerp(from.x, to.x, t)), Mathf.FloorToInt(Mathf.Lerp(from.z, to.z, t)), ViewColor);
            }
        }

        private void Plot(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= _w || y >= _h) return;
            _pixels[y * _w + x] = c;
        }
    }
}
