using System.Collections.Generic;
using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// Milestone 1 placeholder art (plan section 4): a few low-poly primitives per unit line and
    /// building, so each reads at a glance, with a player-colour ring under everything owned
    /// and units at 1.5x scale relative to buildings (design section 7). Materials are shared
    /// per colour with GPU instancing on, so one colour costs one material.
    /// </summary>
    public static class PlaceholderArt
    {
        public static readonly Color[] PlayerColors = { new Color(0.2f, 0.45f, 1f), new Color(0.95f, 0.22f, 0.18f), new Color(0.2f, 0.8f, 0.3f), new Color(0.95f, 0.85f, 0.2f) };

        /// <summary>Units are drawn this much larger than their footprint, relative to buildings.</summary>
        public const float UnitScale = 1.5f;

        private static readonly Color Skin = new Color(0.93f, 0.76f, 0.6f);
        private static readonly Color Wood = new Color(0.52f, 0.36f, 0.2f);
        private static readonly Color DarkWood = new Color(0.36f, 0.25f, 0.15f);
        private static readonly Color StoneGrey = new Color(0.62f, 0.62f, 0.6f);
        private static readonly Color DarkGrey = new Color(0.3f, 0.3f, 0.32f);
        private static readonly Color Plaster = new Color(0.85f, 0.8f, 0.68f);
        private static readonly Color Steel = new Color(0.75f, 0.78f, 0.82f);
        private static readonly Color Horse = new Color(0.45f, 0.3f, 0.18f);
        private static readonly Color DarkHorse = new Color(0.22f, 0.2f, 0.2f);
        private static readonly Color Leaf = new Color(0.16f, 0.45f, 0.18f);
        private static readonly Color GoldOre = new Color(0.98f, 0.8f, 0.2f);
        private static readonly Color Wheat = new Color(0.85f, 0.75f, 0.35f);
        private static readonly Color Robe = new Color(0.9f, 0.62f, 0.25f);
        private static readonly Color Selection = new Color(1f, 1f, 1f, 0.9f);

        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();
        private static Material _template;

        private enum Paint : byte
        {
            Fixed,
            Team,
        }

        private struct Part
        {
            public PrimitiveType Type;
            public Vector3 Position;
            public Vector3 Scale;
            public Vector3 Euler;
            public Paint Paint;
            public Color Color;
        }

        private static readonly List<Part> Parts = new List<Part>(8);

        public static Color PlayerColor(int owner) => owner >= 0 ? PlayerColors[owner % PlayerColors.Length] : Color.white;

        /// <summary>A shared, instanced material of this colour, made from the pipeline's default material.</summary>
        public static Material MaterialFor(Color color)
        {
            Material m;
            if (Materials.TryGetValue(color, out m)) return m;
            if (_template == null)
            {
                GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _template = probe.GetComponent<Renderer>().sharedMaterial;
                Object.Destroy(probe);
            }
            m = new Material(_template) { color = color, enableInstancing = true, name = "Placeholder " + ColorUtility.ToHtmlStringRGB(color) };
            Materials.Add(color, m);
            return m;
        }

        /// <summary>
        /// A new alpha-blended material of this colour, from the game's own unlit shader, or null
        /// if no transparent shader is available (the caller then skips the effect).
        /// </summary>
        public static Material TransparentMaterial(Color color)
        {
            Shader shader = Shader.Find("Oduncu/UnlitTransparent");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("Oduncu: no transparent shader found; fog and placement previews are off.");
                return null;
            }
            return new Material(shader) { color = color };
        }

        /// <summary>Build the view for an entity: a root carrying EntityView, its model, team ring and selection ring.</summary>
        public static EntityView Build(Entity e, Transform parent)
        {
            var root = new GameObject(e.Def.Name);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<EntityView>();
            view.Kind = e.Kind;
            view.Owner = e.Owner;

            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            view.Model = model;

            Parts.Clear();
            Describe(e.Def, e.Id);
            Color team = PlayerColor(e.Owner);
            for (int i = 0; i < Parts.Count; i++)
            {
                Part p = Parts[i];
                AddPrimitive(p.Type, model, p.Position, p.Scale, Quaternion.Euler(p.Euler), p.Paint == Paint.Team ? team : p.Color);
            }
            Parts.Clear();

            float footprint = Footprint(e.Def);
            if (e.IsUnit) model.localScale = Vector3.one * UnitScale;
            if (e.Owner >= 0 && e.Kind != EntityKind.Farm)
            {
                view.Ring = AddPrimitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.01f, 0f),
                    new Vector3(footprint, 0.01f, footprint), Quaternion.identity, team).transform;
            }
            view.SelectionRing = AddPrimitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.005f, 0f),
                new Vector3(footprint * 1.25f, 0.005f, footprint * 1.25f), Quaternion.identity, Selection);
            view.SelectionRing.SetActive(false);
            return view;
        }

        /// <summary>Diameter of the ring under an entity, in cells.</summary>
        public static float Footprint(EntityDef def)
        {
            if (def.IsUnit) return def.HasTag(EntityTag.Cavalry) || def.HasTag(EntityTag.Siege) ? 1.1f : 0.8f;
            return def.Size * 1.05f;
        }

        public static GameObject AddPrimitive(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Quaternion rotation, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            // Picking goes through the simulation's own geometry (PlayerController), not physics.
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.transform.localRotation = rotation;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = MaterialFor(color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // ------------------------------------------------------------------ looks

        private static void Add(PrimitiveType type, float x, float y, float z, float sx, float sy, float sz, Color color)
        {
            Parts.Add(new Part { Type = type, Position = new Vector3(x, y, z), Scale = new Vector3(sx, sy, sz), Paint = Paint.Fixed, Color = color });
        }

        private static void AddTeam(PrimitiveType type, float x, float y, float z, float sx, float sy, float sz)
        {
            Parts.Add(new Part { Type = type, Position = new Vector3(x, y, z), Scale = new Vector3(sx, sy, sz), Paint = Paint.Team });
        }

        private static void Tilt(float ex, float ey, float ez)
        {
            Part p = Parts[Parts.Count - 1];
            p.Euler = new Vector3(ex, ey, ez);
            Parts[Parts.Count - 1] = p;
        }

        private static void Describe(EntityDef def, int id)
        {
            switch (def.Kind)
            {
                // ---------------------------------------------------------- units
                case EntityKind.Villager:
                    Body(Plaster);
                    Head();
                    Add(PrimitiveType.Cube, 0f, 0.32f, -0.14f, 0.16f, 0.18f, 0.08f, Wood);
                    return;
                case EntityKind.Militia:
                case EntityKind.LongSwordsman:
                case EntityKind.Champion:
                    Body(null);
                    Head();
                    Add(PrimitiveType.Cube, 0.17f, 0.32f, 0.04f, 0.04f, 0.26f, 0.2f, Steel);
                    Add(PrimitiveType.Cube, -0.17f, 0.42f, 0.08f, 0.03f, 0.03f, 0.34f, Steel);
                    return;
                case EntityKind.Spearman:
                case EntityKind.Pikeman:
                case EntityKind.Halberdier:
                    Body(null);
                    Head();
                    Add(PrimitiveType.Cylinder, 0.17f, 0.5f, 0f, 0.035f, 0.42f, 0.035f, Wood);
                    Add(PrimitiveType.Cube, 0.17f, 0.95f, 0f, 0.06f, 0.1f, 0.02f, Steel);
                    return;
                case EntityKind.Archer:
                case EntityKind.Crossbowman:
                case EntityKind.Arbalester:
                    Body(null);
                    Add(PrimitiveType.Sphere, 0f, 0.68f, 0f, 0.2f, 0.2f, 0.2f, Leaf);
                    Add(PrimitiveType.Cube, 0.16f, 0.42f, 0.06f, 0.03f, 0.38f, 0.03f, DarkWood);
                    Tilt(0f, 0f, 15f);
                    return;
                case EntityKind.Longbowman:
                case EntityKind.EliteLongbowman:
                    Body(null);
                    Add(PrimitiveType.Sphere, 0f, 0.68f, 0f, 0.2f, 0.2f, 0.2f, Leaf);
                    Add(PrimitiveType.Cube, 0.16f, 0.45f, 0.06f, 0.03f, 0.6f, 0.03f, DarkWood);
                    return;
                case EntityKind.Skirmisher:
                case EntityKind.EliteSkirmisher:
                    Body(null);
                    Head();
                    Add(PrimitiveType.Cylinder, -0.12f, 0.45f, -0.12f, 0.025f, 0.22f, 0.025f, Wood);
                    Tilt(25f, 0f, 0f);
                    Add(PrimitiveType.Cylinder, -0.06f, 0.45f, -0.14f, 0.025f, 0.22f, 0.025f, Wood);
                    Tilt(25f, 0f, 0f);
                    return;
                case EntityKind.Scout:
                case EntityKind.LightCavalry:
                case EntityKind.Hussar:
                    Mount(Horse);
                    return;
                case EntityKind.Knight:
                case EntityKind.Cavalier:
                    Mount(DarkHorse);
                    Add(PrimitiveType.Cube, 0.16f, 0.62f, 0.3f, 0.03f, 0.03f, 0.55f, Steel);
                    return;
                case EntityKind.BatteringRam:
                case EntityKind.CappedRam:
                    Add(PrimitiveType.Cube, 0f, 0.2f, 0f, 0.42f, 0.3f, 0.72f, Wood);
                    AddTeam(PrimitiveType.Cube, 0f, 0.4f, 0f, 0.46f, 0.08f, 0.76f);
                    Add(PrimitiveType.Cylinder, 0f, 0.2f, 0.38f, 0.1f, 0.12f, 0.1f, DarkGrey);
                    Tilt(90f, 0f, 0f);
                    return;
                case EntityKind.Mangonel:
                case EntityKind.Onager:
                    Add(PrimitiveType.Cube, 0f, 0.12f, 0f, 0.42f, 0.16f, 0.6f, Wood);
                    Add(PrimitiveType.Cube, 0f, 0.38f, -0.05f, 0.05f, 0.05f, 0.55f, DarkWood);
                    Tilt(-35f, 0f, 0f);
                    AddTeam(PrimitiveType.Cube, 0.18f, 0.3f, -0.2f, 0.04f, 0.18f, 0.12f);
                    return;
                case EntityKind.Trebuchet:
                    Add(PrimitiveType.Cube, 0f, 0.1f, 0f, 0.55f, 0.14f, 0.7f, Wood);
                    Add(PrimitiveType.Cube, 0f, 0.55f, 0f, 0.08f, 0.9f, 0.08f, DarkWood);
                    Add(PrimitiveType.Cube, 0f, 0.95f, 0.05f, 0.05f, 0.05f, 0.8f, DarkWood);
                    Tilt(-30f, 0f, 0f);
                    AddTeam(PrimitiveType.Cube, 0f, 0.3f, -0.3f, 0.3f, 0.2f, 0.06f);
                    return;
                case EntityKind.Monk:
                    Add(PrimitiveType.Capsule, 0f, 0.3f, 0f, 0.3f, 0.32f, 0.3f, Robe);
                    Head();
                    AddTeam(PrimitiveType.Cube, 0f, 0.4f, 0f, 0.31f, 0.05f, 0.31f);
                    return;
                case EntityKind.Sheep:
                    Add(PrimitiveType.Sphere, 0f, 0.18f, 0f, 0.3f, 0.24f, 0.38f, Color.white);
                    Add(PrimitiveType.Sphere, 0f, 0.24f, 0.2f, 0.12f, 0.12f, 0.12f, DarkGrey);
                    return;
                case EntityKind.Deer:
                    Add(PrimitiveType.Cube, 0f, 0.3f, 0f, 0.18f, 0.18f, 0.42f, new Color(0.7f, 0.5f, 0.3f));
                    Add(PrimitiveType.Cube, 0f, 0.45f, 0.22f, 0.1f, 0.18f, 0.1f, new Color(0.7f, 0.5f, 0.3f));
                    return;
                case EntityKind.Boar:
                    Add(PrimitiveType.Sphere, 0f, 0.2f, 0f, 0.32f, 0.28f, 0.48f, new Color(0.3f, 0.22f, 0.18f));
                    Add(PrimitiveType.Cube, 0.06f, 0.18f, 0.26f, 0.02f, 0.02f, 0.1f, Plaster);
                    return;

                // ---------------------------------------------------------- resources
                case EntityKind.Tree:
                {
                    float shade = 0.85f + 0.3f * ((id * 37) % 10) / 10f;
                    Add(PrimitiveType.Cylinder, 0f, 0.4f, 0f, 0.16f, 0.4f, 0.16f, DarkWood);
                    Add(PrimitiveType.Sphere, 0f, 1.25f, 0f, 0.85f, 1.1f, 0.85f, Leaf * shade);
                    return;
                }
                case EntityKind.GoldMine:
                    Add(PrimitiveType.Cube, 0f, 0.18f, 0f, 0.62f, 0.36f, 0.62f, GoldOre);
                    Tilt(0f, (id * 53) % 90, 0f);
                    return;
                case EntityKind.StoneMine:
                    Add(PrimitiveType.Cube, 0f, 0.2f, 0f, 0.66f, 0.4f, 0.66f, StoneGrey);
                    Tilt(0f, (id * 53) % 90, 0f);
                    return;
                case EntityKind.Berries:
                    Add(PrimitiveType.Sphere, 0f, 0.3f, 0f, 0.75f, 0.55f, 0.75f, new Color(0.12f, 0.38f, 0.14f));
                    Add(PrimitiveType.Sphere, 0.15f, 0.5f, 0.15f, 0.14f, 0.14f, 0.14f, new Color(0.8f, 0.1f, 0.2f));
                    Add(PrimitiveType.Sphere, -0.18f, 0.45f, 0.05f, 0.14f, 0.14f, 0.14f, new Color(0.8f, 0.1f, 0.2f));
                    return;

                // ---------------------------------------------------------- buildings
                case EntityKind.Farm:
                    Add(PrimitiveType.Cube, 0f, 0.03f, 0f, def.Size * 0.95f, 0.06f, def.Size * 0.95f, Wheat);
                    return;
                case EntityKind.House:
                    Block(def.Size, 0.8f, Plaster);
                    AddTeam(PrimitiveType.Cube, 0f, 0.8f, 0f, def.Size * 0.9f, 0.55f, 0.55f);
                    Tilt(45f, 0f, 0f);
                    return;
                case EntityKind.TownCenter:
                    Block(def.Size, 1.4f, StoneGrey);
                    Roof(def.Size, 1.4f);
                    AddTeam(PrimitiveType.Cylinder, 0f, 1.9f, 0f, 1.1f, 0.5f, 1.1f);
                    Add(PrimitiveType.Cylinder, 0f, 2.5f, 0f, 0.7f, 0.15f, 0.7f, DarkWood);
                    return;
                case EntityKind.Barracks:
                    Block(def.Size, 1.2f, DarkWood);
                    Roof(def.Size, 1.2f);
                    Add(PrimitiveType.Cube, 0.9f, 1.6f, 0.9f, 0.05f, 0.8f, 0.05f, Steel);
                    return;
                case EntityKind.ArcheryRange:
                    Block(def.Size, 1.0f, new Color(0.4f, 0.5f, 0.3f));
                    Roof(def.Size, 1.0f);
                    Add(PrimitiveType.Cylinder, 1.0f, 0.5f, -1.2f, 0.5f, 0.05f, 0.5f, new Color(0.85f, 0.2f, 0.2f));
                    Tilt(90f, 0f, 0f);
                    return;
                case EntityKind.Stable:
                    Block(def.Size, 1.0f, Wood);
                    Roof(def.Size, 1.0f);
                    Add(PrimitiveType.Cube, 0f, 0.5f, 1.3f, 1.8f, 0.6f, 0.1f, Horse);
                    return;
                case EntityKind.SiegeWorkshop:
                    Block(def.Size, 1.1f, DarkGrey);
                    Roof(def.Size, 1.1f);
                    Add(PrimitiveType.Cylinder, 0.8f, 1.3f, 0.8f, 0.3f, 0.06f, 0.3f, Wood);
                    Tilt(90f, 0f, 0f);
                    return;
                case EntityKind.Forge:
                    Block(def.Size, 1.0f, DarkGrey);
                    Roof(def.Size, 1.0f);
                    Add(PrimitiveType.Cylinder, 0.8f, 1.5f, 0.8f, 0.35f, 0.6f, 0.35f, StoneGrey);
                    return;
                case EntityKind.Mill:
                    Block(def.Size, 0.9f, Plaster);
                    Roof(def.Size, 0.9f);
                    Add(PrimitiveType.Cube, 0f, 1.5f, 0.8f, 0.1f, 1.6f, 0.05f, Wood);
                    Tilt(0f, 0f, 45f);
                    Add(PrimitiveType.Cube, 0f, 1.5f, 0.8f, 0.1f, 1.6f, 0.05f, Wood);
                    Tilt(0f, 0f, -45f);
                    return;
                case EntityKind.LumberCamp:
                    Block(def.Size, 0.6f, Wood);
                    Roof(def.Size, 0.6f);
                    Add(PrimitiveType.Cylinder, 0.5f, 0.2f, -0.5f, 0.25f, 0.4f, 0.25f, DarkWood);
                    Tilt(0f, 0f, 90f);
                    return;
                case EntityKind.MiningCamp:
                    Block(def.Size, 0.6f, StoneGrey);
                    Roof(def.Size, 0.6f);
                    Add(PrimitiveType.Cube, 0.5f, 0.15f, -0.5f, 0.35f, 0.3f, 0.35f, GoldOre);
                    return;
                case EntityKind.Market:
                    Block(def.Size, 0.9f, Plaster);
                    AddTeam(PrimitiveType.Cube, 0f, 1.0f, 0f, def.Size * 0.95f, 0.08f, def.Size * 0.6f);
                    Tilt(15f, 0f, 0f);
                    Add(PrimitiveType.Cube, 0f, 1.0f, 0f, def.Size * 0.95f, 0.08f, def.Size * 0.3f, Color.white);
                    Tilt(15f, 0f, 0f);
                    return;
                case EntityKind.Monastery:
                    Block(def.Size, 1.2f, new Color(0.92f, 0.9f, 0.84f));
                    AddTeam(PrimitiveType.Sphere, 0f, 1.3f, 0f, 1.6f, 1.2f, 1.6f);
                    return;
                case EntityKind.Castle:
                    Block(def.Size, 1.8f, StoneGrey);
                    Roof(def.Size, 1.8f);
                    for (int i = 0; i < 4; i++)
                    {
                        float cx = (i % 2 == 0 ? -1f : 1f) * def.Size * 0.45f;
                        float cz = (i < 2 ? -1f : 1f) * def.Size * 0.45f;
                        Add(PrimitiveType.Cylinder, cx, 1.2f, cz, 0.8f, 1.2f, 0.8f, StoneGrey);
                        AddTeam(PrimitiveType.Cylinder, cx, 2.45f, cz, 0.85f, 0.06f, 0.85f);
                    }
                    return;
                case EntityKind.Tower:
                    Add(PrimitiveType.Cylinder, 0f, 1.2f, 0f, 0.8f, 1.2f, 0.8f, StoneGrey);
                    AddTeam(PrimitiveType.Cylinder, 0f, 2.5f, 0f, 0.95f, 0.12f, 0.95f);
                    return;
                case EntityKind.PalisadeWall:
                    Add(PrimitiveType.Cube, 0f, 0.5f, 0f, 0.7f, 1.0f, 0.7f, Wood);
                    AddTeam(PrimitiveType.Cube, 0f, 0.95f, 0f, 0.72f, 0.08f, 0.72f);
                    return;
                case EntityKind.StoneWall:
                    Add(PrimitiveType.Cube, 0f, 0.6f, 0f, 1f, 1.2f, 1f, StoneGrey);
                    AddTeam(PrimitiveType.Cube, 0f, 1.15f, 0f, 1.02f, 0.08f, 1.02f);
                    return;
                case EntityKind.Gate:
                    Add(PrimitiveType.Cube, 0f, 0.7f, 0f, 1f, 1.4f, 1f, DarkGrey);
                    AddTeam(PrimitiveType.Cube, 0f, 1.0f, 0f, 1.02f, 0.25f, 1.02f);
                    return;
            }

            // Anything added to the data tables before it gets a look.
            if (def.IsUnit)
            {
                Body(null);
                Head();
            }
            else
            {
                Block(def.Size, 1f, def.IsResource ? StoneGrey : Plaster);
            }
        }

        /// <summary>A unit's body, team-coloured unless a colour is given.</summary>
        private static void Body(Color? color)
        {
            if (color.HasValue) Add(PrimitiveType.Capsule, 0f, 0.3f, 0f, 0.28f, 0.3f, 0.28f, color.Value);
            else AddTeam(PrimitiveType.Capsule, 0f, 0.3f, 0f, 0.28f, 0.3f, 0.28f);
        }

        private static void Head()
        {
            Add(PrimitiveType.Sphere, 0f, 0.68f, 0f, 0.17f, 0.17f, 0.17f, Skin);
        }

        private static void Mount(Color horse)
        {
            Add(PrimitiveType.Cube, 0f, 0.3f, 0f, 0.22f, 0.22f, 0.55f, horse);
            Add(PrimitiveType.Cube, 0f, 0.45f, 0.3f, 0.12f, 0.22f, 0.14f, horse);
            Tilt(-30f, 0f, 0f);
            AddTeam(PrimitiveType.Capsule, 0f, 0.6f, -0.02f, 0.2f, 0.2f, 0.2f);
            Add(PrimitiveType.Sphere, 0f, 0.86f, -0.02f, 0.13f, 0.13f, 0.13f, Skin);
        }

        private static void Block(int size, float height, Color color)
        {
            Add(PrimitiveType.Cube, 0f, height / 2f, 0f, size * 0.9f, height, size * 0.9f, color);
        }

        private static void Roof(int size, float height)
        {
            AddTeam(PrimitiveType.Cube, 0f, height + 0.08f, 0f, size * 0.95f, 0.16f, size * 0.95f);
        }
    }
}
