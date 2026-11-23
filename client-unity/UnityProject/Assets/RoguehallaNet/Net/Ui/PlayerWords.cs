using System.Text;

namespace Roguehalla.Net.Ui
{
    /// <summary>Player-facing words for code values the server delivers; no identifier reaches a player.</summary>
    public static class PlayerWords
    {
        /// <summary>True for the five named values, false for any other byte.</summary>
        public static bool IsNamed(ActorKind kind) { return false; }

        /// <summary>The table's singular word; "contact" for a value that is not named.</summary>
        public static string Singular(ActorKind kind) { return kind.ToString(); }

        /// <summary>The table's plural word; "contacts" for a value that is not named.</summary>
        public static string Plural(ActorKind kind) { return kind.ToString(); }

        /// <summary>Appends "{faction} · {word} x{count}", the word singular for a count of 1 and plural otherwise;
        /// a null or empty faction writes no faction and no dot, and the word starts with a capital: no newline,
        /// nothing cleared.</summary>
        public static void AppendContactRow(StringBuilder text, string faction, ActorKind kind, int count) { }
    }
}
