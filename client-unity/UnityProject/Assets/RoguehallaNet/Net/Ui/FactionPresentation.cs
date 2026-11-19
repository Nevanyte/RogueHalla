namespace Roguehalla.Net.Ui
{
    /// <summary>Player-facing faction names and champion role copy. These labels do not alter faction or wire identity.</summary>
    public static class FactionPresentation
    {
        public const int Count = 5;

        static readonly string[] Names = { "Fire", "Haunted", "Plague", "Frost", "Holy" };
        static readonly string[] Roles =
        {
            "Burst duelist · woman",
            "Reaper bruiser · man",
            "Hooded infection necromancer",
            "Burly guardian · man",
            "Armored paladin · woman"
        };

        public static string Name(int faction) { return faction >= 0 && faction < Count ? Names[faction] : "Unknown"; }
        public static string Role(int faction) { return faction >= 0 && faction < Count ? Roles[faction] : "Unknown role"; }
        public static string GildedName(int faction) { return faction >= 0 && faction < Count ? "Gilded " + Names[faction] : "Gilded Unknown"; }
    }
}
