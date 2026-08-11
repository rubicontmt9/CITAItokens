namespace CitaiTokens.Cards
{
    /// <summary>
    /// 属性相性。三すくみ: Wood -> Earth -> Water -> Wood (矢印は「有利」の向き)。
    /// Type advantage. Three-way cycle: Wood -> Earth -> Water -> Wood (arrow points at what it beats).
    /// </summary>
    public static class TypeAdvantage
    {
        public const float AdvantageMultiplier = 1.5f;
        public const float DisadvantageMultiplier = 0.67f;
        public const float NeutralMultiplier = 1.0f;

        /// <summary>
        /// 攻撃側から見たダメージ倍率を返す。 / Returns the damage multiplier from the attacker's point of view.
        /// </summary>
        public static float GetMultiplier(ElementType attacker, ElementType defender)
        {
            if (attacker == defender)
            {
                return NeutralMultiplier;
            }

            if (Beats(attacker, defender))
            {
                return AdvantageMultiplier;
            }

            return DisadvantageMultiplier;
        }

        /// <summary>
        /// <paramref name="attacker"/> が <paramref name="defender"/> に有利かどうか。
        /// Whether <paramref name="attacker"/> has the advantage over <paramref name="defender"/>.
        /// </summary>
        public static bool Beats(ElementType attacker, ElementType defender)
        {
            return (attacker == ElementType.Wood && defender == ElementType.Earth)
                || (attacker == ElementType.Earth && defender == ElementType.Water)
                || (attacker == ElementType.Water && defender == ElementType.Wood);
        }
    }
}
