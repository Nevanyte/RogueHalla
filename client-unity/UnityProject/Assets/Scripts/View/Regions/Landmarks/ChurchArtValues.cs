using System;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Serializable church painting parameters and the per-field inheritance rules shared by gameplay and the editor.</summary>
    [Flags]
    public enum ChurchArtOverrideFields
    {
        None = 0,
        Width = 1 << 0, Depth = 1 << 1, Height = 1 << 2, RotationDegrees = 1 << 3,
        MountHeight = 1 << 4, LocalOffset = 1 << 5, Contact = 1 << 6, FlameSocket = 1 << 7,
        Tint = 1 << 8, FlameColor = 1 << 9, FlameIntensity = 1 << 10,
        SpillRadius = 1 << 11, SpillIntensity = 1 << 12, SortingOffset = 1 << 13,
        Front = 1 << 14, Side = 1 << 15, Back = 1 << 16, Material = 1 << 17, PaneMode = 1 << 18,
        FlameLayer = 1 << 19, FlameLayerSize = 1 << 20,
        All = (1 << 21) - 1
    }

    [Serializable]
    public struct ChurchArtValues
    {
        public float Width, Depth, Height, RotationDegrees, MountHeight;
        public Vector2 LocalOffset, Contact, FlameSocket;
        public Color Tint, FlameColor;
        public float FlameIntensity, SpillRadius, SpillIntensity;
        public int SortingOffset;
        public Sprite Front, Side, Back;
        public Material Material;
        public ChurchWindowPaneMode PaneMode;
        public Sprite FlameLayer;
        public Vector2 FlameLayerSize;

        public static ChurchArtValues Merge(ChurchArtValues defaults, ChurchArtValues over, ChurchArtOverrideFields fields)
        {
            if ((fields & ChurchArtOverrideFields.Width) != 0) defaults.Width = over.Width;
            if ((fields & ChurchArtOverrideFields.Depth) != 0) defaults.Depth = over.Depth;
            if ((fields & ChurchArtOverrideFields.Height) != 0) defaults.Height = over.Height;
            if ((fields & ChurchArtOverrideFields.RotationDegrees) != 0) defaults.RotationDegrees = over.RotationDegrees;
            if ((fields & ChurchArtOverrideFields.MountHeight) != 0) defaults.MountHeight = over.MountHeight;
            if ((fields & ChurchArtOverrideFields.LocalOffset) != 0) defaults.LocalOffset = over.LocalOffset;
            if ((fields & ChurchArtOverrideFields.Contact) != 0) defaults.Contact = over.Contact;
            if ((fields & ChurchArtOverrideFields.FlameSocket) != 0) defaults.FlameSocket = over.FlameSocket;
            if ((fields & ChurchArtOverrideFields.Tint) != 0) defaults.Tint = over.Tint;
            if ((fields & ChurchArtOverrideFields.FlameColor) != 0) defaults.FlameColor = over.FlameColor;
            if ((fields & ChurchArtOverrideFields.FlameIntensity) != 0) defaults.FlameIntensity = over.FlameIntensity;
            if ((fields & ChurchArtOverrideFields.SpillRadius) != 0) defaults.SpillRadius = over.SpillRadius;
            if ((fields & ChurchArtOverrideFields.SpillIntensity) != 0) defaults.SpillIntensity = over.SpillIntensity;
            if ((fields & ChurchArtOverrideFields.SortingOffset) != 0) defaults.SortingOffset = over.SortingOffset;
            if ((fields & ChurchArtOverrideFields.Front) != 0) defaults.Front = over.Front;
            if ((fields & ChurchArtOverrideFields.Side) != 0) defaults.Side = over.Side;
            if ((fields & ChurchArtOverrideFields.Back) != 0) defaults.Back = over.Back;
            if ((fields & ChurchArtOverrideFields.Material) != 0) defaults.Material = over.Material;
            if ((fields & ChurchArtOverrideFields.PaneMode) != 0) defaults.PaneMode = over.PaneMode;
            if ((fields & ChurchArtOverrideFields.FlameLayer) != 0) defaults.FlameLayer = over.FlameLayer;
            if ((fields & ChurchArtOverrideFields.FlameLayerSize) != 0) defaults.FlameLayerSize = over.FlameLayerSize;
            return defaults;
        }
    }

    public enum ChurchArtMount { Floor, Upright, Wall }
    public enum ChurchWindowPaneMode { Full, Jamb, Concealed }

    public struct ChurchArtResolved
    {
        public ChurchArtValues Values;
        public bool Mirrored;
        public ChurchArtMount Mount;
        public bool EmitsCandleLight;
    }
}
