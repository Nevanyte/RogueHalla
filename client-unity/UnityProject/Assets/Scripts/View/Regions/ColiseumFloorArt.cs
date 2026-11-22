using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Editable albedo and map-plane orientation for the worn Coliseum paving.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Coliseum floor", fileName = "ColiseumFloorArt")]
    public sealed class ColiseumFloorArt : ScriptableObject
    {
        [Tooltip("Unlit limestone pigment. World light and shadows are applied by the arena material.")]
        public Texture2D Pigment;

        [Tooltip("Width and depth of one painting in world tiles, before reflection at its edges.")]
        public Vector2 RepeatSpan = new Vector2(4.5f, 6.25f);

        [Range(-180f, 180f), Tooltip("Rotation of the courses in map-space degrees, around the arena centre.")]
        public float RotationDegrees;

        [Range(0f, 1f), Tooltip("Painted coverage. At zero the original paving and its relief return.")]
        public float WashStrength = 1f;

        internal Texture2D ActivePigment => Pigment != null && Pigment.isReadable && WashStrength > 0f ? Pigment : null;

        internal void Bind(Material material, int slice)
        {
            float angle = RotationDegrees * Mathf.Deg2Rad;
            float x = 1f / Mathf.Max(.25f, RepeatSpan.x), y = 1f / Mathf.Max(.25f, RepeatSpan.y);
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            material.SetVector("_ColiseumPavingUV", new Vector4(c * x, s * x, -s * y, c * y));
            material.SetFloat("_ColiseumPavingSlice", slice);
            material.SetFloat("_ColiseumPavingWash", Mathf.Clamp01(WashStrength));
        }
    }
}
