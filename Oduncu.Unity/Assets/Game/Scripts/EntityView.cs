using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>Links a scene object back to the simulation entity it displays.</summary>
    public sealed class EntityView : MonoBehaviour
    {
        public int EntityId;
        public EntityKind Kind;
        public int Owner;
        public Vector3 PreviousPosition;
        public Vector3 CurrentPosition;
        public Transform Model;
        public Transform Ring;
        public GameObject SelectionRing;
        /// <summary>Seconds left of the death effect; the view is pooled when it reaches zero.</summary>
        public float Dying;

        // Animation and overlays, filled in by EntityPresenter each tick.
        public bool IsUnit;
        /// <summary>Siege engines roll: no walking bob, no working lean.</summary>
        public bool Rigid;
        public UnitState State;
        public bool Moving;
        public int LastCooldown;
        /// <summary>Seconds left of the attack lunge.</summary>
        public float AttackPulse;
        /// <summary>Per-entity offset so a group does not bob in step.</summary>
        public float Phase;
        /// <summary>Hit points as a fraction of the maximum, 0 to 1.</summary>
        public float HpFraction = 1f;
        public bool Selected;
        /// <summary>Top of the model in world units, where the health bar floats.</summary>
        public float Height;
        public float BarWidth;
        /// <summary>The load on a villager's back; shown and coloured by what is carried.</summary>
        public Renderer CarryPack;
        public Transform HealthBar;
        public Transform HealthFill;
        public Renderer HealthFillRenderer;
        public GameObject Fire;
    }
}
