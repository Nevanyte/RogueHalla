using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Registers the Pox Behemoth's rigid cleaver and gravity-hung censer to its painted hands.</summary>
    public sealed partial class SpriteActorPresenter
    {
        const float PoxHeight = 1.21f;
        static readonly Sprite[] PoxCleaver = new Sprite[8];
        static readonly Vector2Int[] PoxCenserHooks =
        {
            new(55, 54), new(57, 54), new(63, 52), new(71, 54),
            new(73, 52), new(68, 53), new(65, 53), new(72, 54)
        };
        static Sprite _poxCenser;
        static bool _poxItemsLoaded;

        static bool IsPox(Art art) => art.Kind == ActorKind.RegionalMob &&
            art.MobType == MobTypeId.PoxBehemoth && art.Faction == FactionId.Plague;

        static void LoadPoxItems()
        {
            if (_poxItemsLoaded) return;
            _poxItemsLoaded = true;
            var cleaver = Resources.Load<Texture2D>("StormInk/Weapons/PoxCleaverViews");
            var censer = Resources.Load<Texture2D>("StormInk/Weapons/PoxCenser");
            if (cleaver == null || cleaver.width != 512 || cleaver.height != 256 || censer == null) return;
            _poxCenser = Sprite.Create(censer, new Rect(0, 0, 64, 64), new Vector2(.5f, 61f / 64f),
                128, 0, SpriteMeshType.FullRect, Vector4.zero, false);
            for (int row = 0; row < PoxCleaver.Length; row++)
            {
                var rect = new Rect((row % 4) * 128, (1 - row / 4) * 128, 128, 128);
                var sprite = Sprite.Create(cleaver, rect, new Vector2(.5f, 28f / 128f),
                    128, 0, SpriteMeshType.FullRect, Vector4.zero, false);
                sprite.name = cleaver.name + "/" + row;
                PoxCleaver[row] = sprite;
            }
        }

        static bool PresentPoxCleaver(Art art, int heading, int order, float alpha, bool dead)
        {
            if (!IsPox(art) || art.WeaponId != WeaponId.Axe || dead || alpha <= 0f) return false;
            LoadPoxItems();
            var cleaver = PoxCleaver[heading];
            if (cleaver == null || !StormInkActors.TryMobHand(art.MobType, art.Body.sprite, false, out var right) ||
                !StormInkActors.TryMobHand(art.MobType, art.Body.sprite, true, out var left)) return false;

            var rearHand = BodySocket(art.Body, right);
            var forwardHand = BodySocket(art.Body, left);
            var shaft = forwardHand - rearHand;
            var rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(shaft.y, shaft.x) * Mathf.Rad2Deg - 90f);
            var weapon = art.Weapon;
            float size = 1.12f;
            weapon.sprite = cleaver;
            weapon.sortingOrder = order + (heading >= 3 && heading <= 5 ? -2 : 2);
            weapon.transform.localRotation = rotation;
            weapon.transform.localScale = Vector3.one * size;
            weapon.transform.localPosition = rearHand;

            if (art.PoxCenser == null)
            {
                art.PoxCenser = ShowcaseSprites.Create(art.Root.transform, "Pox cleaver censer", "weapon4",
                    weapon.sortingOrder, Color.white, Vector2.one, Vector3.zero);
                art.PoxCenser.sprite = _poxCenser;
            }
            var hook = PoxCenserHooks[heading];
            var hookUv = new Vector2(hook.x / 128f, 1f - hook.y / 128f);
            art.PoxCenser.transform.localPosition = rearHand +
                rotation * SpriteSocket(cleaver, hookUv, weapon.transform.localScale);
            art.PoxCenser.transform.localScale = Vector3.one * .62f;
            art.PoxCenser.sortingOrder = weapon.sortingOrder - 1;
            art.PoxCenser.color = new Color(1f, 1f, 1f, alpha);
            art.PoxCenser.enabled = true;
            return true;
        }
    }
}
