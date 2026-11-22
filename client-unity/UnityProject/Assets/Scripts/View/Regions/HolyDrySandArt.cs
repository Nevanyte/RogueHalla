using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Artist-owned albedo and repeat scale for dry sand outside the Holy oasis.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Holy dry sand", fileName = "HolyDrySandArt")]
    public sealed class HolyDrySandArt : ScriptableObject
    {
        [Tooltip("Matte, unlit pigment. Existing arena lighting and exposure are applied by the ground shader.")]
        public Texture2D Pigment;

        [Min(0.25f), Tooltip("Texture repeat span in world tiles. Packed sampling mirrors each repeat across the texture-array slice.")]
        public float RepeatSpan = 12f;

        [Range(0f, 1f), Tooltip("Blend from procedural sand to the authored pigment. Zero restores the original ground completely.")]
        public float WashStrength = 1f;

        public static HolyDrySandArt Load() => Resources.Load<HolyDrySandArt>("StormInk/Holy/HolyDrySandArt");

        internal bool CanPackPigment => Pigment != null && Pigment.isReadable && WashStrength > 0f;

        internal void Bind(Material material, bool pigmentPacked)
        {
            if (material == null) return;

            // An absent/unreadable source, like a zero wash, leaves the original eight slices intact.
            material.SetFloat("_HolyDrySandWash", pigmentPacked ? Mathf.Clamp01(WashStrength) : 0f);
            if (!pigmentPacked) return;

            material.SetFloat("_HolyDrySandRepeatSpan", Mathf.Max(0.25f, RepeatSpan));
            material.SetFloat("_HolyDrySandSlice", RegionGroundTextures.HolyDrySandSlice);
        }
    }
}
