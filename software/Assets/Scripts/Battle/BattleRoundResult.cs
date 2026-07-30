using System;
using System.Collections.Generic;
using CitaiTokens.Cards;

namespace CitaiTokens.Battle
{
    /// <summary>
    /// 1回の攻撃の結果。UIはこの内容だけを見て演出を再生できる。
    /// The result of a single attack. The UI can drive its animation from this alone.
    /// </summary>
    public sealed class BattleAttackResult
    {
        /// <summary>
        /// 攻撃1回分の結果を作る。値は確定後のものを渡す。
        /// Creates one attack result. All values must already be resolved.
        /// </summary>
        public BattleAttackResult(
            bool attackerIsPlayer,
            string attackerName,
            string defenderName,
            int damage,
            float typeMultiplier,
            int defenderHpAfter,
            bool defeatedDefender)
        {
            AttackerIsPlayer = attackerIsPlayer;
            AttackerName = attackerName;
            DefenderName = defenderName;
            Damage = damage;
            TypeMultiplier = typeMultiplier;
            DefenderHpAfter = defenderHpAfter;
            DefeatedDefender = defeatedDefender;
        }

        /// <summary>攻撃したのがプレイヤー側か。 / Whether the attacker was the player's side.</summary>
        public bool AttackerIsPlayer { get; }

        /// <summary>攻撃側のカード名。 / The attacking card's display name.</summary>
        public string AttackerName { get; }

        /// <summary>防御側のカード名。 / The defending card's display name.</summary>
        public string DefenderName { get; }

        /// <summary>
        /// 計算されたダメージ値。過剰ダメージも含むので、実際のHP減少は <see cref="DefenderHpAfter"/> を見る。
        /// The calculated damage, including overkill; use <see cref="DefenderHpAfter"/> for the real HP change.
        /// </summary>
        public int Damage { get; }

        /// <summary>適用された属性倍率。 / The type multiplier that was applied.</summary>
        public float TypeMultiplier { get; }

        /// <summary>属性有利だったか (「こうかは ばつぐんだ」)。 / Whether the attack had the type advantage.</summary>
        public bool WasAdvantage => TypeMultiplier > TypeAdvantage.NeutralMultiplier;

        /// <summary>属性不利だったか (「こうかは いまひとつ」)。 / Whether the attack had the type disadvantage.</summary>
        public bool WasDisadvantage => TypeMultiplier < TypeAdvantage.NeutralMultiplier;

        /// <summary>攻撃後の防御側の残HP。0で下止まり。 / The defender's HP after the attack, clamped at zero.</summary>
        public int DefenderHpAfter { get; }

        /// <summary>この攻撃で防御側が倒れたか。 / Whether this attack defeated the defender.</summary>
        public bool DefeatedDefender { get; }
    }

    /// <summary>
    /// 1ラウンドの結果。素早さ順に1〜2回の攻撃を含む。
    /// The result of one round, containing one or two attacks in speed order.
    /// </summary>
    public sealed class BattleRoundResult
    {
        private static readonly IReadOnlyList<BattleAttackResult> NoAttacks =
            Array.Empty<BattleAttackResult>();

        /// <summary>
        /// ラウンド結果を作る。<paramref name="attacks"/> は解決順に並んでいること。
        /// Creates a round result. <paramref name="attacks"/> must be in resolution order.
        /// </summary>
        public BattleRoundResult(
            int roundNumber,
            IReadOnlyList<BattleAttackResult> attacks,
            bool battleFinished,
            BattleOutcome outcome,
            bool wasNoOp)
        {
            RoundNumber = roundNumber;
            Attacks = attacks ?? NoAttacks;
            BattleFinished = battleFinished;
            Outcome = outcome;
            WasNoOp = wasNoOp;
        }

        /// <summary>このラウンドの番号 (1始まり)。 / This round's number, starting at one.</summary>
        public int RoundNumber { get; }

        /// <summary>
        /// 解決順に並んだ攻撃 (1回または2回)。先攻が倒された場合は1回だけ。
        /// The attacks in resolution order (one or two); only one when the first attack ended the battle.
        /// </summary>
        public IReadOnlyList<BattleAttackResult> Attacks { get; }

        /// <summary>このラウンド終了時点で戦闘が終わったか。 / Whether the battle is over as of the end of this round.</summary>
        public bool BattleFinished { get; }

        /// <summary>このラウンド終了時点の勝敗。 / The outcome as of the end of this round.</summary>
        public BattleOutcome Outcome { get; }

        /// <summary>
        /// 何も起きなかったか。決着後に <c>AdvanceRound</c> を呼んだ場合に true。
        /// Whether nothing happened; true when <c>AdvanceRound</c> was called after the battle finished.
        /// </summary>
        public bool WasNoOp { get; }

        /// <summary>
        /// 何も起きなかったことを表す結果を作る。 / Builds a result that represents a no-op call.
        /// </summary>
        /// <param name="roundNumber">現在のラウンド番号。 / The current round number.</param>
        /// <param name="outcome">現在の勝敗。 / The current outcome.</param>
        public static BattleRoundResult NoOp(int roundNumber, BattleOutcome outcome)
        {
            return new BattleRoundResult(roundNumber, NoAttacks, true, outcome, true);
        }
    }
}
