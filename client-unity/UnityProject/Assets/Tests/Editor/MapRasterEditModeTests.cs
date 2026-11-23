using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>Shared maps and picture comparison of the two P-UI-MAP-RASTER-1 fixtures. The packet (docs/backlog/packets/P-UI-MAP-RASTER-1.md) has the rules and the numbers.</summary>
    static class RasterFixtures
    {
        public const int Side = 16;

        /// <summary>Map A: 16 x 16 (8 texels a tile), six vertical region bands, walls at (4,4), (9,9) and row 0 from x = 0 to 11, a low at (11,11).</summary>
        public static MapBlob MapA()
        {
            var tiles = new byte[Side * Side];
            tiles[4 * Side + 4] = MapBlob.Wall; tiles[9 * Side + 9] = MapBlob.Wall; tiles[11 * Side + 11] = MapBlob.Low;
            for (int x = 0; x <= 11; x++) tiles[x] = MapBlob.Wall;
            return new MapBlob(Side, Side, tiles, null, Bands());
        }

        public static byte[] Bands()
        {
            var regions = new byte[Side * Side];
            for (int y = 0; y < Side; y++) for (int x = 0; x < Side; x++) regions[y * Side + x] = (byte)(1 + x * 6 / Side);
            return regions;
        }

        /// <summary>A new map object over copies of the arrays of <paramref name="map"/>: equal terrain, not the same object.</summary>
        public static MapBlob Copy(MapBlob map)
        {
            return new MapBlob(map.W, map.H, (byte[])map.Tiles.Clone(), null, map.Regions == null ? null : (byte[])map.Regions.Clone());
        }

        /// <summary>Map C: map A with one region byte different.</summary>
        public static MapBlob MapC()
        {
            var regions = Bands(); regions[15 * Side + 15] = 1;
            return new MapBlob(Side, Side, MapA().Tiles, null, regions);
        }

        public static uint Cell(int x, int y) { return (uint)(y * Side + x); }

        /// <summary>The pixels the picture shows: mip 0, mip 1 and the last mip.</summary>
        public static Color32[][] Snap(Texture2D texture)
        {
            int last = texture.mipmapCount - 1;
            return new[] { texture.GetPixels32(0), texture.GetPixels32(1), texture.GetPixels32(last) };
        }

        /// <summary>Premise of the reference picture: two themes made by Skin() are equal in every colour the raster reads.</summary>
        public static string ThemeDifference(UiTheme a, UiTheme b)
        {
            string Diff(string name, Color32 x, Color32 y) { return x.r == y.r && x.g == y.g && x.b == y.b ? null : name; }
            string d = Diff("SurfaceSunken", a.SurfaceSunken, b.SurfaceSunken) ?? Diff("Border", a.Border, b.Border)
                ?? Diff("TextSecondary", a.TextSecondary, b.TextSecondary) ?? Diff("AccentDim", a.AccentDim, b.AccentDim);
            for (int r = 1; r <= 6 && d == null; r++) d = Diff("ZoneColor(" + r + ")", a.ZoneColor(r), b.ZoneColor(r));
            return d;
        }

        /// <summary>
        /// The reference picture: a whole build of <paramref name="map"/> under a fresh Skin() (never Current, so it is destroyed at its release).
        /// Acquire it while the picture under test is still held (a whole build destroys the spare first). Returns null when the picture is the same, else
        /// a sentence naming the first difference.
        /// </summary>
        public static string Differs(Texture2D picture, MapBlob map, UiTheme pictureTheme)
        {
            var theme = UiTheme.Skin();
            string premise = ThemeDifference(theme, pictureTheme);
            if (premise != null) return "premise: the reference theme differs from the picture's theme in " + premise;
            var reference = TerrainRaster.Acquire(map, theme);
            try
            {
                if (picture == null) return "the picture under test is destroyed";
                if (picture.width != reference.Texture.width || picture.height != reference.Texture.height) return "size " + picture.width + "x" + picture.height + " against " + reference.Texture.width + "x" + reference.Texture.height;
                if (picture.mipmapCount != reference.Texture.mipmapCount) return "mip count " + picture.mipmapCount + " against " + reference.Texture.mipmapCount;
                var got = Snap(picture); var want = Snap(reference.Texture);
                for (int level = 0; level < got.Length; level++)
                    for (int i = 0; i < want[level].Length; i++)
                        if (!got[level][i].Equals(want[level][i])) return "first differing texel: mip slot " + level + " index " + i + " is " + got[level][i] + ", a whole build gives " + want[level][i];
                return null;
            }
            finally { TerrainRaster.Release(reference); }
        }

        public static string FirstDifference(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) return "length";
            for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return "index " + i;
            return null;
        }
    }

    /// <summary>
    /// P-UI-MAP-RASTER-1, fixture (a): <see cref="TerrainRaster"/> used directly. A picture outlives a rebind (parked, one spare, only for
    /// the Current theme), equal terrain is the same picture, changed tiles are patched from a kin instead of built whole. The cache and the
    /// spare are static: every test drops the spare in SetUp and TearDown and releases every entry it acquired.
    /// </summary>
    public sealed class MapRasterEditModeTests
    {
        const int K = 8;
        const int TileTexels = K * K;
        UiTheme _saved, _fixtureTheme;
        readonly List<TerrainRaster.Entry> _held = new List<TerrainRaster.Entry>();

        [SetUp]
        public void SetUp()
        {
            TerrainRaster.DropSpare();
            _saved = UiTheme.Current;
            _fixtureTheme = UiTheme.Skin();
            UiTheme.Current = _fixtureTheme;
            _held.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            try { foreach (var entry in _held) TerrainRaster.Release(entry); _held.Clear(); TerrainRaster.DropSpare(); }
            finally { UiTheme.Current = _saved; }
        }

        TerrainRaster.Entry Get(MapBlob map, UiTheme theme)
        {
            var entry = TerrainRaster.Acquire(map, theme);
            _held.Add(entry);
            Assert.That(entry.TexelsPerTile, Is.EqualTo(K), "set-up: 16 x 16 draws 8 texels a tile");
            return entry;
        }

        void Put(TerrainRaster.Entry entry)
        {
            TerrainRaster.Release(entry);
            _held.Remove(entry);
        }

        static void SameAsWholeBuild(Texture2D picture, MapBlob map, UiTheme theme, string what)
        {
            Assert.That(RasterFixtures.Differs(picture, map, theme), Is.Null, what);
        }

        // ---- red on the stub ---------------------------------------------------------------------------------------------------------

        /// <summary>Red: the last release of a Current-theme picture parks it; an equal map object takes it back with no build.</summary>
        [Test]
        public void AnEqualMapObjectReusesThePicture()
        {
            var a = MapRasterFixtureA();
            var entry = Get(a, _fixtureTheme); var texture = entry.Texture;
            int full = TerrainRaster.FullBuilds, derived = TerrainRaster.DerivedBuilds;
            Put(entry);
            Assert.That(TerrainRaster.HasSpare, Is.True, "the last release of a picture of the Current theme parks it");
            Assert.That(texture != null, Is.True, "the parked texture is alive");
            var again = Get(RasterFixtures.Copy(a), _fixtureTheme);
            Assert.That(again, Is.SameAs(entry), "an equal map object is the same entry");
            Assert.That(again.Texture, Is.SameAs(texture), "and the same texture");
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full), "no build");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived), "no derive");
            Assert.That(TerrainRaster.HasSpare, Is.False, "taken back from the spare");
        }

        /// <summary>Red: held twice, the picture is parked at the last release only; DropSpare destroys it.</summary>
        [Test]
        public void APictureHeldTwiceIsParkedAtTheSecondRelease()
        {
            var a = MapRasterFixtureA();
            var first = Get(RasterFixtures.Copy(a), _fixtureTheme); var texture = first.Texture;
            var second = Get(a, _fixtureTheme);
            Assert.That(second, Is.SameAs(first), "held twice: one entry");
            Put(first);
            Assert.That(texture != null, Is.True, "after the first release the texture is alive");
            Assert.That(TerrainRaster.HasSpare, Is.False, "and not parked while held");
            Put(second);
            Assert.That(texture != null, Is.True, "after the second release the texture is alive");
            Assert.That(TerrainRaster.HasSpare, Is.True, "parked at the last release");
            TerrainRaster.DropSpare();
            Assert.That(texture == null, Is.True, "DropSpare destroys it");
            Assert.That(TerrainRaster.HasSpare, Is.False, "nothing parked");
        }

        /// <summary>Red: a destroyed cell is patched into a copy of the held picture, not built whole.</summary>
        [Test]
        public void ADestroyedCellIsPatchedNotRebuilt()
        {
            var theme = UiTheme.Skin();
            var a = MapRasterFixtureA();
            var held = Get(a, theme);
            var b = a.WithDestroyedTerrain(new[] { RasterFixtures.Cell(4, 4), RasterFixtures.Cell(11, 11) });
            int full = TerrainRaster.FullBuilds, derived = TerrainRaster.DerivedBuilds;
            var picture = Get(b, theme);
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full), "no whole build");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived + 1), "one derive");
            Assert.That(TerrainRaster.LastDerivedTexels, Is.EqualTo(18 * TileTexels), "two cells, nine tiles each, 64 texels a tile");
            SameAsWholeBuild(picture.Texture, b, theme, "the derived picture is a whole build of the new map");
            Assert.That(RasterFixtures.FirstDifference(held.Texture.GetPixels32(), picture.Texture.GetPixels32()), Is.Not.Null, "the new picture differs from the old one");
        }

        /// <summary>Red: a map restored to a held picture's neighbour is patched back from it.</summary>
        [Test]
        public void ARestoredMapIsPatchedBack()
        {
            var a = MapRasterFixtureA();
            var heldA = Get(a, _fixtureTheme);
            var b = a.WithDestroyedTerrain(new[] { RasterFixtures.Cell(4, 4), RasterFixtures.Cell(11, 11) });
            int full = TerrainRaster.FullBuilds, derived = TerrainRaster.DerivedBuilds;
            var heldB = Get(b, _fixtureTheme);
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived + 1), "premise: B is derived from A");
            int firstDerive = TerrainRaster.LastDerivedTexels;
            Assert.That(firstDerive, Is.EqualTo(18 * TileTexels), "premise: the count of ADestroyedCellIsPatchedNotRebuilt");
            var textureA = heldA.Texture;
            Put(heldA);
            Assert.That(textureA == null, Is.True, "A's picture is destroyed at its last release: B, its kin, is held");
            Assert.That(TerrainRaster.HasSpare, Is.False, "and not parked");
            full = TerrainRaster.FullBuilds; derived = TerrainRaster.DerivedBuilds;
            var heldA2 = Get(RasterFixtures.Copy(a), _fixtureTheme);
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full), "no whole build");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived + 1), "patched back from B");
            Assert.That(TerrainRaster.LastDerivedTexels, Is.EqualTo(firstDerive), "the same eighteen tiles");
            SameAsWholeBuild(heldA2.Texture, a, _fixtureTheme, "the restored picture is a whole build of A");
            Assert.That(heldB.Texture != null, Is.True, "B is still held");
        }

        /// <summary>Red: cells at the border and in a line still match a whole build, with the clamped count.</summary>
        [Test]
        public void CellsAtTheBorderAndInALineStillMatchAWholeBuild()
        {
            var theme = UiTheme.Skin();
            var a = MapRasterFixtureA();
            Get(a, theme);
            var row = new uint[12]; for (int x = 0; x < 12; x++) row[x] = RasterFixtures.Cell(x, 0);
            var line = a.WithDestroyedTerrain(row);
            int full = TerrainRaster.FullBuilds, derived = TerrainRaster.DerivedBuilds;
            var lined = Get(line, theme);
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full), "(i) no whole build");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived + 1), "(i) one derive");
            Assert.That(TerrainRaster.LastDerivedTexels, Is.EqualTo(26 * TileTexels), "(i) twelve walls of row 0: 2 rows x 13 columns = 26 tiles, 1,664 texels");
            SameAsWholeBuild(lined.Texture, line, theme, "(i) the line equals a whole build");
            Put(lined);   // one kin only for the next derive: A
            var corner = a.WithDestroyedTerrain(new[] { RasterFixtures.Cell(0, 0) });
            full = TerrainRaster.FullBuilds; derived = TerrainRaster.DerivedBuilds;
            var cornered = Get(corner, theme);
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full), "(ii) no whole build");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived + 1), "(ii) one derive");
            Assert.That(TerrainRaster.LastDerivedTexels, Is.EqualTo(4 * TileTexels), "(ii) the corner cell: 4 tiles, 256 texels");
            SameAsWholeBuild(cornered.Texture, corner, theme, "(ii) the corner equals a whole build");
        }

        /// <summary>Red: two holders move from A to B in the surface's order; the old picture does not stay beside the new one.</summary>
        [Test]
        public void TheOldPictureIsNotKeptBesideTheNewOne()
        {
            var a = MapRasterFixtureA();
            var first = Get(a, _fixtureTheme); var second = Get(RasterFixtures.Copy(a), _fixtureTheme);
            Assert.That(second, Is.SameAs(first), "premise: two holders of one picture");
            var textureA = first.Texture;
            var b = a.WithDestroyedTerrain(new[] { RasterFixtures.Cell(4, 4) });
            int full = TerrainRaster.FullBuilds, derived = TerrainRaster.DerivedBuilds;
            Put(first);
            var movedFirst = Get(b, _fixtureTheme);
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full), "the first move builds nothing whole");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived + 1), "the first move derives");
            Put(second);
            var movedSecond = Get(b, _fixtureTheme);
            Assert.That(movedSecond, Is.SameAs(movedFirst), "the second holder shares the new picture");
            Assert.That(textureA == null, Is.True, "A's texture is destroyed after both moves");
            Assert.That(TerrainRaster.HasSpare, Is.False, "and A is not parked");
            var textureB = movedFirst.Texture;
            Put(movedFirst);
            Assert.That(textureB != null, Is.True, "after the first release of B its texture is alive");
            Assert.That(TerrainRaster.HasSpare, Is.False, "and B is not parked while held");
            Put(movedSecond);
            Assert.That(textureB != null, Is.True, "after the second release it is alive");
            Assert.That(TerrainRaster.HasSpare, Is.True, "and parked");
            TerrainRaster.DropSpare();
            Assert.That(textureB == null, Is.True, "DropSpare destroys it");
        }

        /// <summary>Red: at most one picture that nobody holds is alive.</summary>
        [Test]
        public void OnlyOnePictureWaitsUnheld()
        {
            var a = MapRasterFixtureA();
            var entryA = Get(a, _fixtureTheme); var textureA = entryA.Texture;
            Put(entryA);
            Assert.That(TerrainRaster.HasSpare, Is.True, "A is parked");
            Assert.That(textureA != null, Is.True, "A's texture is alive");
            var entryC = Get(RasterFixtures.MapC(), _fixtureTheme); var textureC = entryC.Texture;
            Assert.That(textureA == null, Is.True, "a whole build of another map destroys the spare");
            Put(entryC);
            Assert.That(TerrainRaster.HasSpare, Is.True, "C is the spare");
            Assert.That(textureC != null, Is.True, "C's texture is alive");
            TerrainRaster.DropSpare();
            Assert.That(textureC == null, Is.True, "DropSpare destroys it");
        }

        /// <summary>
        /// Red: a derive allocates little managed memory. The meter checks itself on every run: a kept 4 MiB array must read at least 3 MB
        /// (measured 2026-11-23 on 6000.0.85f2: 4.2 MB). The map is 160 x 160 (6 texels a tile): a whole build reads about 7 MB.
        /// </summary>
        [Test]
        public void ADeriveAllocatesLittleManagedMemory()
        {
            const int side = 160;
            var tiles = new byte[side * side]; var regions = new byte[side * side];
            for (int y = 0; y < side; y++) for (int x = 0; x < side; x++) regions[y * side + x] = (byte)(1 + x * 6 / side);
            for (int i = 0; i < 60; i++) tiles[(i * 37 % side) * side + i * 53 % side] = MapBlob.Wall;
            tiles[80 * side + 80] = MapBlob.Wall;
            var a = new MapBlob(side, side, tiles, null, regions);
            var b = a.WithDestroyedTerrain(new[] { (uint)(80 * side + 80) });
            var theme = UiTheme.Skin();
            var held = TerrainRaster.Acquire(a, theme); _held.Add(held);
            Assert.That(held.TexelsPerTile, Is.EqualTo(6), "set-up: 160 x 160 draws 6 texels a tile");

            GC.Collect();
            long c0 = Profiler.GetMonoUsedSizeLong();
            var keep = new byte[4 * 1024 * 1024]; keep[7] = 1;
            long control = Profiler.GetMonoUsedSizeLong() - c0;
            GC.KeepAlive(keep);
            Assert.That(control, Is.GreaterThanOrEqualTo(3L * 1024 * 1024), "the heap meter reads a kept 4 MiB array");

            GC.Collect();
            int derived = TerrainRaster.DerivedBuilds;
            long m0 = Profiler.GetMonoUsedSizeLong();
            var picture = TerrainRaster.Acquire(b, theme); _held.Add(picture);
            long grown = Profiler.GetMonoUsedSizeLong() - m0;
            Assert.That(grown, Is.LessThan(1024 * 1024), "one derive grows the managed heap by " + grown + " bytes");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived + 1), "the second map is a derive");
        }

        // ---- standing guards (green on the stub and after) -----------------------------------------------------------------------------

        [Test]
        public void TheFirstPictureIsAWholeBuild()
        {
            var theme = UiTheme.Skin();
            int full = TerrainRaster.FullBuilds, derived = TerrainRaster.DerivedBuilds;
            var entry = Get(MapRasterFixtureA(), theme);
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full + 1), "one whole build");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived), "no derive");
            Assert.That(entry.Tinted, Is.True, "the map carries regions");
            Assert.That(entry.Texture != null, Is.True, "a live texture");
        }

        [Test]
        public void AThemeChangeStillBuildsWhole()
        {
            var a = MapRasterFixtureA();
            Get(a, UiTheme.Skin());
            int full = TerrainRaster.FullBuilds, derived = TerrainRaster.DerivedBuilds;
            Get(a, UiTheme.Skin());
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full + 1), "the same map under another theme object builds whole");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived), "no derive");
        }

        [Test]
        public void AnotherMapBuildsWhole()
        {
            var theme = UiTheme.Skin();
            Get(MapRasterFixtureA(), theme);
            int full = TerrainRaster.FullBuilds, derived = TerrainRaster.DerivedBuilds;
            Get(RasterFixtures.MapC(), theme);
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full + 1), "a map with other regions is not a kin: whole build");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived), "no derive");
        }

        [Test]
        public void APictureOfAnotherThemeIsDestroyedAtItsLastRelease()
        {
            var entry = Get(MapRasterFixtureA(), UiTheme.Skin()); var texture = entry.Texture;
            Put(entry);
            Assert.That(texture == null, Is.True, "a theme that is not Current is destroyed, not parked");
            Assert.That(TerrainRaster.HasSpare, Is.False, "nothing parked");
        }

        [Test]
        public void AThemeChangeLeavesNothingParked()
        {
            var entry = Get(MapRasterFixtureA(), _fixtureTheme); var texture = entry.Texture;
            UiTheme.Current = UiTheme.Skin();   // a surface's Rebuild releases inside Changed, with the new theme already Current
            Put(entry);
            Assert.That(texture == null, Is.True, "the old picture is destroyed, not parked");
            Assert.That(TerrainRaster.HasSpare, Is.False, "nothing parked");
        }

        static MapBlob MapRasterFixtureA() { return RasterFixtures.MapA(); }
    }

    /// <summary>
    /// P-UI-MAP-RASTER-1, fixture (b): the real <see cref="TacticalMapController"/> on a recording transport (the rig of
    /// <see cref="MapPaintEditModeTests"/>, copied: its members are private). A rebind keeps both surfaces' pictures; a destroyed-terrain
    /// delivery patches them once.
    /// </summary>
    public sealed class MapRasterControllerEditModeTests
    {
        sealed class RecordingTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Connect(string url) { }
            public void Send(byte[] frame) { }
            public void Close() { Closed?.Invoke("closed"); }
            public void Poll() { }
            public void Open() { Opened?.Invoke(); }
            public void Deliver(ServerMsg message) { Received?.Invoke(message.Encode()); }
        }

        static readonly MapCamp[] Camps = { new MapCamp(256, 256, 8, 1) };

        GameObject _clientObject, _mapObject, _canvasObject, _arenaObject, _cameraObject;
        MatchClient _client;
        TacticalMapController _map;
        TacticalMapSurface _mini, _large;
        Session _session;
        RecordingTransport _transport;
        Keyboard _keyboard;
        Mouse _mouse;
        Func<double> _priorClock;
        UiTheme _saved, _fixtureTheme;
        bool _priorToggle, _priorShowRoute;
        double _now;
        uint _tick;

        [SetUp]
        public void SetUp()
        {
            TerrainRaster.DropSpare();
            _saved = UiTheme.Current;
            _fixtureTheme = UiTheme.Skin();
            UiTheme.Current = _fixtureTheme;
            _priorClock = TacticalMapController.SecondsClock;
            Build();
        }

        [TearDown]
        public void TearDown()
        {
            try { Dispose(); }
            finally
            {
                TacticalMapController.SecondsClock = _priorClock;
                WorldLabelVisibility.Hidden = false;
                TerrainRaster.DropSpare();
                UiTheme.Current = _saved;
            }
        }

        /// <summary>A 16 x 16 map (8 texels a tile) with the camp, the region bands, walls at (5,5) and (6,5) and a low at (10,9).</summary>
        static ServerMsg.Welcome Welcome()
        {
            var tiles = new byte[RasterFixtures.Side * RasterFixtures.Side];
            tiles[5 * RasterFixtures.Side + 5] = MapBlob.Wall; tiles[5 * RasterFixtures.Side + 6] = MapBlob.Wall; tiles[9 * RasterFixtures.Side + 10] = MapBlob.Low;
            return new ServerMsg.Welcome
            {
                Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16], PlayerId = 1,
                TickRate = 20, Map = new MapBlob(RasterFixtures.Side, RasterFixtures.Side, tiles, null, RasterFixtures.Bands()).WithPublic(Camps, null, null, null, null)
            };
        }

        void Build()
        {
            _now = 100d; _tick = 1;   // NUnit shares one fixture instance between tests
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            TacticalMapController.SecondsClock = () => _now;
            WorldLabelVisibility.Hidden = false;

            _transport = new RecordingTransport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://localhost/map-raster", Profile = "fixture", Now = () => 1000 });
            _session.Connect();
            _transport.Open();
            _transport.Deliver(Welcome()); Snapshot();

            _clientObject = new GameObject("Map raster client");
            _client = _clientObject.AddComponent<MatchClient>();
            _client.enabled = false;
            _client.Url = "ws://localhost/map-raster"; _client.Profile = "fixture";
            typeof(MatchClient).GetField("<Session>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_client, _session);
            _arenaObject = new GameObject("Map raster arena");
            _arenaObject.transform.SetParent(_clientObject.transform, false);
            _client.Arena = _arenaObject.AddComponent<ArenaView>();
            _cameraObject = new GameObject("Map raster camera", typeof(Camera));
            _cameraObject.transform.SetParent(_clientObject.transform, false);
            _client.Cam = _cameraObject.GetComponent<Camera>();

            _canvasObject = new GameObject("Map raster canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            ((RectTransform)_canvasObject.transform).sizeDelta = new Vector2(1000, 800);
            _mapObject = new GameObject("Map raster controller");
            _map = _mapObject.AddComponent<TacticalMapController>();
            _map.Initialize(_client, _canvasObject.transform);
            _priorToggle = _map.ToggleMode; _priorShowRoute = _map.ShowRoute;
            _map.SetToggleMode(false);
            _map.Bind(_session);
            _mini = _canvasObject.transform.Find("Minimap/TerrainFrame").GetComponent<TacticalMapSurface>();
            _large = _canvasObject.transform.Find("TacticalMap/MapFrame/PublicMap").GetComponent<TacticalMapSurface>();
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up");
            Frame(); Frame();
            Assert.That(Texture(_mini) != null && Texture(_large) != null, Is.True, "set-up: both surfaces hold a picture");
            Assert.That(Texture(_mini), Is.SameAs(Texture(_large)), "set-up: the two surfaces share one picture");
        }

        void Dispose()
        {
            try
            {
                if (_map != null) { _map.SetToggleMode(_priorToggle); _map.SetShowRoute(_priorShowRoute); _map.Bind(null); }
                if (_session != null) _session.Leave();
            }
            finally
            {
                WorldLabelVisibility.Hidden = false;
                if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
                if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
                if (_mapObject != null) UnityEngine.Object.DestroyImmediate(_mapObject);
                if (_canvasObject != null) UnityEngine.Object.DestroyImmediate(_canvasObject);
                if (_clientObject != null) UnityEngine.Object.DestroyImmediate(_clientObject);
                _mapObject = _canvasObject = _clientObject = null; _map = null; _session = null; _keyboard = null; _mouse = null;
            }
        }

        static Texture2D Texture(TacticalMapSurface surface) { return surface.GetComponent<RawImage>().texture as Texture2D; }

        /// <summary>One player-loop frame: time advances, a snapshot arrives (unless told not to), the HUD reads input, the map ticks, input flushes.</summary>
        void Frame(double dtMs = 16.7, bool snapshot = true)
        {
            _now += dtMs / 1000d;
            if (snapshot) Snapshot();
            _map.ReadUiInput(false, false);
            _map.Tick();
            _session.FlushInput();
        }

        void Snapshot()
        {
            _tick++;
            _transport.Deliver(new ServerMsg.Snapshot
            {
                Tick = _tick, Phase = 1, Actors = new WireActor[0],
                Me = new WireSelf { Id = 1, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 }
            });
        }

        // ---- red on the stub ---------------------------------------------------------------------------------------------------------

        /// <summary>Red, and the proof of the cause: Bind with the same session lets both surfaces go; the picture is still the same one after the next Tick.</summary>
        [Test]
        public void ARebindKeepsTheTerrainPicture()
        {
            var mini = Texture(_mini); var large = Texture(_large);
            int full = TerrainRaster.FullBuilds;
            _map.Bind(_session);
            Frame(5, false);
            Assert.That(Texture(_mini), Is.SameAs(mini), "the minimap shows the same picture object");
            Assert.That(Texture(_large), Is.SameAs(large), "the Tab map shows the same picture object");
            Assert.That(mini != null && large != null, Is.True, "and it is alive");
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full), "no whole build");
        }

        /// <summary>Red: an unbind and a rebind (the timeline-reset pair) keep the picture.</summary>
        [Test]
        public void AnUnbindAndRebindKeepsThePicture()
        {
            var mini = Texture(_mini); var large = Texture(_large);
            int full = TerrainRaster.FullBuilds;
            _map.Bind(null);
            _map.Bind(_session);
            Frame(5, false);
            Assert.That(Texture(_mini), Is.SameAs(mini), "the minimap shows the same picture object");
            Assert.That(Texture(_large), Is.SameAs(large), "the Tab map shows the same picture object");
            Assert.That(mini != null && large != null, Is.True, "and it is alive");
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full), "no whole build");
        }

        /// <summary>Red: a destroyed-terrain delivery patches the picture once for both surfaces.</summary>
        [Test]
        public void ADestroyedTerrainDeliveryPatchesBothSurfaces()
        {
            var old = Texture(_mini);
            int full = TerrainRaster.FullBuilds, derived = TerrainRaster.DerivedBuilds;
            var before = _session.TerrainMap;
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { new DestroyedTerrainState { Cells = new uint[] { 85 } } } });
            Assert.That(ReferenceEquals(_session.TerrainMap, before), Is.False, "premise: the session drew a new terrain map");
            Frame(5, false);
            Assert.That(TerrainRaster.FullBuilds, Is.EqualTo(full), "no whole build");
            Assert.That(TerrainRaster.DerivedBuilds, Is.EqualTo(derived + 1), "one derive for both surfaces");
            Assert.That(TerrainRaster.LastDerivedTexels, Is.EqualTo(9 * 64), "the wall (5,5): nine tiles, 64 texels a tile");
            Assert.That(Texture(_mini), Is.SameAs(Texture(_large)), "both surfaces show the same new texture");
            Assert.That(Texture(_mini), Is.Not.SameAs(old), "a new picture");
            Assert.That(old == null, Is.True, "the old picture is destroyed");
            Assert.That(TerrainRaster.HasSpare, Is.False, "nothing parked");
            Assert.That(RasterFixtures.Differs(Texture(_mini), _session.TerrainMap, _fixtureTheme), Is.Null, "the new picture is a whole build of the new map");
        }
    }
}
