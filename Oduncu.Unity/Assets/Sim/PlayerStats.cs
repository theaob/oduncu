namespace Oduncu.Sim
{
    /// <summary>The stats of one entity kind for one player, after that player's upgrades.</summary>
    public sealed class UnitStats
    {
        public int MaxHp;
        public int Attack;
        public int MeleeArmor;
        public int PierceArmor;
        public FP Range;
        public int AttackTicks;
        /// <summary>Tiles per tick.</summary>
        public FP Speed;
        public int LineOfSight;
        public int TrainTicks;
        public int BuildTicks;
        public Cost Cost;

        internal void CopyFrom(EntityDef d)
        {
            MaxHp = d.MaxHp;
            Attack = d.Attack;
            MeleeArmor = d.MeleeArmor;
            PierceArmor = d.PierceArmor;
            Range = d.Range;
            AttackTicks = d.AttackTicks;
            Speed = d.Speed;
            LineOfSight = d.LineOfSight;
            TrainTicks = d.TrainTicks;
            BuildTicks = d.BuildTicks;
            Cost = d.Cost;
        }

        internal void Apply(StatId stat, EffectOp op, FP value)
        {
            switch (stat)
            {
                case StatId.MaxHp: MaxHp = Max(1, Combine(MaxHp, op, value)); break;
                case StatId.Attack: Attack = Max(0, Combine(Attack, op, value)); break;
                case StatId.MeleeArmor: MeleeArmor = Combine(MeleeArmor, op, value); break;
                case StatId.PierceArmor: PierceArmor = Combine(PierceArmor, op, value); break;
                case StatId.Range: Range = FP.Max(FP.Zero, op == EffectOp.Add ? Range + value : Range * value); break;
                case StatId.Reload: AttackTicks = Max(1, Combine(AttackTicks, op, value)); break;
                case StatId.Speed: Speed = FP.Max(FP.Zero, op == EffectOp.Add ? Speed + value : Speed * value); break;
                case StatId.LineOfSight: LineOfSight = Max(0, Combine(LineOfSight, op, value)); break;
                case StatId.TrainTime: TrainTicks = Max(1, Combine(TrainTicks, op, value)); break;
                case StatId.BuildTime: BuildTicks = Max(1, Combine(BuildTicks, op, value)); break;
                case StatId.CostFood: Cost = new Cost(Max(0, Combine(Cost.Food, op, value)), Cost.Wood, Cost.Gold, Cost.Stone); break;
                case StatId.CostWood: Cost = new Cost(Cost.Food, Max(0, Combine(Cost.Wood, op, value)), Cost.Gold, Cost.Stone); break;
                case StatId.CostGold: Cost = new Cost(Cost.Food, Cost.Wood, Max(0, Combine(Cost.Gold, op, value)), Cost.Stone); break;
                case StatId.CostStone: Cost = new Cost(Cost.Food, Cost.Wood, Cost.Gold, Max(0, Combine(Cost.Stone, op, value))); break;
            }
        }

        private static int Combine(int current, EffectOp op, FP value)
        {
            if (op == EffectOp.Add) return current + value.RoundToInt();
            return (FP.FromInt(current) * value).RoundToInt();
        }

        private static int Max(int a, int b) => a > b ? a : b;
    }

    /// <summary>
    /// One player's resolved stat table: base data plus every researched tech, applied in tech
    /// id order and then in table order. Rebuilt in place (no allocation) only when a tech
    /// completes. Entities keep a reference to their row, so changes reach existing units.
    /// </summary>
    public sealed class PlayerStats
    {
        private readonly UnitStats[] _byKind = new UnitStats[GameData.EntityKindCount];
        private readonly bool[] _researched = new bool[GameData.TechCount];

        public PlayerStats()
        {
            for (int i = 0; i < _byKind.Length; i++) _byKind[i] = new UnitStats();
            Rebuild();
        }

        public UnitStats Of(EntityKind kind) => _byKind[(int)kind];

        public bool HasResearched(TechId tech) => _researched[(int)tech];

        /// <summary>Mark a tech researched and rebuild. Returns false if it already was.</summary>
        public bool Research(TechId tech)
        {
            if (tech == TechId.None || _researched[(int)tech]) return false;
            _researched[(int)tech] = true;
            Rebuild();
            return true;
        }

        private void Rebuild()
        {
            EntityDef[] defs = GameData.Entities;
            for (int k = 0; k < _byKind.Length; k++) _byKind[k].CopyFrom(defs[k]);

            TechEffect[] effects = GameData.TechEffects;
            for (int t = 1; t < _researched.Length; t++)
            {
                if (!_researched[t]) continue;
                for (int i = 0; i < effects.Length; i++)
                {
                    TechEffect fx = effects[i];
                    if ((int)fx.Tech != t) continue;
                    for (int k = 1; k < defs.Length; k++)
                    {
                        if (fx.Applies(defs[k])) _byKind[k].Apply(fx.Stat, fx.Op, fx.Value);
                    }
                }
            }
        }

        public void WriteState(StateHasher h)
        {
            for (int t = 0; t < _researched.Length; t++) h.Write(_researched[t]);
        }
    }
}
