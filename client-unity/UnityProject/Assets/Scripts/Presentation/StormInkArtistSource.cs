using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    public enum StormInkArtistSourceKind { None, FireAxeBody, EquippedAxe, DroppedAxe, FireSwordBody, EquippedSword, DroppedSword }

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
        public bool IsDropped => Kind == StormInkArtistSourceKind.DroppedAxe || Kind == StormInkArtistSourceKind.DroppedSword;
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
            ScriptableObject asset = weapon switch
            {
                WeaponId.Axe => StormInkAxeArtAuthoring.Load(),
                WeaponId.Sword => StormInkSwordArtAuthoring.Load(),
                _ => null
            };
            if (asset == null || renderer.sprite == null || renderer.sprite != StormInkWeaponArt.Get(weapon.Value, row))
            {
                source?.Clear();
                return source;
            }
            source = Ensure(source, renderer);
            source.Weapon = weapon;
            source.WeaponSource = asset;
            source.Kind = weapon == WeaponId.Sword
                ? dropped ? StormInkArtistSourceKind.DroppedSword : StormInkArtistSourceKind.EquippedSword
                : dropped ? StormInkArtistSourceKind.DroppedAxe : StormInkArtistSourceKind.EquippedAxe;
            source.PoseRow = row;
            return source;
        }

        internal void Clear()
        {
            FireBody = null;
            SwordBody = null;
            WeaponSource = null;
            Weapon = null;
            Kind = StormInkArtistSourceKind.None;
            PoseRow = -1;
        }
    }
}
