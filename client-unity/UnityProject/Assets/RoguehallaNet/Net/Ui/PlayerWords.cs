using System.Text;

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
    }
}
