using System;
using System.Collections.Generic;
using Oduncu.Sim;

namespace Oduncu.Game
{
    /// <summary>One button on the command card.</summary>
    public struct CardButton
    {
        public string Label;
        /// <summary>Second line: a cost, a requirement or a count.</summary>
        public string Detail;
        public bool Enabled;
        /// <summary>The detail is a cost the player cannot pay yet.</summary>
        public bool Short;
        /// <summary>Flash for attention (the house button near the population cap).</summary>
        public bool Alert;
        public Action Click;
    }

    /// <summary>
    /// The context-sensitive command card (design section 7): at most 8 buttons; the build
    /// menu is a second page grouped Economy / Military / Defence; longer lists (a Town Center's
    /// research, a market) page with a More button.
    /// </summary>
    public sealed class CommandCard
    {
        public const int Slots = 8;

        private enum Page
        {
            Main,
            Economy,
            Military,
            Defence,
        }

        private static readonly EntityKind[] Economy = { EntityKind.House, EntityKind.Farm, EntityKind.Mill, EntityKind.LumberCamp, EntityKind.MiningCamp, EntityKind.Market };
        private static readonly EntityKind[] Military = { EntityKind.Barracks, EntityKind.ArcheryRange, EntityKind.Stable, EntityKind.SiegeWorkshop, EntityKind.Forge, EntityKind.Monastery, EntityKind.Castle };
        private static readonly EntityKind[] Defence = { EntityKind.PalisadeWall, EntityKind.StoneWall, EntityKind.Gate, EntityKind.Tower };
        private static readonly ResourceKind[] Tradable = { ResourceKind.Food, ResourceKind.Wood, ResourceKind.Stone };

        private readonly PlayerController _player;
        private readonly List<CardButton> _all = new List<CardButton>(32);
        private Page _page;
        private int _offset;
        private int _selectionVersion = -1;

        public CommandCard(PlayerController player)
        {
            _player = player;
        }

        /// <summary>Fill the buttons for the current selection and mode; at most Slots entries.</summary>
        public void Build(Simulation sim, List<CardButton> into)
        {
            into.Clear();
            if (_player.Selection.Version != _selectionVersion)
            {
                _selectionVersion = _player.Selection.Version;
                _page = Page.Main;
                _offset = 0;
            }

            _all.Clear();
            switch (_player.Mode)
            {
                case TapMode.PlaceBuilding:
                case TapMode.PlaceWall:
                    _all.Add(new CardButton { Label = "Confirm", Detail = EntityDefs.Get(_player.PlacingKind).Name, Enabled = _player.CanPlaceNow, Click = _player.ConfirmPlacement });
                    _all.Add(new CardButton { Label = "Cancel", Enabled = true, Click = _player.CancelMode });
                    break;
                case TapMode.AttackMove:
                case TapMode.Rally:
                case TapMode.Garrison:
                    _all.Add(new CardButton { Label = "Cancel", Enabled = true, Click = _player.CancelMode });
                    break;
                default:
                    Fill(sim);
                    break;
            }
            Paginate(into);
        }

        private void Paginate(List<CardButton> into)
        {
            if (_all.Count <= Slots)
            {
                into.AddRange(_all);
                return;
            }
            if (_offset >= _all.Count) _offset = 0;
            int room = Slots - 1;
            for (int i = _offset; i < _all.Count && into.Count < room; i++) into.Add(_all[i]);
            bool more = _offset + room < _all.Count;
            into.Add(new CardButton
            {
                Label = more ? "More" : "Back",
                Detail = (_offset / room + 1) + "/" + ((_all.Count + room - 1) / room),
                Enabled = true,
                Click = () => _offset = more ? _offset + room : 0,
            });
        }

        private void Fill(Simulation sim)
        {
            int me = _player.LocalPlayer;
            Entity primary = _player.Selection.Primary(sim);
            if (primary == null || primary.Owner != me) return;
            PlayerState p = sim.Players[me];

            if (primary.IsUnit)
            {
                bool builders = false;
                IReadOnlyList<int> ids = _player.Selection.Ids;
                for (int i = 0; i < ids.Count; i++)
                {
                    Entity e = sim.Find(ids[i]);
                    if (e != null && e.Owner == me && e.Def.CanBuild) builders = true;
                }
                if (builders) FillBuilder(sim, p);
                else FillMilitary(primary);
                return;
            }
            if (primary.IsBuilding && !primary.UnderConstruction) FillBuilding(sim, p, primary);
        }

        private void FillBuilder(Simulation sim, PlayerState p)
        {
            bool housed = p.PopulationCap < SimConstants.MaxPopulation && p.PopulationCap - p.Population <= 5;
            switch (_page)
            {
                case Page.Main:
                    _all.Add(new CardButton { Label = "Economy", Detail = "build", Enabled = true, Alert = housed, Click = () => _page = Page.Economy });
                    _all.Add(new CardButton { Label = "Military", Detail = "build", Enabled = true, Click = () => _page = Page.Military });
                    _all.Add(new CardButton { Label = "Defence", Detail = "build", Enabled = true, Click = () => _page = Page.Defence });
                    _all.Add(new CardButton { Label = "Stop", Enabled = true, Click = _player.Stop });
                    _all.Add(new CardButton { Label = "Garrison", Enabled = true, Click = _player.BeginGarrison });
                    return;
                case Page.Economy:
                    AddBuildings(sim, p, Economy, housed);
                    break;
                case Page.Military:
                    AddBuildings(sim, p, Military, false);
                    break;
                case Page.Defence:
                    AddBuildings(sim, p, Defence, false);
                    break;
            }
            _all.Add(new CardButton { Label = "Back", Enabled = true, Click = () => _page = Page.Main });
        }

        private void AddBuildings(Simulation sim, PlayerState p, EntityKind[] kinds, bool housed)
        {
            for (int i = 0; i < kinds.Length; i++)
            {
                EntityKind kind = kinds[i];
                EntityDef def = EntityDefs.Get(kind);
                bool ageOk = def.MinAge <= p.Age;
                Cost cost = p.Stats.Of(kind).Cost;
                EntityKind k = kind;
                _all.Add(new CardButton
                {
                    Label = def.Name,
                    Detail = ageOk ? Format(cost) + (def.HasTag(EntityTag.Wall) && kind != EntityKind.Gate ? " each" : "") : AgeName(def.MinAge),
                    Enabled = ageOk,
                    Short = ageOk && !p.CanAfford(cost),
                    Alert = housed && kind == EntityKind.House,
                    Click = () => _player.BeginPlacement(k),
                });
            }
        }

        private void FillMilitary(Entity primary)
        {
            _all.Add(new CardButton { Label = "Attack move", Enabled = true, Click = _player.BeginAttackMove });
            _all.Add(new CardButton { Label = "Stop", Enabled = true, Click = _player.Stop });
            bool holding = primary.HoldGround;
            _all.Add(new CardButton
            {
                Label = holding ? "Aggressive" : "Hold ground",
                Detail = "stance",
                Enabled = true,
                Click = () => _player.SetStance(holding ? Stance.Aggressive : Stance.HoldGround),
            });
            _all.Add(new CardButton { Label = "Garrison", Enabled = true, Click = _player.BeginGarrison });
        }

        private void FillBuilding(Simulation sim, PlayerState p, Entity b)
        {
            EntityDef def = b.Def;
            for (int i = 0; i < def.Trains.Length; i++)
            {
                EntityKind kind = Simulation.LineMemberFor(def.Trains[i], p.Age);
                EntityDef unit = EntityDefs.Get(kind);
                bool ageOk = unit.MinAge <= p.Age;
                Cost cost = p.Stats.Of(kind).Cost;
                EntityKind k = kind;
                _all.Add(new CardButton
                {
                    Label = unit.Name,
                    Detail = ageOk ? Format(cost) : AgeName(unit.MinAge),
                    Enabled = ageOk,
                    Short = ageOk && !p.CanAfford(cost),
                    Click = () => _player.Train(k),
                });
            }

            if (b.Kind == EntityKind.TownCenter && (int)p.Age + 1 < GameData.AgeCount)
            {
                AgeDef next = GameData.Ages[(int)p.Age + 1];
                string why = sim.AgeUpRejection(p.Index);
                _all.Add(new CardButton
                {
                    Label = next.Name,
                    Detail = why == null ? Format(next.Cost) : Capitalise(why),
                    Enabled = why == null,
                    Short = why == null && !p.CanAfford(next.Cost),
                    Click = _player.AgeUp,
                });
            }

            for (int t = 1; t < GameData.TechCount; t++)
            {
                TechDef tech = GameData.Techs[t];
                if (tech.ResearchedAt != b.Kind || tech.IsCivBonus) continue;
                string why = sim.ResearchRejection(p.Index, tech.Id);
                if (why != null && !why.StartsWith("requires", StringComparison.Ordinal)) continue;
                TechId id = tech.Id;
                _all.Add(new CardButton
                {
                    Label = tech.Name,
                    Detail = why == null ? Format(tech.Cost) : Capitalise(why),
                    Enabled = why == null,
                    Short = why == null && !p.CanAfford(tech.Cost),
                    Click = () => _player.Research(id),
                });
            }

            if (b.Kind == EntityKind.Market)
            {
                for (int i = 0; i < Tradable.Length; i++)
                {
                    ResourceKind r = Tradable[i];
                    int price = sim.BuyPrice(r);
                    _all.Add(new CardButton { Label = "Buy " + r, Detail = price + " gold", Enabled = true, Short = p.Gold < price, Click = () => _player.Market(r, true) });
                }
                for (int i = 0; i < Tradable.Length; i++)
                {
                    ResourceKind r = Tradable[i];
                    _all.Add(new CardButton { Label = "Sell " + r, Detail = "+" + sim.SellPrice(r) + " gold", Enabled = true, Short = p.Get(r) < Simulation.MarketLot, Click = () => _player.Market(r, false) });
                }
            }

            if (def.Trains.Length > 0)
            {
                _all.Add(new CardButton { Label = "Rally", Enabled = true, Click = _player.BeginRally });
                bool auto = b.AutoQueue;
                _all.Add(new CardButton { Label = auto ? "Auto: on" : "Auto: off", Detail = "repeat queue", Enabled = true, Click = () => _player.SetAutoQueue(!auto) });
            }
            if (b.GarrisonCount > 0)
            {
                _all.Add(new CardButton { Label = "Ungarrison", Detail = b.GarrisonCount + " inside", Enabled = true, Click = _player.Ungarrison });
            }
        }

        public static string Format(Cost c)
        {
            string s = "";
            if (c.Food > 0) s += c.Food + "F ";
            if (c.Wood > 0) s += c.Wood + "W ";
            if (c.Gold > 0) s += c.Gold + "G ";
            if (c.Stone > 0) s += c.Stone + "S ";
            return s.Length == 0 ? "free" : s.TrimEnd();
        }

        private static string AgeName(AgeId age) => GameData.Ages[(int)age].Name;

        private static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
