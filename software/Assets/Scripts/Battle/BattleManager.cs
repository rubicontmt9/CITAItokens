using System.Collections.Generic;
using CitaiTokens.Cards;

namespace CitaiTokens.Battle
{
    /// <summary>
    /// 戦闘の勝敗。 / The outcome of a battle.
    /// </summary>
    public enum BattleOutcome
    {
        /// <summary>まだ決着していない。 / Still undecided.</summary>
        InProgress = 0,

        /// <summary>プレイヤーの勝ち。 / The player won.</summary>
        PlayerWin = 1,

        /// <summary>プレイヤーの負け。 / The player lost.</summary>
        PlayerLose = 2,
    }

    /// <summary>
    /// 1対1のターン制戦闘を1ラウンドずつ進行させる。MonoBehaviour ではないので、UI側が好きな速度で駆動できる。
    /// Drives a one-versus-one turn-based battle round by round. Not a MonoBehaviour, so the UI can step it at its own pace.
    /// </summary>
    /// <remarks>
    /// ルール: 1ラウンド = 両者が素早さ順に1回ずつ攻撃する。ただし先攻の攻撃で相手のHPが0になった場合、後攻は攻撃しない。
    /// 素早さが同値のときはプレイヤーが先攻 (単人プレイのMVPなので人間側を有利にして決定論を保つ)。
    /// Rules: one round = each side attacks once in speed order, except the second attack is skipped when the
    /// first one already reduced the defender to zero HP. On a speed tie the player attacks first
    /// (single-player MVP, so favouring the human keeps it both friendly and deterministic).
    /// </remarks>
    public sealed class BattleManager
    {
        /// <summary>
        /// 引き分け防止の安全上限。このラウンド数を消化しても決着しない場合、残HP割合で判定する。
        /// Safety cap against endless battles; after this many rounds the winner is decided by remaining HP ratio.
        /// </summary>
        public const int MaxRounds = 50;

        private readonly System.Random random;
        private readonly List<string> log = new List<string>();
        private readonly IReadOnlyList<string> readOnlyLog;

        /// <summary>
        /// プレイヤーのカードとCPUのカードで戦闘を初期化する。
        /// Initializes a battle between the player's card and the CPU's card.
        /// </summary>
        /// <param name="playerCard">プレイヤーのカード。null は不可。 / The player's card; must not be null.</param>
        /// <param name="cpuCard">CPUのカード。null は不可。 / The CPU's card; must not be null.</param>
        /// <param name="random">乱数源。null なら新規生成。固定シードを渡すと戦闘を再現できる。 / Random source; a new one is created when null. Pass a seeded instance to reproduce a battle.</param>
        public BattleManager(Card playerCard, Card cpuCard, System.Random random = null)
        {
            Player = new BattleCombatant(playerCard);
            Cpu = new BattleCombatant(cpuCard);
            this.random = random ?? new System.Random();
            readOnlyLog = log.AsReadOnly();

            RoundNumber = 1;
            Outcome = BattleOutcome.InProgress;

            log.Add($"{Player.DisplayName} と {Cpu.DisplayName} のバトル開始！");
        }

        /// <summary>プレイヤー側の戦闘状態。 / The player's side of the battle.</summary>
        public BattleCombatant Player { get; }

        /// <summary>CPU側の戦闘状態。 / The CPU's side of the battle.</summary>
        public BattleCombatant Cpu { get; }

        /// <summary>現在のラウンド番号 (1始まり)。 / The current round number, starting at one.</summary>
        public int RoundNumber { get; private set; }

        /// <summary>戦闘が終了したか。 / Whether the battle has finished.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>現在の勝敗。 / The current outcome.</summary>
        public BattleOutcome Outcome { get; private set; }

        /// <summary>
        /// ターンログ用の日本語の行。進行に応じて追記される。
        /// Japanese log lines for the turn log, appended as the battle progresses.
        /// </summary>
        public IReadOnlyList<string> Log => readOnlyLog;

        /// <summary>
        /// このラウンドでプレイヤーが先攻かどうか。素早さ同値ならプレイヤー。
        /// Whether the player attacks first this round; the player wins speed ties.
        /// </summary>
        public bool PlayerAttacksFirst => Player.Speed >= Cpu.Speed;

        /// <summary>
        /// 1ラウンドを解決する。決着後に呼んだ場合は例外を投げず、no-op の結果を返す。
        /// Resolves one full round. When called after the battle finished it returns a no-op result instead of throwing.
        /// </summary>
        /// <returns>このラウンドで起きたこと。 / What happened during this round.</returns>
        public BattleRoundResult AdvanceRound()
        {
            if (IsFinished)
            {
                return BattleRoundResult.NoOp(RoundNumber, Outcome);
            }

            int resolvedRound = RoundNumber;
            var attacks = new List<BattleAttackResult>(2);

            log.Add($"--- ラウンド {resolvedRound} ---");

            bool playerFirst = PlayerAttacksFirst;
            BattleCombatant first = playerFirst ? Player : Cpu;
            BattleCombatant second = playerFirst ? Cpu : Player;

            attacks.Add(ResolveAttack(first, second, playerFirst));

            if (!second.IsDefeated)
            {
                attacks.Add(ResolveAttack(second, first, !playerFirst));
            }

            if (Player.IsDefeated || Cpu.IsDefeated)
            {
                // 同時に倒れることはない (攻撃は逐次解決される)。 / Both cannot fall at once; attacks resolve one at a time.
                Finish(Cpu.IsDefeated ? BattleOutcome.PlayerWin : BattleOutcome.PlayerLose);
            }
            else if (resolvedRound >= MaxRounds)
            {
                FinishByTimeout();
            }
            else
            {
                RoundNumber = resolvedRound + 1;
            }

            return new BattleRoundResult(
                resolvedRound,
                attacks.AsReadOnly(),
                IsFinished,
                Outcome,
                false);
        }

        /// <summary>
        /// 攻撃1回を解決し、ログを追記する。 / Resolves a single attack and appends the log lines.
        /// </summary>
        private BattleAttackResult ResolveAttack(
            BattleCombatant attacker,
            BattleCombatant defender,
            bool attackerIsPlayer)
        {
            float multiplier = TypeAdvantage.GetMultiplier(attacker.Element, defender.Element);
            int damage = DamageCalculator.Calculate(attacker, defender, random);
            defender.ApplyDamage(damage);

            var result = new BattleAttackResult(
                attackerIsPlayer,
                attacker.DisplayName,
                defender.DisplayName,
                damage,
                multiplier,
                defender.CurrentHp,
                defender.IsDefeated);

            log.Add($"{attacker.DisplayName} の攻撃！ {defender.DisplayName} に {damage} ダメージ。");

            if (result.WasAdvantage)
            {
                log.Add("こうかは ばつぐんだ！");
            }
            else if (result.WasDisadvantage)
            {
                log.Add("こうかは いまひとつのようだ…");
            }

            log.Add($"{defender.DisplayName} の残りHP: {defender.CurrentHp}/{defender.MaxHp}");

            if (result.DefeatedDefender)
            {
                log.Add($"{defender.DisplayName} は たおれた！");
            }

            return result;
        }

        /// <summary>
        /// 勝敗を確定してログに書く。 / Locks in the outcome and writes it to the log.
        /// </summary>
        private void Finish(BattleOutcome outcome)
        {
            IsFinished = true;
            Outcome = outcome;
            log.Add(outcome == BattleOutcome.PlayerWin ? "バトルに勝利した！" : "バトルに敗北した…");
        }

        /// <summary>
        /// 上限ラウンドに達したので、残HP割合の大きい側を勝ちにする。同値ならプレイヤーの勝ち。
        /// The round cap was reached, so the side with the higher remaining HP ratio wins; a tie goes to the player.
        /// </summary>
        private void FinishByTimeout()
        {
            log.Add($"{MaxRounds} ラウンドが経過した。時間切れのため、残りHPの割合で判定する。");
            Finish(Player.HpRatio >= Cpu.HpRatio ? BattleOutcome.PlayerWin : BattleOutcome.PlayerLose);
        }
    }
}
