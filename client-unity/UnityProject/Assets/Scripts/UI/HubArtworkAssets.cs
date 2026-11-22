using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Loads decorative hub artwork and slices the checked-in resource atlases by their shared contract.</summary>
    public static class HubArtworkAssets
    {
        static bool _guildHallLoaded, _portraitsLoaded, _equipmentLoaded;
        static Sprite _guildHall;
        static Sprite[] _portraits, _equipment;

        public static Sprite GuildHall
        {
            get
            {
                if (!_guildHallLoaded)
                {
                    _guildHallLoaded = true;
                    _guildHall = Resources.Load<Sprite>(HubArtworkContract.GuildHallResource);
                }
                return _guildHall;
            }
        }

        public static Sprite FactionPortrait(int factionIndex)
        {
            if ((uint)factionIndex >= 5u) return null;
            if (!_portraitsLoaded)
            {
                _portraitsLoaded = true;
                _portraits = Slice(HubArtworkContract.FactionPortraitsResource, 5, 1, "FactionPortrait");
            }
            return _portraits != null ? _portraits[factionIndex] : null;
        }

        public static Sprite EquipmentPreview(string cosmeticId)
        {
            int cell = HubArtworkContract.EquipmentPreviewCell(cosmeticId);
            if (cell < 0) return null;
            if (!_equipmentLoaded)
            {
                _equipmentLoaded = true;
                _equipment = Slice(HubArtworkContract.EquipmentPreviewsResource,
                    HubArtworkContract.EquipmentColumns, HubArtworkContract.EquipmentRows, "EquipmentPreview");
            }
            return _equipment != null ? _equipment[cell] : null;
        }

        static Sprite[] Slice(string resource, int columns, int rows, string name)
        {
            var texture = Resources.Load<Texture2D>(resource);
            int cell = HubArtworkContract.CellPixels;
            if (texture == null || texture.width != columns * cell || texture.height != rows * cell)
                return null;

            var sprites = new Sprite[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    int y = (rows - 1 - row) * cell;
                    var sprite = Sprite.Create(texture, new Rect(column * cell, y, cell, cell), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
                    sprite.name = name + "_" + index;
                    sprite.hideFlags = HideFlags.HideAndDontSave;
                    sprites[index] = sprite;
                }
            }
            return sprites;
        }
    }
}
