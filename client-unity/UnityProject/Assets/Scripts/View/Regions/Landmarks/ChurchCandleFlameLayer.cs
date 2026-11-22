using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>A separate, painted main flame that flickers independently of its candle wick and candelabrum.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ChurchCandleFlameLayer : MonoBehaviour
    {
        SpriteRenderer _renderer;
        Vector3 _baseScale;
        float _phase;

        public SpriteRenderer Renderer { get { return _renderer != null ? _renderer : (_renderer = GetComponent<SpriteRenderer>()); } }

        public void Configure(SpriteRenderer baseRenderer, Sprite flame, Vector2 flameSocket, Vector2 layerSize,
            Color tint, Material material, float phase, int sortingOrder)
        {
            if (baseRenderer == null || flame == null || layerSize.x <= 0f || layerSize.y <= 0f)
            { gameObject.SetActive(false); return; }
            gameObject.SetActive(true);
            _renderer = GetComponent<SpriteRenderer>();
            var parent = baseRenderer.transform;
            if (transform.parent != parent) transform.SetParent(parent, false);
            _renderer.sprite = flame; _renderer.color = tint; _renderer.sharedMaterial = material;
            _renderer.sortingOrder = sortingOrder;
            Vector2 pivot = new Vector2(baseRenderer.sprite.pivot.x / baseRenderer.sprite.rect.width,
                baseRenderer.sprite.pivot.y / baseRenderer.sprite.rect.height);
            Vector2 sourceSize = baseRenderer.sprite.bounds.size;
            float socketX = (flameSocket.x - pivot.x) * (baseRenderer.flipX ? -1f : 1f);
            ConfigureSizeAndMotion(parent, flame, layerSize, tint, material, phase, sortingOrder,
                new Vector3(socketX * sourceSize.x, (flameSocket.y - pivot.y) * sourceSize.y, -.01f));
        }

        /// <summary>Configures a flame using an already resolved body-local foot offset from its source painting.</summary>
        public void ConfigureAtLocalOffset(SpriteRenderer baseRenderer, Sprite flame, Vector2 localOffset, Vector2 layerSize,
            Color tint, Material material, float phase, int sortingOrder)
        {
            if (baseRenderer == null || baseRenderer.sprite == null || flame == null || layerSize.x <= 0f || layerSize.y <= 0f)
            { gameObject.SetActive(false); return; }
            gameObject.SetActive(true);
            _renderer = GetComponent<SpriteRenderer>();
            var parent = baseRenderer.transform;
            if (transform.parent != parent) transform.SetParent(parent, false);
            ConfigureSizeAndMotion(parent, flame, layerSize, tint, material, phase, sortingOrder,
                new Vector3(localOffset.x, localOffset.y, -.01f));
        }

        void ConfigureSizeAndMotion(Transform parent, Sprite flame, Vector2 layerSize, Color tint,
            Material material, float phase, int sortingOrder, Vector3 localPosition)
        {
            _renderer.sprite = flame; _renderer.color = tint; _renderer.sharedMaterial = material;
            _renderer.sortingOrder = sortingOrder;
            transform.localPosition = localPosition;
            Vector2 flameSize = flame.bounds.size;
            Vector3 parentScale = parent.lossyScale;
            transform.localScale = new Vector3(layerSize.x / Mathf.Max(.001f, flameSize.x * Mathf.Abs(parentScale.x)),
                layerSize.y / Mathf.Max(.001f, flameSize.y * Mathf.Abs(parentScale.y)), 1f);
            _baseScale = transform.localScale;
            _phase = phase;
        }

        void LateUpdate()
        {
            if (_renderer == null || !_renderer.enabled) return;
            if (AtmosphereQuality.ReducedMotion)
            {
                transform.localScale = _baseScale;
                transform.localRotation = Quaternion.identity;
                return;
            }
            float t = Time.unscaledTime;
            float sway = Mathf.Sin(t * 9.1f + _phase) * .035f + Mathf.Sin(t * 16.4f + _phase * 1.7f) * .018f;
            float pulse = Mathf.Sin(t * 7.3f + _phase * .61f) * .035f;
            transform.localScale = new Vector3(_baseScale.x * (1f - pulse), _baseScale.y * (1f + pulse), _baseScale.z);
            transform.localRotation = Quaternion.Euler(0f, 0f, sway * 18f);
        }
    }
}
