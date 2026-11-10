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
            /// <summary>The villager's load, which the presenter shows, hides and recolours.</summary>
            public bool CarryPack;
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

        private static readonly Dictionary<Color, Material> UnlitMaterials = new Dictionary<Color, Material>();

        /// <summary>A shared unlit material of this colour, for overlays that should not be shaded; null without the shader.</summary>
        public static Material UnlitMaterial(Color color)
        {
            Material m;
            if (UnlitMaterials.TryGetValue(color, out m) && m != null) return m;
            m = TransparentMaterial(color);
            if (m != null) m.name = "Unlit " + ColorUtility.ToHtmlStringRGBA(color);
            UnlitMaterials[color] = m;
            return m;
        }

        public static readonly Color HealthHigh = new Color(0.25f, 0.85f, 0.3f);
        public static readonly Color HealthMid = new Color(0.95f, 0.8f, 0.2f);
        public static readonly Color HealthLow = new Color(0.9f, 0.25f, 0.2f);
        private static readonly Color BarBack = new Color(0f, 0f, 0f, 0.65f);
        private static readonly Color Flame = new Color(1f, 0.55f, 0.1f);
        private static readonly Color Ember = new Color(0.95f, 0.25f, 0.1f);
        private static readonly Color Smoke = new Color(0.2f, 0.2f, 0.2f);

        /// <summary>
        /// Add a health bar above a view: a dark backing quad and a fill quad that the presenter
        /// scales, recolours and turns to face the camera. Returns false without the unlit shader.
        /// </summary>
        public static bool AddHealthBar(EntityView view)
        {
            Material back = UnlitMaterial(BarBack);
            Material fill = UnlitMaterial(HealthHigh);
            if (back == null || fill == null) return false;
            var bar = new GameObject("Health").transform;
            bar.SetParent(view.transform, false);
            bar.localPosition = new Vector3(0f, view.Height, 0f);
            Overlay(PrimitiveType.Quad, bar, Vector3.zero, new Vector3(view.BarWidth + 0.06f, 0.14f, 1f), back);
            GameObject f = Overlay(PrimitiveType.Quad, bar, new Vector3(0f, 0f, -0.01f), new Vector3(view.BarWidth, 0.08f, 1f), fill);
            view.HealthBar = bar;
            view.HealthFill = f.transform;
            view.HealthFillRenderer = f.GetComponent<Renderer>();
            return true;
        }

        /// <summary>Flames and smoke on a badly damaged building; the presenter flickers the children.</summary>
        public static GameObject AddFire(EntityView view, int size)
        {
            var fire = new GameObject("Fire");
            fire.transform.SetParent(view.transform, false);
            float spread = size * 0.28f;
            float y = Mathf.Max(0.5f, view.Height - 0.6f);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * spread;
                float z = (i == 1 ? -1f : 1f) * spread * 0.6f;
                AddPrimitive(PrimitiveType.Cube, fire.transform, new Vector3(x, y, z), new Vector3(0.3f, 0.5f, 0.3f), Quaternion.Euler(0f, 45f, 0f), i == 1 ? Ember : Flame);
                AddPrimitive(PrimitiveType.Sphere, fire.transform, new Vector3(x + 0.1f, y + 0.6f, z), new Vector3(0.35f, 0.35f, 0.35f), Quaternion.identity, Smoke);
            }
            return fire;
        }

        /// <summary>A rally flag: pole and pennant in the player's colour.</summary>
        public static GameObject BuildFlag(Transform parent, Color team)
        {
            var flag = new GameObject("Rally flag");
            flag.transform.SetParent(parent, false);
            AddPrimitive(PrimitiveType.Cylinder, flag.transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.06f, 0.7f, 0.06f), Quaternion.identity, DarkWood);
            AddPrimitive(PrimitiveType.Cube, flag.transform, new Vector3(0.22f, 1.2f, 0f), new Vector3(0.4f, 0.28f, 0.03f), Quaternion.identity, team);
            AddPrimitive(PrimitiveType.Cylinder, flag.transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.5f, 0.01f, 0.5f), Quaternion.identity, team);
            return flag;
        }

        /// <summary>A flat disc on the ground for order feedback; the presenter scales and pools it.</summary>
        public static GameObject BuildPing(Transform parent, Color color)
        {
            Material m = UnlitMaterial(color);
            if (m == null) return null;
            GameObject ping = Overlay(PrimitiveType.Cylinder, parent, Vector3.zero, new Vector3(1f, 0.01f, 1f), m);
            ping.name = "Ping";
            return ping;
        }

        private static GameObject Overlay(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// The ground's material: the base green broken up by soft darker and lighter patches,
        /// so the map does not read as one flat colour. Deterministic, so every match looks alike.
        /// </summary>
        public static Material GroundMaterial(Color baseColor, int cells)
        {
            const int px = 256;
            var tex = new Texture2D(px, px, TextureFormat.RGBA32, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Ground" };
            var pixels = new Color32[px * px];
            for (int y = 0; y < px; y++)
            {
                for (int x = 0; x < px; x++)
                {
                    float u = x / (float)px * cells, v = y / (float)px * cells;
                    float broad = Mathf.PerlinNoise(u * 0.11f + 3.7f, v * 0.11f + 9.1f);
                    float fine = Mathf.PerlinNoise(u * 0.9f + 17.3f, v * 0.9f + 4.2f);
                    float k = 0.84f + 0.24f * broad + 0.08f * fine;
                    // Drier, yellower grass where the broad noise is high.
                    var c = new Color(baseColor.r * (k + 0.1f * broad), baseColor.g * k, baseColor.b * (k - 0.08f * broad), 1f);
                    pixels[y * px + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            Material template = MaterialFor(Color.white);
            return new Material(template) { color = Color.white, mainTexture = tex, name = "Ground" };
        }

        /// <summary>The colour of a villager's load for what it carries.</summary>
        public static Color CarryColor(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Wood: return Wood;
                case ResourceKind.Gold: return GoldOre;
                case ResourceKind.Stone: return StoneGrey;
                default: return new Color(0.78f, 0.25f, 0.22f);
            }
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
            float top = 0f;
            for (int i = 0; i < Parts.Count; i++)
            {
                Part p = Parts[i];
                GameObject part = AddPrimitive(p.Type, model, p.Position, p.Scale, Quaternion.Euler(p.Euler), p.Paint == Paint.Team ? team : p.Color);
                if (p.CarryPack)
                {
                    view.CarryPack = part.GetComponent<Renderer>();
                    part.SetActive(false);
                }
                // Cylinders and capsules are two units tall at scale one; the rest are one.
                float half = p.Type == PrimitiveType.Cylinder || p.Type == PrimitiveType.Capsule ? p.Scale.y : p.Scale.y * 0.5f;
                top = Mathf.Max(top, p.Position.y + half);
            }
            Parts.Clear();

            float footprint = Footprint(e.Def);
            if (e.IsUnit) model.localScale = Vector3.one * UnitScale;
            view.IsUnit = e.IsUnit;
            view.Rigid = e.Def.HasTag(EntityTag.Siege);
            view.Height = top * (e.IsUnit ? UnitScale : 1f) + 0.25f;
            view.BarWidth = e.IsUnit ? 0.7f : Mathf.Max(1f, e.Def.Size * 0.7f);
            view.Phase = (e.Id * 0.618f) % 1f * 6.2832f;
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
                    // A straw hat, a team sash so villagers still read as yours, and the load on the back.
                    Add(PrimitiveType.Cylinder, 0f, 0.79f, 0f, 0.3f, 0.015f, 0.3f, Wheat);
                    AddTeam(PrimitiveType.Cube, 0f, 0.34f, 0f, 0.29f, 0.07f, 0.29f);
                    Add(PrimitiveType.Cube, 0f, 0.36f, -0.17f, 0.2f, 0.22f, 0.12f, Wood);
                    MarkCarryPack();
                    return;
                case EntityKind.Militia:
                case EntityKind.LongSwordsman:
                case EntityKind.Champion:
                {
                    int tier = def.Kind == EntityKind.Militia ? 0 : def.Kind == EntityKind.LongSwordsman ? 1 : 2;
                    Body(null);
                    Head();
                    if (tier >= 1) Helmet();
                    if (tier >= 2) Plume();
                    // Shield on the left arm grows with the line; the sword gets longer.
                    Add(PrimitiveType.Cube, 0.17f, 0.32f, 0.04f, 0.04f, 0.26f + 0.05f * tier, 0.2f + 0.04f * tier, tier == 0 ? Wood : Steel);
                    Add(PrimitiveType.Cube, -0.17f, 0.42f, 0.08f + 0.03f * tier, 0.03f, 0.03f, 0.34f + 0.08f * tier, Steel);
                    return;
                }
                case EntityKind.Spearman:
                case EntityKind.Pikeman:
                case EntityKind.Halberdier:
                {
                    int tier = def.Kind == EntityKind.Spearman ? 0 : def.Kind == EntityKind.Pikeman ? 1 : 2;
                    float shaft = 0.42f + 0.1f * tier;
                    Body(null);
                    Head();
                    if (tier >= 1) Helmet();
                    Add(PrimitiveType.Cylinder, 0.17f, shaft + 0.08f, 0f, 0.035f, shaft, 0.035f, Wood);
                    Add(PrimitiveType.Cube, 0.17f, shaft * 2f + 0.11f, 0f, 0.06f, 0.1f, 0.02f, Steel);
                    // The halberd's axe blade.
                    if (tier >= 2) Add(PrimitiveType.Cube, 0.17f, shaft * 2f - 0.02f, 0.06f, 0.02f, 0.12f, 0.12f, Steel);
                    return;
                }
                case EntityKind.Archer:
                    Body(null);
                    Hood();
                    Add(PrimitiveType.Cube, 0.16f, 0.42f, 0.06f, 0.03f, 0.38f, 0.03f, DarkWood);
                    Tilt(0f, 0f, 15f);
                    Quiver();
                    return;
                case EntityKind.Crossbowman:
                case EntityKind.Arbalester:
                    Body(null);
                    Hood();
                    if (def.Kind == EntityKind.Arbalester) Helmet();
                    // A crossbow held level: stock forward, bow across.
                    Add(PrimitiveType.Cube, 0.12f, 0.42f, 0.16f, 0.04f, 0.04f, 0.3f, DarkWood);
                    Add(PrimitiveType.Cube, 0.12f, 0.42f, 0.28f, def.Kind == EntityKind.Arbalester ? 0.4f : 0.3f, 0.03f, 0.03f, def.Kind == EntityKind.Arbalester ? Steel : DarkWood);
                    Quiver();
                    return;
                case EntityKind.Longbowman:
                case EntityKind.EliteLongbowman:
                    Body(null);
                    Hood();
                    if (def.Kind == EntityKind.EliteLongbowman) Plume();
                    Add(PrimitiveType.Cube, 0.16f, 0.45f, 0.06f, 0.03f, 0.6f, 0.03f, DarkWood);
                    Quiver();
                    return;
                case EntityKind.Skirmisher:
                case EntityKind.EliteSkirmisher:
                    Body(null);
                    Head();
                    if (def.Kind == EntityKind.EliteSkirmisher) Helmet();
                    // A small round shield.
                    Add(PrimitiveType.Cylinder, 0.17f, 0.36f, 0.05f, 0.22f, 0.02f, 0.22f, Wood);
                    Tilt(0f, 0f, 90f);
                    Add(PrimitiveType.Cylinder, -0.12f, 0.45f, -0.12f, 0.025f, 0.22f, 0.025f, Wood);
                    Tilt(25f, 0f, 0f);
                    Add(PrimitiveType.Cylinder, -0.06f, 0.45f, -0.14f, 0.025f, 0.22f, 0.025f, Wood);
                    Tilt(25f, 0f, 0f);
                    return;
                case EntityKind.Scout:
                case EntityKind.LightCavalry:
                case EntityKind.Hussar:
                    Mount(Horse);
                    if (def.Kind != EntityKind.Scout) Add(PrimitiveType.Sphere, 0f, 0.9f, -0.02f, 0.15f, 0.12f, 0.15f, Steel);
                    // The hussar's wing.
                    if (def.Kind == EntityKind.Hussar) AddTeam(PrimitiveType.Cube, 0f, 0.85f, -0.16f, 0.04f, 0.4f, 0.08f);
                    return;
                case EntityKind.Knight:
                case EntityKind.Cavalier:
                    Mount(DarkHorse);
                    Add(PrimitiveType.Sphere, 0f, 0.9f, -0.02f, 0.15f, 0.12f, 0.15f, Steel);
                    Add(PrimitiveType.Cube, 0.16f, 0.62f, 0.3f, 0.03f, 0.03f, 0.55f, Steel);
                    if (def.Kind == EntityKind.Cavalier)
                    {
                        // Barding on the horse and a plume.
                        AddTeam(PrimitiveType.Cube, 0f, 0.36f, 0f, 0.24f, 0.12f, 0.4f);
                        AddTeam(PrimitiveType.Cube, 0f, 1.0f, -0.06f, 0.03f, 0.1f, 0.12f);
                    }
                    return;
                case EntityKind.BatteringRam:
                case EntityKind.CappedRam:
                    Add(PrimitiveType.Cube, 0f, 0.2f, 0f, 0.42f, 0.3f, 0.72f, Wood);
                    AddTeam(PrimitiveType.Cube, 0f, 0.4f, 0f, 0.46f, 0.08f, 0.76f);
                    Add(PrimitiveType.Cylinder, 0f, 0.2f, 0.38f, 0.1f, 0.12f, 0.1f, def.Kind == EntityKind.CappedRam ? Steel : DarkGrey);
                    Tilt(90f, 0f, 0f);
                    Wheels(0.24f, 0.24f);
                    return;
                case EntityKind.Mangonel:
                case EntityKind.Onager:
                    Add(PrimitiveType.Cube, 0f, 0.12f, 0f, 0.42f, 0.16f, 0.6f, Wood);
                    Add(PrimitiveType.Cube, 0f, 0.38f, -0.05f, 0.05f, 0.05f, 0.55f, DarkWood);
                    Tilt(-35f, 0f, 0f);
                    AddTeam(PrimitiveType.Cube, 0.18f, 0.3f, -0.2f, 0.04f, 0.18f, 0.12f);
                    // The stone in the sling; the onager throws a bigger one.
                    Add(PrimitiveType.Sphere, 0f, 0.56f, 0.2f, def.Kind == EntityKind.Onager ? 0.16f : 0.11f, def.Kind == EntityKind.Onager ? 0.16f : 0.11f, def.Kind == EntityKind.Onager ? 0.16f : 0.11f, StoneGrey);
                    Wheels(0.24f, 0.2f);
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
                    Legs(0.08f, 0.1f, 0.15f, DarkGrey);
                    return;
                case EntityKind.Deer:
                    Add(PrimitiveType.Cube, 0f, 0.3f, 0f, 0.18f, 0.18f, 0.42f, new Color(0.7f, 0.5f, 0.3f));
                    Add(PrimitiveType.Cube, 0f, 0.45f, 0.22f, 0.1f, 0.18f, 0.1f, new Color(0.7f, 0.5f, 0.3f));
                    Legs(0.07f, 0.22f, 0.2f, new Color(0.6f, 0.42f, 0.25f));
                    Add(PrimitiveType.Cube, 0.05f, 0.6f, 0.2f, 0.02f, 0.14f, 0.02f, Plaster);
                    Tilt(0f, 0f, -20f);
                    Add(PrimitiveType.Cube, -0.05f, 0.6f, 0.2f, 0.02f, 0.14f, 0.02f, Plaster);
                    Tilt(0f, 0f, 20f);
                    return;
                case EntityKind.Boar:
                    Add(PrimitiveType.Sphere, 0f, 0.2f, 0f, 0.32f, 0.28f, 0.48f, new Color(0.3f, 0.22f, 0.18f));
                    Add(PrimitiveType.Cube, 0.06f, 0.18f, 0.26f, 0.02f, 0.02f, 0.1f, Plaster);
                    return;

                // ---------------------------------------------------------- resources
                case EntityKind.Tree:
                {
                    // Three shapes and a spread of sizes and greens, picked by id so a forest is not a row of clones.
                    float shade = 0.85f + 0.3f * ((id * 37) % 10) / 10f;
                    float size = 0.85f + 0.3f * ((id * 53) % 7) / 6f;
                    float ox = (((id * 29) % 5) - 2) * 0.05f, oz = (((id * 71) % 5) - 2) * 0.05f;
                    Color leaf = Shade(Leaf, shade);
                    Add(PrimitiveType.Cylinder, ox, 0.4f * size, oz, 0.16f, 0.4f * size, 0.16f, DarkWood);
                    switch (id % 3)
                    {
                        case 0:
                            // Round broadleaf.
                            Add(PrimitiveType.Sphere, ox, 1.25f * size, oz, 0.85f * size, 1.1f * size, 0.85f * size, leaf);
                            break;
                        case 1:
                            // Conifer: three stacked tiers, narrowing.
                            Add(PrimitiveType.Cylinder, ox, 0.8f * size, oz, 0.8f * size, 0.2f * size, 0.8f * size, leaf);
                            Add(PrimitiveType.Cylinder, ox, 1.2f * size, oz, 0.58f * size, 0.2f * size, 0.58f * size, leaf);
                            Add(PrimitiveType.Cylinder, ox, 1.6f * size, oz, 0.34f * size, 0.2f * size, 0.34f * size, leaf);
                            break;
                        default:
                            // Two-lobed crown.
                            Add(PrimitiveType.Sphere, ox - 0.12f, 1.1f * size, oz, 0.7f * size, 0.8f * size, 0.7f * size, leaf);
                            Add(PrimitiveType.Sphere, ox + 0.15f, 1.45f * size, oz + 0.1f, 0.55f * size, 0.65f * size, 0.55f * size, Shade(leaf, 0.92f));
                            break;
                    }
                    return;
                }
                case EntityKind.GoldMine:
                    Add(PrimitiveType.Cube, 0f, 0.16f, 0f, 0.66f, 0.32f, 0.66f, StoneGrey);
                    Tilt(0f, (id * 53) % 90, 0f);
                    Add(PrimitiveType.Cube, 0.08f, 0.36f, -0.06f, 0.34f, 0.26f, 0.34f, GoldOre);
                    Tilt(20f, (id * 31) % 90, 15f);
                    Add(PrimitiveType.Cube, -0.2f, 0.26f, 0.16f, 0.2f, 0.2f, 0.2f, GoldOre);
                    Tilt(0f, (id * 17) % 90, 30f);
                    return;
                case EntityKind.StoneMine:
                    Add(PrimitiveType.Cube, 0f, 0.2f, 0f, 0.66f, 0.4f, 0.66f, StoneGrey);
                    Tilt(0f, (id * 53) % 90, 0f);
                    Add(PrimitiveType.Cube, 0.12f, 0.42f, 0.05f, 0.36f, 0.3f, 0.36f, Shade(StoneGrey, 1.15f));
                    Tilt(15f, (id * 31) % 90, 20f);
                    return;
                case EntityKind.Berries:
                    Add(PrimitiveType.Sphere, 0f, 0.3f, 0f, 0.75f, 0.55f, 0.75f, new Color(0.12f, 0.38f, 0.14f));
                    Add(PrimitiveType.Sphere, 0.15f, 0.5f, 0.15f, 0.14f, 0.14f, 0.14f, new Color(0.8f, 0.1f, 0.2f));
                    Add(PrimitiveType.Sphere, -0.18f, 0.45f, 0.05f, 0.14f, 0.14f, 0.14f, new Color(0.8f, 0.1f, 0.2f));
                    return;

                // ---------------------------------------------------------- buildings
                case EntityKind.Farm:
                    Add(PrimitiveType.Cube, 0f, 0.03f, 0f, def.Size * 0.95f, 0.06f, def.Size * 0.95f, new Color(0.45f, 0.33f, 0.2f));
                    for (int row = 0; row < def.Size * 2; row++)
                    {
                        float rz = (row + 0.5f) / (def.Size * 2f) * def.Size * 0.9f - def.Size * 0.45f;
                        Add(PrimitiveType.Cube, 0f, 0.11f, rz, def.Size * 0.88f, 0.12f, 0.2f, row % 2 == 0 ? Wheat : Shade(Wheat, 0.9f));
                    }
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
            Legs(0.08f, 0.2f, 0.26f, Shade(horse, 0.8f));
            // Tail.
            Add(PrimitiveType.Cube, 0f, 0.28f, -0.32f, 0.04f, 0.2f, 0.04f, Shade(horse, 0.6f));
            Tilt(25f, 0f, 0f);
        }

        /// <summary>A building's body, with a door on the side that faces the camera.</summary>
        private static void Block(int size, float height, Color color)
        {
            Add(PrimitiveType.Cube, 0f, height / 2f, 0f, size * 0.9f, height, size * 0.9f, color);
            float door = Mathf.Min(0.6f, height * 0.6f);
            Add(PrimitiveType.Cube, 0f, door / 2f, -size * 0.45f, 0.4f, door, 0.04f, color == DarkWood ? Wood : DarkWood);
        }

        private static Color Shade(Color c, float k) => new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);

        private static void MarkCarryPack()
        {
            Part p = Parts[Parts.Count - 1];
            p.CarryPack = true;
            Parts[Parts.Count - 1] = p;
        }

        private static void Helmet()
        {
            Add(PrimitiveType.Sphere, 0f, 0.72f, 0f, 0.19f, 0.14f, 0.19f, Steel);
        }

        private static void Plume()
        {
            AddTeam(PrimitiveType.Cube, 0f, 0.84f, -0.02f, 0.03f, 0.1f, 0.14f);
        }

        private static void Hood()
        {
            Add(PrimitiveType.Sphere, 0f, 0.68f, 0f, 0.2f, 0.2f, 0.2f, Leaf);
            Add(PrimitiveType.Sphere, 0f, 0.67f, 0.06f, 0.13f, 0.13f, 0.13f, Skin);
        }

        private static void Quiver()
        {
            Add(PrimitiveType.Cylinder, -0.08f, 0.42f, -0.15f, 0.06f, 0.14f, 0.06f, Wood);
            Tilt(0f, 0f, 20f);
        }

        /// <summary>Four wheels under a siege engine.</summary>
        private static void Wheels(float halfWidth, float halfLength)
        {
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * halfWidth;
                float z = (i < 2 ? -1f : 1f) * halfLength;
                Add(PrimitiveType.Cylinder, x, 0.1f, z, 0.2f, 0.03f, 0.2f, DarkWood);
                Tilt(0f, 0f, 90f);
            }
        }

        private static void Legs(float halfWidth, float height, float halfLength, Color color)
        {
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * halfWidth;
                float z = (i < 2 ? -1f : 1f) * halfLength * 0.8f;
                Add(PrimitiveType.Cube, x, height / 2f, z, 0.04f, height, 0.04f, color);
            }
        }

        private static void Roof(int size, float height)
        {
            AddTeam(PrimitiveType.Cube, 0f, height + 0.08f, 0f, size * 0.95f, 0.16f, size * 0.95f);
        }
    }
}
