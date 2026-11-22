using System;

namespace Roguehalla.Client
{
    /// <summary>Resource and cell contracts shared by the hub artwork and account catalog.</summary>
    public static class HubArtworkContract
    {
        public const string GuildHallResource = "StormInk/Hub/GuildHall";
        public const string FactionPortraitsResource = "StormInk/Hub/FactionPortraits";
        public const string EquipmentPreviewsResource = "StormInk/Hub/EquipmentPreviews";
        public const int CellPixels = 256;
        public const int EquipmentColumns = 5;
        public const int EquipmentRows = 2;

        static readonly string[] Factions = { "Fire", "Sentenced", "Plague", "Frost", "Holy" };
        static readonly string[] EquipmentIds =
        {
            "gilded-fire", "gilded-sentenced", "gilded-plague", "gilded-frost", "gilded-holy",
            "gilded-sword", "gilded-axe", "gilded-bow", "gilded-wand", "gilded-shield"
        };

        public static int FactionPortraitCell(string factionName)
        {
            for (int i = 0; i < Factions.Length; i++)
                if (string.Equals(Factions[i], factionName, StringComparison.Ordinal)) return i;
            return -1;
        }

        public static int EquipmentPreviewCell(string cosmeticId)
        {
            for (int i = 0; i < EquipmentIds.Length; i++)
                if (string.Equals(EquipmentIds[i], cosmeticId, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
