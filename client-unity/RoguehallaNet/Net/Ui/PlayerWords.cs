using System.Text;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Ui
{
    /// <summary>Player-facing words for code values the server delivers; no identifier reaches a player.</summary>
    public static class PlayerWords
    {
        /// <summary>True for the five named values, false for any other byte.</summary>
        public static bool IsNamed(ActorKind kind)
        {
            switch (kind)
            {
                case ActorKind.Player:
                case ActorKind.RegionalMob:
                case ActorKind.Zombie:
                case ActorKind.TrainingDummy:
                case ActorKind.QuestGiver:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The table's singular word; "contact" for a value that is not named.</summary>
        public static string Singular(ActorKind kind)
        {
            switch (kind)
            {
                case ActorKind.Player: return "player";
                case ActorKind.RegionalMob: return "creature";
                case ActorKind.Zombie: return "zombie";
                case ActorKind.TrainingDummy: return "training dummy";
                case ActorKind.QuestGiver: return "quest giver";
                default: return "contact";
            }
        }

        /// <summary>The table's plural word; "contacts" for a value that is not named.</summary>
        public static string Plural(ActorKind kind)
        {
            switch (kind)
            {
                case ActorKind.Player: return "players";
                case ActorKind.RegionalMob: return "creatures";
                case ActorKind.Zombie: return "zombies";
                case ActorKind.TrainingDummy: return "training dummies";
                case ActorKind.QuestGiver: return "quest givers";
                default: return "contacts";
            }
        }

        /// <summary>Appends "{faction} · {word} x{count}", the word singular for a count of 1 and plural otherwise;
        /// a null or empty faction writes no faction and no dot, and the word starts with a capital: no newline,
        /// nothing cleared.</summary>
        public static void AppendContactRow(StringBuilder text, string faction, ActorKind kind, int count)
        {
            string word = count == 1 ? Singular(kind) : Plural(kind);
            if (string.IsNullOrEmpty(faction)) text.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
            else text.Append(faction).Append(" · ").Append(word);
            text.Append(" x").Append(count);
        }

        // P-UI-PLAYER-WORDS-2 stubs: the implementer replaces each body with a switch over the packet's tables.
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(AbilityId value) { return false; }
        /// <summary>The player's word for the ability.</summary>
        public static string Word(AbilityId value) { return ""; }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(ActorStatus value) { return false; }
        /// <summary>The player's word for the status.</summary>
        public static string Word(ActorStatus value) { return ""; }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(PropAppearance value) { return false; }
        /// <summary>The player's word for the prop.</summary>
        public static string Word(PropAppearance value) { return ""; }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(BoostId value) { return false; }
        /// <summary>The player's word for the boost.</summary>
        public static string Word(BoostId value) { return ""; }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(TradeKind value) { return false; }
        /// <summary>The player's word for the trade kind.</summary>
        public static string Word(TradeKind value) { return ""; }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(TradeStat value) { return false; }
        /// <summary>The player's word for the trade stat.</summary>
        public static string Word(TradeStat value) { return ""; }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(SessionState value) { return false; }
        /// <summary>The player's word for the connection state.</summary>
        public static string Word(SessionState value) { return ""; }
        /// <summary>True for one of the fourteen catalog tags.</summary>
        public static bool IsNamedCardTag(string tag) { return false; }
        /// <summary>The player's word for one catalog tag.</summary>
        public static string CardTag(string tag) { return ""; }
        /// <summary>The words of a comma separated tag list, joined by ", ".</summary>
        public static string CardTags(string tags) { return tags; }
    }
}
