using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Attachments for confirmed damage and healing. Each stroke has an ink edge and its own pigment.</summary>
    public static class StormInkHitMarks
    {
        const int Cells = 5;
        static readonly Sprite[] Marks = new Sprite[Cells];
        static Texture2D _atlas;

        public static Sprite ForSignature(byte code) =>
            code >= WireFx.HitKindBurn && code <= WireFx.HitKindMark ? Get(code - WireFx.HitKindBurn) : null;

        // Heal identifies the recipient and amount only, so every source uses the same sage wash.
        public static Sprite Heal => Get(4);

        // Contacts are anchored beside the hit actor. Separating the weight-bearing foot, shoulder,
        // elbow and flank keeps concurrent impressions distinct and leaves the floating numbers clear.
        public static Vector2 Anchor(byte code, bool heal)
        {
            if (heal) return new Vector2(.00f, .12f);
            switch (code)
            {
                case WireFx.HitKindBurn: return new Vector2(-.46f, .90f);
                case WireFx.HitKindDisease: return new Vector2(-.43f, .20f);
                case WireFx.HitKindBacklash: return new Vector2(.90f, .72f);
                case WireFx.HitKindMark: return new Vector2(.43f, .84f);
                default: return Vector2.zero;
            }
        }

        public static float Scale(byte code, bool heal) =>
            !heal && code == WireFx.HitKindBacklash ? .82f : .59f;

        static Sprite Get(int index)
        {
            if (Marks[index] != null) return Marks[index];
            if (_atlas == null) _atlas = Resources.Load<Texture2D>("StormInk/Combat/SignatureBrushes");
            if (_atlas == null || _atlas.width != Cells * _atlas.height) return null;
            int cell = _atlas.height;
            var sprite = Sprite.Create(_atlas, new Rect(index * cell, 0, cell, cell), new Vector2(.5f, .5f), cell,
                0, SpriteMeshType.FullRect, Vector4.zero, false);
            sprite.name = "SignatureBrushes/" + index;
            return Marks[index] = sprite;
        }
    }
}
