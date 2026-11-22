using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkSignatureCueEditModeTests
    {
        GameObject _root;
        ArenaView _arena;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Delivered signature crowd");
            _arena = _root.AddComponent<ArenaView>();
            typeof(ArenaView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_arena, null);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void SixDeliveredSignatureCuesKeepTheirTextAndClearOtherCuesAndActorHeaders()
        {
            var (self, actors) = Crowd();
            _arena.Present(actors, self, null, 0f);
            AssertCuesClear(6);
        }

        [Test]
        public void HiddenCueRemainsAbsentAndConfirmedDamageKeepsIndividuallyDrawnValues()
        {
            var (self, actors) = Crowd();
            var hidden = actors[0];
            hidden.Effects = Indicators.Hidden;
            actors[0] = hidden;
            _arena.Present(actors, self, null, 0f);
            var amounts = new[] { 10, 44, 200 };
            _arena.ConfirmedFx(new ServerMsg.Fx
            {
                List = amounts.Select(v => new WireFx { Kind = FxKind.Hit, Victim = self.Id, By = 2, Amount = v }).ToList()
            });
            _arena.Present(actors, self, null, 0f);

            AssertCuesClear(5);
            var damage = _root.GetComponentsInChildren<TextMeshPro>()
                .Where(p => p.name == "Confirmed damage amount").Select(p => p.text);
            Assert.That(damage, Is.EquivalentTo(amounts.Select(v => v.ToString())));
            foreach (var cue in EnabledSprites("Signature cue plate"))
            foreach (var plate in EnabledSprites("Confirmed damage plate"))
                Assert.That(RectOf(cue.bounds).Overlaps(RectOf(plate.bounds)), Is.False, "Signature cue covers a confirmed damage number.");
        }

        void AssertCuesClear(int count)
        {
            var plates = EnabledSprites("Signature cue plate");
            Assert.That(plates.Length, Is.EqualTo(count));
            var labels = _root.GetComponentsInChildren<TextMeshPro>().Where(p => p.name == "Signature cue").ToArray();
            Assert.That(labels.Select(p => p.text), Is.All.EqualTo("EXEC BLOCKED"));
            Assert.That(labels.Length, Is.EqualTo(count));
            foreach (var label in labels)
            {
                var plate = plates.Single(p => p.transform.parent == label.transform.parent);
                Assert.That(Vector2.Distance(label.transform.position, plate.transform.position), Is.LessThan(.001f));
            }
            for (int i = 0; i < plates.Length; i++)
            for (int j = i + 1; j < plates.Length; j++)
                Assert.That(RectOf(plates[i].bounds).Overlaps(RectOf(plates[j].bounds)), Is.False, $"Signature cue plates {i} and {j} overlap.");

            foreach (var cue in plates)
            foreach (var name in _root.GetComponentsInChildren<TextMeshPro>().Where(p => p.name == "Name"))
            {
                name.ForceMeshUpdate();
                Assert.That(RectOf(cue.bounds).Overlaps(RectOf(name.GetComponent<MeshRenderer>().bounds)), Is.False,
                    "Signature cue covers a delivered fighter name.");
            }
        }

        SpriteRenderer[] EnabledSprites(string name) =>
            _root.GetComponentsInChildren<SpriteRenderer>().Where(p => p.name == name && p.enabled).ToArray();

        static Rect RectOf(Bounds bounds) => Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);

        static (SelfView, List<InterpActor>) Crowd()
        {
            const byte blocked = OverheadExtras.ExecBlockedBit;
            var self = new SelfView
            {
                Id = 1, X = 689.2f, Y = 1520.7f, Faction = FactionId.Sentenced,
                Hp = 885, MaxHp = 1000, Status = ActorStatus.Alive, Weapon = WeaponId.Fists, SelfSig = blocked
            };
            var sites = new[] { (607.9f, 1480f), (611.3f, 1571.5f), (656f, 1488f), (656f, 1552f), (602.5f, 1520.3f) };
            var actors = new List<InterpActor>();
            for (int i = 0; i < sites.Length; i++)
            {
                var (x, y) = sites[i];
                actors.Add(new InterpActor
                {
                    Id = (uint)i + 2, X = x, Y = y, HpPct = 85, Faction = FactionId.Sentenced,
                    Status = ActorStatus.Alive, Weapon = WeaponId.Sword, Kind = ActorKind.RegionalMob,
                    MobType = i == 4 ? MobTypeId.Executioner : MobTypeId.ChainWarden, SourceSig = blocked
                });
            }
            return (self, actors);
        }
    }
}
