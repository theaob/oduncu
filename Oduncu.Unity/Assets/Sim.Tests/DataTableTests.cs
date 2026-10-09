using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    /// <summary>
    /// Checks on the generated tables in Assets/Sim/Generated/Defs.g.cs. Staleness against the
    /// CSV files is checked in CI by `dotnet run --project tools/DataGen -- -check`.
    /// </summary>
    public class DataTableTests
    {
        [Test]
        public void GeneratorUsesTheSimulationTickRate()
        {
            Assert.AreEqual(SimConstants.TicksPerSecond, GameData.GeneratedTicksPerSecond);
        }

        [Test]
        public void EveryRowSitsAtTheIndexOfItsId()
        {
            for (int i = 0; i < GameData.Entities.Length; i++)
            {
                if (GameData.Entities[i] == null) continue;
                Assert.AreEqual(i, (int)GameData.Entities[i].Kind, "entity row " + i);
            }
            for (int i = 0; i < GameData.Techs.Length; i++)
            {
                if (GameData.Techs[i] == null) continue;
                Assert.AreEqual(i, (int)GameData.Techs[i].Id, "tech row " + i);
            }
            for (int i = 0; i < GameData.Ages.Length; i++) Assert.AreEqual(i, (int)GameData.Ages[i].Id, "age row " + i);
        }

        [Test]
        public void EveryTrainedKindIsAUnitWithATrainTime()
        {
            foreach (EntityDef def in GameData.Entities)
            {
                if (def == null) continue;
                foreach (EntityKind kind in def.Trains)
                {
                    Assert.IsTrue(def.IsBuilding, def.Key + " trains units but is not a building");
                    EntityDef unit = EntityDefs.Get(kind);
                    Assert.IsNotNull(unit, def.Key + " trains a missing kind " + kind);
                    Assert.IsTrue(unit.IsUnit, def.Key + " trains " + unit.Key + ", which is not a unit");
                    Assert.Greater(unit.TrainTicks, 0, unit.Key + " has no train time");
                }
            }
        }

        [Test]
        public void EveryTechReferencesExistingBuildingsAndTechs()
        {
            for (int i = 1; i < GameData.Techs.Length; i++)
            {
                TechDef tech = GameData.Techs[i];
                if (tech == null) continue;
                EntityDef at = EntityDefs.Get(tech.ResearchedAt);
                Assert.IsNotNull(at, tech.Key + " researched at a missing kind");
                Assert.IsTrue(at.IsBuilding, tech.Key + " researched at " + at.Key + ", which is not a building");
                Assert.Greater(tech.ResearchTicks, 0, tech.Key);

                // The Requires chain must end without a cycle.
                TechId t = tech.Requires;
                for (int steps = 0; t != TechId.None; steps++)
                {
                    Assert.Less(steps, GameData.Techs.Length, tech.Key + " has a Requires cycle");
                    Assert.IsNotNull(GameData.Techs[(int)t], tech.Key + " requires a missing tech");
                    t = GameData.Techs[(int)t].Requires;
                }
            }
        }

        [Test]
        public void EveryTechEffectHitsAtLeastOneKind()
        {
            foreach (TechEffect fx in GameData.TechEffects)
            {
                Assert.IsNotNull(GameData.Techs[(int)fx.Tech], "effect for missing tech " + fx.Tech);
                if (fx.TargetKind != EntityKind.None) Assert.IsNotNull(EntityDefs.Get(fx.TargetKind));
            }
        }

        [Test]
        public void AgesReferenceExistingBuildingsAndEarlierAges()
        {
            for (int i = 1; i < GameData.Ages.Length; i++)
            {
                AgeDef age = GameData.Ages[i];
                Assert.Less((int)age.RequiredBuildingAge, i, age.Key + " requires buildings from its own or a later age");
                if (age.OrBuilding != EntityKind.None) Assert.IsTrue(EntityDefs.Get(age.OrBuilding).IsBuilding, age.Key);
                Assert.Greater(age.ResearchTicks, 0, age.Key);
            }
        }

        [Test]
        public void UnitsMoveAndHaveHitPoints()
        {
            foreach (EntityDef def in GameData.Entities)
            {
                if (def == null || !def.IsUnit) continue;
                Assert.Greater(def.MaxHp, 0, def.Key);
                Assert.IsTrue(def.Speed > FP.Zero, def.Key + " has no speed");
                Assert.Greater(def.Population, 0, def.Key);
            }
        }

        [Test]
        public void ResourcesYieldSomething()
        {
            foreach (EntityDef def in GameData.Entities)
            {
                if (def == null || !def.IsResource) continue;
                Assert.AreNotEqual(ResourceKind.None, def.Yields, def.Key);
                Assert.Greater(def.ResourceAmount, 0, def.Key);
            }
        }

        [Test]
        public void VillagersCostFood()
        {
            Assert.AreEqual(new Cost(50, 0, 0, 0), EntityDefs.Get(EntityKind.Villager).Cost);
        }
    }
}
