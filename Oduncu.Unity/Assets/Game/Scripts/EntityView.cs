using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>Links a scene object back to the simulation entity it displays.</summary>
    public sealed class EntityView : MonoBehaviour
    {
        public int EntityId;
        public Vector3 PreviousPosition;
        public Vector3 CurrentPosition;
    }
}
