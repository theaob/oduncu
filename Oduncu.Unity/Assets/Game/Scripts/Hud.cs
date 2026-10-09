using System.Collections.Generic;
using System.Text;
using Oduncu.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Oduncu.Game
{
    /// <summary>
    /// The match HUD and menus in UI Toolkit (plan section 4, design section 7), built in code
    /// on a panel scaled to physical size so one unit is one dp: resource bar with population,
    /// minimap, menu button, quick-select buttons, three control groups, alert stack,
    /// selection panel, command card, economy planner, and the main, pause and end screens.
    /// Everything stays inside the screen's safe area.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        /// <summary>Elements with this class (and their children) count as HUD for touch routing.</summary>
        public const string HitClass = "oduncu-hit";

        private const float RefreshSeconds = 0.1f;
        private const float MinimapSeconds = 0.2f;
        private const float ToastSeconds = 3f;
        private const int MaxAlerts = 4;
        private const float AlertSeconds = 20f;
        private const float AttackAlertCooldown = 12f;

        private static readonly Color Panel = new Color(0.08f, 0.08f, 0.1f, 0.78f);
        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.7f);

        private GameRoot _game;
        private SimRunner _runner;
        private PlayerController _player;
        private CameraRig _rig;
        private GestureInput _gestures;
        private CommandCard _card;
        private readonly Minimap _minimap = new Minimap();

        private UIDocument _doc;
        private VisualElement _root;
        private VisualElement _safe;
        private VisualElement _match;
        private Label _resources;
        private Label _age;
        private Image _minimapImage;
        private TapPad _idle;
        private readonly TapPad[] _groups = new TapPad[Selection.GroupCount];
        private VisualElement _alerts;
        private VisualElement _selectionPanel;
        private Label _selTitle;
        private Label _selInfo;
        private VisualElement _queueRow;
        private readonly TapPad[] _queue = new TapPad[SimConstants.TrainQueueLength];
        private readonly TapPad[] _cardPads = new TapPad[CommandCard.Slots];
        private readonly List<CardButton> _cardButtons = new List<CardButton>(CommandCard.Slots);
        private readonly System.Action[] _cardActions = new System.Action[CommandCard.Slots];
        private Label _toast;
        private VisualElement _box;
        private Label _debug;
        private VisualElement _planner;
        private EconomySlider _slider;
        private VisualElement _presetRow;
        private Label _plannerState;
        private VisualElement _overlay;
        private VisualElement _mainMenu;
        private VisualElement _pauseMenu;
        private VisualElement _endScreen;
        private Label _endTitle;
        private Label _endInfo;

        private readonly List<TapPad> _allPads = new List<TapPad>(48);
        private readonly List<AlertEntry> _alertEntries = new List<AlertEntry>();
        private readonly StringBuilder _sb = new StringBuilder(256);
        private readonly Dictionary<EntityKind, int> _kindCounts = new Dictionary<EntityKind, int>();
        private float _nextRefresh;
        private float _nextMinimap;
        private float _toastUntil;
        private float _lastAttackAlert = -100f;
        private int _lastIdle;
        private bool _housedShown;
        private bool[] _researched;
        private AgeId _lastAge;
        private Vector4 _safeInsets = new Vector4(-1f, -1f, -1f, -1f);
        private float _fps;

        private sealed class AlertEntry
        {
            public string Key;
            public string Text;
            public float Until;
            public System.Action Act;
            public VisualElement Row;
        }

        public void Init(GameRoot game, SimRunner runner, PlayerController player, CameraRig rig, GestureInput gestures)
        {
            _game = game;
            _runner = runner;
            _player = player;
            _rig = rig;
            _gestures = gestures;
            _card = new CommandCard(player);
            _player.Toast += ShowToast;
            _player.OwnDamaged += OnOwnDamaged;
            _runner.MatchStarted += OnMatchStarted;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            ThemeStyleSheet theme = Resources.Load<ThemeStyleSheet>("OduncuTheme");
            if (theme == null) Debug.LogWarning("Oduncu: Resources/OduncuTheme.tss did not load; HUD text falls back to the built-in font.");
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
            settings.referenceDpi = 160f;
            settings.fallbackDpi = 160f;
            settings.sortingOrder = 10;

            var host = new GameObject("HUD");
            host.SetActive(false);
            host.transform.SetParent(transform, false);
            _doc = host.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            host.SetActive(true);

            BuildTree();
            if (theme == null)
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null) _root.style.unityFontDefinition = FontDefinition.FromFont(font);
            }
            ShowMenu();
        }

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.Toast -= ShowToast;
                _player.OwnDamaged -= OnOwnDamaged;
            }
            if (_runner != null) _runner.MatchStarted -= OnMatchStarted;
        }

        // ------------------------------------------------------------------ touch routing

        /// <summary>Whether a screen point (pixels, origin bottom-left) is on a HUD element.</summary>
        public bool IsOverUi(Vector2 screen)
        {
            if (_root == null || _root.panel == null) return false;
            IPanel panel = _root.panel;
            Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            for (VisualElement e = panel.Pick(p); e != null; e = e.parent)
            {
                if (e.ClassListContains(HitClass)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ screens

        public void ShowMenu()
        {
            _match.style.display = DisplayStyle.None;
            ShowOverlay(_mainMenu);
        }

        public void ShowMatch()
        {
            _match.style.display = DisplayStyle.Flex;
            _overlay.style.display = DisplayStyle.None;
            _planner.style.display = DisplayStyle.None;
        }

        public void ShowPause()
        {
            ShowOverlay(_pauseMenu);
        }

        public void ShowEnd(bool won, int ticks)
        {
            _endTitle.text = won ? "Victory" : "Defeat";
            int seconds = ticks / SimConstants.TicksPerSecond;
            _endInfo.text = "Match time " + (seconds / 60) + ":" + (seconds % 60).ToString("00");
            ShowOverlay(_endScreen);
        }

        private void ShowOverlay(VisualElement screen)
        {
            _gestures.Cancel();
            _overlay.style.display = DisplayStyle.Flex;
            _mainMenu.style.display = screen == _mainMenu ? DisplayStyle.Flex : DisplayStyle.None;
            _pauseMenu.style.display = screen == _pauseMenu ? DisplayStyle.Flex : DisplayStyle.None;
            _endScreen.style.display = screen == _endScreen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------------ building the tree

        private void BuildTree()
        {
            _root = _doc.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            _root.style.flexGrow = 1;

            _safe = Layer(_root);
            _match = Layer(_safe);

            BuildTopBar();
            BuildTopRight();
            BuildQuickSelect();
            BuildSelectionPanel();
            BuildCommandCard();
            BuildPlanner();

            _alerts = new VisualElement { pickingMode = PickingMode.Ignore };
            Place(_alerts, left: 64, top: 50);
            _alerts.style.width = 260;
            _match.Add(_alerts);

            _toast = new Label { pickingMode = PickingMode.Ignore };
            Place(_toast, left: 0, right: 0, top: 50);
            _toast.style.unityTextAlign = TextAnchor.MiddleCenter;
            _toast.style.color = Color.white;
            _toast.style.fontSize = 15;
            _toast.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toast.style.display = DisplayStyle.None;
            _match.Add(_toast);

            _box = new VisualElement { pickingMode = PickingMode.Ignore };
            _box.style.position = Position.Absolute;
            _box.style.borderLeftWidth = _box.style.borderRightWidth = _box.style.borderTopWidth = _box.style.borderBottomWidth = 2;
            _box.style.borderLeftColor = _box.style.borderRightColor = _box.style.borderTopColor = _box.style.borderBottomColor = Color.white;
            _box.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            _box.style.display = DisplayStyle.None;
            _root.Add(_box);

            _debug = new Label { pickingMode = PickingMode.Ignore };
            Place(_debug, left: 0, right: 0, bottom: 2);
            _debug.style.unityTextAlign = TextAnchor.LowerCenter;
            _debug.style.color = new Color(1f, 1f, 0.6f);
            _debug.style.fontSize = 11;
            _debug.style.display = Debug.isDebugBuild ? DisplayStyle.Flex : DisplayStyle.None;
            _safe.Add(_debug);

            BuildMenus();
        }

        private static VisualElement Layer(VisualElement parent)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            Place(e, left: 0, right: 0, top: 0, bottom: 0);
            parent.Add(e);
            return e;
        }

        private static void Place(VisualElement e, float? left = null, float? right = null, float? top = null, float? bottom = null)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
        }

        private static VisualElement Box(VisualElement parent, FlexDirection direction, bool hit)
        {
            var e = new VisualElement { pickingMode = hit ? PickingMode.Position : PickingMode.Ignore };
            if (hit) e.AddToClassList(HitClass);
            e.style.flexDirection = direction;
            parent.Add(e);
            return e;
        }

        private TapPad Pad(VisualElement parent, string title, System.Action tapped, float width = 64f, float height = 56f)
        {
            var pad = new TapPad(title, width, height) { Tapped = tapped };
            parent.Add(pad);
            _allPads.Add(pad);
            return pad;
        }

        private static Label Text(VisualElement parent, string text, int size, Color color)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size;
            l.style.color = color;
            parent.Add(l);
            return l;
        }

        private void BuildTopBar()
        {
            VisualElement bar = Box(_match, FlexDirection.Row, true);
            Place(bar, left: 0, top: 0);
            bar.style.backgroundColor = Panel;
            bar.style.height = 44;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 8;
            bar.style.paddingRight = 4;
            TapPad.SetRadius(bar, 6);
            _resources = Text(bar, "", 15, Color.white);
            _resources.style.marginRight = 10;
            _age = Text(bar, "", 13, new Color(0.9f, 0.85f, 0.6f));
            _age.style.marginRight = 6;
            Pad(bar, "Planner", TogglePlanner, 72, 48);
        }

        private void BuildTopRight()
        {
            VisualElement corner = Box(_match, FlexDirection.Row, false);
            Place(corner, right: 0, top: 0);
            corner.style.alignItems = Align.FlexStart;

            var frame = Box(corner, FlexDirection.Column, true);
            frame.style.backgroundColor = Panel;
            frame.style.paddingLeft = frame.style.paddingRight = frame.style.paddingTop = frame.style.paddingBottom = 3;
            _minimapImage = new Image { scaleMode = ScaleMode.StretchToFill };
            _minimapImage.style.width = 124;
            _minimapImage.style.height = 124;
            _minimapImage.RegisterCallback<PointerDownEvent>(OnMinimapPointer);
            _minimapImage.RegisterCallback<PointerMoveEvent>(OnMinimapDrag);
            _minimapImage.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_minimapImage.HasPointerCapture(evt.pointerId)) _minimapImage.ReleasePointer(evt.pointerId);
            });
            frame.Add(_minimapImage);

            Pad(corner, "Menu", () => _game.Pause(), 56, 48);
        }

        private void BuildQuickSelect()
        {
            VisualElement column = Box(_match, FlexDirection.Column, false);
            Place(column, left: 0, top: 50);
            _idle = Pad(column, "Idle", _player.SelectIdleVillager, 56, 50);
            Pad(column, "Army", _player.SelectArmy, 56, 50);
            Pad(column, "TC", _player.SelectTownCenter, 56, 50);
            VisualElement groups = Box(column, FlexDirection.Row, false);
            for (int g = 0; g < Selection.GroupCount; g++)
            {
                int group = g;
                TapPad pad = Pad(groups, "G" + (g + 1), () => _player.SelectGroup(group), 48, 48);
                pad.LongPressed = () => _player.AssignGroup(group);
                _groups[g] = pad;
            }
        }

        private void BuildSelectionPanel()
        {
            _selectionPanel = Box(_match, FlexDirection.Column, true);
            Place(_selectionPanel, left: 62, bottom: 0);
            _selectionPanel.style.width = 250;
            _selectionPanel.style.minHeight = 96;
            _selectionPanel.style.backgroundColor = Panel;
            _selectionPanel.style.paddingLeft = _selectionPanel.style.paddingRight = 8;
            _selectionPanel.style.paddingTop = _selectionPanel.style.paddingBottom = 6;
            TapPad.SetRadius(_selectionPanel, 6);
            _selTitle = Text(_selectionPanel, "", 15, Color.white);
            _selTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _selInfo = Text(_selectionPanel, "", 12, new Color(0.85f, 0.85f, 0.85f));
            _selInfo.style.whiteSpace = WhiteSpace.Normal;
            _queueRow = Box(_selectionPanel, FlexDirection.Row, false);
            for (int i = 0; i < _queue.Length; i++)
            {
                int slot = i;
                _queue[i] = Pad(_queueRow, "", () => _player.CancelQueueSlot(slot), 44, 48);
                _queue[i].style.width = 44;
            }
        }

        private void BuildCommandCard()
        {
            VisualElement card = Box(_match, FlexDirection.Row, false);
            Place(card, right: 0, bottom: 0);
            card.style.flexWrap = Wrap.Wrap;
            card.style.width = 4 * 68;
            card.style.justifyContent = Justify.FlexEnd;
            card.style.alignContent = Align.FlexEnd;
            for (int i = 0; i < _cardPads.Length; i++)
            {
                int slot = i;
                _cardPads[i] = Pad(card, "", () => _cardActions[slot]?.Invoke(), 64, 60);
            }
        }

        private void BuildPlanner()
        {
            _planner = Box(_match, FlexDirection.Column, true);
            Place(_planner, left: 70, top: 50);
            _planner.style.backgroundColor = Panel;
            _planner.style.paddingLeft = _planner.style.paddingRight = _planner.style.paddingTop = _planner.style.paddingBottom = 8;
            _planner.style.width = 340;
            TapPad.SetRadius(_planner, 8);
            Text(_planner, "Economy planner", 15, Color.white).style.unityFontStyleAndWeight = FontStyle.Bold;
            _plannerState = Text(_planner, "", 12, new Color(0.85f, 0.85f, 0.85f));
            _plannerState.style.whiteSpace = WhiteSpace.Normal;
            _plannerState.style.marginBottom = 6;
            _slider = new EconomySlider();
            _slider.AddToClassList(HitClass);
            _slider.Changed += t => _player.SetEconomyTargets(t);
            _planner.Add(_slider);
            _presetRow = Box(_planner, FlexDirection.Row, false);
            _presetRow.style.flexWrap = Wrap.Wrap;
            _presetRow.style.marginTop = 6;
            VisualElement buttons = Box(_planner, FlexDirection.Row, false);
            Pad(buttons, "Off", () => _player.SetEconomyTargets(EconomyTargets.Off), 72, 48);
            Pad(buttons, "Close", TogglePlanner, 72, 48);
            _planner.style.display = DisplayStyle.None;
        }

        private void RebuildPresets(AgeId age)
        {
            _presetRow.Clear();
            for (int i = 0; i < GameData.EconomyPresets.Length; i++)
            {
                EconomyPreset preset = GameData.EconomyPresets[i];
                if (preset.Age != age) continue;
                EconomyTargets t = preset.Targets;
                var pad = new TapPad(preset.Name, 76, 48) { Tapped = () => _player.SetEconomyTargets(t) };
                pad.Set(preset.Name, t.Food + "/" + t.Wood + "/" + t.Gold + "/" + t.Stone, true, false, false);
                _presetRow.Add(pad);
            }
        }

        private void TogglePlanner()
        {
            bool open = _planner.style.display == DisplayStyle.None;
            _planner.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open && _runner.HasMatch) RebuildPresets(_runner.Sim.Players[_runner.LocalPlayer].Age);
        }

        private void BuildMenus()
        {
            _overlay = new VisualElement();
            _overlay.AddToClassList(HitClass);
            Place(_overlay, left: 0, right: 0, top: 0, bottom: 0);
            _overlay.style.backgroundColor = Scrim;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.alignItems = Align.Center;
            _safe.Add(_overlay);

            _mainMenu = MenuColumn();
            Text(_mainMenu, "Oduncu", 40, Color.white).style.unityFontStyleAndWeight = FontStyle.Bold;
            Text(_mainMenu, "Milestone 1 vertical slice", 13, new Color(0.8f, 0.8f, 0.8f)).style.marginBottom = 16;
            Pad(_mainMenu, "Skirmish vs Standard AI", () => _game.StartSkirmish(), 260, 56);

            _pauseMenu = MenuColumn();
            Text(_pauseMenu, "Paused", 30, Color.white).style.marginBottom = 12;
            Pad(_pauseMenu, "Resume", () => _game.Resume(), 220, 52);
            Pad(_pauseMenu, "Restart", () => _game.Restart(), 220, 52);
            Pad(_pauseMenu, "Quit to menu", () => _game.QuitToMenu(), 220, 52);

            _endScreen = MenuColumn();
            _endTitle = Text(_endScreen, "", 36, Color.white);
            _endTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _endInfo = Text(_endScreen, "", 14, new Color(0.85f, 0.85f, 0.85f));
            _endInfo.style.marginBottom = 12;
            Pad(_endScreen, "Rematch", () => _game.Restart(), 220, 52);
            Pad(_endScreen, "Back to menu", () => _game.QuitToMenu(), 220, 52);
        }

        private VisualElement MenuColumn()
        {
            var col = new VisualElement();
            col.style.alignItems = Align.Center;
            _overlay.Add(col);
            return col;
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (_root == null) return;
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            ApplySafeArea();
            for (int i = 0; i < _allPads.Count; i++) _allPads[i].Tick();
            UpdateBox();

            if (_toast.style.display == DisplayStyle.Flex && Time.unscaledTime > _toastUntil) _toast.style.display = DisplayStyle.None;
            if (!_runner.HasMatch) return;

            float now = Time.unscaledTime;
            if (now >= _nextRefresh)
            {
                _nextRefresh = now + RefreshSeconds;
                Simulation sim = _runner.Sim;
                RefreshTopBar(sim);
                RefreshSelection(sim);
                RefreshCard(sim);
                RefreshAlerts(sim);
                RefreshDebug(sim);
            }
            if (now >= _nextMinimap)
            {
                _nextMinimap = now + MinimapSeconds;
                _minimap.Redraw(_runner.Sim, _runner.LocalPlayer, _rig);
                if (_minimapImage.image != _minimap.Texture) _minimapImage.image = _minimap.Texture;
            }
        }

        /// <summary>
        /// Keep the HUD inside Screen.safeArea (notches, rounded corners). Recomputed every frame
        /// because the panel's scale is only known after its first layout, but applied only on change.
        /// </summary>
        private void ApplySafeArea()
        {
            if (_root.panel == null) return;
            Rect area = Screen.safeArea;
            IPanel panel = _root.panel;
            Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(area.xMin, Screen.height - area.yMax));
            Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(area.xMax, Screen.height - area.yMin));
            Vector2 full = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, Screen.height));
            var insets = new Vector4(topLeft.x, topLeft.y, Mathf.Max(0f, full.x - bottomRight.x), Mathf.Max(0f, full.y - bottomRight.y));
            if ((insets - _safeInsets).sqrMagnitude < 0.25f) return;
            _safeInsets = insets;
            _safe.style.left = insets.x;
            _safe.style.top = insets.y;
            _safe.style.right = insets.z;
            _safe.style.bottom = insets.w;
        }

        private void UpdateBox()
        {
            Rect? box = _gestures.Box;
            if (!box.HasValue || _root.panel == null)
            {
                _box.style.display = DisplayStyle.None;
                return;
            }
            Rect r = box.Value;
            IPanel panel = _root.panel;
            Vector2 a = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(r.xMin, Screen.height - r.yMax));
            Vector2 b = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(r.xMax, Screen.height - r.yMin));
            _box.style.display = DisplayStyle.Flex;
            _box.style.left = a.x;
            _box.style.top = a.y;
            _box.style.width = b.x - a.x;
            _box.style.height = b.y - a.y;
        }

        private void RefreshTopBar(Simulation sim)
        {
            PlayerState p = sim.Players[_runner.LocalPlayer];
            _sb.Clear();
            _sb.Append("F ").Append(p.Food).Append("   W ").Append(p.Wood).Append("   G ").Append(p.Gold)
               .Append("   S ").Append(p.Stone).Append("   Pop ").Append(p.Population).Append('/').Append(p.PopulationCap);
            SetText(_resources, _sb.ToString());
            SetText(_age, GameData.Ages[(int)p.Age].Name);
            _resources.style.color = p.Population >= p.PopulationCap && p.PopulationCap < SimConstants.MaxPopulation ? TapPad.ShortColor : Color.white;

            int idle = _player.IdleVillagerCount();
            _idle.Set("Idle", idle > 0 ? idle.ToString() : "", true, false, idle > 0);
            for (int g = 0; g < _groups.Length; g++)
            {
                int n = _player.Selection.GroupSize(g);
                _groups[g].Set("G" + (g + 1), n > 0 ? n.ToString() : "hold", true, false, false);
            }

            if (_planner.style.display == DisplayStyle.Flex)
            {
                EconomyTargets t = p.EconomyTargets;
                _slider.SetValue(t);
                SetText(_plannerState, t.IsOff
                    ? "Off: villagers keep their jobs. Drag the split or pick a preset to have new and idle villagers fill the gaps."
                    : "On: new and idle villagers go where the split is furthest behind.");
                if (p.Age != _lastAge) RebuildPresets(p.Age);
            }
        }

        private void RefreshSelection(Simulation sim)
        {
            Selection selection = _player.Selection;
            Entity primary = selection.Primary(sim);
            if (primary == null)
            {
                _selectionPanel.style.display = DisplayStyle.None;
                return;
            }
            _selectionPanel.style.display = DisplayStyle.Flex;
            int me = _runner.LocalPlayer;

            if (selection.Count > 1)
            {
                _kindCounts.Clear();
                IReadOnlyList<int> ids = selection.Ids;
                for (int i = 0; i < ids.Count; i++)
                {
                    Entity e = sim.Find(ids[i]);
                    if (e == null || !e.Alive) continue;
                    int n;
                    _kindCounts.TryGetValue(e.Kind, out n);
                    _kindCounts[e.Kind] = n + 1;
                }
                SetText(_selTitle, selection.Count + " selected");
                _sb.Clear();
                foreach (KeyValuePair<EntityKind, int> kv in _kindCounts)
                {
                    if (_sb.Length > 0) _sb.Append(", ");
                    _sb.Append(EntityDefs.Get(kv.Key).Name).Append(" x").Append(kv.Value);
                }
                SetText(_selInfo, _sb.ToString());
                _queueRow.style.display = DisplayStyle.None;
                return;
            }

            EntityDef def = primary.Def;
            SetText(_selTitle, def.Name + (primary.Owner >= 0 && primary.Owner != me ? " (enemy)" : ""));
            _sb.Clear();
            if (primary.IsResource || def.IsGatherable)
            {
                if (primary.Amount > 0) _sb.Append(primary.Amount).Append(' ').Append(def.Yields).Append(" left");
                if (primary.IsBuilding) _sb.Append("   HP ").Append(primary.Hp).Append('/').Append(primary.Stats.MaxHp);
            }
            else
            {
                _sb.Append("HP ").Append(primary.Hp).Append('/').Append(primary.Stats.MaxHp);
                if (primary.IsUnit)
                {
                    _sb.Append("   Attack ").Append(primary.Stats.Attack).Append("   Armor ").Append(primary.Stats.MeleeArmor).Append('/').Append(primary.Stats.PierceArmor);
                    if (primary.Carry > 0) _sb.Append("\nCarrying ").Append(primary.Carry).Append(' ').Append(primary.CarryKind);
                    if (primary.Owner == me) _sb.Append('\n').Append(Describe(primary.State, primary));
                }
                else if (primary.UnderConstruction)
                {
                    int ticks = Mathf.Max(1, primary.Stats.BuildTicks);
                    _sb.Append("\nUnder construction ").Append(primary.BuildProgress * 100 / ticks).Append('%');
                }
                else if (primary.GarrisonCount > 0)
                {
                    _sb.Append("\nGarrisoned ").Append(primary.GarrisonCount).Append('/').Append(def.GarrisonCapacity);
                }
            }
            SetText(_selInfo, _sb.ToString());

            bool showQueue = primary.IsBuilding && primary.Owner == me && primary.TrainQueue.Count > 0;
            _queueRow.style.display = showQueue ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showQueue) return;
            for (int i = 0; i < _queue.Length; i++)
            {
                if (i >= primary.TrainQueue.Count)
                {
                    _queue[i].style.display = DisplayStyle.None;
                    continue;
                }
                QueueItem item = primary.TrainQueue[i];
                string name = item.IsAgeUp ? "Age" : item.IsResearch ? "Tech" : Short(EntityDefs.Get(item.Unit).Name);
                string detail = i == 0 ? Progress(sim, primary, item) : "x";
                _queue[i].style.display = DisplayStyle.Flex;
                _queue[i].Set(name, detail, true, false, false);
            }
        }

        private static string Progress(Simulation sim, Entity b, QueueItem item)
        {
            int total = item.IsAgeUp ? GameData.Ages[(int)item.Age].ResearchTicks
                : item.IsResearch ? GameData.Techs[(int)item.Tech].ResearchTicks
                : sim.Players[b.Owner].Stats.Of(item.Unit).TrainTicks;
            return Mathf.Clamp(b.TrainProgress * 100 / Mathf.Max(1, total), 0, 100) + "%";
        }

        private static string Short(string name) => name.Length <= 6 ? name : name.Substring(0, 5) + ".";

        private static string Describe(UnitState state, Entity e)
        {
            switch (state)
            {
                case UnitState.Idle: return "Idle";
                case UnitState.Moving: return "Moving";
                case UnitState.Gathering: return "Gathering " + EntityDefs.Get(e.GatherKind).Name.ToLowerInvariant();
                case UnitState.Returning: return "Returning " + e.CarryKind.ToString().ToLowerInvariant();
                case UnitState.Building: return "Building";
                case UnitState.Attacking: return "Attacking";
                case UnitState.Repairing: return "Repairing";
                case UnitState.Garrisoning: return "Going inside";
                case UnitState.Healing: return "Healing";
                default: return state.ToString();
            }
        }

        private void RefreshCard(Simulation sim)
        {
            _card.Build(sim, _cardButtons);
            for (int i = 0; i < _cardPads.Length; i++)
            {
                TapPad pad = _cardPads[i];
                if (i >= _cardButtons.Count)
                {
                    pad.style.visibility = Visibility.Hidden;
                    _cardActions[i] = null;
                    continue;
                }
                CardButton b = _cardButtons[i];
                pad.style.visibility = Visibility.Visible;
                pad.Set(b.Label, b.Detail, b.Enabled, b.Short, b.Alert);
                _cardActions[i] = b.Enabled ? b.Click : null;
            }
        }

        private void RefreshDebug(Simulation sim)
        {
            if (!Debug.isDebugBuild) return;
            _sb.Clear();
            _sb.Append("tick ").Append(sim.CurrentTick)
               .Append("   sim ").Append(_runner.LastTickMs.ToString("0.00")).Append(" ms (peak ").Append(_runner.PeakTickMs.ToString("0.00")).Append(')')
               .Append("   entities ").Append(sim.Entities.Count)
               .Append("   hash ").Append(_runner.LastHash.ToString("X16"))
               .Append("   ").Append(Mathf.RoundToInt(_fps)).Append(" fps   seed ").Append(_runner.Seed);
            SetText(_debug, _sb.ToString());
        }

        private static void SetText(Label l, string text)
        {
            if (l.text != text) l.text = text;
        }

        // ------------------------------------------------------------------ minimap

        private void OnMinimapPointer(PointerDownEvent evt)
        {
            _minimapImage.CapturePointer(evt.pointerId);
            JumpFromMinimap(evt.localPosition);
            evt.StopPropagation();
        }

        private void OnMinimapDrag(PointerMoveEvent evt)
        {
            if (!_minimapImage.HasPointerCapture(evt.pointerId)) return;
            JumpFromMinimap(evt.localPosition);
        }

        private void JumpFromMinimap(Vector3 local)
        {
            if (!_runner.HasMatch) return;
            float w = _minimapImage.resolvedStyle.width, h = _minimapImage.resolvedStyle.height;
            if (w <= 0f || h <= 0f) return;
            FogOfWar fog = _runner.Sim.Fog;
            float x = local.x / w * fog.Width;
            float y = (1f - local.y / h) * fog.Height;
            _rig.JumpTo(new Vector3(x, 0f, y));
        }

        // ------------------------------------------------------------------ toasts and alerts

        public void ShowToast(string text)
        {
            if (_toast == null) return;
            _toast.text = text;
            _toast.style.display = DisplayStyle.Flex;
            _toastUntil = Time.unscaledTime + ToastSeconds;
        }

        private void OnMatchStarted(Simulation sim)
        {
            for (int i = _alertEntries.Count - 1; i >= 0; i--) RemoveAlert(i);
            _researched = new bool[GameData.TechCount];
            _lastAge = sim.Players[_runner.LocalPlayer].Age;
            _lastIdle = 0;
            _housedShown = false;
            _lastAttackAlert = -100f;
            _planner.style.display = DisplayStyle.None;
            RebuildPresets(_lastAge);
        }

        private void OnOwnDamaged(Entity e)
        {
            Vector3 world = EntityPresenter.ToWorld(e);
            if (_rig.IsOnScreen(world)) return;
            float now = Time.unscaledTime;
            if (now - _lastAttackAlert < AttackAlertCooldown) return;
            _lastAttackAlert = now;
            Cell at = e.Cell;
            _minimap.Flash(at, 4f);
            PushAlert("attack", "Under attack: " + e.Def.Name, () => _rig.JumpTo(world));
        }

        private void RefreshAlerts(Simulation sim)
        {
            PlayerState p = sim.Players[_runner.LocalPlayer];

            int idle = _player.IdleVillagerCount();
            if (idle > 0 && _lastIdle == 0) PushAlert("idle", "Idle villagers", _player.SelectIdleVillager);
            if (idle == 0) DropAlert("idle");
            _lastIdle = idle;

            bool housed = p.Population >= p.PopulationCap && p.PopulationCap < SimConstants.MaxPopulation;
            if (housed && !_housedShown) PushAlert("housed", "Housed: build a house", null);
            if (!housed) DropAlert("housed");
            _housedShown = housed;

            if (p.Age != _lastAge)
            {
                PushAlert("age", "Reached the " + GameData.Ages[(int)p.Age].Name, null);
                _lastAge = p.Age;
                if (_planner.style.display == DisplayStyle.Flex) RebuildPresets(p.Age);
            }
            for (int t = 1; t < GameData.TechCount; t++)
            {
                bool has = p.Stats.HasResearched((TechId)t);
                if (has && !_researched[t] && !GameData.Techs[t].IsCivBonus) PushAlert("tech" + t, "Research complete: " + GameData.Techs[t].Name, null);
                _researched[t] = has;
            }

            float now = Time.unscaledTime;
            for (int i = _alertEntries.Count - 1; i >= 0; i--)
            {
                if (now > _alertEntries[i].Until) RemoveAlert(i);
            }
        }

        private void PushAlert(string key, string text, System.Action act)
        {
            DropAlert(key);
            while (_alertEntries.Count >= MaxAlerts) RemoveAlert(0);
            var entry = new AlertEntry { Key = key, Text = text, Until = Time.unscaledTime + AlertSeconds, Act = act };
            VisualElement row = Box(_alerts, FlexDirection.Row, false);
            row.style.marginBottom = 2;
            var body = new TapPad(text, 200, 48);
            body.style.width = 200;
            body.style.alignItems = Align.FlexStart;
            body.style.paddingLeft = 8;
            body.Tapped = () =>
            {
                act?.Invoke();
                DropAlert(key);
            };
            row.Add(body);
            var close = new TapPad("x", 48, 48) { Tapped = () => DropAlert(key) };
            row.Add(close);
            entry.Row = row;
            _alertEntries.Add(entry);
        }

        private void DropAlert(string key)
        {
            for (int i = _alertEntries.Count - 1; i >= 0; i--)
            {
                if (_alertEntries[i].Key == key) RemoveAlert(i);
            }
        }

        private void RemoveAlert(int index)
        {
            _alertEntries[index].Row.RemoveFromHierarchy();
            _alertEntries.RemoveAt(index);
        }
    }
}
