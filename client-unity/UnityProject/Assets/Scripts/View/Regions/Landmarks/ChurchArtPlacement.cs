using UnityEngine;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Client
{
    /// <summary>The three painted views supported by a mounted church furnishing.</summary>
    public enum ChurchArtView { Front, Side, Back }

    /// <summary>Resolved, artist-editable placement on one generated church part.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ChurchArtPlacement : MonoBehaviour
    {
        public ChurchArtDefinition Definition;
        public ChurchInteriorDefinition Layout;
        public string SlotId;
        public ChurchArtView View;

        Landmark _landmark;
        MapAnchor _anchor;
        Vector2 _contactTile;
        float _slotRotation;
        bool? _mirrorOverride;
        ChurchArtResolved _resolved;
        SpriteRenderer _renderer;
        Sprite _originalSprite;
        Color _originalColor;
        int _baseSortingOrder;
        bool _configured;

        public ChurchArtResolved Resolve(MapAnchor anchor)
        {
            return Layout != null ? Layout.Resolve(SlotId, Definition, anchor, _mirrorOverride) :
                new ChurchArtResolved { Values = Definition != null ? Definition.Defaults : default(ChurchArtValues), Mirrored = ChurchArtOrientation.Resolve(anchor, _mirrorOverride).Mirrored, Mount = Definition != null ? Definition.Mount : ChurchArtMount.Upright, EmitsCandleLight = Definition != null && Definition.EmitsCandleLight };
        }

        public void Configure(Landmark landmark, MapAnchor anchor, Vector2 contactTile, float slotRotation = 0f, bool? mirrorOverride = null)
        {
            _landmark = landmark; _anchor = anchor; _contactTile = contactTile; _slotRotation = slotRotation; _mirrorOverride = mirrorOverride;
            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer != null)
            {
                _originalSprite = _renderer.sprite;
                _originalColor = _renderer.color;
                _baseSortingOrder = _renderer.sortingOrder;
            }
            _configured = true;
            RefreshResolved();
        }

        public Vector3 ContactPoint { get { return PointAtUv(_resolved.Values.Contact); } }
        public Vector3 FlameWorldPoint { get { return PointAtUv(_resolved.Values.FlameSocket); } }

        public Vector2 WorldToLocalOffset(Vector3 worldPoint)
        {
            Vector3 p = transform.parent != null ? transform.parent.InverseTransformPoint(worldPoint) : worldPoint;
            Vector2 tile = ChurchArtOrientation.LocalToTile(new Vector2(p.x, p.y - _resolved.Values.MountHeight));
            return ChurchArtOrientation.Resolve(_anchor, _mirrorOverride).MapToLocalOffset(tile - _contactTile);
        }

        public Vector2 WorldToUv(Vector3 worldPoint)
        {
            if (_renderer == null || _renderer.sprite == null) return Vector2.zero;
            Vector3 p = transform.InverseTransformPoint(worldPoint);
            Vector2 pivot = new Vector2(_renderer.sprite.pivot.x / _renderer.sprite.rect.width, _renderer.sprite.pivot.y / _renderer.sprite.rect.height);
            Vector2 size = _renderer.sprite.bounds.size;
            return new Vector2(pivot.x + p.x / Mathf.Max(.001f, size.x), pivot.y + p.y / Mathf.Max(.001f, size.y));
        }

        public void RefreshResolved()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (!_configured) return;
            _resolved = Resolve(_anchor);
            var value = _resolved.Values;
            Sprite selected = View == ChurchArtView.Side ? value.Side : View == ChurchArtView.Back ? value.Back : value.Front;
            Sprite sprite = selected != null ? selected : _originalSprite;
            if (_renderer == null || sprite == null) return;
            _renderer.sprite = sprite;
            _renderer.color = Definition != null ? value.Tint : _originalColor;
            _renderer.sharedMaterial = value.Material != null ? value.Material : _landmark != null ? _landmark.PaintedMaterial(_resolved.EmitsCandleLight) : ShowcaseSprites.Lit;
            float width = Mathf.Max(.001f, value.Width);
            float angle = _slotRotation + value.RotationDegrees;
            bool floorLengthRunsAlongMapY = _resolved.Mount == ChurchArtMount.Floor && Mathf.Abs(Mathf.Sin(angle * Mathf.Deg2Rad)) > .5f;
            float projectedWidth = width * (floorLengthRunsAlongMapY ? ArenaView.GroundSquash : 1f);
            float vertical = _resolved.Mount == ChurchArtMount.Floor ? Mathf.Max(.001f, value.Depth) * ArenaView.GroundSquash : Mathf.Max(.001f, value.Height);
            var bounds = sprite.bounds.size;
            transform.localScale = new Vector3(projectedWidth / Mathf.Max(.001f, bounds.x), vertical / Mathf.Max(.001f, bounds.y), 1f);
            Vector2 pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            Vector2 originOffset = new Vector2((value.Contact.x - pivot.x) * projectedWidth,
                (value.Contact.y - pivot.y) * vertical);
            float radians = (_slotRotation + value.RotationDegrees) * Mathf.Deg2Rad;
            Vector2 rotatedContact = new Vector2(originOffset.x * Mathf.Cos(radians) - originOffset.y * Mathf.Sin(radians),
                originOffset.x * Mathf.Sin(radians) + originOffset.y * Mathf.Cos(radians));
            var orientation = ChurchArtOrientation.Resolve(_anchor, _mirrorOverride);
            Vector2 tileContact = _contactTile + orientation.LocalToMapOffset(value.LocalOffset);
            Vector2 local = ChurchArtOrientation.TileToLocal(tileContact) - rotatedContact;
            local.y += value.MountHeight;
            transform.localPosition = new Vector3(local.x, local.y, transform.localPosition.z);
            transform.localRotation = Quaternion.Euler(0f, 0f, _slotRotation + value.RotationDegrees);
            _renderer.sortingOrder = _baseSortingOrder + value.SortingOffset;
            if (_resolved.EmitsCandleLight && _landmark != null)
            {
                float lift = Mathf.Max(.05f, (value.FlameSocket.y - value.Contact.y) * vertical + value.MountHeight);
                Vector2 mapContact = tileContact;
                _landmark.RegisterCandleSocket(GetInstanceID(), mapContact, lift, value.FlameColor,
                    value.FlameIntensity, value.SpillRadius, value.SpillIntensity);
            }
            else if (_landmark != null) _landmark.RemoveCandleSocket(GetInstanceID());
        }

        Vector3 PointAtUv(Vector2 uv)
        {
            if (_renderer == null || _renderer.sprite == null) return transform.position;
            var sprite = _renderer.sprite;
            Vector2 pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            Vector2 size = sprite.bounds.size;
            return transform.TransformPoint(new Vector3((uv.x - pivot.x) * size.x, (uv.y - pivot.y) * size.y, 0f));
        }
    }
}
