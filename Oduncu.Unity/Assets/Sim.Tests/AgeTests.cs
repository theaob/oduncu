using System.Collections.Generic;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    /// <summary>Milestone 1 step 4 (plan section 3.3): ages, research, tech effects, civilization bonuses, market.</summary>
    public class AgeTests
    {
        private static readonly List<Command> None = new List<Command>();

        private static void Step(Simulation sim, params Command[] commands) => sim.Step(new List<Command>(commands));

        private static Simulation Base(out Entity tc)
        {
            var sim = new Simulation(48, 48, 2, 1);
            tc = sim.SpawnStructure(EntityKind.TownCenter, 0, new Cell(2, 2), false);
            sim.Players[0].Set(new Cost(5000, 5000, 5000, 5000));
            return sim;
        }

        /// <summary>Advance a player through ages directly, as setup for tests that need a later age.</summary>
        private static void ReachAge(Simulation sim, int player, AgeId age)
        {
            for (int a = (int)sim.Players[player].Age + 1; a <= (int)age; a++) sim.CompleteAge(player, (AgeId)a);
        }

        // ------------------------------------------------------------------ age-up

        [Test]
        public void AgeUpNeedsTwoDifferentBuildingsOfTheCurrentAge()
        {
            Entity tc;
            var sim = Base(out tc);
            sim.SpawnStructure(EntityKind.House, 0, new Cell(10, 2), false);
            sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(14, 2), false);
            sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(18, 2), false);
            Step(sim, Command.AgeUp(0, tc.Id));
            Assert.AreEqual("needs 2 different Dark Age buildings", sim.LastRejection, "houses and a second barracks do not count");
            Assert.AreEqual(0, tc.TrainQueue.Count);

            Entity mill = sim.SpawnStructure(EntityKind.Mill, 0, new Cell(22, 2), true);
            Step(sim, Command.AgeUp(0, tc.Id));
            Assert.AreEqual(0, tc.TrainQueue.Count, "a building under construction does not count");

            mill.UnderConstruction = false;
            int food = sim.Players[0].Food;
            Step(sim, Command.AgeUp(0, tc.Id));
            Assert.AreEqual(1, tc.TrainQueue.Count);
            Assert.AreEqual(food - GameData.Ages[(int)AgeId.Feudal].Cost.Food, sim.Players[0].Food);
        }

        [Test]
        public void AgeUpCompletesAfterExactlyTheListedTime()
        {
            Entity tc;
            var sim = Base(out tc);
            sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(14, 2), false);
            sim.SpawnStructure(EntityKind.LumberCamp, 0, new Cell(20, 2), false);
            int ticks = GameData.Ages[(int)AgeId.Feudal].ResearchTicks;
            Assert.AreEqual(600, ticks, "60 seconds");

            Step(sim, Command.AgeUp(0, tc.Id));
            while (sim.CurrentTick < ticks - 1) sim.Step(None);
            Assert.AreEqual(AgeId.Dark, sim.Players[0].Age);
            sim.Step(None);
            Assert.AreEqual(AgeId.Feudal, sim.Players[0].Age);
            Assert.AreEqual(0, tc.TrainQueue.Count);
        }

        [Test]
        public void AgeUpUnlocksTheNextTier()
        {
            Entity tc;
            var sim = Base(out tc);
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(10, 10));
            Step(sim, Command.Build(0, new[] { v.Id }, EntityKind.ArcheryRange, new Cell(14, 14)));
            Assert.AreEqual("requires Feudal Age", sim.LastRejection);

            Entity barracks = sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(20, 2), false);
            Step(sim, Command.Train(0, barracks.Id, EntityKind.Spearman));
            Assert.AreEqual("requires Feudal Age", sim.LastRejection);

            ReachAge(sim, 0, AgeId.Feudal);
            Step(sim, Command.Build(0, new[] { v.Id }, EntityKind.ArcheryRange, new Cell(14, 14)));
            Assert.AreEqual(UnitState.Building, v.State);
            Step(sim, Command.Train(0, barracks.Id, EntityKind.Spearman));
            Assert.AreEqual(EntityKind.Spearman, barracks.TrainQueue[0].Unit);
        }

        [Test]
        public void UnitLinesUpgradeAutomaticallyOnAgeUp()
        {
            Entity tc;
            var sim = Base(out tc);
            Entity militia = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(10, 10));
            Entity enemyMilitia = sim.SpawnUnit(EntityKind.Militia, 1, new Cell(30, 30));
            militia.Hp = 20; // half of 40
            Entity barracks = sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(20, 2), false);
            Step(sim, Command.Train(0, barracks.Id, EntityKind.Militia));

            ReachAge(sim, 0, AgeId.Castle);
            Assert.AreEqual(EntityKind.LongSwordsman, militia.Kind);
            Assert.AreEqual(30, militia.Hp, "keeps half of the new maximum");
            Assert.AreEqual(EntityKind.Militia, enemyMilitia.Kind, "only the player who aged up");

            // A militia queued before the age-up comes out as a long swordsman.
            Entity trained = null;
            for (int t = 0; t < 400 && trained == null; t++)
            {
                sim.Step(None);
                foreach (Entity e in sim.Entities) if (e.Owner == 0 && e.IsUnit && e.Id != militia.Id) trained = e;
            }
            Assert.IsNotNull(trained);
            Assert.AreEqual(EntityKind.LongSwordsman, trained.Kind);

            // Ordering the base unit trains the current line member at its price.
            int gold = sim.Players[0].Gold;
            Step(sim, Command.Train(0, barracks.Id, EntityKind.Militia));
            Assert.AreEqual(EntityKind.LongSwordsman, barracks.TrainQueue[0].Unit);
            Assert.AreEqual(gold - sim.Players[0].Stats.Of(EntityKind.LongSwordsman).Cost.Gold, sim.Players[0].Gold);
        }

        [Test]
        public void ImperialAgeNeedsTwoCastleBuildingsOrACastle()
        {
            Entity tc;
            var sim = Base(out tc);
            ReachAge(sim, 0, AgeId.Castle);
            sim.SpawnStructure(EntityKind.Monastery, 0, new Cell(20, 2), false);
            Assert.AreEqual("needs 2 different Castle Age buildings", sim.AgeUpRejection(0));
            sim.SpawnStructure(EntityKind.Castle, 0, new Cell(20, 20), false);
            Assert.IsNull(sim.AgeUpRejection(0));
        }

        // ------------------------------------------------------------------ research

        [Test]
        public void ResearchingTheSameTechTwiceIsRejected()
        {
            Entity tc;
            var sim = Base(out tc);
            Entity camp = sim.SpawnStructure(EntityKind.LumberCamp, 0, new Cell(20, 2), false);
            Step(sim, Command.Research(0, tc.Id, TechId.Loom));
            Assert.AreEqual(1, tc.TrainQueue.Count);
            Step(sim, Command.Research(0, tc.Id, TechId.Loom));
            Assert.AreEqual("already being researched", sim.LastRejection);
            Step(sim, Command.Research(0, camp.Id, TechId.Loom));
            Assert.AreEqual("already being researched", sim.LastRejection);

            for (int t = 0; t < 300; t++) sim.Step(None);
            Assert.IsTrue(sim.Players[0].Stats.HasResearched(TechId.Loom));
            Step(sim, Command.Research(0, tc.Id, TechId.Loom));
            Assert.AreEqual("already researched", sim.LastRejection);
            Assert.AreEqual(0, tc.TrainQueue.Count);
        }

        [Test]
        public void ResearchChecksAgePrerequisiteBuildingAndOneAtATime()
        {
            Entity tc;
            var sim = Base(out tc);
            Entity camp = sim.SpawnStructure(EntityKind.LumberCamp, 0, new Cell(20, 2), false);
            Step(sim, Command.Research(0, camp.Id, TechId.Woodcutting2));
            Assert.AreEqual("requires Feudal Age", sim.LastRejection);
            ReachAge(sim, 0, AgeId.Feudal);
            Step(sim, Command.Research(0, camp.Id, TechId.Woodcutting2));
            Assert.AreEqual("requires Double-Bit Axe", sim.LastRejection);
            Step(sim, Command.Research(0, tc.Id, TechId.Woodcutting1));
            Assert.AreEqual("researched elsewhere", sim.LastRejection);
            Step(sim, Command.Research(0, tc.Id, TechId.Loom));
            Step(sim, Command.Research(0, tc.Id, TechId.Wheelbarrow));
            Assert.AreEqual("building is already researching", sim.LastRejection);
        }

        [Test]
        public void CancelledResearchIsRefunded()
        {
            Entity tc;
            var sim = Base(out tc);
            Cost before = new Cost(sim.Players[0].Food, sim.Players[0].Wood, sim.Players[0].Gold, sim.Players[0].Stone);
            Step(sim, Command.Research(0, tc.Id, TechId.Loom));
            Step(sim, Command.CancelTrain(0, tc.Id, 0));
            Assert.AreEqual(before, new Cost(sim.Players[0].Food, sim.Players[0].Wood, sim.Players[0].Gold, sim.Players[0].Stone));
            for (int t = 0; t < 300; t++) sim.Step(None);
            Assert.IsFalse(sim.Players[0].Stats.HasResearched(TechId.Loom));
        }

        [Test]
        public void ThereAreAboutThirtyResearchableUpgrades()
        {
            int researchable = 0;
            for (int i = 1; i < GameData.Techs.Length; i++) if (!GameData.Techs[i].IsCivBonus) researchable++;
            Assert.That(researchable, Is.InRange(28, 34));
        }

        // ------------------------------------------------------------------ every tech effect row

        private static IEnumerable<TestCaseData> EffectRows()
        {
            for (int i = 0; i < GameData.TechEffects.Length; i++)
            {
                TechEffect fx = GameData.TechEffects[i];
                yield return new TestCaseData(i).SetName("TechEffect_" + i + "_" + fx.Tech + "_" + fx.Stat);
            }
        }

        [TestCaseSource(nameof(EffectRows))]
        public void TechEffectChangesTheStatByTheListedAmount(int row)
        {
            TechEffect fx = GameData.TechEffects[row];
            EntityDef target = null;
            for (int k = 1; k < GameData.Entities.Length && target == null; k++)
                if (GameData.Entities[k] != null && fx.Applies(GameData.Entities[k])) target = GameData.Entities[k];
            Assert.IsNotNull(target, "row " + row + " targets nothing");

            var before = new PlayerStats();
            var after = new PlayerStats();
            after.Research(fx.Tech);

            // Every row of this tech that hits the same kind and stat, in table order.
            FP expected = Read(before.Of(target.Kind), fx.Stat);
            bool integer = IsInteger(fx.Stat);
            for (int i = 0; i < GameData.TechEffects.Length; i++)
            {
                TechEffect e = GameData.TechEffects[i];
                if (e.Tech != fx.Tech || e.Stat != fx.Stat || !e.Applies(target)) continue;
                if (e.Op == EffectOp.Add) expected = expected + (integer ? FP.FromInt(e.Value.RoundToInt()) : e.Value);
                else expected = integer ? FP.FromInt((expected * e.Value).RoundToInt()) : expected * e.Value;
            }
            Assert.AreEqual(expected, Read(after.Of(target.Kind), fx.Stat), fx.Tech + " on " + target.Key + " " + fx.Stat);
            Assert.AreNotEqual(Read(before.Of(target.Kind), fx.Stat), expected, "the row changes something");
        }

        private static bool IsInteger(StatId stat) => stat != StatId.Range && stat != StatId.Speed && stat != StatId.GatherRate && stat != StatId.Splash;

        private static FP Read(UnitStats s, StatId stat)
        {
            switch (stat)
            {
                case StatId.MaxHp: return s.MaxHp;
                case StatId.Attack: return s.Attack;
                case StatId.MeleeArmor: return s.MeleeArmor;
                case StatId.PierceArmor: return s.PierceArmor;
                case StatId.Range: return s.Range;
                case StatId.Reload: return s.AttackTicks;
                case StatId.Speed: return s.Speed;
                case StatId.LineOfSight: return s.LineOfSight;
                case StatId.TrainTime: return s.TrainTicks;
                case StatId.BuildTime: return s.BuildTicks;
                case StatId.CostFood: return s.Cost.Food;
                case StatId.CostWood: return s.Cost.Wood;
                case StatId.CostGold: return s.Cost.Gold;
                case StatId.CostStone: return s.Cost.Stone;
                case StatId.GatherRate: return s.GatherRate;
                case StatId.CarryCapacity: return s.CarryCapacity;
                case StatId.Splash: return s.SplashRadius;
                default: throw new System.ArgumentOutOfRangeException(nameof(stat));
            }
        }

        // ------------------------------------------------------------------ civilization

        [Test]
        public void WoodlandersBonusesApplyAtStartAndInTheCastleAge()
        {
            var sim = new Simulation(32, 32, 2, 1);
            sim.SetCivilization(0, CivId.Woodlanders);
            var plain = new PlayerStats();
            Assert.AreEqual(plain.Of(EntityKind.LumberCamp).Cost.Wood / 2, sim.Players[0].Stats.Of(EntityKind.LumberCamp).Cost.Wood);
            Assert.IsTrue(sim.Players[0].Stats.Of(EntityKind.Tree).GatherRate > plain.Of(EntityKind.Tree).GatherRate, "wood is gathered faster");
            Assert.AreEqual(plain.Of(EntityKind.Archer).Range, sim.Players[0].Stats.Of(EntityKind.Archer).Range, "range bonus waits for the Castle Age");
            Assert.AreEqual(plain.Of(EntityKind.LumberCamp).Cost, sim.Players[1].Stats.Of(EntityKind.LumberCamp).Cost, "player 1 has no civilization");

            sim.CompleteAge(0, AgeId.Feudal);
            sim.CompleteAge(0, AgeId.Castle);
            Assert.AreEqual(plain.Of(EntityKind.Crossbowman).Range + FP.One, sim.Players[0].Stats.Of(EntityKind.Crossbowman).Range);
        }

        [Test]
        public void UniqueTechsBelongToTheirCivilization()
        {
            var sim = new Simulation(48, 48, 2, 1);
            sim.SetCivilization(0, CivId.Woodlanders);
            for (int p = 0; p < 2; p++)
            {
                sim.Players[p].Set(new Cost(5000, 5000, 5000, 5000));
                ReachAge(sim, p, AgeId.Castle);
            }
            Entity c0 = sim.SpawnStructure(EntityKind.Castle, 0, new Cell(4, 4), false);
            Entity c1 = sim.SpawnStructure(EntityKind.Castle, 1, new Cell(30, 30), false);
            Step(sim, Command.Research(1, c1.Id, TechId.Yeomen));
            Assert.AreEqual("not available to your civilization", sim.LastRejection);
            Step(sim, Command.Research(0, c0.Id, TechId.Yeomen));
            Assert.AreEqual(1, c0.TrainQueue.Count);
            Step(sim, Command.Research(0, c0.Id, TechId.WoodlandersForestry));
            Assert.AreEqual("cannot research that", sim.LastRejection);
        }

        // ------------------------------------------------------------------ market

        [Test]
        public void MarketPricesMoveWithTrades()
        {
            Entity tc;
            var sim = Base(out tc);
            Entity market = sim.SpawnStructure(EntityKind.Market, 0, new Cell(20, 2), false);
            int buy = sim.BuyPrice(ResourceKind.Wood);
            int gold = sim.Players[0].Gold, wood = sim.Players[0].Wood;
            Step(sim, Command.MarketBuy(0, market.Id, ResourceKind.Wood));
            Assert.AreEqual(gold - buy, sim.Players[0].Gold);
            Assert.AreEqual(wood + Simulation.MarketLot, sim.Players[0].Wood);
            Assert.Greater(sim.BuyPrice(ResourceKind.Wood), buy, "buying raises the price");

            int sell = sim.SellPrice(ResourceKind.Stone);
            gold = sim.Players[0].Gold;
            Step(sim, Command.MarketSell(0, market.Id, ResourceKind.Stone));
            Assert.AreEqual(gold + sell, sim.Players[0].Gold);
            Assert.Less(sim.SellPrice(ResourceKind.Stone), sell, "selling lowers the price");

            Step(sim, Command.MarketBuy(0, market.Id, ResourceKind.Gold));
            Assert.AreEqual("the market trades food, wood and stone", sim.LastRejection);
            sim.Players[0].Set(new Cost(0, 0, 0, 0));
            Step(sim, Command.MarketSell(0, market.Id, ResourceKind.Food));
            Assert.AreEqual("not enough food", sim.LastRejection);
        }
    }
}
