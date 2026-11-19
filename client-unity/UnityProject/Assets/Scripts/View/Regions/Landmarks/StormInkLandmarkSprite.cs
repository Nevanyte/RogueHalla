using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Fits a painted, upright cutout to a measured map-space contact and screen-space size.</summary>
    internal static class StormInkLandmarkSprite
    {
        internal static SpriteRenderer Place(Landmark landmark, Sprite sprite, string name, Vector2 contact,
            Vector2 size, Vector2 normalizedContact, int sortingOrder)
        {
            var bounds = sprite.bounds;
            var scale = new Vector2(size.x / bounds.size.x, size.y / bounds.size.y);
            var origin = (Vector2)bounds.min + Vector2.Scale(bounds.size, normalizedContact);
            var position = contact - Vector2.Scale(origin, scale);
            var go = new GameObject(name);
            go.transform.SetParent(landmark.Root.transform, false);
            go.transform.localPosition = new Vector3(position.x, position.y, 0f);
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = landmark.Material;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        internal static int RowOrder(float tileY) { return 2000 + 16 * Mathf.FloorToInt(tileY) + 2; }
    }
}
