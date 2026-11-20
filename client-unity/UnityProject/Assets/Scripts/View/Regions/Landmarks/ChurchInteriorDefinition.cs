using System;
using System.Collections.Generic;
using UnityEngine;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Client
{
    /// <summary>Owns reusable, named church slot overrides and resolves them against the furnishing’s shared defaults.</summary>
    [Serializable]
    public sealed class ChurchArtSlotOverride
    {
        public string SlotId;
        public ChurchArtOverrideFields OverrideFields;
        public ChurchArtValues Values;
    }

    [CreateAssetMenu(menuName = "RogueHalla/Church Interior Layout", fileName = "ChurchInterior")]
    public sealed class ChurchInteriorDefinition : ScriptableObject
    {
        public List<ChurchArtSlotOverride> SlotOverrides = new List<ChurchArtSlotOverride>();

        public bool IsMirrored(MapAnchor anchor) { return ChurchArtOrientation.Resolve(anchor).Mirrored; }

        public ChurchArtResolved Resolve(string slotId, ChurchArtDefinition definition, MapAnchor anchor, bool? mirrorOverride = null)
        {
            ChurchArtValues values = definition != null ? definition.Defaults : default(ChurchArtValues);
            if (SlotOverrides != null)
            {
                for (int i = 0; i < SlotOverrides.Count; i++)
                {
                    var entry = SlotOverrides[i];
                    if (entry == null || !string.Equals(entry.SlotId, slotId, StringComparison.Ordinal)) continue;
                    values = ChurchArtValues.Merge(values, entry.Values, entry.OverrideFields);
                    break;
                }
            }
            return new ChurchArtResolved
            {
                Values = values,
                Mirrored = ChurchArtOrientation.Resolve(anchor, mirrorOverride).Mirrored,
                Mount = definition != null ? definition.Mount : ChurchArtMount.Upright,
                EmitsCandleLight = definition != null && definition.EmitsCandleLight
            };
        }
    }
}
