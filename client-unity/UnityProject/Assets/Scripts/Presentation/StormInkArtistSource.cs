using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Identifies the painted actor or shared equipment that a designer selects in the gameplay scene.</summary>
    public enum StormInkArtistSourceKind { None, FireAxeBody, EquippedAxe, DroppedAxe, FireSwordBody, EquippedSword, DroppedSword, EquippedBow, DroppedBow }

    /// <summary>Identifies the editable asset and painted registration that produced an observed sprite.</summary>
    [DisallowMultipleComponent]
    public sealed class StormInkArtistSource : MonoBehaviour
    {
        public StormInkArtistSourceKind Kind { get; private set; }
        public SpriteRenderer Renderer { get; private set; }
        public Sprite Sprite => Renderer != null && Kind != StormInkArtistSourceKind.None ? Renderer.sprite : null;
        public StormInkFireAxeAuthoring FireBody { get; private set; }
        public StormInkFireSwordAuthoring SwordBody { get; private set; }
        public ScriptableObject BodyPainting => SwordBody != null ? (ScriptableObject)SwordBody : FireBody;
        public ScriptableObject WeaponSource { get; private set; }
        public StormInkAxeArtAuthoring SharedAxe => WeaponSource as StormInkAxeArtAuthoring;
        public WeaponId? Weapon { get; private set; }
        public int PoseRow { get; private set; } = -1;
        public Vector2 HeldGripUv => Weapon.HasValue ? StormInkWeaponArt.Grip(Weapon.Value, PoseRow) : default;
        public bool IsDropped { get; private set; }
        public Vector2 GroundCentre => IsDropped ? StormInkWeaponArt.GroundCentre(Weapon.Value) : default;

        public bool TryBodyPalm(bool left, out Vector2 uv)
        {
            if (SwordBody != null) return SwordBody.TryHand(Sprite, left, out uv);
            if (FireBody != null) return FireBody.TryHand(Sprite, left, out uv);
            uv = default;
            return false;
        }

        static StormInkArtistSource Ensure(StormInkArtistSource source, SpriteRenderer renderer)
        {
            if (source == null) source = renderer.gameObject.AddComponent<StormInkArtistSource>();
            source.Renderer = renderer;
            return source;
        }

        internal static StormInkArtistSource BindBody(StormInkArtistSource source,
            SpriteRenderer renderer, StormInkFireAxeAuthoring asset, StormInkFireSwordAuthoring sword = null)
        {
            if (asset == null || !(sword != null ? sword.TryHand(renderer.sprite, false, out _) :
                    asset.TryHand(renderer.sprite, false, out _)))
            {
                source?.Clear();
                return source;
            }
            source = Ensure(source, renderer);
            source.FireBody = asset;
            source.SwordBody = sword;
            source.Kind = sword != null ? StormInkArtistSourceKind.FireSwordBody : StormInkArtistSourceKind.FireAxeBody;
            var rect = renderer.sprite.rect;
            source.PoseRow = 7 - Mathf.RoundToInt(rect.y / rect.height);
            return source;
        }

        internal static StormInkArtistSource BindWeapon(StormInkArtistSource source,
            SpriteRenderer renderer, WeaponId? weapon, int row, bool dropped)
        {
            var asset = StormInkWeaponArt.Definition(weapon);
            if (asset == null || renderer.sprite == null || renderer.sprite != StormInkWeaponArt.Get(weapon.Value, row))
            {
                source?.Clear();
                return source;
            }
            source = Ensure(source, renderer);
            source.Weapon = weapon;
            source.WeaponSource = asset;
            source.IsDropped = dropped;
            source.Kind = weapon switch
            {
                WeaponId.Axe => dropped ? StormInkArtistSourceKind.DroppedAxe : StormInkArtistSourceKind.EquippedAxe,
                WeaponId.Bow => dropped ? StormInkArtistSourceKind.DroppedBow : StormInkArtistSourceKind.EquippedBow,
                _ => dropped ? StormInkArtistSourceKind.DroppedSword : StormInkArtistSourceKind.EquippedSword
            };
            source.PoseRow = row;
            return source;
        }

        internal void Clear()
        {
            FireBody = null;
            SwordBody = null;
            WeaponSource = null;
            IsDropped = false;
            Weapon = null;
            Kind = StormInkArtistSourceKind.None;
            PoseRow = -1;
        }
    }
}
