using System;
using System.Collections.Generic;
using System.IO;

namespace Oduncu.Sim
{
    public enum CommandKind : byte
    {
        None = 0,
        Move = 1,
        Gather = 2,
        Build = 3,
        Train = 4,
        Attack = 5,
        Stop = 6,
        CancelTrain = 7,
        SetRally = 8,
        Repair = 9,
        SetAutoQueue = 10,
        SetEconomyTargets = 11,
        Garrison = 12,
        Ungarrison = 13,
        SetStance = 14,
        Research = 15,
        AgeUp = 16,
        MarketBuy = 17,
        MarketSell = 18,
        BuildWall = 19,
        AttackMove = 20,
    }

    public enum Stance : byte
    {
        Aggressive = 0,
        /// <summary>Attack only what is in range and never chase it (design section 5.3).</summary>
        HoldGround = 1,
    }

    /// <summary>
    /// The only way anything (player, AI, network) changes simulation state. Commands are
    /// plain data so they can be logged, replayed and sent over the wire.
    /// </summary>
    public sealed class Command
    {
        private static readonly int[] NoUnits = Array.Empty<int>();

        public CommandKind Kind;
        public int Player;
        /// <summary>Acting units, sorted ascending and de-duplicated so issue order never matters.</summary>
        public int[] Units = NoUnits;
        public int Target;
        public EntityKind EntityType;
        public Cell Cell;
        /// <summary>Second cell for commands that span two points (walls, from milestone 1).</summary>
        public Cell Cell2;
        /// <summary>Small integer argument: queue slot for CancelTrain, 0/1 for SetAutoQueue, packed shares for SetEconomyTargets, the Stance for SetStance, the TechId for Research, the ResourceKind for market trades.</summary>
        public int Arg;

        public static Command Move(int player, IEnumerable<int> units, Cell to)
            => new Command { Kind = CommandKind.Move, Player = player, Units = Normalize(units), Cell = to };

        public static Command Gather(int player, IEnumerable<int> units, int resourceId)
            => new Command { Kind = CommandKind.Gather, Player = player, Units = Normalize(units), Target = resourceId };

        public static Command Build(int player, IEnumerable<int> villagers, EntityKind building, Cell origin)
            => new Command { Kind = CommandKind.Build, Player = player, Units = Normalize(villagers), EntityType = building, Cell = origin };

        public static Command Train(int player, int buildingId, EntityKind unit)
            => new Command { Kind = CommandKind.Train, Player = player, Target = buildingId, EntityType = unit };

        public static Command Attack(int player, IEnumerable<int> units, int targetId)
            => new Command { Kind = CommandKind.Attack, Player = player, Units = Normalize(units), Target = targetId };

        public static Command Stop(int player, IEnumerable<int> units)
            => new Command { Kind = CommandKind.Stop, Player = player, Units = Normalize(units) };

        public static Command CancelTrain(int player, int buildingId, int slot)
            => new Command { Kind = CommandKind.CancelTrain, Player = player, Target = buildingId, Arg = slot };

        /// <summary>Rally a production building's new units to a cell, or onto an entity (a resource to gather) when targetId is not 0.</summary>
        public static Command SetRally(int player, int buildingId, Cell cell, int targetId = 0)
            => new Command { Kind = CommandKind.SetRally, Player = player, Target = buildingId, Cell = cell, Arg = targetId };

        /// <summary>Villagers repair a damaged building, or help build one under construction.</summary>
        public static Command Repair(int player, IEnumerable<int> villagers, int buildingId)
            => new Command { Kind = CommandKind.Repair, Player = player, Units = Normalize(villagers), Target = buildingId };

        public static Command SetAutoQueue(int player, int buildingId, bool on)
            => new Command { Kind = CommandKind.SetAutoQueue, Player = player, Target = buildingId, Arg = on ? 1 : 0 };

        public static Command SetEconomyTargets(int player, EconomyTargets targets)
            => new Command { Kind = CommandKind.SetEconomyTargets, Player = player, Arg = targets.Pack() };

        public static Command Garrison(int player, IEnumerable<int> units, int buildingId)
            => new Command { Kind = CommandKind.Garrison, Player = player, Units = Normalize(units), Target = buildingId };

        /// <summary>Everyone inside the building comes out onto free cells around it.</summary>
        public static Command Ungarrison(int player, int buildingId)
            => new Command { Kind = CommandKind.Ungarrison, Player = player, Target = buildingId };

        public static Command SetStance(int player, IEnumerable<int> units, Stance stance)
            => new Command { Kind = CommandKind.SetStance, Player = player, Units = Normalize(units), Arg = (int)stance };

        public static Command Research(int player, int buildingId, TechId tech)
            => new Command { Kind = CommandKind.Research, Player = player, Target = buildingId, Arg = (int)tech };

        /// <summary>Research the next age at a Town Center.</summary>
        public static Command AgeUp(int player, int townCenterId)
            => new Command { Kind = CommandKind.AgeUp, Player = player, Target = townCenterId };

        /// <summary>Buy 100 food, wood or stone for gold at a market.</summary>
        public static Command MarketBuy(int player, int marketId, ResourceKind kind)
            => new Command { Kind = CommandKind.MarketBuy, Player = player, Target = marketId, Arg = (int)kind };

        /// <summary>Sell 100 food, wood or stone for gold at a market.</summary>
        public static Command MarketSell(int player, int marketId, ResourceKind kind)
            => new Command { Kind = CommandKind.MarketSell, Player = player, Target = marketId, Arg = (int)kind };

        /// <summary>Villagers lay a straight wall (or line of gates) from one cell to another.</summary>
        public static Command BuildWall(int player, IEnumerable<int> villagers, EntityKind wall, Cell from, Cell to)
            => new Command { Kind = CommandKind.BuildWall, Player = player, Units = Normalize(villagers), EntityType = wall, Cell = from, Cell2 = to };

        /// <summary>Move, but stop to fight any enemy met on the way, then carry on.</summary>
        public static Command AttackMove(int player, IEnumerable<int> units, Cell to)
            => new Command { Kind = CommandKind.AttackMove, Player = player, Units = Normalize(units), Cell = to };

        private static int[] Normalize(IEnumerable<int> units)
        {
            if (units == null) return NoUnits;
            var set = new SortedSet<int>(units);
            var arr = new int[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        public void WriteState(StateHasher h)
        {
            h.Write((int)Kind);
            h.Write(Player);
            h.Write(Units.Length);
            for (int i = 0; i < Units.Length; i++) h.Write(Units[i]);
            h.Write(Target);
            h.Write((int)EntityType);
            h.Write(Cell);
            h.Write(Cell2);
            h.Write(Arg);
        }

        public void Write(BinaryWriter w)
        {
            w.Write((byte)Kind);
            w.Write(Player);
            w.Write(Units.Length);
            for (int i = 0; i < Units.Length; i++) w.Write(Units[i]);
            w.Write(Target);
            w.Write((byte)EntityType);
            w.Write(Cell.X);
            w.Write(Cell.Y);
            w.Write(Cell2.X);
            w.Write(Cell2.Y);
            w.Write(Arg);
        }

        public static Command Read(BinaryReader r)
        {
            var c = new Command
            {
                Kind = (CommandKind)r.ReadByte(),
                Player = r.ReadInt32(),
            };
            int n = r.ReadInt32();
            var units = new int[n];
            for (int i = 0; i < n; i++) units[i] = r.ReadInt32();
            c.Units = units;
            c.Target = r.ReadInt32();
            c.EntityType = (EntityKind)r.ReadByte();
            int x = r.ReadInt32();
            int y = r.ReadInt32();
            c.Cell = new Cell(x, y);
            int x2 = r.ReadInt32();
            int y2 = r.ReadInt32();
            c.Cell2 = new Cell(x2, y2);
            c.Arg = r.ReadInt32();
            return c;
        }

        public override string ToString()
            => Kind + " p" + Player + " units=" + Units.Length + " target=" + Target + " type=" + EntityType + " cell=" + Cell + " cell2=" + Cell2 + " arg=" + Arg;
    }

    /// <summary>Every command of a match, by tick. Replaying a log reproduces the match exactly.</summary>
    public sealed class CommandLog
    {
        /// <summary>
        /// Bumped whenever command kinds or fields change; older logs are rejected rather than
        /// misread. Version 2 added CancelTrain and the Cell2 and Arg fields; version 3 adds
        /// SetRally, Repair, SetAutoQueue and SetEconomyTargets; version 4 adds Garrison,
        /// Ungarrison and SetStance; version 5 adds Research, AgeUp, MarketBuy and MarketSell;
        /// version 6 adds BuildWall; version 7 adds AttackMove.
        /// </summary>
        public const int FormatVersion = 7;

        public readonly struct Entry
        {
            public readonly int Tick;
            public readonly Command Command;
            public Entry(int tick, Command command) { Tick = tick; Command = command; }
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Command> _scratch = new List<Command>();

        public int Seed;
        public IReadOnlyList<Entry> Entries => _entries;

        public void Record(int tick, IReadOnlyList<Command> commands)
        {
            for (int i = 0; i < commands.Count; i++) _entries.Add(new Entry(tick, commands[i]));
        }

        /// <summary>Commands recorded for one tick, in recorded order. The list is reused between calls.</summary>
        public IReadOnlyList<Command> CommandsAt(int tick)
        {
            _scratch.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Tick == tick) _scratch.Add(_entries[i].Command);
            }
            return _scratch;
        }

        public void Save(Stream stream)
        {
            using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(FormatVersion);
                w.Write(Seed);
                w.Write(_entries.Count);
                for (int i = 0; i < _entries.Count; i++)
                {
                    w.Write(_entries[i].Tick);
                    _entries[i].Command.Write(w);
                }
            }
        }

        public static CommandLog Load(Stream stream)
        {
            using (var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                int version = r.ReadInt32();
                if (version != FormatVersion)
                    throw new InvalidDataException("Unsupported command log version " + version + "; this build reads version " + FormatVersion + " only.");
                var log = new CommandLog { Seed = r.ReadInt32() };
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    int tick = r.ReadInt32();
                    log._entries.Add(new Entry(tick, Command.Read(r)));
                }
                return log;
            }
        }
    }
}
