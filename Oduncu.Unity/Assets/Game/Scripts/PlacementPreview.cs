using System.Collections.Generic;
using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// Translucent footprints for a building or a wall line being placed (plan section 4),
    /// green where the simulation's own CanPlace check would accept it and red where not.
    /// </summary>
    public sealed class PlacementPreview : MonoBehaviour
    {
        private static readonly Color Valid = new Color(0.2f, 1f, 0.3f, 0.45f);
        private static readonly Color Invalid = new Color(1f, 0.2f, 0.15f, 0.45f);

        private readonly List<Renderer> _blocks = new List<Renderer>();
        private Material _valid;
        private Material _invalid;
        private int _used;

        private void Awake()
        {
            _valid = PlaceholderArt.TransparentMaterial(Valid) ?? PlaceholderArt.MaterialFor(new Color(Valid.r, Valid.g, Valid.b));
            _invalid = PlaceholderArt.TransparentMaterial(Invalid) ?? PlaceholderArt.MaterialFor(new Color(Invalid.r, Invalid.g, Invalid.b));
            _valid.renderQueue = 3200;
            _invalid.renderQueue = 3200;
        }

        public void Begin() => _used = 0;

        /// <summary>One footprint of the given size with its origin cell at the bottom-left.</summary>
        public void Add(Cell origin, int size, float height, bool ok)
        {
            Renderer r = Block(_used++);
            r.transform.position = new Vector3(origin.X + size / 2f, height / 2f, origin.Y + size / 2f);
            r.transform.localScale = new Vector3(size * 0.98f, height, size * 0.98f);
            r.sharedMaterial = ok ? _valid : _invalid;
        }

        public void End()
        {
            for (int i = _used; i < _blocks.Count; i++)
            {
                if (_blocks[i].gameObject.activeSelf) _blocks[i].gameObject.SetActive(false);
            }
        }

        public void Hide()
        {
            Begin();
            End();
        }

        /// <summary>Show one building at an origin, checked the way the Build command will be.</summary>
        public void ShowBuilding(Simulation sim, int player, EntityKind kind, Cell origin)
        {
            Begin();
            EntityDef def = EntityDefs.Get(kind);
            Add(origin, def.Size, 1.2f, sim.CanPlace(player, kind, origin));
            End();
        }

        /// <summary>Show a wall line between two cells, each segment checked on its own.</summary>
        public void ShowWall(Simulation sim, int player, EntityKind kind, Cell from, Cell to, List<Cell> scratch)
        {
            Begin();
            Simulation.WallLine(from, to, scratch);
            for (int i = 0; i < scratch.Count; i++) Add(scratch[i], 1, 1f, sim.CanPlace(player, kind, scratch[i]));
            End();
        }

        private Renderer Block(int index)
        {
            while (_blocks.Count <= index)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Placement";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _blocks.Add(r);
            }
            Renderer block = _blocks[index];
            if (!block.gameObject.activeSelf) block.gameObject.SetActive(true);
            return block;
        }
    }
}
