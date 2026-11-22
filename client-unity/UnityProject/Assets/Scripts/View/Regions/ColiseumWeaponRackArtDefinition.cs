using System;
using System.Collections.Generic;
using Roguehalla.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Roguehalla.Client
{
    /// <summary>Authored Coliseum rack body and the shared physical weapons mounted on it.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Coliseum weapon rack", fileName = "ColiseumWeaponRackArt")]
    public sealed class ColiseumWeaponRackArtDefinition : StormInkPropArtDefinition
    {
        [Serializable]
        public struct WeaponSlot
        {
            [Tooltip("The existing transferable weapon sprite to display.")]
            public WeaponId weapon;

            [Range(0, 7), Tooltip("Shared weapon view row: S, SE, E, NE, N, NW, W, SW.")]
            public int viewRow;

            [Tooltip("Anchor on the alpha-cropped rack source painting, normalized from bottom-left.")]
            public Vector2 normalizedAnchor;

            [Range(.1f, 1.2f), Tooltip("Uniform scale of the shared 1 × 1 world-unit weapon sprite.")]
            public float scale;

            [Range(-45f, 45f), Tooltip("Small display lean in degrees; does not alter the transferable weapon art.")]
            public float angleDegrees;
        }

        [Header("Mounted shared equipment")]
        [SerializeField] WeaponSlot[] weaponSlots =
        {
            new WeaponSlot { weapon = WeaponId.Sword, viewRow = 0, normalizedAnchor = new Vector2(.285f, .76f), scale = .68f, angleDegrees = -12f },
            new WeaponSlot { weapon = WeaponId.Bow, viewRow = 0, normalizedAnchor = new Vector2(.51f, .76f), scale = .73f, angleDegrees = 8f },
            new WeaponSlot { weapon = WeaponId.Axe, viewRow = 0, normalizedAnchor = new Vector2(.72f, .76f), scale = .68f, angleDegrees = -6f },
        };

        public IReadOnlyList<WeaponSlot> WeaponSlots => weaponSlots ?? Array.Empty<WeaponSlot>();

        public static ColiseumWeaponRackArtDefinition Load() =>
            Resources.Load<ColiseumWeaponRackArtDefinition>("StormInk/Props/ColiseumWeaponRackArt");

        /// <summary>Only place original shared weapon views over a successfully authored rack body.</summary>
        public bool HasAuthoredRackAndSlots => HasReadablePigment && TryGetFittedSourceOffset(TextureFoot, out _) &&
            weaponSlots != null && weaponSlots.Length > 0;

        /// <summary>
        /// Converts a normalized point in the source art's alpha-cropped bounds into the same foot-relative world space
        /// used by the RegionPropArt contain-fit. Returning false keeps the complete procedural rack fallback intact.
        /// </summary>
        public bool TryGetFittedOffset(Vector2 normalizedAnchor, out Vector2 offset)
        {
            return TryGetFittedSourceOffset(
                new Vector2(Mathf.Clamp01(normalizedAnchor.x), Mathf.Clamp01(normalizedAnchor.y)), out offset);
        }
    }

    /// <summary>Layers the exact shared equipment sprites over an authored rack inside RegionProps' ordinary lighting and sort path.</summary>
    internal static class ColiseumWeaponRackPresenter
    {
        internal static int AddMountedWeapons(Transform rack, SpriteRenderer bodyRenderer,
            ColiseumWeaponRackArtDefinition definition, bool bodyUsesAuthoredPigment, Material litMaterial)
        {
            if (rack == null || bodyRenderer == null || definition == null || !bodyUsesAuthoredPigment ||
                !definition.HasAuthoredRackAndSlots)
                return 0;

            if (!rack.TryGetComponent<SortingGroup>(out var group))
                group = rack.gameObject.AddComponent<SortingGroup>();
            group.sortingLayerID = bodyRenderer.sortingLayerID;
            group.sortingOrder = bodyRenderer.sortingOrder;
            bodyRenderer.sortingOrder = 0;

            var matteRigid = new MaterialPropertyBlock();
            matteRigid.SetFloat(Shader.PropertyToID("_StormInkResponse"), 1f);
            matteRigid.SetFloat(Shader.PropertyToID("_RhSwayV"), 0f);
            int added = 0;
            var slots = definition.WeaponSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var sprite = StormInkWeaponArt.Get(slot.weapon, slot.viewRow);
                if (sprite == null || !definition.TryGetFittedOffset(slot.normalizedAnchor, out var offset)) continue;

                var item = new GameObject("Rack item " + slot.weapon);
                item.transform.SetParent(rack, false);
                item.transform.localPosition = offset;
                item.transform.localScale = Vector3.one * Mathf.Clamp(slot.scale, .1f, 1.2f);
                item.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Clamp(slot.angleDegrees, -45f, 45f));
                var renderer = item.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sharedMaterial = litMaterial;
                renderer.sortingOrder = 1 + i;
                renderer.color = Color.white;
                renderer.SetPropertyBlock(matteRigid);
                added++;
            }
            return added;
        }
    }
}
