using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-WORLD-NAMES-2: the shared rig of <see cref="WorldNamesEditModeTests"/> and <see cref="WorldNamesTimeEditModeTests"/>. A stateless set of
    /// helpers: an arena with a presenter (<see cref="Stage"/>), actors as plain values (<see cref="Actor"/>), a frame as a list of them
    /// (<see cref="Scene"/>: Begin, every Present and PresentIndicators, no End), measures from the drawn objects, the slide that puts one name
    /// a chosen gap beside another, and Settle, which repeats a scene until every lift has reached its target. Positions are legacy pixels as
    /// Present takes them; every measure is in world units. Copied from NameLegibleEditModeTests (frames, measures) and
    /// NamePackGroup2EditModeTests.PlayersFrame; the slide is adapted from NamePackFix4EditModeTests.
    /// </summary>
    public static class WorldNamesRig
    {
        /// <summary>One frame of the tests: a sixtieth of a second.</summary>
        public const float Dt = 1f / 60f;

        /// <summary>An arena with the one presenter the tests drive (a null catalog: equal legacy y gives equal base y).</summary>
        public sealed class Stage : IDisposable
        {
            public readonly GameObject Arena;
            public readonly SpriteActorPresenter P;

            public Stage(string name)
            {
                Arena = new GameObject(name);
                P = new SpriteActorPresenter(Arena.transform, null) { Delivered = new HashSet<uint>() };
            }

            public void Dispose() { if (Arena != null) UnityEngine.Object.DestroyImmediate(Arena); }
        }

        public enum Kind { Fighter, Mob, Dummy }

        /// <summary>One actor of a scene, in legacy pixels. Built by the factories, changed by the With and Moved methods.</summary>
        public struct Actor
        {
            public Kind Kind; public uint Id; public float X, Y; public bool Local;
            public string Name; public MobTypeId Type; public float Hp;
            public int Bounty; public bool OffHand; public byte Top1, Top2, Tier; public int WishState; public ushort Effects;

            public static Actor Fighter(uint id, float x, string name, bool local = false, float y = 640f) =>
                new Actor { Kind = Kind.Fighter, Id = id, X = x, Y = y, Local = local, Name = name, Hp = 100f };

            public static Actor Mob(uint id, float x, float y, MobTypeId type, float hp = 100f) =>
                new Actor { Kind = Kind.Mob, Id = id, X = x, Y = y, Type = type, Hp = hp };

            public static Actor Dummy(uint id, float x, float y) => new Actor { Kind = Kind.Dummy, Id = id, X = x, Y = y, Hp = 100f };

            public Actor Marks(int bounty, bool offHand, byte top1, byte top2, byte tier)
            {
                var a = this; a.Bounty = bounty; a.OffHand = offHand; a.Top1 = top1; a.Top2 = top2; a.Tier = tier; return a;
            }

            public Actor Hidden() { var a = this; a.Effects = Indicators.Hidden; return a; }
            public Actor Visible() { var a = this; a.Effects = 0; return a; }
            public Actor At(float x, float y) { var a = this; a.X = x; a.Y = y; return a; }
            public Actor Moved(float dxPx, float dyPx) { var a = this; a.X += dxPx; a.Y += dyPx; return a; }
        }

        /// <summary>The frame: actors in the order they are presented.</summary>
        public sealed class Scene
        {
            public readonly List<Actor> Actors = new List<Actor>();

            public static Scene Of(params Actor[] actors) { var s = new Scene(); s.Actors.AddRange(actors); return s; }

            public static Scene Join(params Scene[] scenes) { var s = new Scene(); foreach (var o in scenes) s.Actors.AddRange(o.Actors); return s; }

            public Scene Moved(float dxPx, float dyPx)
            {
                var s = new Scene(); foreach (var a in Actors) s.Actors.Add(a.Moved(dxPx, dyPx)); return s;
            }

            /// <summary>Begin, every Present and PresentIndicators. The caller ends the frame.</summary>
            public void Present(SpriteActorPresenter p)
            {
                p.Begin();
                foreach (var a in Actors) PresentOne(p, a);
            }

            /// <summary>PresentIndicators again for every actor, with no Begin and no Present: the second half of a frame presented twice.</summary>
            public void Reindicate(SpriteActorPresenter p)
            {
                foreach (var a in Actors) IndicateOne(p, a);
            }
        }

        static void PresentOne(SpriteActorPresenter p, Actor a)
        {
            if (a.Kind == Kind.Fighter)
            {
                p.Present(a.Id, a.X, a.Y, 0, a.Hp, FactionId.Holy, ActorStatus.Alive, WeaponId.Fists, ActorKind.Player, null, a.Effects, 0, a.Local);
                p.PresentMarks(a.Id, new OverheadIndicators.Marks { Name = a.Name, Top1 = a.Top1, Top2 = a.Top2, Tier = a.Tier });
            }
            else if (a.Kind == Kind.Dummy)
                p.Present(a.Id, a.X, a.Y, 0, a.Hp, null, ActorStatus.Alive, null, ActorKind.TrainingDummy, null, 0, 0, false, 0, 0, "grunt");
            else
                p.Present(a.Id, a.X, a.Y, 0, a.Hp, null, ActorStatus.Alive, null, ActorKind.RegionalMob, a.Type, 0, 0, false, 0, 0, "grunt");
            IndicateOne(p, a);
        }

        static void IndicateOne(SpriteActorPresenter p, Actor a)
        {
            if (a.Kind == Kind.Fighter) p.PresentIndicators(a.Id, a.Bounty, a.OffHand, WeaponId.Bow, false, -1, false, false, a.WishState, 0);
            else p.PresentIndicators(a.Id, 0, false, WeaponId.Fists, false);
        }

        /// <summary>A frame: the scene presented, then the presenter's End with a sixtieth of a second.</summary>
        public static void Frame(SpriteActorPresenter p, Scene scene, float dt = Dt)
        {
            scene.Present(p); p.End(dt);
        }

        /// <summary>The most frames a lift needs to climb the largest lift there is, and two to spare.</summary>
        public static int FrameBound { get { return Mathf.CeilToInt(OverheadBoxLayout.MaxLift / (LabelLift.RiseUnitsPerSecond / 60f)) + 2; } }

        /// <summary>
        /// Runs the scene again and again (each frame ended with a sixtieth of a second) until every <see cref="SpriteActorPresenter.TryNameLift"/> lift
        /// of its actors equals its target (1e-5): a scene slid out of first sight has its target jump at once and its drawn lift climb at the rise rate.
        /// Fails the test when it runs out of frames. Returns the number of frames it ran (at least one).
        /// </summary>
        public static int Settle(SpriteActorPresenter p, Scene scene)
        {
            int bound = FrameBound;
            for (int n = 1; n <= bound; n++)
            {
                Frame(p, scene);
                bool settled = true;
                foreach (var a in scene.Actors)
                    if (p.TryNameLift(a.Id, out float lift, out float target) && Mathf.Abs(lift - target) > 1e-5f) settled = false;
                if (settled) return n;
            }
            Assert.Fail("the lifts had not settled after " + bound + " frames");
            return -1;
        }

        // ---- the drawn objects ----

        static object ArtOf(SpriteActorPresenter p, uint id)
        {
            var active = (System.Collections.IDictionary)typeof(SpriteActorPresenter).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(p);
            Assert.That(active.Contains(id), Is.True, "the presenter has an art for id " + id);
            return active[id];
        }

        /// <summary>The root object of an actor's art (its overhead objects are descendants).</summary>
        public static GameObject RootOf(SpriteActorPresenter p, uint id)
        {
            var art = ArtOf(p, id);
            var field = art.GetType().GetField("Root", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Art.Root is a public field");
            return (GameObject)field.GetValue(art);
        }

        /// <summary>A descendant of an actor's root by object name (active or not); the test fails when there is none.</summary>
        public static Transform Child(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            Assert.Fail("no object \"" + name + "\" under " + root.name); return null;
        }

        public static TextMeshPro NameOf(SpriteActorPresenter p, uint id) { return Child(RootOf(p, id), "Name").GetComponent<TextMeshPro>(); }

        public static SpriteRenderer SpriteOf(SpriteActorPresenter p, uint id, string name) { return Child(RootOf(p, id), name).GetComponent<SpriteRenderer>(); }

        /// <summary>
        /// A name's line box: preferred width times the label's lossy scale in x by preferred height times its scale in y, centred on the label's
        /// position, measured after a mesh update. The label must be active in the hierarchy (one that never was measures a tenth).
        /// </summary>
        public static Rect LineBox(TextMeshPro name)
        {
            Assert.That(name.gameObject.activeInHierarchy, Is.True, "set-up: the label \"" + name.text + "\" is active in the hierarchy before it is measured");
            name.ForceMeshUpdate();
            float w = name.preferredWidth * name.transform.lossyScale.x, h = name.preferredHeight * name.transform.lossyScale.y;
            var c = name.transform.position;
            return new Rect(c.x - w / 2f, c.y - h / 2f, w, h);
        }

        public static Rect LineBoxOf(SpriteActorPresenter p, uint id) { return LineBox(NameOf(p, id)); }

        public static Rect XY(Bounds b) { return new Rect(b.min.x, b.min.y, b.size.x, b.size.y); }

        /// <summary>The names drawn now: every "Name" label active in the hierarchy.</summary>
        public static List<TextMeshPro> ActiveNames(GameObject arena)
        {
            var list = new List<TextMeshPro>();
            foreach (var t in arena.GetComponentsInChildren<TextMeshPro>(true)) if (t.name == "Name" && t.gameObject.activeInHierarchy) list.Add(t);
            return list;
        }

        /// <summary>The id of the one actor of a pack of one name that shows it (the lowest id).</summary>
        public static uint Carrier(SpriteActorPresenter p, params uint[] ids)
        {
            foreach (uint id in ids) if (p.NameShown(id)) return id;
            Assert.Fail("none of the pack's members shows the name"); return 0;
        }

        /// <summary>The display name of a creature, as the presenter writes it over its head.</summary>
        public static string MobName(MobTypeId type)
        {
            Assert.That(ShowcaseIdentityAssets.TryGetMobInfo(type, out var info), Is.True, "mob info of " + type);
            return info.DisplayName;
        }

        /// <summary>The Health and barrier track of an actor is drawn.</summary>
        public static bool BarShown(SpriteActorPresenter p, uint id)
        {
            var r = SpriteOf(p, id, "Health and barrier track");
            return r.enabled && r.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// Legacy pixels that move a second name so its line box starts <paramref name="gap"/> to the right of the first one's right edge, on one
        /// centre y: both boxes are measured alone, then the world offset is converted (dx / Scale, dy / (-Scale * GroundSquash)).
        /// </summary>
        public static Vector2 SlidePx(Rect first, Rect second, float gap)
        {
            float dx = first.xMax + gap + second.width / 2f - second.center.x;
            float dy = first.center.y - second.center.y;
            return new Vector2(dx / ArenaView.Scale, dy / (-ArenaView.Scale * ArenaView.GroundSquash));
        }

        /// <summary>Legacy pixels that move an actor's art so a world point lands on another (dx / Scale, dy / (-Scale * GroundSquash)).</summary>
        public static Vector2 WorldToPx(Vector2 world)
        {
            return new Vector2(world.x / ArenaView.Scale, world.y / (-ArenaView.Scale * ArenaView.GroundSquash));
        }

        /// <summary>The rectangles hold one that contains the box (each edge within <paramref name="tol"/>).</summary>
        public static bool AnyContains(List<Rect> rects, Rect box, float tol)
        {
            foreach (var r in rects)
                if (r.xMin <= box.xMin + tol && r.xMax >= box.xMax - tol && r.yMin <= box.yMin + tol && r.yMax >= box.yMax - tol) return true;
            return false;
        }

        /// <summary>The rectangles hold one equal to the expected one (each edge within <paramref name="tol"/>).</summary>
        public static bool AnyEquals(List<Rect> rects, Rect expected, float tol)
        {
            foreach (var r in rects)
                if (Mathf.Abs(r.xMin - expected.xMin) <= tol && Mathf.Abs(r.xMax - expected.xMax) <= tol
                    && Mathf.Abs(r.yMin - expected.yMin) <= tol && Mathf.Abs(r.yMax - expected.yMax) <= tol) return true;
            return false;
        }

        /// <summary>The vertical clearance from the top of <paramref name="below"/> to the bottom of <paramref name="above"/> (negative when they overlap in y).</summary>
        public static float Clearance(Rect below, Rect above) { return above.yMin - below.yMax; }

        /// <summary>The lift of an actor this frame; the test fails when it took no part in the layout.</summary>
        public static float LiftOf(SpriteActorPresenter p, uint id)
        {
            Assert.That(p.TryNameLift(id, out float lift, out _), Is.True, "id " + id + " takes part in the name layout");
            return lift;
        }

        /// <summary>The lift and the target of an actor this frame; the test fails when it took no part in the layout.</summary>
        public static (float lift, float target) LiftAndTarget(SpriteActorPresenter p, uint id)
        {
            Assert.That(p.TryNameLift(id, out float lift, out float target), Is.True, "id " + id + " takes part in the name layout");
            return (lift, target);
        }

        /// <summary>Every transform position under the arena, in hierarchy order.</summary>
        public static List<Vector3> Positions(GameObject arena)
        {
            var list = new List<Vector3>();
            foreach (var t in arena.GetComponentsInChildren<Transform>(true)) list.Add(t.position);
            return list;
        }

        // ---- the picture-40 camp: a pack of two Chain Wardens (one name, no camp rows) and a Gaoler (elite, with its star) ----

        public static readonly Actor Gaoler = Actor.Mob(13, 900f, 640f, MobTypeId.Gaoler);

        public static Scene Wardens() { return Scene.Of(Actor.Mob(11, 640f, 640f, MobTypeId.ChainWarden), Actor.Mob(12, 700f, 640f, MobTypeId.ChainWarden)); }

        /// <summary>The Gaoler and the pack measured alone (one frame each) and the pack slid so its name starts a gap right of the Gaoler's.</summary>
        public sealed class Picture
        {
            public Rect GaolerBox, PackBox, SlidPackBox; public Vector3 GaolerNameAt, GaolerStarAt; public Scene Slid, All; public uint Carrier;

            /// <summary>The Gaoler and the pack slid so the pack's name starts <paramref name="gap"/> right of the Gaoler's, on one centre y.</summary>
            public Scene At(float gap)
            {
                var slide = SlidePx(GaolerBox, PackBox, gap);
                return Scene.Join(Scene.Of(Gaoler), Wardens().Moved(slide.x, slide.y));
            }
        }

        /// <summary>Frames the Gaoler alone, then the pack alone, on this presenter (so a later scene is not first sight for the keys that stay), and builds the slid scene.</summary>
        public static Picture MeasurePicture(SpriteActorPresenter p, float gap)
        {
            var pic = new Picture();
            Frame(p, Scene.Of(Gaoler));
            pic.GaolerBox = LineBoxOf(p, 13); pic.GaolerNameAt = NameOf(p, 13).transform.position; pic.GaolerStarAt = SpriteOf(p, 13, "Elite star").transform.position;
            Frame(p, Wardens());
            pic.Carrier = Carrier(p, 11, 12);
            pic.PackBox = LineBoxOf(p, pic.Carrier);
            var slide = SlidePx(pic.GaolerBox, pic.PackBox, gap);
            pic.Slid = Wardens().Moved(slide.x, slide.y);
            pic.All = Scene.Join(Scene.Of(Gaoler), pic.Slid);
            pic.SlidPackBox = new Rect(pic.GaolerBox.xMax + gap, pic.GaolerBox.center.y - pic.PackBox.height / 2f, pic.PackBox.width, pic.PackBox.height);
            return pic;
        }

        public static Rect Grown(Rect r, float pad) { return new Rect(r.xMin - pad, r.yMin, r.width + 2f * pad, r.height); }
    }
}
