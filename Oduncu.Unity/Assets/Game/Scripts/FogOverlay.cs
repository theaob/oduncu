using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// Fog of war drawn from the simulation's visibility grid (plan section 4): black where the
    /// local player has never looked, dimmed where they have explored but cannot see now.
    /// One texel per cell on a transparent quad above the battlefield, shifted so each texel
    /// sits over its own ground cell from the fixed camera angle.
    /// </summary>
    public sealed class FogOverlay : MonoBehaviour
    {
        private const float Height = 3f;
        private const byte ExploredAlpha = 120;

        private SimRunner _runner;
        private Texture2D _texture;
        private Color32[] _pixels;
        private GameObject _quad;

        public void Init(SimRunner runner, Camera cam)
        {
            _runner = runner;
            _runner.Ticked += Redraw;
            _runner.MatchStarted += Build;
            _camera = cam;
        }

        private Camera _camera;

        private void OnDestroy()
        {
            if (_runner == null) return;
            _runner.Ticked -= Redraw;
            _runner.MatchStarted -= Build;
        }

        private void Build(Simulation sim)
        {
            int w = sim.Fog.Width, h = sim.Fog.Height;
            if (_texture == null || _texture.width != w || _texture.height != h)
            {
                _texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Fog" };
                _pixels = new Color32[w * h];
            }
            if (_quad == null)
            {
                _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _quad.name = "Fog";
                Destroy(_quad.GetComponent<Collider>());
                _quad.transform.SetParent(transform, false);
                Material material = PlaceholderArt.TransparentMaterial(Color.white);
                if (material == null)
                {
                    _quad.SetActive(false);
                    material = new Material(PlaceholderArt.MaterialFor(Color.black));
                }
                else
                {
                    material.renderQueue = 3100;
                }
                var r = _quad.GetComponent<Renderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            _quad.GetComponent<Renderer>().sharedMaterial.mainTexture = _texture;

            Vector3 forward = _camera.transform.forward;
            Vector3 centre = new Vector3(w / 2f, 0f, h / 2f) - forward * (Height / -forward.y);
            _quad.transform.position = centre;
            _quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _quad.transform.localScale = new Vector3(w, h, 1f);
            Redraw(sim);
        }

        private void Redraw(Simulation sim)
        {
            FogOfWar fog = sim.Fog;
            int player = _runner.LocalPlayer;
            bool all = sim.Reveal == RevealMode.AllVisible;
            for (int y = 0; y < fog.Height; y++)
            {
                for (int x = 0; x < fog.Width; x++)
                {
                    byte alpha = all || fog.IsVisible(player, x, y) ? (byte)0 : fog.IsExplored(player, x, y) ? ExploredAlpha : (byte)255;
                    _pixels[y * fog.Width + x] = new Color32(0, 0, 0, alpha);
                }
            }
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }
    }
}
