using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Draws the Warden's weighted chain from the painted hands on its existing melee timeline.</summary>
    internal sealed class StormInkManacles
    {
        const int LinkCount = 9;
        static Sprite _iron;
        readonly Transform _root;
        readonly SpriteRenderer[] _links = new SpriteRenderer[LinkCount];

        public StormInkManacles(Transform parent, Material material)
        {
            _root = new GameObject("Warden manacle lash").transform;
            _root.SetParent(parent, false);
            if (_iron == null) _iron = PaintLink();
            for (int i = 0; i < LinkCount; i++)
            {
                var link = new GameObject(i == LinkCount - 1 ? "Weighted cuff" : "Iron link " + i);
                link.transform.SetParent(_root, false);
                var renderer = link.AddComponent<SpriteRenderer>();
                renderer.sprite = _iron;
                renderer.sharedMaterial = material;
                _links[i] = renderer;
            }
        }

        public void Hide() { if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false); }

        public void Draw(Vector2 palm, Vector2 otherPalm, int row, StormInkWeaponMotion.Phase phase,
            float progress, bool reduced, int order, float alpha)
        {
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            float yaw = (2 - row) * Mathf.PI / 4f;
            var forward = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw) * ArenaView.GroundSquash);
            var across = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw) * ArenaView.GroundSquash);
            float t = Mathf.Clamp01(progress);
            var hang = otherPalm + new Vector2(0, -.12f);
            var lash = palm + forward * .96f + across * .17f - Vector2.up * .07f;
            var tip = hang;
            var bend = (palm + tip) * .5f - Vector2.up * .49f;
            switch (phase)
            {
                case StormInkWeaponMotion.Phase.Ready:
                    tip = palm - forward * .26f - across * .37f + Vector2.up * (.15f + .12f * t);
                    bend = palm - across * .53f - Vector2.up * .24f;
                    break;
                case StormInkWeaponMotion.Phase.Strike:
                    tip = lash;
                    bend = (palm + tip) * .5f - across * (reduced ? .08f : .32f * (1f - t));
                    break;
                case StormInkWeaponMotion.Phase.Follow:
                    tip = Vector2.Lerp(lash, hang, Mathf.SmoothStep(0, 1, t));
                    bend = (palm + tip) * .5f + across * (.16f * (1f - t)) - Vector2.up * (.48f * t);
                    break;
            }
            int baseOrder = order + (row >= 3 && row <= 5 ? -1 : 3);
            Vector2 start = palm;
            for (int i = 0; i < LinkCount; i++)
            {
                float u = (i + 1f) / LinkCount;
                var end = Vector2.Lerp(Vector2.Lerp(palm, bend, u), Vector2.Lerp(bend, tip, u), u);
                var axis = end - start;
                bool cuff = i == LinkCount - 1;
                var link = _links[i];
                link.sortingOrder = baseOrder + (i % 2);
                link.color = new Color(1, 1, 1, alpha);
                link.transform.localPosition = (start + end) * .5f;
                link.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg);
                link.transform.localScale = new Vector3(Mathf.Max(cuff ? .18f : .064f, axis.magnitude * 1.32f),
                    cuff ? .139f : i % 2 == 0 ? .081f : .045f, 1);
                start = end;
            }
        }

        // Flat iron faces, a broken cold edge and an ink-black opening survive at gameplay scale.
        static Sprite PaintLink()
        {
            const int n = 64;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Warden iron link",
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + .5f) * 2 / n - 1, v = (y + .5f) * 2 / n - 1;
                float d = Mathf.Max(Mathf.Abs(v) * 1.12f, Mathf.Abs(u) * .94f + Mathf.Abs(v) * .34f);
                if (d > .96f || d < .37f) continue;
                Color32 ink = new Color32(31, 24, 34, 255), iron = new Color32(156, 142, 151, 255);
                bool edge = v > .13f && u > -.72f && u < .49f && (u > -.17f || u < -.25f);
                pixels[y * n + x] = d < .48f || d > .83f ? ink : edge ? new Color32(222, 195, 158, 255) : iron;
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
