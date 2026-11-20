using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Exercises the same membership, collection, then actor draw order as MatchClient.Present.</summary>
    public sealed class StormInkWandReleaseOrderEditModeTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(true)]
        [TestCase(false)]
        public void AShippedProjectileCanReleaseOnlyItsCurrentlyPresentedOwner(bool visibleOwner)
        {
            var root = new GameObject("Observed wand in a real presentation frame");
            try
            {
                var arena = root.AddComponent<ArenaView>();
                typeof(ArenaView).GetMethod("Awake", Private).Invoke(arena, null);
                var rows = new List<InterpActor> { new InterpActor { Id = 7, X = 640, Y = 640,
                    Status = ActorStatus.Alive, Kind = ActorKind.RegionalMob, MobType = MobTypeId.BloodSaint,
                    Faction = FactionId.Holy, Weapon = WeaponId.Wand, HpPct = 100 } };
                var shots = new CollectionsView();
                arena.DiagSetExitFades(true);
                arena.RetainObservedActors(rows, null);
                arena.PresentCollections(shots);
                var replacement = rows[0]; replacement.Id = 17; rows[0] = replacement;
                arena.RetainObservedActors(rows, null, true);
                var shot = new WireProjectile { Owner = visibleOwner ? 17u : 7u, Kind = (byte)ProjectileKind.Ball,
                    X = Quant.QuantizePos(650), Y = Quant.QuantizePos(640), Facing = Quant.QuantizeFacing(0f) };
                var state = new ServerMsg.State { Tick = 2, Items = new StateItem[] {
                    new ProjectilesState { Rows = new[] { shot } }
                } };
                typeof(CollectionsView).GetMethod("Apply", Private).Invoke(shots, new object[] { state });
                arena.PresentCollections(shots);
                arena.Present(rows, null, null, 0f);
                arena.Present(rows, null, null, 0f);
                bool striking = root.GetComponentsInChildren<SpriteRenderer>().Any(s => s.gameObject.name == "Authored character"
                    && s.enabled && s.sprite && s.sprite.name.StartsWith("BloodSaint/") && s.sprite.name.EndsWith("/3"));
                Assert.AreEqual(visibleOwner, striking);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
