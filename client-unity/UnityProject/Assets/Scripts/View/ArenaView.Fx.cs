using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client
{
    /// <summary>
    /// Confirmed fx intake and scheduling, the damage-number flashes, projectile drawing and the Frost Wall aim preview.
    /// </summary>
    public sealed partial class ArenaView
    {
        /// <summary>Presentation-only feedback at the owner's click; see <see cref="SpriteActorPresenter.LocalAttackCue"/>.</summary>
        public void LocalAttackCue(uint id) { _actors.LocalAttackCue(id); }
        float _frostLength = 224;
        /// <summary>Draws or puts away the Frost Wall aim preview (display only; WishAim sends the cast).</summary>
        public void TickFrostWallAim(Session session, bool aiming, in WishAimFrame f)
        {
            if (f.Numbers.HasValue) _frostLength = (float)f.Numbers.Value.Length;
            _frostWall.Aim(session, aiming, f, _frostTint);
        }
        public void ConfirmedFx(ServerMsg.Fx fx) { ConfirmedFx(fx, 0, double.NaN); }
        /// <summary>
        /// Cues on a remote player drawn by interpolation wait until render time reaches <paramref name="dueMs"/> (source ms
        /// of the Fx tick), so they meet the drawn pose. Cues involving <paramref name="owner"/>, NPCs (drawn at their newest
        /// position) and NaN <paramref name="dueMs"/> are shown on arrival.
        /// </summary>
        public void ConfirmedFx(ServerMsg.Fx fx, uint owner, double dueMs)
        {
            if (fx == null) return;
            foreach (var f in fx.List)
            {
                if (f.Kind == FxKind.Hit || f.Kind == FxKind.SigHit) _campAttack.NoteHit(f.By, f.Victim);   // camp patience outlines: damage the local player trades with a camp's mobs
                if (f.Kind == FxKind.WishStarted || f.Kind == FxKind.WishLanded || f.Kind == FxKind.WishEnded)
                {
                    _wishes.Accept(f);
                    _wishFx.Accept(f);
                    if (f.Kind == FxKind.WishStarted && f.Ability == (byte)AbilityId.Freeze && _frostWish == FrostWishKind.Wall && f.HasAnchor && f.Caster.HasValue && _actors.TryPosition(f.Caster.Value, out var cw))
                        _frostWall.Telegraph(cw.x / Scale, -cw.y / (Scale * GroundSquash), f.AnchorX, f.AnchorY, _frostLength, f.EventTime, f.Duration);
                    continue;
                }
                // Protocol 16 fx (ADR0026): the wind-up telegraph and its end; the rest are numbers drawn by PresentFx.
                if (f.Kind == FxKind.Windup) { DiagWindupFx++; _hitZones?.Windup(f.By, f.Weapon, f.A, f.Ms, Time.unscaledTime); DiagWindupDrawn = HitZonePresenter.DiagWindupsStarted; }
                if (f.Kind == FxKind.Cancel) _hitZones?.CancelWindup(f.Id);
                if (f.Kind == FxKind.Stagger) _hitZones?.CancelWindup(f.Id);
                if (f.Kind == FxKind.Hit && f.Critical) DiagCritFx++;
                if (f.Kind == FxKind.Hit) { DiagHitFx++; if (f.Victim == _localId) { DiagHitFxOnMe++; DiagLastAmountOnMe = f.Amount; DiagLastPassiveOnMe = f.Passive; } } // scenario probe counters
                if (f.Kind == FxKind.Hit && f.Victim == _localId && _localId != 0 && !f.Passive) _vignette.Hit(f.Amount, _selfMaxHp, Time.unscaledTime); // legacy screenFeedback: real hits only
                uint subject = f.Kind == FxKind.Hit || f.Kind == FxKind.SigHit ? f.Victim : f.Kind == FxKind.Stagger || f.Kind == FxKind.Block || f.Kind == FxKind.Heal || f.Kind == FxKind.Cancel ? f.Id : f.By;
                var cue=new PendingFx{Kind=f.Kind,Subject=subject,Amount=f.Amount,X=f.X,Y=f.Y,A=f.A,Weapon=f.Weapon,WindupMs=f.Ms,By=f.By,Passive=f.Passive,Crit=f.Critical,SigKind=f.HitKind,CreatedAt=Time.unscaledTime};
                bool own = owner != 0 && (subject == owner || f.By == owner || f.Victim == owner);
                if (!own && _renderReady && dueMs > _renderMs && dueMs - _renderMs <= MaxFxLeadMs &&
                    _scheduledFx.Count < MaxScheduledFx && _actors.Contains(subject) && !IsLatestNpc(subject))
                {
                    cue.DueMs = dueMs; cue.QueuedAt = Time.unscaledTime;
                    _scheduledFx.Add(cue);
                    continue;
                }
                if (_actors.Contains(subject)) PresentFx(cue);
                else if(_pendingFx.Count<64) _pendingFx.Add(cue); // Copy scalars; decoded messages are recycled.
            }
        }
        bool IsLatestNpc(uint id)
        {
            foreach (var a in _latestNpcs) if (a.Id == id) return true;
            return false;
        }
        /// <summary>Called after this frame's poses are drawn; compacts in place without allocating.</summary>
        void ReleaseScheduledFx()
        {
            int keep = 0; float now = Time.unscaledTime;
            for (int i = 0; i < _scheduledFx.Count; i++)
            {
                var cue = _scheduledFx[i];
                bool due = !_renderReady || cue.DueMs <= _renderMs || cue.DueMs - _renderMs > MaxFxLeadMs || now - cue.QueuedAt > MaxFxHoldSeconds;
                if (!due) { _scheduledFx[keep++] = cue; continue; }
                if (_actors.Contains(cue.Subject)) PresentFx(cue); // An actor that left view drops its cue.
            }
            _scheduledFx.RemoveRange(keep, _scheduledFx.Count - keep);
        }
        /// <summary>A clock epoch cut clears cosmetics only; latest membership and collections remain intact.</summary>
        public void ResetTransientPresentation(double serverTimeMs = 0)
        {
            _sourceTimeMs = serverTimeMs;
            _pendingFx.Clear();
            _scheduledFx.Clear();
            _wishes.Reset();
            _wishFx.Reset(); _campAttack.Clear(); _hitZones?.Clear(); _worldIndicators?.Clear(); _vignette?.Clear(); _patience?.Update(null, null, null, false, null); _carrierFlags?.Update(null, null, null);
            _actors.CancelFades(); _visionFog.Reset();
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var flash = _flashes[i];
                flash.Sprite.gameObject.SetActive(false); flash.Text.gameObject.SetActive(false); flash.Plate.gameObject.SetActive(false);
                _flashes.RemoveAt(i); _flashPool.Push(flash);
            }
            _actors.ResetTransientTimers(); _sparks.Clear();
        }

        void PresentFx(PendingFx f)
        {
                if (f.Kind == FxKind.Heal && Indicators.HealText(f.Amount) == null) return;      // heals under 1 are not shown
                if (!_actors.TryPosition(f.Subject, out var p)) return;
                _actors.ConfirmedCue(f.Subject, f.Kind, (WeaponId)f.Weapon, f.A, f.WindupMs);
                if (f.Kind == FxKind.Windup || f.Kind == FxKind.Cancel) return;
                if (f.Kind == FxKind.Hit || f.Kind == FxKind.Block)
                {
                    FactionId? pigment = _actors.TrySoundContext(f.By, out _, out var faction, out _, out _) ? faction : (FactionId?)null;
                    var delta = _actors.TryPosition(f.By, out var source) ? p - source : Vector3.right;
                    _sparks.Hit(p, f.Kind == FxKind.Block, ref _sparkRng, pigment, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, f.Passive);
                }
                if (f.Kind == FxKind.Swing && Time.unscaledTime - f.CreatedAt < .35f) _hitZones.Swing(f.Subject, f.Weapon, f.X, f.Y, f.A, Time.unscaledTime);
                if (_flashes.Count >= 48) return;
                var flash = _flashPool.Count > 0 ? _flashPool.Pop() : CreateFlash();
                flash.Actor = f.Subject; flash.Position = p + new Vector3(0, .65f); flash.Start = Time.unscaledTime; flash.Duration = f.Kind==FxKind.Hit || f.Kind==FxKind.SigHit ? .55f : .2f;
                flash.Size = f.Kind == FxKind.Swing ? 1.25f : .8f;
                flash.Color = f.Kind == FxKind.Block ? new Color(.65f, .85f, 2) : new Color(2, 1.4f, .6f);
                bool number = f.Kind == FxKind.Hit || f.Kind == FxKind.SigHit || f.Kind == FxKind.Heal;
                string healText = f.Kind == FxKind.Heal ? Indicators.HealText(f.Amount) : null;
                flash.Sprite.gameObject.SetActive(f.Kind != FxKind.SigHit && f.Kind != FxKind.Heal);
                flash.Text.gameObject.SetActive(number); flash.Plate.gameObject.SetActive(number);
                if(number)
                {
                    var theme = UiTheme.Current;
                    flash.Text.text = f.Kind == FxKind.Heal ? healText : Indicators.DamageText(f.Amount, f.Crit);
                    if (f.Kind == FxKind.Heal) { flash.TextColor = RgbColour(Indicators.HealColour); flash.Text.fontSize = 3f * 13f / 13f; flash.Duration = .8f; flash.Position += new Vector3(0, .1f, 0); flash.LaneX = NumberSide; }
                    else if (f.Kind == FxKind.SigHit) { flash.TextColor = RgbColour(Indicators.SigColour(f.SigKind)); flash.Text.fontSize = 3f * 11f / 13f; flash.LaneX = NumberSide + Indicators.DamageLaneOffset(_numberLane++) * .026f; }
                    else
                    {
                        // Legacy FxView: red on me, gold by me, grey otherwise; 13 px + 1 per 40 damage (max +7, +5 critical); simultaneous numbers fan over 5 lanes.
                        var tone = Indicators.DamageColour(f.Subject, f.By, _localId);
                        flash.TextColor = (Color)(tone == Indicators.DamageTone.OnMe ? theme.Danger : tone == Indicators.DamageTone.ByMe ? theme.Accent : theme.TextSecondary);
                        flash.Text.fontSize = 3f * (f.Passive ? 11f : Indicators.DamageSize(f.Amount, f.Crit)) / 13f;
                        flash.LaneX = NumberSide + Indicators.DamageLaneOffset(_numberLane++) * .026f;
                    }
                    // The tone is set once, opaque; the fade then rewrites only the vertex alpha (SetNumberAlpha). Fading through TMP_Text.color
                    // marks the text dirty and TextMeshPro regenerates the whole mesh every frame (PERF_REPORT section 5).
                    var spawnTone = flash.TextColor; spawnTone.a = 1f; flash.Text.color = spawnTone; flash.AlphaByte = 255;
                    flash.Text.ForceMeshUpdate();
                }
                _flashes.Add(flash);
        }
        /// <summary>Numbers rise beside the fighter, not over its overhead stack (wish, build row, bar): with the protocol 16 marks they sat on top of each other (UI lab).</summary>
        const float NumberSide = .62f;
        static Color RgbColour(uint rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f); }
        void UpdateEffects()
        {
            bool reduced = ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion;
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var f = _flashes[i]; float t = (Time.unscaledTime - f.Start) / f.Duration;
                if (t >= 1 || !_actors.Contains(f.Actor)) { f.Sprite.gameObject.SetActive(false); f.Text.gameObject.SetActive(false); f.Plate.gameObject.SetActive(false); _flashes.RemoveAt(i); _flashPool.Push(f); continue; }
                f.Sprite.transform.position = f.Position;
                f.Sprite.transform.localScale = Vector3.one * f.Size * (reduced ? 1 : 1 + t * .55f);
                // Keep the silhouette readable even when confirmed hits overlap rapidly.
                // Damage text retains its longer lifetime; the hollow impact rim fades quickly.
                var c = f.Color; c.a = .55f * Mathf.Clamp01(1 - (Time.unscaledTime - f.Start) / .16f); f.Sprite.color = c;
                f.Text.transform.position=f.Position+new Vector3(f.LaneX,.7f+(reduced ? 0 : t*.35f),0); SetNumberAlpha(f, 1 - t);
                // A dark plate under the number (lab finding: grey digits vanished on sand and snow); legacy used a black stroke.
                if (f.Plate.gameObject.activeSelf) { var pc = (Color)UiTheme.Current.OutlineDark; pc.a = .88f * (1 - t); f.Plate.color = pc; f.Plate.transform.position = f.Text.transform.position; f.Plate.transform.localScale = new Vector3(f.Text.preferredWidth + .14f, f.Text.fontSize * .1f + .06f, 1); }
            }
        }

        Flash CreateFlash()
        {
            var sprite=ShowcaseSprites.Create(_fxRoot,"Confirmed impact","ring",30000,Color.white,Vector2.one,Vector3.zero,false);
            var go=new GameObject("Confirmed damage amount");go.transform.SetParent(_fxRoot,false);
            var text=go.AddComponent<TextMeshPro>();text.font=TMP_Settings.defaultFontAsset;text.fontSize=3;text.alignment=TextAlignmentOptions.Center;
            text.fontSharedMaterial=OverheadIndicators.OutlinedMaterial(text.font);text.rectTransform.sizeDelta=new Vector2(3,1);text.GetComponent<MeshRenderer>().sortingOrder=30001;
            go.SetActive(false);
            var plate = ShowcaseSprites.Create(_fxRoot, "Confirmed damage plate", "bar", 30000, Color.white, Vector2.one, Vector3.zero, false); plate.gameObject.SetActive(false);
            return new Flash{Sprite=sprite,Plate=plate,Text=text};
        }
    }
}
