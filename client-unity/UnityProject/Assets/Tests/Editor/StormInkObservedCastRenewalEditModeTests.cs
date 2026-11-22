using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Real NPC membership renewal during a disclosed wand cast, including ambiguous and absent cases.</summary>
    public sealed class StormInkObservedCastRenewalEditModeTests
    {
        static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        GameObject _root;
        ArenaView _arena;
        SpriteActorPresenter _actors;
        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Observed caster renewal");
            _arena = _root.AddComponent<ArenaView>();
            typeof(ArenaView).GetMethod("Awake", Members).Invoke(_arena, null);
            _actors = (SpriteActorPresenter)typeof(ArenaView).GetField("_actors", Members).GetValue(_arena);
            _actors.ExitFadesAllowed = true;
        }
        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        static InterpActor Saint(uint id, float x) => new InterpActor {
            Id = id, X = x, Y = 368, Facing = Mathf.PI, Status = ActorStatus.Alive, Weapon = WeaponId.Wand,
            HpPct = 100, Kind = ActorKind.RegionalMob, Faction = FactionId.Holy, MobType = MobTypeId.BloodSaint
        };
        void Deliver(params InterpActor[] actors) => _arena.RetainObservedActors(new List<InterpActor>(actors), null);
        string Body(uint id)
        {
            var active = (IDictionary)typeof(SpriteActorPresenter).GetField("_active", Members).GetValue(_actors);
            object state = active[id];
            return ((SpriteRenderer)state.GetType().GetField("Body", Members).GetValue(state)).sprite.name;
        }
        void StartCast()
        {
            Deliver(Saint(7, 4688));
            _actors.ConfirmedCue(7, FxKind.Windup, WeaponId.Wand, Mathf.PI, 400f);
            Deliver(Saint(7, 4688));
            Assert.That(Body(7), Does.EndWith("/2"), "original visible windup");
        }
        [Test]
        public void OneVisibleCasterRetainsTheDisclosedWindupOnConsecutiveNewHandles()
        {
            StartCast();
            Deliver(Saint(18, 4696)); Deliver(Saint(47, 4704));
            Assert.That(_actors.Contains(7), Is.False); Assert.That(_actors.Contains(18), Is.False);
            Assert.That(Body(47), Does.EndWith("/2"));
        }
        [Test]
        public void AVisibilityGapThenANearbyLookalikeDoesNotAcquireTheWindup()
        {
            StartCast(); Deliver(); Deliver(Saint(55, 4688));
            Assert.That(Body(55), Does.Not.EndWith("/2"));
        }
        [Test]
        public void TwoFreshSameLookingRecipientsCannotInheritOneWindup()
        {
            StartCast(); Deliver(Saint(55, 4686), Saint(66, 4690));
            Assert.That(Body(55), Does.Not.EndWith("/2")); Assert.That(Body(66), Does.Not.EndWith("/2"));
        }
        [Test]
        public void OneArrivalCannotBorrowACastFromEitherOfTwoLookalikeDepartures()
        {
            Deliver(Saint(7, 4688), Saint(9, 4693)); _actors.ConfirmedCue(7, FxKind.Windup, WeaponId.Wand, Mathf.PI, 400f);
            Deliver(Saint(55, 4689)); Assert.That(Body(55), Does.Not.EndWith("/2"));
        }
        [Test]
        public void ADisplacedLookalikeDoesNotInheritTheWandWindup()
        {
            StartCast(); Deliver(Saint(55, 4736)); Assert.That(Body(55), Does.Not.EndWith("/2"));
        }
    }
}
