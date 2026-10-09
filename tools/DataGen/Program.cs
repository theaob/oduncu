using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Oduncu.Tools.DataGen
{
    /// <summary>
    /// Reads the balance tables in Oduncu.Unity/Assets/Data and writes plain C# initialisers to
    /// Oduncu.Unity/Assets/Sim/Generated/Defs.g.cs. The simulation never parses files, so it
    /// stays free of IO and string handling, and every balance change is a reviewable diff.
    ///
    ///   dotnet run --project tools/DataGen            regenerate
    ///   dotnet run --project tools/DataGen -- -check  exit 1 if the committed file is stale
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool check = args.Contains("-check") || args.Contains("--check");
            string root = FindRepoRoot();
            if (root == null)
            {
                Console.Error.WriteLine("error: run from inside the oduncu repository");
                return 2;
            }
            string dataDir = Path.Combine(root, "Oduncu.Unity", "Assets", "Data");
            string outFile = Path.Combine(root, "Oduncu.Unity", "Assets", "Sim", "Generated", "Defs.g.cs");

            string code;
            try
            {
                code = Generator.Generate(dataDir);
            }
            catch (DataException e)
            {
                Console.Error.WriteLine("error: " + e.Message);
                return 2;
            }

            if (check)
            {
                string existing = File.Exists(outFile) ? File.ReadAllText(outFile).Replace("\r\n", "\n") : "";
                if (existing != code)
                {
                    Console.Error.WriteLine("error: " + Relative(root, outFile) + " is stale. Run `dotnet run --project tools/DataGen` and commit the result.");
                    return 1;
                }
                Console.WriteLine(Relative(root, outFile) + " is up to date.");
                return 0;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            File.WriteAllText(outFile, code, new UTF8Encoding(false));
            Console.WriteLine("Wrote " + Relative(root, outFile));
            return 0;
        }

        private static string FindRepoRoot()
        {
            string dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "Oduncu.Unity", "Assets", "Data"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    internal sealed class DataException : Exception
    {
        public DataException(string message) : base(message) { }
    }

    /// <summary>A comma-separated table with a header row. Blank lines and lines starting with # are ignored.</summary>
    internal sealed class Table
    {
        public readonly string File;
        public readonly List<Row> Rows = new List<Row>();

        public sealed class Row
        {
            public string File;
            public int Line;
            public Dictionary<string, string> Cells;

            public string this[string column]
            {
                get
                {
                    if (!Cells.TryGetValue(column, out string v)) throw Error("missing column " + column);
                    return v;
                }
            }

            public DataException Error(string message) => new DataException(File + ":" + Line + ": " + message);
        }

        public Table(string dir, string name)
        {
            File = name;
            string path = Path.Combine(dir, name);
            if (!System.IO.File.Exists(path)) throw new DataException("missing " + path);
            string[] lines = System.IO.File.ReadAllLines(path);
            string[] header = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] cells = line.Split(',').Select(c => c.Trim()).ToArray();
                if (header == null)
                {
                    header = cells;
                    continue;
                }
                if (cells.Length != header.Length)
                    throw new DataException(name + ":" + (i + 1) + ": expected " + header.Length + " columns, found " + cells.Length);
                var row = new Row { File = name, Line = i + 1, Cells = new Dictionary<string, string>() };
                for (int c = 0; c < header.Length; c++) row.Cells[header[c]] = cells[c];
                Rows.Add(row);
            }
            if (header == null) throw new DataException(name + ": no header row");
        }
    }

    internal static class Generator
    {
        /// <summary>Must match SimConstants.TicksPerSecond; a simulation test checks the two agree.</summary>
        public const int TicksPerSecond = 10;

        private static readonly string[] Stats =
        {
            "MaxHp", "Attack", "MeleeArmor", "PierceArmor", "Range", "Reload", "Speed", "LineOfSight",
            "TrainTime", "BuildTime", "CostFood", "CostWood", "CostGold", "CostStone", "GatherRate",
            "CarryCapacity", "Splash",
        };

        private static readonly string[] Resources = { "Food", "Wood", "Gold", "Stone" };

        private static readonly Regex Identifier = new Regex("^[A-Z][A-Za-z0-9]*$");
        private static readonly Regex TagKey = new Regex("^[a-z][a-z0-9_]*$");
        private static readonly Regex Number = new Regex(@"^-?\d+(\.\d+)?$");

        private sealed class Entity
        {
            public Table.Row Row;
            public int Id;
            public string Key;
        }

        public static string Generate(string dataDir)
        {
            var tags = new Table(dataDir, "tags.csv");
            var ages = new Table(dataDir, "ages.csv");
            var entities = new Table(dataDir, "entities.csv");
            var techs = new Table(dataDir, "techs.csv");
            var effects = new Table(dataDir, "tech_effects.csv");
            var presets = new Table(dataDir, "economy_presets.csv");

            // ---------------------------------------------------------------- keys and ids
            var tagIds = new SortedDictionary<int, string>();
            var tagByKey = new Dictionary<string, string>();
            foreach (var r in tags.Rows)
            {
                int id = Int(r, "Id");
                string key = r["Key"];
                if (!TagKey.IsMatch(key)) throw r.Error("tag key must be lower_snake_case: " + key);
                if (id < 0 || id > 31) throw r.Error("tag id must be 0..31");
                if (tagIds.ContainsKey(id)) throw r.Error("duplicate tag id " + id);
                if (tagByKey.ContainsKey(key)) throw r.Error("duplicate tag " + key);
                tagIds[id] = key;
                tagByKey[key] = Pascal(key);
            }

            var ageIds = new SortedDictionary<int, Table.Row>();
            var ageKeys = new HashSet<string>();
            foreach (var r in ages.Rows)
            {
                int id = Int(r, "Id");
                string key = r["Key"];
                RequireIdentifier(r, key);
                if (ageIds.ContainsKey(id) || !ageKeys.Add(key)) throw r.Error("duplicate age " + key);
                ageIds[id] = r;
            }
            for (int i = 0; i < ageIds.Count; i++)
                if (!ageIds.ContainsKey(i)) throw new DataException("ages.csv: ages must be numbered 0.." + (ageIds.Count - 1));

            var entityById = new SortedDictionary<int, Entity>();
            var entityByKey = new Dictionary<string, Entity>();
            foreach (var r in entities.Rows)
            {
                var e = new Entity { Row = r, Id = Int(r, "Id"), Key = r["Key"] };
                RequireIdentifier(r, e.Key);
                if (e.Id < 1 || e.Id > 255) throw r.Error("entity id must be 1..255");
                if (e.Key == "None") throw r.Error("None is reserved");
                if (entityById.ContainsKey(e.Id) || entityByKey.ContainsKey(e.Key)) throw r.Error("duplicate entity " + e.Key);
                entityById[e.Id] = e;
                entityByKey[e.Key] = e;
            }

            var techById = new SortedDictionary<int, Table.Row>();
            var techKeys = new HashSet<string>();
            var civs = new List<string>();
            foreach (var r in techs.Rows)
            {
                string civ = r["Civ"];
                if (civ.Length != 0)
                {
                    RequireIdentifier(r, civ);
                    if (civ == "None") throw r.Error("None is reserved");
                    if (!civs.Contains(civ)) civs.Add(civ);
                }
                int id = Int(r, "Id");
                string key = r["Key"];
                RequireIdentifier(r, key);
                if (id < 1 || id > 255) throw r.Error("tech id must be 1..255");
                if (key == "None") throw r.Error("None is reserved");
                if (techById.ContainsKey(id) || !techKeys.Add(key)) throw r.Error("duplicate tech " + key);
                techById[id] = r;
            }

            // ---------------------------------------------------------------- output
            var sb = new StringBuilder();
            sb.Append("// <auto-generated>\n");
            sb.Append("// Generated by tools/DataGen from Oduncu.Unity/Assets/Data/*.csv. Do not edit by hand:\n");
            sb.Append("// change the CSV files and run `dotnet run --project tools/DataGen` from the repository root.\n");
            sb.Append("// </auto-generated>\n");
            sb.Append("namespace Oduncu.Sim\n{\n");

            // Enums
            sb.Append("    public enum EntityKind : byte\n    {\n        None = 0,\n");
            foreach (var e in entityById.Values) sb.Append("        ").Append(e.Key).Append(" = ").Append(e.Id).Append(",\n");
            sb.Append("    }\n\n");

            sb.Append("    [System.Flags]\n    public enum EntityTag : uint\n    {\n        None = 0,\n");
            foreach (var kv in tagIds) sb.Append("        ").Append(Pascal(kv.Value)).Append(" = 1u << ").Append(kv.Key).Append(",\n");
            sb.Append("    }\n\n");

            sb.Append("    public enum AgeId : byte\n    {\n");
            foreach (var kv in ageIds) sb.Append("        ").Append(kv.Value["Key"]).Append(" = ").Append(kv.Key).Append(",\n");
            sb.Append("    }\n\n");

            sb.Append("    public enum TechId : byte\n    {\n        None = 0,\n");
            foreach (var kv in techById) sb.Append("        ").Append(kv.Value["Key"]).Append(" = ").Append(kv.Key).Append(",\n");
            sb.Append("    }\n\n");

            sb.Append("    public enum CivId : byte\n    {\n        None = 0,\n");
            for (int i = 0; i < civs.Count; i++) sb.Append("        ").Append(civs[i]).Append(" = ").Append(i + 1).Append(",\n");
            sb.Append("    }\n\n");

            sb.Append("    public static partial class GameData\n    {\n");
            sb.Append("        public const int GeneratedTicksPerSecond = ").Append(TicksPerSecond).Append(";\n");
            sb.Append("        public const int EntityKindCount = ").Append(entityById.Keys.Max() + 1).Append(";\n");
            sb.Append("        public const int TechCount = ").Append(techById.Count == 0 ? 1 : techById.Keys.Max() + 1).Append(";\n");
            sb.Append("        public const int AgeCount = ").Append(ageIds.Count).Append(";\n");
            sb.Append("        public const int EconomyPresetCount = ").Append(presets.Rows.Count).Append(";\n\n");

            // Entities
            sb.Append("        private static EntityDef[] CreateEntities()\n        {\n");
            sb.Append("            var t = new EntityDef[EntityKindCount];\n");
            sb.Append("            t[0] = new EntityDef { Kind = EntityKind.None, Key = \"None\", Name = \"None\" };\n");
            foreach (var e in entityById.Values) AppendEntity(sb, e, entityByKey, tagByKey, ageKeys);
            sb.Append("            return t;\n        }\n\n");

            // Techs
            sb.Append("        private static TechDef[] CreateTechs()\n        {\n");
            sb.Append("            var t = new TechDef[TechCount];\n");
            sb.Append("            t[0] = new TechDef { Id = TechId.None, Key = \"None\", Name = \"None\" };\n");
            foreach (var kv in techById) AppendTech(sb, kv.Value, entityByKey, techKeys, ageKeys);
            sb.Append("            return t;\n        }\n\n");

            // Effects
            sb.Append("        private static TechEffect[] CreateTechEffects()\n        {\n");
            sb.Append("            return new[]\n            {\n");
            foreach (var r in effects.Rows) AppendEffect(sb, r, entityByKey, tagByKey, techKeys);
            sb.Append("            };\n        }\n\n");

            // Ages
            sb.Append("        private static AgeDef[] CreateAges()\n        {\n");
            sb.Append("            var t = new AgeDef[AgeCount];\n");
            foreach (var kv in ageIds) AppendAge(sb, kv.Value, entityByKey, ageKeys);
            sb.Append("            return t;\n        }\n\n");

            // Economy planner presets
            sb.Append("        private static EconomyPreset[] CreateEconomyPresets()\n        {\n");
            sb.Append("            return new[]\n            {\n");
            var seenPresets = new HashSet<string>();
            foreach (var r in presets.Rows) AppendPreset(sb, r, ageKeys, seenPresets);
            sb.Append("            };\n        }\n");

            sb.Append("    }\n}\n");
            return sb.ToString();
        }

        private static void AppendEntity(StringBuilder sb, Entity e, Dictionary<string, Entity> byKey, Dictionary<string, string> tagByKey, HashSet<string> ageKeys)
        {
            var r = e.Row;
            string category = r["Category"];
            string categoryCode;
            switch (category)
            {
                case "unit": categoryCode = "EntityCategory.Unit"; break;
                case "building": categoryCode = "EntityCategory.Building"; break;
                case "resource": categoryCode = "EntityCategory.Resource"; break;
                default: throw r.Error("Category must be unit, building or resource");
            }

            string tagList = r["Tags"];
            if (category == "building") tagList = tagList.Length == 0 ? "building" : tagList + ";building";
            string tags = TagExpression(r, tagList, tagByKey);
            string age = r["MinAge"];
            if (!ageKeys.Contains(age)) throw r.Error("unknown age " + age);

            string yields = r["Yields"];
            string yieldsCode;
            if (category == "resource" && yields.Length == 0) throw r.Error("resources must have Yields");
            if (yields.Length != 0)
            {
                // Resources, animals (units) and farms (buildings) can all be gathered.
                if (!Resources.Contains(yields)) throw r.Error("Yields must be Food, Wood, Gold or Stone");
                if (Int(r, "ResourceAmount") <= 0) throw r.Error("gatherable entities need a ResourceAmount");
                ParseDecimal(r, "GatherRate", out long rateNum, out long _);
                if (rateNum <= 0) throw r.Error("gatherable entities need a GatherRate");
                yieldsCode = "ResourceKind." + yields;
            }
            else
            {
                yieldsCode = "ResourceKind.None";
            }

            if (category == "unit")
            {
                if (Int(r, "MaxHp") <= 0) throw r.Error("units need MaxHp");
                if (Int(r, "Size") != 1) throw r.Error("units have Size 1");
            }
            if (category == "building" && Int(r, "MaxHp") <= 0) throw r.Error("buildings need MaxHp");

            var trains = new List<string>();
            foreach (string t in SplitList(r["Trains"]))
            {
                if (!byKey.TryGetValue(t, out Entity target)) throw r.Error("Trains references unknown entity " + t);
                if (target.Row["Category"] != "unit") throw r.Error("Trains references " + t + ", which is not a unit");
                if (category != "building") throw r.Error("only buildings train units");
                if (Seconds(target.Row, "TrainSeconds") <= 0) throw r.Error(t + " is trainable but has no TrainSeconds");
                trains.Add("EntityKind." + t);
            }

            string attackType = r["AttackType"];
            if (attackType.Length == 0) attackType = "melee";
            if (attackType != "melee" && attackType != "pierce") throw r.Error("AttackType must be melee or pierce");

            var bonuses = new List<string>();
            foreach (string pair in SplitList(r["Bonus"]))
            {
                string[] kv = pair.Split(':');
                if (kv.Length != 2 || !tagByKey.TryGetValue(kv[0], out string bonusTag)) throw r.Error("Bonus entries are tag:amount with a known tag, got " + pair);
                if (!int.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out int amount)) throw r.Error("bad bonus amount in " + pair);
                bonuses.Add("new BonusDamage(EntityTag." + bonusTag + ", " + amount + ")");
            }

            string upgrades = r["UpgradesTo"];
            if (upgrades.Length != 0)
            {
                if (!byKey.TryGetValue(upgrades, out Entity next) || next.Row["Category"] != "unit") throw r.Error("UpgradesTo must be a unit, got " + upgrades);
                if (category != "unit") throw r.Error("only units upgrade");
            }

            sb.Append("            t[").Append(e.Id).Append("] = new EntityDef\n            {\n");
            sb.Append("                Kind = EntityKind.").Append(e.Key)
              .Append(", Key = \"").Append(e.Key).Append("\", Name = \"").Append(Escape(r["Name"])).Append("\"")
              .Append(", Category = ").Append(categoryCode).Append(",\n");
            sb.Append("                Tags = ").Append(tags).Append(", MinAge = AgeId.").Append(age).Append(",\n");
            sb.Append("                MaxHp = ").Append(Int(r, "MaxHp"))
              .Append(", Attack = ").Append(Int(r, "Attack"))
              .Append(", MeleeArmor = ").Append(Int(r, "MeleeArmor"))
              .Append(", PierceArmor = ").Append(Int(r, "PierceArmor")).Append(",\n");
            sb.Append("                AttackType = AttackType.").Append(attackType == "melee" ? "Melee" : "Pierce")
              .Append(", MinRange = ").Append(Fp(r, "MinRange", 1))
              .Append(", ProjectileSpeed = ").Append(Fp(r, "ProjectileSpeed", TicksPerSecond))
              .Append(", SplashRadius = ").Append(Fp(r, "SplashRadius", 1)).Append(",\n");
            sb.Append("                Bonuses = ");
            if (bonuses.Count == 0) sb.Append("System.Array.Empty<BonusDamage>()");
            else sb.Append("new[] { ").Append(string.Join(", ", bonuses)).Append(" }");
            sb.Append(",\n");
            sb.Append("                Arrows = ").Append(Int(r, "Arrows")).Append(", GarrisonCapacity = ").Append(Int(r, "GarrisonCapacity"))
              .Append(", UpgradesTo = EntityKind.").Append(upgrades.Length == 0 ? "None" : upgrades).Append(",\n");
            sb.Append("                Range = ").Append(Fp(r, "Range", 1)).Append(", AttackTicks = ").Append(Seconds(r, "ReloadSeconds"))
              .Append(", Speed = ").Append(Fp(r, "Speed", TicksPerSecond)).Append(", LineOfSight = ").Append(Int(r, "LineOfSight"))
              .Append(", Size = ").Append(Int(r, "Size")).Append(",\n");
            sb.Append("                Cost = new Cost(").Append(Int(r, "Food")).Append(", ").Append(Int(r, "Wood")).Append(", ")
              .Append(Int(r, "Gold")).Append(", ").Append(Int(r, "Stone")).Append("),\n");
            sb.Append("                TrainTicks = ").Append(Seconds(r, "TrainSeconds"))
              .Append(", BuildTicks = ").Append(Seconds(r, "BuildSeconds"))
              .Append(", Population = ").Append(Int(r, "Population"))
              .Append(", Housing = ").Append(Int(r, "Housing")).Append(",\n");
            sb.Append("                ResourceAmount = ").Append(Int(r, "ResourceAmount"))
              .Append(", Yields = ").Append(yieldsCode)
              .Append(", GatherRate = ").Append(Fp(r, "GatherRate", TicksPerSecond)).Append(",\n");
            sb.Append("                Trains = ");
            if (trains.Count == 0) sb.Append("System.Array.Empty<EntityKind>()");
            else sb.Append("new[] { ").Append(string.Join(", ", trains)).Append(" }");
            sb.Append(",\n            };\n");
        }

        private static void AppendTech(StringBuilder sb, Table.Row r, Dictionary<string, Entity> byKey, HashSet<string> techKeys, HashSet<string> ageKeys)
        {
            // A row with a Civ and no ResearchedAt is a civilization bonus, granted on reaching MinAge.
            string civ = r["Civ"];
            string at = r["ResearchedAt"];
            bool bonus = at.Length == 0;
            if (bonus && civ.Length == 0) throw r.Error("ResearchedAt is required unless the row is a civilization bonus");
            if (bonus) at = "None";
            else if (!byKey.TryGetValue(at, out Entity building) || building.Row["Category"] != "building")
                throw r.Error("ResearchedAt must be a building, got " + at);
            string age = r["MinAge"];
            if (!ageKeys.Contains(age)) throw r.Error("unknown age " + age);
            string requires = r["Requires"];
            if (requires.Length != 0 && !techKeys.Contains(requires)) throw r.Error("Requires unknown tech " + requires);
            if (requires == r["Key"]) throw r.Error("a tech cannot require itself");
            int ticks = Seconds(r, "ResearchSeconds");
            if (!bonus && ticks <= 0) throw r.Error("ResearchSeconds must be positive");

            sb.Append("            t[").Append(Int(r, "Id")).Append("] = new TechDef\n            {\n");
            sb.Append("                Id = TechId.").Append(r["Key"]).Append(", Key = \"").Append(r["Key"])
              .Append("\", Name = \"").Append(Escape(r["Name"])).Append("\",\n");
            sb.Append("                Description = \"").Append(Escape(r["Description"])).Append("\",\n");
            sb.Append("                ResearchedAt = EntityKind.").Append(at).Append(", MinAge = AgeId.").Append(age)
              .Append(", Cost = new Cost(").Append(Int(r, "Food")).Append(", ").Append(Int(r, "Wood")).Append(", ")
              .Append(Int(r, "Gold")).Append(", ").Append(Int(r, "Stone")).Append("),\n");
            sb.Append("                ResearchTicks = ").Append(ticks).Append(", Requires = TechId.")
              .Append(requires.Length == 0 ? "None" : requires).Append(", Civ = CivId.")
              .Append(civ.Length == 0 ? "None" : civ).Append(",\n            };\n");
        }

        private static void AppendEffect(StringBuilder sb, Table.Row r, Dictionary<string, Entity> byKey, Dictionary<string, string> tagByKey, HashSet<string> techKeys)
        {
            string tech = r["Tech"];
            if (!techKeys.Contains(tech)) throw r.Error("unknown tech " + tech);

            string target = r["Target"];
            string kindCode = "EntityKind.None", tagCode = "EntityTag.None";
            if (target.StartsWith("tag:"))
            {
                string tag = target.Substring(4);
                if (!tagByKey.TryGetValue(tag, out string pascal)) throw r.Error("unknown tag " + tag);
                tagCode = "EntityTag." + pascal;
            }
            else if (target != "all")
            {
                if (!byKey.ContainsKey(target)) throw r.Error("unknown target " + target);
                kindCode = "EntityKind." + target;
            }

            string stat = r["Stat"];
            if (!Stats.Contains(stat)) throw r.Error("unknown stat " + stat + "; expected one of " + string.Join(", ", Stats));

            string op = r["Op"];
            string value;
            if (op == "mul")
            {
                value = Fp(r, "Value", 1);
            }
            else if (op == "add")
            {
                if (stat == "Speed" || stat == "GatherRate") value = Fp(r, "Value", TicksPerSecond);
                else if (stat == "Reload" || stat == "TrainTime" || stat == "BuildTime") value = "FP.FromInt(" + Seconds(r, "Value") + ")";
                else value = Fp(r, "Value", 1);
            }
            else throw r.Error("Op must be add or mul");

            sb.Append("                new TechEffect(TechId.").Append(tech).Append(", ").Append(kindCode).Append(", ").Append(tagCode)
              .Append(", StatId.").Append(stat).Append(", EffectOp.").Append(op == "add" ? "Add" : "Multiply")
              .Append(", ").Append(value).Append("),\n");
        }

        private static void AppendAge(StringBuilder sb, Table.Row r, Dictionary<string, Entity> byKey, HashSet<string> ageKeys)
        {
            string reqAge = r["RequiredBuildingAge"];
            int required = Int(r, "RequiredBuildings");
            if (reqAge.Length == 0)
            {
                if (required != 0) throw r.Error("RequiredBuildings needs a RequiredBuildingAge");
                reqAge = "Dark";
            }
            if (!ageKeys.Contains(reqAge)) throw r.Error("unknown age " + reqAge);
            string or = r["OrBuilding"];
            if (or.Length != 0 && (!byKey.TryGetValue(or, out Entity b) || b.Row["Category"] != "building"))
                throw r.Error("OrBuilding must be a building, got " + or);

            sb.Append("            t[").Append(Int(r, "Id")).Append("] = new AgeDef\n            {\n");
            sb.Append("                Id = AgeId.").Append(r["Key"]).Append(", Key = \"").Append(r["Key"])
              .Append("\", Name = \"").Append(Escape(r["Name"])).Append("\",\n");
            sb.Append("                Cost = new Cost(").Append(Int(r, "Food")).Append(", ").Append(Int(r, "Wood")).Append(", ")
              .Append(Int(r, "Gold")).Append(", ").Append(Int(r, "Stone")).Append("), ResearchTicks = ").Append(Seconds(r, "ResearchSeconds")).Append(",\n");
            sb.Append("                RequiredBuildings = ").Append(required).Append(", RequiredBuildingAge = AgeId.").Append(reqAge)
              .Append(", OrBuilding = EntityKind.").Append(or.Length == 0 ? "None" : or).Append(",\n            };\n");
        }

        private static void AppendPreset(StringBuilder sb, Table.Row r, HashSet<string> ageKeys, HashSet<string> seen)
        {
            string key = r["Key"];
            RequireIdentifier(r, key);
            string age = r["Age"];
            if (!ageKeys.Contains(age)) throw r.Error("unknown age " + age);
            if (!seen.Add(key + "/" + age)) throw r.Error("duplicate preset " + key + " for " + age);
            int f = Int(r, "Food"), w = Int(r, "Wood"), g = Int(r, "Gold"), s = Int(r, "Stone");
            if (f < 0 || w < 0 || g < 0 || s < 0 || f + w + g + s != 100) throw r.Error("preset shares must be non-negative and add up to 100");
            sb.Append("                new EconomyPreset(\"").Append(key).Append("\", \"").Append(Escape(r["Name"])).Append("\", AgeId.").Append(age)
              .Append(", new EconomyTargets(").Append(f).Append(", ").Append(w).Append(", ").Append(g).Append(", ").Append(s).Append(")),\n");
        }

        // ---------------------------------------------------------------- value helpers

        private static string TagExpression(Table.Row r, string list, Dictionary<string, string> tagByKey)
        {
            var parts = new List<string>();
            foreach (string t in SplitList(list))
            {
                if (!tagByKey.TryGetValue(t, out string pascal)) throw r.Error("unknown tag " + t);
                parts.Add("EntityTag." + pascal);
            }
            return parts.Count == 0 ? "EntityTag.None" : string.Join(" | ", parts);
        }

        private static IEnumerable<string> SplitList(string value)
        {
            if (value.Length == 0) yield break;
            foreach (string part in value.Split(';'))
            {
                string p = part.Trim();
                if (p.Length != 0) yield return p;
            }
        }

        private static int Int(Table.Row r, string column)
        {
            string v = r[column];
            if (v.Length == 0) return 0;
            if (!int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int result))
                throw r.Error(column + " must be a whole number, got '" + v + "'");
            return result;
        }

        /// <summary>A decimal in seconds converted to whole ticks; fails if it does not land on a tick.</summary>
        private static int Seconds(Table.Row r, string column)
        {
            ParseDecimal(r, column, out long num, out long den);
            long ticks = num * TicksPerSecond;
            if (ticks % den != 0) throw r.Error(column + " must be a multiple of " + (1.0 / TicksPerSecond).ToString(CultureInfo.InvariantCulture) + " s");
            return checked((int)(ticks / den));
        }

        /// <summary>A decimal as an exact FP expression, divided by a per-tick divisor.</summary>
        private static string Fp(Table.Row r, string column, int divisor)
        {
            ParseDecimal(r, column, out long num, out long den);
            den *= divisor;
            if (num == 0) return "FP.Zero";
            if (den == 1) return "FP.FromInt(" + num + ")";
            return "FP.Ratio(" + checked((int)num) + ", " + checked((int)den) + ")";
        }

        private static void ParseDecimal(Table.Row r, string column, out long num, out long den)
        {
            string v = r[column];
            if (v.Length == 0) { num = 0; den = 1; return; }
            if (!Number.IsMatch(v)) throw r.Error(column + " must be a decimal number, got '" + v + "'");
            int dot = v.IndexOf('.');
            if (dot < 0)
            {
                num = long.Parse(v, CultureInfo.InvariantCulture);
                den = 1;
                return;
            }
            string digits = v.Remove(dot, 1);
            num = long.Parse(digits, CultureInfo.InvariantCulture);
            den = 1;
            for (int i = dot + 1; i < v.Length; i++) den *= 10;
        }

        private static void RequireIdentifier(Table.Row r, string key)
        {
            if (!Identifier.IsMatch(key)) throw r.Error("key must be PascalCase letters and digits: '" + key + "'");
        }

        private static string Pascal(string snake)
        {
            var sb = new StringBuilder();
            foreach (string part in snake.Split('_'))
            {
                if (part.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(part[0])).Append(part.Substring(1));
            }
            return sb.ToString();
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
