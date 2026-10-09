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
    }
}
