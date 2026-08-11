using System.Globalization;
using CitaiTokens.Battle;
using CitaiTokens.Cards;

namespace CitaiTokens.UI
{
    /// <summary>
    /// 列挙型やステータスを日本語の表示文字列に変換する。6画面で表記が食い違わないよう、変換はここだけに置く。
    /// Turns enums and stats into Japanese display strings. All conversion lives here so the six screens
    /// never disagree on wording.
    /// </summary>
    public static class CardTextFormatter
    {
        /// <summary>属性名 (木 / 土 / 水)。 / Element name (wood, earth, water).</summary>
        public static string ElementName(ElementType element)
        {
            switch (element)
            {
                case ElementType.Wood:
                    return "木";
                case ElementType.Earth:
                    return "土";
                case ElementType.Water:
                    return "水";
                default:
                    return "?";
            }
        }

        /// <summary>
        /// レアリティ名 (並 / 上 / 希少 / 極)。短いほど一覧で読みやすいので1〜2文字に寄せている。
        /// Rarity name. Kept to one or two characters, since short labels read better in the list.
        /// </summary>
        public static string RarityName(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Common:
                    return "並";
                case Rarity.Uncommon:
                    return "上";
                case Rarity.Rare:
                    return "希少";
                case Rarity.Epic:
                    return "極";
                default:
                    return "?";
            }
        }

        /// <summary>
        /// 属性とレアリティを1行にまとめる。 / Combines the element and rarity into a single line.
        /// </summary>
        public static string ElementAndRarity(ElementType element, Rarity rarity)
        {
            return "属性 " + ElementName(element) + " ・ レア度 " + RarityName(rarity);
        }

        /// <summary>
        /// 4つのステータスを1行で表す (一覧用の短い形)。
        /// Renders the four stats on one line, in the compact form used by the list.
        /// </summary>
        public static string CompactStatLine(StatBlock stats)
        {
            return "HP " + stats.Hp
                + " ・ 攻 " + stats.Attack
                + " ・ 防 " + stats.Defense
                + " ・ 速 " + stats.Speed;
        }

        /// <summary>
        /// 4つのステータスを日本語のラベル付きで表す (詳細表示用)。
        /// Renders the four stats with full Japanese labels, for the detail view.
        /// </summary>
        public static string StatLine(StatBlock stats)
        {
            return "たいりょく " + stats.Hp
                + "\nこうげき " + stats.Attack
                + "\nまもり " + stats.Defense
                + "\nすばやさ " + stats.Speed;
        }

        /// <summary>
        /// 属性相性を1行で説明する。ダメージが変動する理由をプレイヤーに見せるために使う。
        /// Describes the type matchup in one line, so the player can see why damage varies.
        /// </summary>
        /// <param name="attacker">攻撃側の属性。 / The attacker's element.</param>
        /// <param name="defender">防御側の属性。 / The defender's element.</param>
        public static string MatchupDescription(ElementType attacker, ElementType defender)
        {
            var multiplier = TypeAdvantage.GetMultiplier(attacker, defender);
            var attackerName = ElementName(attacker);
            var defenderName = ElementName(defender);
            var multiplierText = multiplier.ToString("0.##", CultureInfo.InvariantCulture);

            if (multiplier > TypeAdvantage.NeutralMultiplier)
            {
                return "相性: " + attackerName + " は " + defenderName + " に強い (与ダメージ " + multiplierText + "倍)";
            }

            if (multiplier < TypeAdvantage.NeutralMultiplier)
            {
                return "相性: " + attackerName + " は " + defenderName + " に弱い (与ダメージ " + multiplierText + "倍)";
            }

            return "相性: " + attackerName + " どうしの同属性 (与ダメージ 等倍)";
        }

        /// <summary>
        /// 三すくみの説明。初見のプレイヤー向けの一言。
        /// Explains the three-way cycle, as a one-liner for first-time players.
        /// </summary>
        public static string ElementCycleHint()
        {
            return "木 → 土 → 水 → 木 の順に強い";
        }

        /// <summary>
        /// 勝敗を日本語の見出しにする。未決着は勝ちと言い切らない表現にする。
        /// Turns an outcome into a Japanese headline. An undecided battle never claims a win.
        /// </summary>
        public static string OutcomeHeadline(BattleOutcome outcome)
        {
            switch (outcome)
            {
                case BattleOutcome.PlayerWin:
                    return "勝ち！";
                case BattleOutcome.PlayerLose:
                    return "負け…";
                default:
                    return "引き分け";
            }
        }

        /// <summary>
        /// 勝敗に添える一言。 / A short line of commentary to sit under the outcome headline.
        /// </summary>
        public static string OutcomeComment(BattleOutcome outcome)
        {
            switch (outcome)
            {
                case BattleOutcome.PlayerWin:
                    return "その枝は、なかなかの強さだった。";
                case BattleOutcome.PlayerLose:
                    return "別の場所で、別の自然物を探してみよう。";
                default:
                    return "バトルは決着しませんでした。";
            }
        }
    }
}
