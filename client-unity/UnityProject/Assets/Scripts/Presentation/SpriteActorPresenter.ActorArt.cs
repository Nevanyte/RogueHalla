using Roguehalla.Net;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Per-actor renderers, animation clocks and painter registrations stored with each pooled actor.</summary>
    public sealed partial class SpriteActorPresenter
    {
        sealed class Art
        {
            public GameObject Root;
            public SpriteRenderer PoxCenser, Body, Shadow, Cast, Halo, Hp, HpBack, Barrier, Weapon, WeaponSupport, Status;
            public ShowcaseIdentityOwner Identity;
            public SpriteOutline[] Plates;
            public SpriteOutline OutBody, OutHp, OutFoot;
            public StormInkBowstring Bowstring;
            public SpriteRenderer[] BarrierRing;
            public SpriteRenderer[] StatusIcons;
            public ShowcasePointLight Light;
            public ShowcaseCatalog.Appearance Appearance;
            public Vector3 LastPosition;
            public float AttackUntil;
            public float WindupAt, WindupUntil, AttackFacing;
            public bool HasAttackFacing;
            public uint DrawStartedAt, MotionSuccessor;
            public GaitState Gait;
            public float StampTime;
            public bool LocalCue;
            public int FireAxeReleaseCount, FireSwordReleaseCount, FireBowShotCount;
            public StormInkArtistSource BodyArtistSource, WeaponArtistSource;
            public float AttackAt, HitAt, BlockAt, DeadAt, HitUntil, BlockUntil;
            public float WishAt, WishUntil;
            public byte WishClip;
            public Color WishTint;
            public float WishTintAmount;
            public bool Dead, HoldDeathFrame, VisibleForCombatCue;
            public WeaponId? WeaponId;
            public FactionId? Faction;
            public ActorKind Kind;
            public MobTypeId? MobType;
            public int Stamp;
            public readonly ActorFadeSlot Fade = new ActorFadeSlot(); // vision fade state (SpriteActorPresenter.Fade.cs)
            public OverheadIndicators Ind; // overhead cluster and body marks (OverheadIndicators.cs)
            public OverheadIndicators.Facts IndFacts;
            public MobLook Look; // what a creature looked like when last presented (MobContinuity)
        }
    }
}
