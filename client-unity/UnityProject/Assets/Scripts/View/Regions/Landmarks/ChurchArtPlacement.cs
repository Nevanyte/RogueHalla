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
        ChurchCandleFlameLayer _flameLayer;
        Sprite _originalSprite;
        Color _originalColor;
        int _baseSortingOrder;
        bool _configured;
        bool _originalFlipX;
        public ChurchArtView ResolvedView { get; private set; }

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
                _originalFlipX = _renderer.flipX;
                _originalColor = _renderer.color;
                _baseSortingOrder = _renderer.sortingOrder;
            }
            _configured = true;
            RefreshResolved();
        }

        public Vector3 ContactPoint { get { return PointAtUv(_resolved.Values.Contact); } }
        public Vector3 FlameWickPoint => PointAtUv(_resolved.Values.FlameSocket);
        public Vector3 FlameWorldPoint { get { return PointAtUv(_resolved.Values.FlameSocket) + transform.up * (_resolved.Values.FlameLayerSize.y * .375f); } }
        public SpriteRenderer FlameRenderer { get { return _flameLayer != null ? _flameLayer.Renderer : null; } }

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
            float localTurn = Definition != null ? Definition.LocalTurn(value.RotationDegrees) : value.RotationDegrees;
            float angle = _slotRotation + localTurn;
            ChurchArtView resolvedView = ResolveSupportedView(View, angle, _resolved.Mount);
            ResolvedView = resolvedView;
            Sprite selected = resolvedView == ChurchArtView.Side ? value.Side : resolvedView == ChurchArtView.Back ? value.Back : value.Front;
            if (_resolved.Mount == ChurchArtMount.Wall && value.PaneMode == ChurchWindowPaneMode.Jamb && value.Side != null)
                selected = value.Side;
            Sprite sprite = selected != null ? selected : _originalSprite;
            if (_renderer == null || sprite == null) return;
            _renderer.sprite = sprite;
            _renderer.flipX = _originalFlipX ^ (_resolved.Mirrored && (Definition == null || Definition.ReflectPainting));
            _renderer.enabled = _resolved.Mount != ChurchArtMount.Wall ||
                value.PaneMode != ChurchWindowPaneMode.Concealed && (Definition == null || Definition.AllowsWindow(SlotId));
            _renderer.color = Definition != null ? value.Tint : _originalColor;
            _renderer.sharedMaterial = value.Material != null ? value.Material : _landmark != null ? _landmark.PaintedMaterial(_resolved.EmitsCandleLight) : ShowcaseSprites.Lit;
            float width = Mathf.Max(.001f, value.Width);
            float screenRotation = 0f;
            float projectedWidth = width;
            float vertical = Mathf.Max(.001f, value.Height);
            if (_resolved.Mount == ChurchArtMount.Floor)
            {
                ProjectFloorSize(width, value.Depth, angle, ArenaView.GroundSquash, out projectedWidth, out vertical, out screenRotation);
            }
            if (_resolved.Mount == ChurchArtMount.Wall && value.PaneMode == ChurchWindowPaneMode.Jamb) projectedWidth = Mathf.Max(.001f, value.Depth);
            var bounds = sprite.bounds.size;
            transform.localScale = new Vector3(projectedWidth / Mathf.Max(.001f, bounds.x), vertical / Mathf.Max(.001f, bounds.y), 1f);
            Vector2 pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            Vector2 originOffset = new Vector2((value.Contact.x - pivot.x) * projectedWidth,
                (value.Contact.y - pivot.y) * vertical);
            float screenRadians = screenRotation * Mathf.Deg2Rad;
            Vector2 rotatedContact = new Vector2(originOffset.x * Mathf.Cos(screenRadians) - originOffset.y * Mathf.Sin(screenRadians),
                originOffset.x * Mathf.Sin(screenRadians) + originOffset.y * Mathf.Cos(screenRadians));
            var orientation = ChurchArtOrientation.Resolve(_anchor, _mirrorOverride);
            Vector2 tileContact = _contactTile + orientation.LocalToMapOffset(value.LocalOffset);
            Vector2 local = ChurchArtOrientation.TileToLocal(tileContact) - rotatedContact;
            local.y += value.MountHeight;
            transform.localPosition = new Vector3(local.x, local.y, transform.localPosition.z);
            transform.localRotation = Quaternion.Euler(0f, 0f, screenRotation);
            _renderer.sortingOrder = _baseSortingOrder + value.SortingOffset;
            if (_resolved.EmitsCandleLight && value.FlameLayer != null && value.FlameLayerSize.x > 0f && value.FlameLayerSize.y > 0f)
            {
                if (_flameLayer == null)
                {
                    var flameObject = new GameObject("Church main candle flame");
                    flameObject.transform.SetParent(transform, false);
                    _flameLayer = flameObject.AddComponent<ChurchCandleFlameLayer>();
                }
                _flameLayer.Configure(_renderer, value.FlameLayer, value.FlameSocket, value.FlameLayerSize,
                    value.Tint, _renderer.sharedMaterial, _anchor.Centre.X * .31f + _anchor.Centre.Y * .73f + SlotSeed(SlotId),
                    _renderer.sortingOrder + 1);
            }
            else if (_flameLayer != null) _flameLayer.gameObject.SetActive(false);
            if (_resolved.EmitsCandleLight && _landmark != null)
            {
                float lift = Mathf.Max(.05f, (value.FlameSocket.y - value.Contact.y) * vertical + value.MountHeight + value.FlameLayerSize.y * .375f);
                Vector2 mapContact = tileContact;
                mapContact.x += (value.FlameSocket.x - value.Contact.x) * projectedWidth;
                _landmark.RegisterCandleSocket(GetInstanceID(), mapContact, lift, value.FlameColor,
                    value.FlameIntensity, value.SpillRadius, value.SpillIntensity);
            }
            else if (_landmark != null) _landmark.RemoveCandleSocket(GetInstanceID());
            if (Definition != null && Definition.EmitsWindowLight && _resolved.Mount == ChurchArtMount.Wall)
                ChurchGarnetLight.Sync(_landmark, _renderer, value.PaneMode, tileContact, FlameWickPoint,
                    value.FlameColor, value.SpillRadius, value.FlameIntensity, value.SpillIntensity);
            else ChurchGarnetLight.Detach(_landmark, _renderer);
        }

        void OnEnable()
        {
            if (_configured) RefreshResolved();
        }

        void OnDisable()
        {
            if (_landmark != null) _landmark.RemoveCandleSocket(GetInstanceID());
            ChurchGarnetLight.Detach(_landmark, _renderer);
        }

        Vector3 PointAtUv(Vector2 uv)
        {
            if (_renderer == null || _renderer.sprite == null) return transform.position;
            var sprite = _renderer.sprite;
            Vector2 pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            Vector2 size = sprite.bounds.size;
            return transform.TransformPoint(new Vector3((uv.x - pivot.x) * size.x, (uv.y - pivot.y) * size.y, 0f));
        }

        /// <summary>Projects a floor prop's two map-plane axes continuously. Its authored width remains full-size while map-down depth foreshortens.</summary>
        public static void ProjectFloorSize(float width, float depth, float mapDegrees, float groundSquash,
            out float projectedWidth, out float projectedDepth, out float screenRotation)
        {
            float radians = mapDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            projectedWidth = Mathf.Max(.001f, width);
            projectedDepth = Mathf.Max(.001f, depth) * Mathf.Sqrt(sin * sin + groundSquash * groundSquash * cos * cos);
            screenRotation = Mathf.Atan2(-groundSquash * sin, cos) * Mathf.Rad2Deg;
        }

        /// <summary>Chooses the nearest supported painted view for upright art without rotating the painting flat.</summary>
        public static ChurchArtView ResolveSupportedView(ChurchArtView baseView, float mapDegrees, ChurchArtMount mount)
        {
            if (mount == ChurchArtMount.Floor) return baseView;
            int turn = Mathf.RoundToInt(mapDegrees / 90f) % 4;
            if (turn < 0) turn += 4;
            if (turn == 0) return baseView;
            if (turn == 2) return baseView == ChurchArtView.Front ? ChurchArtView.Back : baseView == ChurchArtView.Back ? ChurchArtView.Front : ChurchArtView.Side;
            return ChurchArtView.Side;
        }

        static float SlotSeed(string slot)
        {
            unchecked { uint h = 2166136261u; for (int i = 0; i < slot.Length; i++) h = (h ^ slot[i]) * 16777619u; return (h & 0xffff) * (Mathf.PI * 2f / 65536f); }
        }
    }
}
