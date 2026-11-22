using System;
using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Loads pixel-centre registrations only for painted regional weapon sheets.</summary>
    public static partial class StormInkActors
    {
        [Serializable] sealed class GripRow { public string facing; public int[] palms; }
        [Serializable] sealed class GripFile { public string weapon; public GripRow[] rows; }
        sealed class GripTrack
        {
            public WeaponId Weapon;
            public Hands[] Palms;
        }

        static readonly string[] GripFacings = { "S", "SE", "E", "NE", "N", "NW", "W", "SW" };
        static readonly GripTrack[] RegionalGrips = new GripTrack[CreaturesCapacity];
        static readonly bool[] GripLoaded = new bool[CreaturesCapacity];

        static GripTrack MobGrips(MobTypeId type)
        {
            int i = (int)type;
            if ((uint)i >= RegionalGrips.Length) return null;
            if (GripLoaded[i]) return RegionalGrips[i];
            GripLoaded[i] = true;
            var asset = Resources.Load<TextAsset>("StormInk/ActorGrips/" + type);
            if (asset == null) return null;
            var file = JsonUtility.FromJson<GripFile>(asset.text);
            if (file?.rows?.Length != Rows || !Enum.TryParse(file.weapon, out WeaponId weapon) ||
                weapon > WeaponId.Axe) return null;
            int columns = MobColumns(type);
            var palms = new Hands[Rows * columns];
            for (int row = 0; row < Rows; row++)
            {
                var entry = file.rows[row];
                if (entry?.facing != GripFacings[row] || entry.palms?.Length != columns * 4) return null;
                for (int col = 0; col < columns; col++)
                {
                    int off = col * 4;
                    for (int k = 0; k < 4; k++) if ((uint)entry.palms[off + k] >= 128) return null;
                    palms[row * columns + col] = new Hands((byte)entry.palms[off], (byte)entry.palms[off + 1],
                        (byte)entry.palms[off + 2], (byte)entry.palms[off + 3]);
                }
            }
            return RegionalGrips[i] = new GripTrack { Weapon = weapon, Palms = palms };
        }
    }
}
