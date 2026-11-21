using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Artist-owned albedo and repeat scale for dry sand outside the Holy oasis.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Holy dry sand", fileName = "HolyDrySandArt")]
    public sealed class HolyDrySandArt : ScriptableObject
    {
        [Tooltip("Matte, unlit pigment. Existing arena lighting and exposure are applied by the ground shader.")]
        public Texture2D Pigment;

        [Min(0.25f), Tooltip("Texture repeat span in world tiles. The texture uses its Mirror wrap mode at the edges.")]
        public float RepeatSpan = 12f;

        [Range(0f, 1f), Tooltip("Blend from procedural sand to the authored pigment. Zero restores the original ground completely.")]
        public float WashStrength = 1f;

        public static HolyDrySandArt Load() => Resources.Load<HolyDrySandArt>("StormInk/Holy/HolyDrySandArt");

        internal void Bind(Material material)
        {
            if (material == null) return;

            // An absent/unassigned source, like a zero wash, keeps the original procedural ground intact.
            bool usable = Pigment != null && WashStrength > 0f;
            material.SetFloat("_HolyDrySandWash", usable ? Mathf.Clamp01(WashStrength) : 0f);
            if (!usable) return;

            material.SetTexture("_HolyDrySandTex", Pigment);
            material.SetFloat("_HolyDrySandRepeatSpan", Mathf.Max(0.25f, RepeatSpan));
        }
    }
}
