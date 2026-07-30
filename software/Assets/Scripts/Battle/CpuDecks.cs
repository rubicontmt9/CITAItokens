using System;
using System.Collections.Generic;
using CitaiTokens.Cards;

namespace CitaiTokens.Battle
{
    /// <summary>
    /// 手書きのCPU対戦相手。ステータスを固定値で持つので、写真もAI呼び出しも無しに戦闘を試せる。
    /// Hand-authored CPU opponents with baked-in stats, so battles are playable without a photo or any AI call.
    /// </summary>
    /// <remarks>
    /// 各属性1体ずつ、Common〜Rare 相当の強さ。ステータスは <see cref="StatBlock"/> の許容範囲内に収めてある。
    /// One opponent per element at roughly Common-to-Rare power. Every stat sits inside the ranges declared on <see cref="StatBlock"/>.
    /// 返される <see cref="Card"/> は共有インスタンスなので、呼び出し側で書き換えないこと。
    /// The returned <see cref="Card"/> instances are shared, so callers must not mutate them.
    /// </remarks>
    public static class CpuDecks
    {
        private static readonly IReadOnlyList<Card> Opponents = BuildOpponents();

        /// <summary>
        /// すべてのCPU対戦相手。属性順 (Wood, Earth, Water)。
        /// Every CPU opponent, in element order (Wood, Earth, Water).
        /// </summary>
        public static IReadOnlyList<Card> All => Opponents;

        /// <summary>
        /// プレイヤーのカードに対する対戦相手を選ぶ。
        /// Picks an opponent for the given player card.
        /// </summary>
        /// <remarks>
        /// 選定ルール: プレイヤーが属性不利にならない相手 (プレイヤー側の倍率が 1.0 以上、
        /// つまり同属性か有利な相手) の中から等確率で選ぶ。MVPでは勝てるバトルにしたいため。
        /// 該当が無い、または <paramref name="playerCard"/> が null の場合は全体から等確率で選ぶ。
        /// Selection rule: choose uniformly among the opponents the player is not at a disadvantage against
        /// (the player's multiplier is at least 1.0, i.e. same element or favourable), because MVP battles
        /// should feel winnable. When nothing matches, or <paramref name="playerCard"/> is null, choose uniformly from all.
        /// </remarks>
        /// <param name="playerCard">プレイヤーのカード。null 可。 / The player's card; may be null.</param>
        /// <param name="random">乱数源。null なら新規生成。 / Random source; a new one is created when null.</param>
        /// <returns>対戦相手のカード。 / The opponent's card.</returns>
        public static Card PickForPlayerCard(Card playerCard, System.Random random = null)
        {
            var rng = random ?? new System.Random();

            if (playerCard == null)
            {
                return Opponents[rng.Next(Opponents.Count)];
            }

            var candidates = new List<Card>(Opponents.Count);
            for (int i = 0; i < Opponents.Count; i++)
            {
                Card candidate = Opponents[i];
                float playerMultiplier = TypeAdvantage.GetMultiplier(playerCard.Element, candidate.Element);
                if (playerMultiplier >= TypeAdvantage.NeutralMultiplier)
                {
                    candidates.Add(candidate);
                }
            }

            if (candidates.Count == 0)
            {
                return Opponents[rng.Next(Opponents.Count)];
            }

            return candidates[rng.Next(candidates.Count)];
        }

        /// <summary>
        /// 固定のCPUカードを組み立てる。 / Builds the fixed set of CPU cards.
        /// </summary>
        private static IReadOnlyList<Card> BuildOpponents()
        {
            DateTime authoredAt = DateTime.UtcNow;

            var list = new List<Card>
            {
                new Card(
                    "cpu-wood-01",
                    "若草の守り手 シダマル",
                    ElementType.Wood,
                    Rarity.Common,
                    new StatBlock(60, 14, 8, 12),
                    "日陰でひっそり育った若いシダ。踏まれても、次の朝にはまた立っている。",
                    null,
                    authoredAt),

                new Card(
                    "cpu-earth-01",
                    "岩肌のゴロウ",
                    ElementType.Earth,
                    Rarity.Uncommon,
                    new StatBlock(80, 18, 14, 8),
                    "川辺で百年ころがり続けた丸石。動きは鈍いが、押してもびくともしない。",
                    null,
                    authoredAt),

                new Card(
                    "cpu-water-01",
                    "しずくの舞姫 ミナモ",
                    ElementType.Water,
                    Rarity.Rare,
                    new StatBlock(70, 22, 6, 24),
                    "朝露が集まって姿を得たという。手を伸ばすと、触れる前に消えてしまう。",
                    null,
                    authoredAt),
            };

            return list.AsReadOnly();
        }
    }
}
