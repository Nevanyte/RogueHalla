using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The pull has two segments. The bow itself keeps the canonical Sprite and dimensions at every phase.</summary>
    internal sealed class StormInkBowstring
    {
        static readonly int ChordId = Shader.PropertyToID("_RhBowChord");
        static readonly int WidthId = Shader.PropertyToID("_RhBowChordWidth");
        static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        static Material _material;
        static readonly Color DrawColor = new Color32(151, 138, 112, 255);
        const float Thickness = .017f;
        static readonly float[] TopX = { .395f, .422f, .496f, .590f, .607f, .570f, .502f, .419f };
        static readonly float[] BottomX = { .395f, .422f, .499f, .590f, .607f, .570f, .502f, .413f };

        readonly SpriteRenderer _top, _bottom;

        StormInkBowstring(Transform parent)
        {
            _top = ShowcaseSprites.Create(parent, "Bowstring upper", "bar", 0, Color.white, Vector2.one, Vector3.zero, false);
            _bottom = ShowcaseSprites.Create(parent, "Bowstring lower", "bar", 0, Color.white, Vector2.one, Vector3.zero, false);
        }

        internal static StormInkBowstring Create(Transform parent)
        {
            if (_material == null)
            {
                var shader = Resources.Load<Shader>("Shaders/RhBowDrawn");
                if (shader == null) return null;
                _material = new Material(shader) { name = "Roguehalla / rigid bow during draw" };
            }
            return new StormInkBowstring(parent);
        }

        static Vector3 Socket(SpriteRenderer bow, Vector2 uv)
        {
            var sprite = bow.sprite;
            Vector2 pixel = Vector2.Scale(uv, sprite.rect.size) - sprite.pivot;
            var t = bow.transform;
            return t.localPosition + t.localRotation * Vector3.Scale((Vector3)(pixel / sprite.pixelsPerUnit), t.localScale);
        }

        internal void Draw(SpriteRenderer bow, SpriteOutline outline, int view, Vector3 rightPalm, float opacity)
        {
            var top = new Vector2(TopX[view], .910f);
            var bottom = new Vector2(BottomX[view], .094f);
            var color = DrawColor;
            color.a = opacity;
            DrawSegment(_top, Socket(bow, top), rightPalm, bow.sortingOrder + 1, color);
            DrawSegment(_bottom, Socket(bow, bottom), rightPalm, bow.sortingOrder + 1, color);

            var rect = bow.sprite.rect;
            var texture = bow.sprite.texture;
            var chord = new Vector4((rect.x + bottom.x * rect.width) / texture.width,
                (rect.x + top.x * rect.width) / texture.width,
                (rect.y + bottom.y * rect.height) / texture.height,
                (rect.y + top.y * rect.height) / texture.height);
            // The narrow side elevations place the rest chord inside the silhouette of the wood.
            float width = view == 2 || view == 6 ? 0f : 4.25f / texture.width;
            bow.sharedMaterial = width > 0f ? _material : ShowcaseSprites.Unlit;
            Block.Clear();
            Block.SetVector(ChordId, chord);
            Block.SetFloat(WidthId, width);
            bow.SetPropertyBlock(Block);
            outline.SetCompanionChord(chord, width);
        }

        internal void Clear(SpriteRenderer bow, SpriteOutline outline)
        {
            if (!_top.enabled) return;
            _top.enabled = _bottom.enabled = false;
            bow.sharedMaterial = ShowcaseSprites.Unlit;
            bow.SetPropertyBlock(null);
            outline.SetCompanionChord(Vector4.zero, 0f);
        }

        static void DrawSegment(SpriteRenderer segment, Vector3 start, Vector3 end, int order, Color color)
        {
            var delta = end - start;
            var t = segment.transform;
            t.localPosition = (start + end) * .5f;
            t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            t.localScale = new Vector3(delta.magnitude, Thickness, 1f);
            segment.sortingOrder = order;
            segment.color = color;
            segment.enabled = true;
        }
    }
}
