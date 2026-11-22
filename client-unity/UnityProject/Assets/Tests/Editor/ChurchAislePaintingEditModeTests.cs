using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using UnityEditor;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Headless contract tests for the editable, top-down processional-stone plate.</summary>
    public sealed class ChurchAislePaintingEditModeTests
    {
        const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void FlagstonePlateIsReadableAndItsQuadrantsFollowEveryNaveTransform()
        {
            var authored = ChurchNaveArt.Load();
            Assert.That(authored, Is.Not.Null, "the nave seating definition must load from Resources");

            var flagstonesMember = FindMember(typeof(ChurchNaveArt), "Flagstones");
            Assert.That(flagstonesMember, Is.Not.Null,
                "ChurchNaveArt must expose its editable Flagstones texture; without it the aisle has no material reference");
            var authoredPlate = Read<Texture2D>(flagstonesMember, authored);
            Assert.That(authoredPlate, Is.Not.Null, "the production nave definition must reference its flagstone plate");
            Assert.That(AssetDatabase.Contains(authoredPlate), Is.True, "the production flagstone reference must resolve to an asset");
            Assert.That(AssetDatabase.GetAssetPath(authoredPlate), Is.Not.Empty);
            Assert.That(authoredPlate.isReadable, Is.True, "the runtime painter samples the plate, so Read/Write must be enabled");
            Assert.That(authoredPlate.GetPixels32(), Is.Not.Empty, "the production plate must contain readable pixels");

            var createPainting = typeof(ChurchNaveArt).GetMethod("CreatePainting", InstanceMembers);
            Assert.That(createPainting, Is.Not.Null, "the church aisle must bake a cached painting from its authored plate");
            var paintingType = createPainting.ReturnType;
            var paint = paintingType.GetMethod("Paint", InstanceMembers);
            Assert.That(paint, Is.Not.Null, "the cached aisle painting must sample using delivered map offsets");

            var diagnostic = new Texture2D(8, 8, TextureFormat.RGBA32, false, true)
            {
                name = "Church aisle quadrant diagnostic",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var clone = Object.Instantiate(authored);
            try
            {
                diagnostic.SetPixels(QuadrantPixels());
                diagnostic.Apply(false, false);
                Write(flagstonesMember, clone, diagnostic);
                SetFloat(clone, "AisleWidth", 4f);
                SetFloat(clone, "Wear", 0f);
                SetFloat(clone, "CarvingStrength", 0f);

                var slate = new Color(.48f, .39f, .31f, 1f);
                foreach (var front in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
                foreach (bool mirrored in new[] { false, true })
                {
                    var painting = createPainting.Invoke(clone, new object[] { front, mirrored, 12f });
                    var transverse = new Vector2Int(front.y, -front.x) * (mirrored ? -1 : 1);
                    foreach (bool rightInChart in new[] { false, true })
                    foreach (bool graveyardSide in new[] { false, true })
                    {
                        float across = (rightInChart ? 1f : -1f) * 1f;
                        float along = (graveyardSide ? 1f : -1f) * 3f;
                        var offset = new Vector2(transverse.x * across + front.x * along,
                            transverse.y * across + front.y * along);
                        var result = (Color)paint.Invoke(painting, new object[] { slate, offset.x, offset.y });
                        AssertQuadrant(result, rightInChart, graveyardSide,
                            $"front={front}, mirrored={mirrored}, uRight={rightInChart}, graveyardSide={graveyardSide}");
                    }

                    // Outside the painted aisle, the exact established slate field remains in force.
                    float outsideAcross = 2.1f;
                    var outside = new Vector2(transverse.x * outsideAcross, transverse.y * outsideAcross);
                    var field = (Color)paint.Invoke(painting, new object[] { slate, outside.x, outside.y });
                    var expectedField = slate * .91f;
                    Assert.That(field.r, Is.EqualTo(expectedField.r).Within(.0001f), "exterior red must be unchanged");
                    Assert.That(field.g, Is.EqualTo(expectedField.g).Within(.0001f), "exterior green must be unchanged");
                    Assert.That(field.b, Is.EqualTo(expectedField.b).Within(.0001f), "exterior blue must be unchanged");
                    Assert.That(field.a, Is.EqualTo(expectedField.a).Within(.0001f), "exterior alpha must be unchanged");
                }
            }
            finally
            {
                Object.DestroyImmediate(clone);
                Object.DestroyImmediate(diagnostic);
            }
        }

        static Color[] QuadrantPixels()
        {
            var pixels = new Color[8 * 8];
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                Color color = y < 4
                    ? (x < 4 ? Color.red : Color.green)
                    : (x < 4 ? Color.blue : Color.yellow);
                pixels[y * 8 + x] = color;
            }
            return pixels;
        }

        static void AssertQuadrant(Color color, bool rightInChart, bool graveyardSide, string context)
        {
            // Positive along points toward the graveyard, which is the lower (v < .5) half of the plate.
            if (graveyardSide)
            {
                if (rightInChart)
                {
                    Assert.That(color.g, Is.GreaterThan(color.r), context + " should sample the lower-right green quadrant");
                    Assert.That(color.g, Is.GreaterThan(color.b), context + " should sample the lower-right green quadrant");
                }
                else
                {
                    Assert.That(color.r, Is.GreaterThan(color.g), context + " should sample the lower-left red quadrant");
                    Assert.That(color.r, Is.GreaterThan(color.b), context + " should sample the lower-left red quadrant");
                }
            }
            else if (rightInChart)
            {
                Assert.That(color.r, Is.GreaterThan(color.b), context + " should sample the upper-right yellow quadrant");
                Assert.That(color.g, Is.GreaterThan(color.b), context + " should sample the upper-right yellow quadrant");
            }
            else
            {
                Assert.That(color.b, Is.GreaterThan(color.r), context + " should sample the upper-left blue quadrant");
                Assert.That(color.b, Is.GreaterThan(color.g), context + " should sample the upper-left blue quadrant");
            }
        }

        static MemberInfo FindMember(System.Type type, string name) =>
            (MemberInfo)type.GetField(name, InstanceMembers) ?? type.GetProperty(name, InstanceMembers);

        static T Read<T>(MemberInfo member, object target)
        {
            if (member is FieldInfo field) return (T)field.GetValue(target);
            if (member is PropertyInfo property) return (T)property.GetValue(target);
            return default;
        }

        static void Write(MemberInfo member, object target, object value)
        {
            if (member is FieldInfo field) field.SetValue(target, value);
            else ((PropertyInfo)member).SetValue(target, value);
        }

        static void SetFloat(object target, string name, float value)
        {
            var field = target.GetType().GetField(name, InstanceMembers);
            Assert.That(field, Is.Not.Null, $"editable church setting {name} must remain available");
            field.SetValue(target, value);
        }
    }
}
