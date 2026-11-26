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
            public StormInkManacles Manacles;
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
            public int LayoutKey; // serial of this art for the name layout (kept by a renewed handle; fresh for a reused pooled art)
            public long LayoutNameKey; // layout memory key of this art's mob name (written by the name merge)
            public bool LayoutPack; // true on the carrier of a pack of two or more this frame: its box is the name alone
            public int LayoutStamp = -1; // the stamp whose layout set LayoutLift and LayoutTarget (-1: never)
            public float LayoutLift, LayoutTarget;
            public int GlideStamp = -1; // the stamp whose merge set GlideX and GlideY (the glided anchor of a pack name)
            public float GlideX, GlideY;
            public long NameKeyPrev; public int NameKeyStamp = int.MinValue; public int NameKeySerial; // last frame's layout key of this art, for Carry
        }
    }
}
