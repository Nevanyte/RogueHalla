using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using ClientArenaView = Roguehalla.Client.ArenaView;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-COLISEUM-HEALS-2 oracle (Unity): a coliseum heal pickup (PropAppearance.Heal) is drawn from existing art,
    /// with a glow under it when its delivered clock has come (WorldIndicatorRules.HealReady: server time >= ReadyAt) and
    /// none while it waits; it never borrows the cannon's barrel or the wind emblem's rune. Not run by the oracle author
    /// (no Unity Editor on that machine).</summary>
    public sealed class HealPropEditModeTests
    {
        readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) UnityEngine.Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test]
        public void AReadyHealGlowsAndAWaitingOneIsDrawnWithoutAGlow()
        {
            var arena = CreateArena();
            typeof(ClientArenaView).GetField("_sourceTimeMs", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(arena, 10_000d);
            arena.PresentCollections(BuildPublicCollections(
                Heal(1, 100, 0),          // available from the start
                Heal(2, 400, 30_000),     // taken: waits until 30 s
                Heal(3, 700, 10_000)));   // its clock is exactly now: available

            var root = FindDirectChild(arena.transform, "Observed collections");
            var glow = ShowcaseSprites.Get("glow");
            foreach (int x in new[] { 100, 400, 700 })
            {
                var sprites = SpritesNear(root, WorldPointFromRaw(x, 200).x);
                Assert.That(sprites.Count, Is.GreaterThanOrEqualTo(2), "heal at " + x + " draws its art beside the shadow");
                CollectionAssert.DoesNotContain(sprites, ShowcaseSprites.Get("rune"), "not the wind emblem's rune");
                CollectionAssert.DoesNotContain(sprites, ShowcaseSprites.Get("bar"), "not the cannon's barrel");
            }
            CollectionAssert.Contains(SpritesNear(root, WorldPointFromRaw(100, 200).x), glow, "a ready heal glows");
            CollectionAssert.Contains(SpritesNear(root, WorldPointFromRaw(700, 200).x), glow, "on the tick its clock comes it glows");
            CollectionAssert.DoesNotContain(SpritesNear(root, WorldPointFromRaw(400, 200).x), glow, "a waiting heal is dim: no glow");
        }

        static WireProp Heal(uint handle, ushort x, uint readyAt)
        {
            return new WireProp { Handle = new PickupHandle(handle), X = x, Y = 200, Kind = PropKind.Heal, ReadyAt = readyAt };
        }

        ClientArenaView CreateArena()
        {
            var arenaObject = new GameObject("heal prop test arena");
            _objects.Add(arenaObject);
            var arena = arenaObject.AddComponent<ClientArenaView>();
            arena.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(arena, null);
            return arena;
        }

        static CollectionsView BuildPublicCollections(params WireProp[] rows)
        {
            var props = new PropsState { Rows = rows };
            var state = new ServerMsg.State { Tick = 7, Items = new StateItem[] { props } };
            var view = new CollectionsView();
            typeof(CollectionsView).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { state });
            return view;
        }

        static Vector3 WorldPointFromRaw(float x, float y)
        {
            return ClientArenaView.WorldPoint(x / 8f, y / 8f);
        }

        static List<Sprite> SpritesNear(Transform root, float x)
        {
            var result = new List<Sprite>();
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                if (renderer.gameObject.activeSelf && Mathf.Abs(renderer.transform.position.x - x) < .6f)
                    result.Add(renderer.sprite);
            return result;
        }

        static Transform FindDirectChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return parent.GetChild(i);
            return null;
        }
    }
}
