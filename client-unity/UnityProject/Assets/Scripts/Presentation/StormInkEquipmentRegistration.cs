using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>One grip per painted view and one pickup offset shared by every carrier of the item.</summary>
    public abstract class StormInkEquipmentRegistration : ScriptableObject
    {
        [SerializeField] Vector2 groundCentre = new Vector2(0f, .39f);
        protected abstract Vector2[] Grips { get; }
        public Vector2 GroundCentre => groundCentre;
        public Vector2 Grip(int row, Vector2 fallback) =>
            Grips != null && (uint)row < Grips.Length ? Grips[row] : fallback;
    }
}
