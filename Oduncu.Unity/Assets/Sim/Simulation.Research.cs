namespace Oduncu.Sim
{
    public sealed partial class Simulation
    {
        /// <summary>Every market trade moves this many units of food, wood or stone.</summary>
        public const int MarketLot = 100;
        public const int MarketStartPrice = 100;
        /// <summary>Each buy raises the price and each sell lowers it by this much (gold per lot).</summary>
        public const int MarketPriceStep = 5;
        public const int MarketMinPrice = 20;
        public const int MarketMaxPrice = 999;

        /// <summary>Market prices in gold per lot, indexed by ResourceKind. Shared by every player, as in AoE2.</summary>
        private readonly int[] _marketPrice = { MarketStartPrice, MarketStartPrice, 0, MarketStartPrice };
        private readonly bool[] _scratchKinds = new bool[GameData.EntityKindCount];

        // ------------------------------------------------------------------ civilizations

        /// <summary>
        /// Set a player's civilization during match setup, before the first tick, and grant the
        /// bonuses it has from the start (and from any age already reached).
        /// </summary>
        public void SetCivilization(int player, CivId civ)
        {
            Players[player].Civ = civ;
            GrantCivBonuses(player);
        }

        private void GrantCivBonuses(int player)
        {
            PlayerState p = Players[player];
            if (p.Civ == CivId.None) return;
            TechDef[] techs = GameData.Techs;
            for (int t = 1; t < techs.Length; t++)
            {
                TechDef def = techs[t];
                if (def.IsCivBonus && def.Civ == p.Civ && def.MinAge <= p.Age) CompleteResearch(player, def.Id);
            }
        }

        // ------------------------------------------------------------------ ages

        /// <summary>The member of a unit line a player in this age gets: Militia becomes Long Swordsman in the Castle Age.</summary>
        public static EntityKind LineMemberFor(EntityKind kind, AgeId age)
        {
            EntityKind k = kind;
            while (true)
            {
                EntityKind next = EntityDefs.Get(k).UpgradesTo;
                if (next == EntityKind.None || EntityDefs.Get(next).MinAge > age) return k;
                k = next;
            }
        }

        private static string RequiresAge(AgeId age) => "requires " + GameData.Ages[(int)age].Name;

        /// <summary>Why the player cannot start researching the next age now, or null if they can.</summary>
        public string AgeUpRejection(int player)
        {
            PlayerState p = Players[player];
            if ((int)p.Age + 1 >= GameData.AgeCount) return "already in the last age";
            if (IsQueuedAnywhere(player, TechId.None, (AgeId)((int)p.Age + 1))) return "already advancing";
            AgeDef next = GameData.Ages[(int)p.Age + 1];
            if (next.OrBuilding != EntityKind.None && OwnsFinished(player, next.OrBuilding)) return null;
            int have = CountRequirementBuildings(player, next.RequiredBuildingAge);
            if (have >= next.RequiredBuildings) return null;
            return "needs " + next.RequiredBuildings + " different " + GameData.Ages[(int)next.RequiredBuildingAge].Name + " buildings";
        }

        /// <summary>
        /// Different kinds of finished building from an age that count toward the next age:
        /// not the Town Center, houses or farms.
        /// </summary>
        private int CountRequirementBuildings(int player, AgeId age)
        {
            bool[] seen = _scratchKinds;
            for (int k = 0; k < seen.Length; k++) seen[k] = false;
            int n = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive || e.Owner != player || !e.IsBuilding || e.UnderConstruction) continue;
                EntityDef d = e.Def;
                if (d.MinAge != age || d.Kind == EntityKind.TownCenter || d.Housing > 0 || d.IsGatherable) continue;
                if (seen[(int)d.Kind]) continue;
                seen[(int)d.Kind] = true;
                n++;
            }
            return n;
        }

        private bool OwnsFinished(int player, EntityKind kind)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (e.Alive && e.Owner == player && e.Kind == kind && !e.UnderConstruction) return true;
            }
            return false;
        }

        /// <summary>Whether any of the player's buildings has this tech (or age, when tech is None) queued.</summary>
        private bool IsQueuedAnywhere(int player, TechId tech, AgeId age)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive || e.Owner != player || !e.IsBuilding) continue;
                for (int q = 0; q < e.TrainQueue.Count; q++)
                {
                    QueueItem item = e.TrainQueue[q];
                    if (!item.IsResearch) continue;
                    if (tech != TechId.None ? item.Tech == tech : item.Age == age) return true;
                }
            }
            return false;
        }

        private static int ResearchTicksOf(QueueItem item)
        {
            return item.IsAgeUp ? GameData.Ages[(int)item.Age].ResearchTicks : GameData.Techs[(int)item.Tech].ResearchTicks;
        }

        /// <summary>
        /// Advance a player to an age: grant the civilization's bonuses for it and upgrade every
        /// unit line automatically (plan open decision 2). Upgraded units keep their share of hit points.
        /// </summary>
        public void CompleteAge(int player, AgeId age)
        {
            PlayerState p = Players[player];
            if (age <= p.Age) return;
            p.Age = age;
            GrantCivBonuses(player);
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive || e.Owner != player || !e.IsUnit) continue;
                EntityKind upgraded = LineMemberFor(e.Kind, age);
                if (upgraded == e.Kind) continue;
                int oldMax = e.Stats.MaxHp;
                e.Kind = upgraded;
                e.Def = EntityDefs.Get(upgraded);
                e.Stats = StatsFor(player, upgraded);
                e.Hp = (int)((long)e.Hp * e.Stats.MaxHp / oldMax);
                if (e.Hp < 1) e.Hp = 1;
            }
        }

        /// <summary>Why the player cannot queue this tech now, or null if they can.</summary>
        public string ResearchRejection(int player, TechId tech)
        {
            if (tech <= TechId.None || (int)tech >= GameData.TechCount) return "unknown tech";
            TechDef def = GameData.Techs[(int)tech];
            PlayerState p = Players[player];
            if (def.IsCivBonus) return "cannot research that";
            if (def.Civ != CivId.None && def.Civ != p.Civ) return "not available to your civilization";
            if (p.Stats.HasResearched(tech)) return "already researched";
            if (p.Age < def.MinAge) return RequiresAge(def.MinAge);
            if (def.Requires != TechId.None && !p.Stats.HasResearched(def.Requires)) return "requires " + GameData.Techs[(int)def.Requires].Name;
            if (IsQueuedAnywhere(player, tech, AgeId.Dark)) return "already being researched";
            return null;
        }

        private static bool IsResearching(Entity b)
        {
            for (int q = 0; q < b.TrainQueue.Count; q++) if (b.TrainQueue[q].IsResearch) return true;
            return false;
        }

        private void ApplyResearch(Command c)
        {
            Entity b = OwnedBuilding(c);
            if (b == null) { Reject("not your building"); return; }
            if (b.UnderConstruction) { Reject("building under construction"); return; }
            TechId tech = (TechId)c.Arg;
            string why = ResearchRejection(c.Player, tech);
            if (why != null) { Reject(why); return; }
            if (GameData.Techs[(int)tech].ResearchedAt != b.Kind) { Reject("researched elsewhere"); return; }
            if (IsResearching(b)) { Reject("building is already researching"); return; }
            if (b.TrainQueue.Count >= SimConstants.TrainQueueLength) { Reject("queue full"); return; }
            PlayerState player = Players[c.Player];
            Cost cost = GameData.Techs[(int)tech].Cost;
            if (!player.CanAfford(cost)) { Reject(player.ShortageMessage(cost)); return; }
            player.Pay(cost);
            b.TrainQueue.Add(new QueueItem { Tech = tech, Paid = cost });
        }

        private void ApplyAgeUp(Command c)
        {
            Entity b = OwnedBuilding(c);
            if (b == null || b.Kind != EntityKind.TownCenter) { Reject("age up at a Town Center"); return; }
            if (b.UnderConstruction) { Reject("building under construction"); return; }
            string why = AgeUpRejection(c.Player);
            if (why != null) { Reject(why); return; }
            if (IsResearching(b)) { Reject("building is already researching"); return; }
            if (b.TrainQueue.Count >= SimConstants.TrainQueueLength) { Reject("queue full"); return; }
            PlayerState player = Players[c.Player];
            AgeId next = (AgeId)((int)player.Age + 1);
            Cost cost = GameData.Ages[(int)next].Cost;
            if (!player.CanAfford(cost)) { Reject(player.ShortageMessage(cost)); return; }
            player.Pay(cost);
            b.TrainQueue.Add(new QueueItem { Age = next, Paid = cost });
        }

        // ------------------------------------------------------------------ market

        /// <summary>Gold to buy one lot of a resource.</summary>
        public int BuyPrice(ResourceKind kind) => _marketPrice[(int)kind] * 13 / 10;

        /// <summary>Gold received for selling one lot of a resource.</summary>
        public int SellPrice(ResourceKind kind) => _marketPrice[(int)kind] * 7 / 10;

        private static bool IsTradable(int arg) => arg == (int)ResourceKind.Food || arg == (int)ResourceKind.Wood || arg == (int)ResourceKind.Stone;

        private Entity OwnedMarket(Command c)
        {
            Entity b = OwnedBuilding(c);
            if (b == null || b.Kind != EntityKind.Market || b.UnderConstruction) { Reject("needs a finished market"); return null; }
            if (!IsTradable(c.Arg)) { Reject("the market trades food, wood and stone"); return null; }
            return b;
        }

        private void ApplyMarketBuy(Command c)
        {
            if (OwnedMarket(c) == null) return;
            var kind = (ResourceKind)c.Arg;
            PlayerState p = Players[c.Player];
            int price = BuyPrice(kind);
            if (p.Gold < price) { Reject("not enough gold"); return; }
            p.Gold -= price;
            p.Add(kind, MarketLot);
            _marketPrice[c.Arg] = System.Math.Min(MarketMaxPrice, _marketPrice[c.Arg] + MarketPriceStep);
        }

        private void ApplyMarketSell(Command c)
        {
            if (OwnedMarket(c) == null) return;
            var kind = (ResourceKind)c.Arg;
            PlayerState p = Players[c.Player];
            if (p.Get(kind) < MarketLot) { Reject(p.ShortageMessage(Cost.Of(kind, MarketLot))); return; }
            p.Add(kind, -MarketLot);
            p.Gold += SellPrice(kind);
            _marketPrice[c.Arg] = System.Math.Max(MarketMinPrice, _marketPrice[c.Arg] - MarketPriceStep);
        }

        private void WriteMarketState(StateHasher h)
        {
            for (int i = 0; i < _marketPrice.Length; i++) h.Write(_marketPrice[i]);
        }
    }
}
