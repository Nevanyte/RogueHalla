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

        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(AbilityId value)
        {
            switch (value)
            {
                case AbilityId.Freeze:
                case AbilityId.Swarm:
                case AbilityId.Trial:
                case AbilityId.Blaze:
                case AbilityId.LastStand:
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>The player's word for the value.</summary>
        public static string Word(AbilityId value)
        {
            switch (value)
            {
                case AbilityId.Freeze: return "Freeze";
                case AbilityId.Swarm: return "Swarm";
                case AbilityId.Trial: return "Trial";
                case AbilityId.Blaze: return "Blaze Step";
                case AbilityId.LastStand: return "Last Stand";
                default: return "Wish";
            }
        }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(ActorStatus value)
        {
            switch (value)
            {
                case ActorStatus.Lobby:
                case ActorStatus.Alive:
                case ActorStatus.Dead:
                case ActorStatus.Mia:
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>The player's word for the value.</summary>
        public static string Word(ActorStatus value)
        {
            switch (value)
            {
                case ActorStatus.Lobby: return "Lobby";
                case ActorStatus.Alive: return "Alive";
                case ActorStatus.Dead: return "Dead";
                case ActorStatus.Mia: return "Missing in action";
                default: return "Unknown";
            }
        }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(PropAppearance value)
        {
            switch (value)
            {
                case PropAppearance.Ground:
                case PropAppearance.Chest:
                case PropAppearance.Body:
                case PropAppearance.Corpse:
                case PropAppearance.WindEmblem:
                case PropAppearance.Cannon:
                case PropAppearance.QuestItem:
                case PropAppearance.Heal:
                case PropAppearance.RebirthEgg:
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>The player's word for the value.</summary>
        public static string Word(PropAppearance value)
        {
            switch (value)
            {
                case PropAppearance.Ground: return "Weapon";
                case PropAppearance.Chest: return "Chest";
                case PropAppearance.Body: return "Body";
                case PropAppearance.Corpse: return "Corpse";
                case PropAppearance.WindEmblem: return "Wind emblem";
                case PropAppearance.Cannon: return "Travel cannon";
                case PropAppearance.QuestItem: return "Quest item";
                case PropAppearance.Heal: return "Heal pickup";
                case PropAppearance.RebirthEgg: return "Rebirth Egg";
                default: return "Item";
            }
        }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(BoostId value)
        {
            switch (value)
            {
                case BoostId.Ad:
                case BoostId.Ap:
                case BoostId.Armor:
                case BoostId.Shield:
                case BoostId.MaxHp:
                case BoostId.AttackSpeed:
                case BoostId.MoveSpeed:
                case BoostId.Lifesteal:
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>The player's word for the value.</summary>
        public static string Word(BoostId value)
        {
            switch (value)
            {
                case BoostId.Ad: return "Offense";
                case BoostId.Ap: return "Soul Offence";
                case BoostId.Armor: return "Defense";
                case BoostId.Shield: return "Soul Defense";
                case BoostId.MaxHp: return "Max health";
                case BoostId.AttackSpeed: return "Attack speed";
                case BoostId.MoveSpeed: return "Move speed";
                case BoostId.Lifesteal: return "Lifesteal";
                default: return "Bonus";
            }
        }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(TradeKind value)
        {
            switch (value)
            {
                case TradeKind.Barter:
                case TradeKind.Stats:
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>The player's word for the value.</summary>
        public static string Word(TradeKind value)
        {
            switch (value)
            {
                case TradeKind.Barter: return "Weapon barter";
                case TradeKind.Stats: return "Stat trader";
                default: return "Trader";
            }
        }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(TradeStat value)
        {
            switch (value)
            {
                case TradeStat.MaxHp:
                case TradeStat.Ad:
                case TradeStat.Ap:
                case TradeStat.AttackSpeed:
                case TradeStat.MoveSpeed:
                case TradeStat.Armor:
                case TradeStat.Shield:
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>The player's word for the value.</summary>
        public static string Word(TradeStat value)
        {
            switch (value)
            {
                case TradeStat.MaxHp: return "max HP";
                case TradeStat.Ad: return "Offense";
                case TradeStat.Ap: return "Soul Offence";
                case TradeStat.AttackSpeed: return "attack speed";
                case TradeStat.MoveSpeed: return "movement speed";
                case TradeStat.Armor: return "Defense";
                case TradeStat.Shield: return "Soul Defense";
                default: return "stat";
            }
        }
        /// <summary>True for a defined value.</summary>
        public static bool IsNamed(SessionState value)
        {
            switch (value)
            {
                case SessionState.Idle:
                case SessionState.Connecting:
                case SessionState.HelloSent:
                case SessionState.InMatch:
                case SessionState.Reconnecting:
                case SessionState.Ended:
                case SessionState.Rejected:
                case SessionState.Failed:
                case SessionState.Detached:
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>The player's word for the value.</summary>
        public static string Word(SessionState value)
        {
            switch (value)
            {
                case SessionState.Idle: return "Not connected";
                case SessionState.Connecting: return "Connecting\u2026";
                case SessionState.HelloSent: return "Connecting\u2026";
                case SessionState.InMatch: return "In the match";
                case SessionState.Reconnecting: return "Reconnecting\u2026";
                case SessionState.Ended: return "The match has ended.";
                case SessionState.Rejected: return "Connection rejected";
                case SessionState.Failed: return "Connection failed";
                case SessionState.Detached: return "Disconnected";
                default: return "Unknown";
            }
        }
        /// <summary>True for one of the fourteen catalog tags.</summary>
        public static bool IsNamedCardTag(string tag)
        {
            switch (tag)
            {
                case "True_":
                case "Dot":
                case "OnHit":
                case "Crit":
                case "Physical":
                case "Magic":
                case "Returned":
                case "Slow":
                case "Heal":
                case "Block":
                case "Ramping":
                case "Execute":
                case "Area":
                case "Wish":
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>The player's word for one catalog tag.</summary>
        public static string CardTag(string tag)
        {
            switch (tag)
            {
                case "True_": return "True";
                case "Dot": return "Damage over time";
                case "OnHit": return "On-hit";
                case "Crit": return "Critical strike";
                case "Physical": return "Physical";
                case "Magic": return "Magic";
                case "Returned": return "Returned";
                case "Slow": return "Slow";
                case "Heal": return "Heal";
                case "Block": return "Block";
                case "Ramping": return "Ramping";
                case "Execute": return "Execute";
                case "Area": return "Area";
                case "Wish": return "Wish";
                default: return "Other";
            }
        }
        /// <summary>The words of a comma separated tag list, joined by ", ".</summary>
        public static string CardTags(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags)) return "";
            var text = new StringBuilder();
            foreach (var piece in tags.Split(','))
            {
                string tag = piece.Trim();
                if (tag.Length == 0) continue;
                if (text.Length > 0) text.Append(", ");
                text.Append(CardTag(tag));
            }
            return text.ToString();
        }
    }
}
