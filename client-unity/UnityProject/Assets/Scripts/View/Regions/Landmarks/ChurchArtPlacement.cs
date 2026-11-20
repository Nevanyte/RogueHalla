using UnityEngine;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Client
{
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
        bool _originalFlipX;
        int _baseSortingOrder;
        bool _configured;
        Transform _floorProjectionRoot;
        public ChurchArtView ResolvedView { get; private set; }
        public ChurchWindowPaneMode ResolvedPaneMode { get; private set; }

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
                _originalFlipX = _renderer.flipX;
                _baseSortingOrder = _renderer.sortingOrder;
            }
            _configured = true;
            RefreshResolved();
        }

        public Vector3 ContactPoint { get { return PointAtUv(_resolved.Values.Contact); } }
        /// <summary>The ground tile under the resolved contact, excluding mounting height.</summary>
        public Vector2 GroundTile
        {
            get
            {
                return _contactTile + ChurchArtOrientation.Resolve(_anchor, _mirrorOverride).LocalToMapOffset(_resolved.Values.LocalOffset);
            }
        }
        public Vector3 FlameWickPoint { get { return PointAtUv(_resolved.Values.FlameSocket); } }
        public Vector3 FlameWorldPoint
        {
            get
            {
                var values = _resolved.Values;
                float pivotY = values.FlameLayer != null ? values.FlameLayer.pivot.y / values.FlameLayer.rect.height : 0.105f;
                return FlameWickPoint + transform.up * ((.5f - pivotY) * values.FlameLayerSize.y);
            }
        }
        public SpriteRenderer FlameRenderer { get { return _flameLayer != null ? _flameLayer.Renderer : null; } }

        public Vector2 WorldToLocalOffset(Vector3 worldPoint)
        {
            Vector3 p = transform.parent != null ? transform.parent.InverseTransformPoint(worldPoint) : worldPoint;
            Vector2 local = new Vector2(p.x, p.y - _resolved.Values.MountHeight);
            Vector2 tile = _floorProjectionRoot != null ? new Vector2(local.x, -local.y) : ChurchArtOrientation.LocalToTile(local);
            return ChurchArtOrientation.Resolve(_anchor, _mirrorOverride).MapToLocalOffset(tile - _contactTile);
        }

        public Vector2 WorldToUv(Vector3 worldPoint)
        {
            if (_renderer == null || _renderer.sprite == null) return Vector2.zero;
            Vector3 p = transform.InverseTransformPoint(worldPoint);
            Vector2 pivot = new Vector2(_renderer.sprite.pivot.x / _renderer.sprite.rect.width, _renderer.sprite.pivot.y / _renderer.sprite.rect.height);
            Vector2 size = _renderer.sprite.bounds.size;
            float u = pivot.x + p.x / Mathf.Max(.001f, size.x);
            if (_renderer.flipX) u = 1f - u;
            return new Vector2(u, pivot.y + p.y / Mathf.Max(.001f, size.y));
        }

        public void RefreshResolved()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (!_configured) return;
            _resolved = Resolve(_anchor);
            var value = _resolved.Values;
            bool floor = _resolved.Mount == ChurchArtMount.Floor;
            EnsureFloorProjection(floor);
            ChurchArtView resolvedView = ResolveSupportedView(View, _slotRotation + value.RotationDegrees, _resolved.Mount);
            ResolvedView = resolvedView;
            ChurchWindowPaneMode paneMode = _resolved.Mount == ChurchArtMount.Wall
                ? ResolvePaneMode(value.PaneMode, resolvedView, value.Side != null) : value.PaneMode;
            ResolvedPaneMode = paneMode;
            ChurchArtView renderView = resolvedView;
            if (_resolved.Mount == ChurchArtMount.Wall && paneMode == ChurchWindowPaneMode.Full && renderView == ChurchArtView.Side)
                renderView = View == ChurchArtView.Back ? ChurchArtView.Back : ChurchArtView.Front;
            Sprite selected = renderView == ChurchArtView.Side ? value.Side : renderView == ChurchArtView.Back ? value.Back : value.Front;
            if (_resolved.Mount == ChurchArtMount.Wall && paneMode == ChurchWindowPaneMode.Jamb) selected = value.Side;
            Sprite sprite = selected != null ? selected : _originalSprite;
            if (_renderer == null || sprite == null) return;
            _renderer.sprite = sprite;
            _renderer.enabled = _resolved.Mount != ChurchArtMount.Wall || paneMode != ChurchWindowPaneMode.Concealed;
            _renderer.flipX = _originalFlipX ^ _resolved.Mirrored;
            _renderer.color = Definition != null ? value.Tint : _originalColor;
            _renderer.sharedMaterial = value.Material != null ? value.Material : _landmark != null ? _landmark.PaintedMaterial(_resolved.EmitsCandleLight) : ShowcaseSprites.Lit;
            float width = Mathf.Max(.001f, value.Width);
            float angle = _slotRotation + value.RotationDegrees;
            float screenRotation = 0f;
            float localWidth = width;
            float localDepth = Mathf.Max(.001f, value.Height);
            if (_resolved.Mount == ChurchArtMount.Floor)
            {
                ProjectFloorSize(width, value.Depth, angle, ArenaView.GroundSquash, out _, out _, out screenRotation);
                localDepth = Mathf.Max(.001f, value.Depth);
            }
            if (_resolved.Mount == ChurchArtMount.Wall && paneMode == ChurchWindowPaneMode.Jamb) localWidth *= .34f;
            var bounds = sprite.bounds.size;
            transform.localScale = new Vector3(localWidth / Mathf.Max(.001f, bounds.x), localDepth / Mathf.Max(.001f, bounds.y), 1f);
            Vector2 pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            Vector2 originOffset = new Vector2((value.Contact.x - pivot.x) * localWidth,
                (value.Contact.y - pivot.y) * localDepth);
            float screenRadians = floor ? -angle * Mathf.Deg2Rad : 0f;
            Vector2 rotatedContact = new Vector2(originOffset.x * Mathf.Cos(screenRadians) - originOffset.y * Mathf.Sin(screenRadians),
                originOffset.x * Mathf.Sin(screenRadians) + originOffset.y * Mathf.Cos(screenRadians));
            var orientation = ChurchArtOrientation.Resolve(_anchor, _mirrorOverride);
            Vector2 tileContact = _contactTile + orientation.LocalToMapOffset(value.LocalOffset);
            Vector2 local = floor ? new Vector2(tileContact.x, -tileContact.y) - rotatedContact : ChurchArtOrientation.TileToLocal(tileContact) - rotatedContact;
            local.y += value.MountHeight;
            transform.localPosition = new Vector3(local.x, local.y, transform.localPosition.z);
            transform.localRotation = Quaternion.Euler(0f, 0f, floor ? -angle : screenRotation);
            int groundRowDelta = Mathf.FloorToInt(tileContact.y) - Mathf.FloorToInt(_contactTile.y);
            _renderer.sortingOrder = _baseSortingOrder + value.SortingOffset + groundRowDelta * 16;
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
                Vector3 lightLocal = _landmark.Root.transform.InverseTransformPoint(FlameWorldPoint);
                // AccentLights.Bind reconstructs a world point from a ground tile plus a vertical lift.
                // Keep those components separate: contact supplies the ground row, while the actual wick/flame
                // height (including MountHeight and the art socket) is carried only by Lift.
                float groundTileY = tileContact.y;
                float lift = Mathf.Max(.05f, lightLocal.y + groundTileY * ArenaView.GroundSquash);
                _landmark.RegisterCandleSocket(GetInstanceID(), new Vector2(lightLocal.x, groundTileY), lift, value.FlameColor,
                    value.FlameIntensity, value.SpillRadius, value.SpillIntensity);
            }
            else if (_landmark != null) _landmark.RemoveCandleSocket(GetInstanceID());
        }

        void EnsureFloorProjection(bool floor)
        {
            if (floor)
            {
                if (_floorProjectionRoot == null)
                {
                    var projection = new GameObject("Church map-plane projection");
                    projection.transform.SetParent(transform.parent, false);
                    projection.transform.localPosition = Vector3.zero;
                    projection.transform.localRotation = Quaternion.identity;
                    projection.transform.localScale = new Vector3(1f, ArenaView.GroundSquash, 1f);
                    transform.SetParent(projection.transform, false);
                    _floorProjectionRoot = projection.transform;
                }
                _floorProjectionRoot.localScale = new Vector3(1f, ArenaView.GroundSquash, 1f);
            }
            else if (_floorProjectionRoot != null)
            {
                var projection = _floorProjectionRoot;
                transform.SetParent(projection.parent, false);
                UnityEngine.Object.Destroy(projection.gameObject);
                _floorProjectionRoot = null;
            }
        }

        Vector3 PointAtUv(Vector2 uv)
        {
            if (_renderer == null || _renderer.sprite == null) return transform.position;
            var sprite = _renderer.sprite;
            Vector2 pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            Vector2 size = sprite.bounds.size;
            if (_renderer.flipX) uv.x = 1f - uv.x;
            return transform.TransformPoint(new Vector3((uv.x - pivot.x) * size.x, (uv.y - pivot.y) * size.y, 0f));
        }

        /// <summary>Projects both authored map-plane lengths continuously through the arena's diagonal squash.</summary>
        public static void ProjectFloorSize(float width, float depth, float mapDegrees, float groundSquash,
            out float projectedWidth, out float projectedDepth, out float screenRotation)
        {
            float radians = mapDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            projectedWidth = Mathf.Max(.001f, width) * Mathf.Sqrt(cos * cos + groundSquash * groundSquash * sin * sin);
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

        /// <summary>Automatic panes follow their resolved wall normal: front glass, edge-on stone jamb, or concealed back face.</summary>
        public static ChurchWindowPaneMode ResolvePaneMode(ChurchWindowPaneMode requested, ChurchArtView view, bool hasJamb)
        {
            if (requested != ChurchWindowPaneMode.Auto) return requested;
            if (view == ChurchArtView.Front) return ChurchWindowPaneMode.Full;
            if (view == ChurchArtView.Side && hasJamb) return ChurchWindowPaneMode.Jamb;
            return ChurchWindowPaneMode.Concealed;
        }

        static float SlotSeed(string slot)
        {
            unchecked { uint h = 2166136261u; for (int i = 0; i < slot.Length; i++) h = (h ^ slot[i]) * 16777619u; return (h & 0xffff) * (Mathf.PI * 2f / 65536f); }
        }
    }
}
