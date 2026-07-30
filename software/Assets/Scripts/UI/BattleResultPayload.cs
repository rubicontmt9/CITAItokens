using CitaiTokens.Battle;

namespace CitaiTokens.UI
{
    /// <summary>
    /// バトル画面から結果画面へ渡す表示用データ。<see cref="BattleManager"/> を渡さないのは、
    /// 結果画面が戦闘を進行させられないようにするため (見せるだけの画面に可変状態を持たせない)。
    /// The display data handed from the battle screen to the result screen. The <see cref="BattleManager"/>
    /// itself is deliberately not passed, so the result screen cannot advance a battle: a read-only screen
    /// should not hold mutable state.
    /// </summary>
    public sealed class BattleResultPayload
    {
        /// <summary>
        /// 結果を作る。値はすべて確定後のものを渡す。
        /// Creates the payload; every value must already be final.
        /// </summary>
        /// <param name="outcome">勝敗。 / The battle outcome.</param>
        /// <param name="playerCardName">プレイヤーのカード名。 / The player's card name.</param>
        /// <param name="cpuCardName">CPUのカード名。 / The CPU's card name.</param>
        /// <param name="playerHpRemaining">プレイヤーの残りHP。 / The player's remaining HP.</param>
        /// <param name="cpuHpRemaining">CPUの残りHP。 / The CPU's remaining HP.</param>
        /// <param name="rounds">消化したラウンド数。 / The number of rounds that were played.</param>
        public BattleResultPayload(
            BattleOutcome outcome,
            string playerCardName,
            string cpuCardName,
            int playerHpRemaining,
            int cpuHpRemaining,
            int rounds)
        {
            Outcome = outcome;
            PlayerCardName = playerCardName;
            CpuCardName = cpuCardName;
            PlayerHpRemaining = playerHpRemaining;
            CpuHpRemaining = cpuHpRemaining;
            Rounds = rounds;
        }

        /// <summary>勝敗。 / The battle outcome.</summary>
        public BattleOutcome Outcome { get; }

        /// <summary>プレイヤーのカード名。 / The player's card name.</summary>
        public string PlayerCardName { get; }

        /// <summary>CPUのカード名。 / The CPU's card name.</summary>
        public string CpuCardName { get; }

        /// <summary>プレイヤーの残りHP。 / The player's remaining HP.</summary>
        public int PlayerHpRemaining { get; }

        /// <summary>CPUの残りHP。 / The CPU's remaining HP.</summary>
        public int CpuHpRemaining { get; }

        /// <summary>消化したラウンド数。 / The number of rounds that were played.</summary>
        public int Rounds { get; }
    }
}
