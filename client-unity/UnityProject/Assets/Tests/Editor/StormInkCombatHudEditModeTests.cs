using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkCombatHudEditModeTests
    {
        GameObject _root;
        ArenaView _arena;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Combat crowd");
            _arena = _root.AddComponent<ArenaView>();
            typeof(ArenaView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_arena, null);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        static SelfView Holy(float x, float y) => new SelfView
        {
            Id = 1, X = x, Y = y, Hp = 1100, MaxHp = 1100, Status = ActorStatus.Alive,
            Faction = FactionId.Holy, Weapon = WeaponId.Sword
        };

        SpriteRenderer[] Sprites(string name) => _root.GetComponentsInChildren<SpriteRenderer>()
            .Where(s => s.enabled && s.name == name).ToArray();

        static Rect BoundsRect(Bounds b) => Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);

        [Test]
        public void MarkPlateKeepsItsExactMeaningAndClearsTheNearbyHolyHelmet()
        {
            var seen = new List<InterpActor> { new InterpActor
            {
                Id = 2, X = 100, Y = 200, Faction = FactionId.Sentenced, Status = ActorStatus.Alive,
                Kind = ActorKind.Player, Weapon = WeaponId.Axe, HpPct = 100, SourceSig = 64
            } };
            _arena.Present(seen, Holy(150, 216), null, 0);
            var label = _root.GetComponentsInChildren<TextMeshPro>()
                .Single(s => s.name == "Signature cue");
            Assert.That(label.text, Is.EqualTo("EXEC BLOCKED"));
            Rect plate = BoundsRect(Sprites("Signature cue plate").Single().bounds);

            var local = Sprites("Authored character").Single(s => s.sprite.name.StartsWith("HolyChampion/"));
            Bounds body = local.bounds;
            Rect helmet = Rect.MinMaxRect(body.center.x - .28f, body.max.y - .56f, body.center.x + .28f, body.max.y - .08f);
            Assert.That(plate.Overlaps(helmet), Is.False, "A neighbor's opaque mark plate must not cover the Holy helmet.");
        }

        [Test]
        public void SimultaneousDamageRemainsIndividuallyReadableAtMeasuredWidths()
        {
            var self = Holy(150, 216);
            var noOthers = new List<InterpActor>();
            _arena.Present(noOthers, self, null, 0);
            var amounts = new[] { 6, 73, 804, 1427, 18, 92, 133, 213 };
            var hits = amounts.Select(v => new WireFx { Kind = FxKind.Hit, Victim = self.Id, By = 2, Amount = v }).ToList();
            _arena.ConfirmedFx(new ServerMsg.Fx { List = hits });
            _arena.Present(noOthers, self, null, 0);
            var numbers = _root.GetComponentsInChildren<TextMeshPro>()
                .Where(n => n.name == "Confirmed damage amount").Select(n => n.text).OrderBy(t => t).ToArray();
            Assert.That(numbers, Is.EqualTo(amounts.Select(v => v.ToString()).OrderBy(t => t)));
            Rect[] bounds = Sprites("Confirmed damage plate").Select(r => BoundsRect(r.bounds)).ToArray();
            Assert.That(bounds.Length, Is.EqualTo(amounts.Length));
            for (int i = 0; i < bounds.Length; i++)
                for (int j = i + 1; j < bounds.Length; j++)
                    Assert.That(bounds[i].Overlaps(bounds[j]), Is.False, $"Damage plates {i} and {j} overlap.");
        }
    }
}
