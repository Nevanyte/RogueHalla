using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Identifies whether the current painting is a Fire body, an equipped axe or a ground pickup.</summary>
    public enum StormInkArtistSourceKind { None, FireAxeBody, EquippedAxe, DroppedAxe }

    /// <summary>Identifies the editable asset and painted registration that produced an observed sprite.</summary>
    [DisallowMultipleComponent]
    public sealed class StormInkArtistSource : MonoBehaviour
    {
        public StormInkArtistSourceKind Kind { get; private set; }
        public SpriteRenderer Renderer { get; private set; }
        public Sprite Sprite => Renderer != null && Kind != StormInkArtistSourceKind.None ? Renderer.sprite : null;
        public StormInkFireAxeAuthoring FireBody { get; private set; }
        public StormInkAxeArtAuthoring SharedAxe { get; private set; }
        public int PoseRow { get; private set; } = -1;
        public Vector2 HeldGripUv => SharedAxe != null ? StormInkWeaponArt.Grip(WeaponId.Axe, PoseRow) : default;
        public Vector2 GroundCentre => Kind == StormInkArtistSourceKind.DroppedAxe ? SharedAxe.GroundCentre : default;

        public bool TryBodyPalm(bool left, out Vector2 uv)
        {
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
            SpriteRenderer renderer, StormInkFireAxeAuthoring asset)
        {
            if (asset == null || !asset.TryHand(renderer.sprite, false, out _))
            {
                source?.Clear();
                return source;
            }
            source = Ensure(source, renderer);
            source.FireBody = asset;
            source.Kind = StormInkArtistSourceKind.FireAxeBody;
            var rect = renderer.sprite.rect;
            source.PoseRow = 7 - Mathf.RoundToInt(rect.y / rect.height);
            return source;
        }

        internal static StormInkArtistSource BindAxe(StormInkArtistSource source,
            SpriteRenderer renderer, int row, bool dropped)
        {
            var asset = StormInkAxeArtAuthoring.Load();
            if (asset == null || renderer.sprite == null || renderer.sprite != StormInkWeaponArt.Get(WeaponId.Axe, row))
            {
                source?.Clear();
                return source;
            }
            source = Ensure(source, renderer);
            source.SharedAxe = asset;
            source.Kind = dropped ? StormInkArtistSourceKind.DroppedAxe : StormInkArtistSourceKind.EquippedAxe;
            source.PoseRow = row;
            return source;
        }

        internal void Clear()
        {
            FireBody = null;
            SharedAxe = null;
            Kind = StormInkArtistSourceKind.None;
            PoseRow = -1;
        }
    }
}
