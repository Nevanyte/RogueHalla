using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Unlit ground pigment with a footprint and direction measured in map space.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Regional floor", fileName = "RegionalFloorArt")]
    public class RegionalFloorArt : ScriptableObject
    {
        [Tooltip("Unlit pigment. Arena lighting supplies the shadows and local light.")]
        public Texture2D Pigment;
        [Tooltip("Width and depth of one painting, in world tiles.")]
        public Vector2 RepeatSpan = new Vector2(4.5f, 6.25f);
        [Range(-180f, 180f), Tooltip("Orientation of the painting on the map, in degrees.")]
        public float RotationDegrees;
        [Range(0f, 1f), Tooltip("Painted coverage. At zero the procedural surface and relief return.")]
        public float WashStrength = 1f;

        internal Texture2D ActivePigment => Pigment != null && Pigment.isReadable && WashStrength > 0f ? Pigment : null;

        internal Vector4 MapToPaint
        {
            get
            {
                float angle = RotationDegrees * Mathf.Deg2Rad;
                float x = 1f / Mathf.Max(.25f, RepeatSpan.x), y = 1f / Mathf.Max(.25f, RepeatSpan.y);
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                return new Vector4(c * x, s * x, -s * y, c * y);
            }
        }
    }
}
