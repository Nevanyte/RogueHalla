using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Editable albedo and map-plane orientation for the worn Coliseum paving.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Coliseum floor", fileName = "ColiseumFloorArt")]
    public sealed class ColiseumFloorArt : RegionalFloorArt
    {
        internal void Bind(Material material, int slice)
        {
            material.SetVector("_ColiseumPavingUV", MapToPaint);
            material.SetFloat("_ColiseumPavingSlice", slice);
            material.SetFloat("_ColiseumPavingWash", Mathf.Clamp01(WashStrength));
        }
    }
}
