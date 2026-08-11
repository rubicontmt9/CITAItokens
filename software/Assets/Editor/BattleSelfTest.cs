#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CitaiTokens.Battle;
using CitaiTokens.Cards;
using UnityEditor;
using UnityEngine;

namespace CitaiTokens.EditorTools
{
    /// <summary>
    /// 戦闘ロジックの自己検証。テストフレームワークを使わず、エディタのメニューから手動で走らせる。
    /// A self-verification harness for the battle logic. No test framework: run it by hand from the editor menu.
    /// </summary>
    /// <remarks>
    /// 失敗した項目は Debug.LogError で理由付きで出力し、最後に合計を1行で出す。
    /// Each failure is reported through Debug.LogError together with an explanation, then a one-line summary is logged.
    /// </remarks>
    public static class BattleSelfTest
    {
        private static int passCount;
        private static int failCount;

        /// <summary>
        /// すべての検証を実行し、結果をコンソールに出力する。
        /// Runs every check and writes the results to the console.
        /// </summary>
        [MenuItem("Tools/CITAItokens/Run Battle Self-Test")]
        public static void Run()
        {
            passCount = 0;
            failCount = 0;

            CheckBaseDamageFormula();
            CheckTypeMultipliers();
            CheckDeterministicDamage();
            CheckDamageNeverBelowOne();
            CheckHpClamping();
            CheckTurnOrder();
            CheckSeededBattleTerminates();
            CheckSeededBattleIsReproducible();
            CheckMaxRoundsCap();
            CheckNoOpAfterFinish();
            CheckCpuDecks();
            CheckNullGuards();

            string summary = $"Battle self-test: {passCount} passed, {failCount} failed";
            if (failCount == 0)
            {
                Debug.Log(summary);
            }
            else
            {
                Debug.LogError(summary);
            }
        }

        // -------------------------------------------------------------------------
        // 個別の検証 / Individual checks
        // -------------------------------------------------------------------------

        /// <summary>
        /// 基礎ダメージ式 max(1, attack - defense / 2) と整数除算を検証する。
        /// Verifies the base damage formula max(1, attack - defense / 2) and its integer division.
        /// </summary>
        private static void CheckBaseDamageFormula()
        {
            CheckEqual(15, DamageCalculator.CalculateBaseDamage(20, 10), "base damage 20 vs 10 should be 20 - 5");
            CheckEqual(15, DamageCalculator.CalculateBaseDamage(20, 11), "defense / 2 must be integer division: 11 / 2 == 5");
            CheckEqual(30, DamageCalculator.CalculateBaseDamage(30, 1), "defense 1 halves to 0 under integer division");
            CheckEqual(1, DamageCalculator.CalculateBaseDamage(20, 40), "attack 20 minus defense 40 / 2 is 0, so the max(1, ...) floor applies");
            CheckEqual(1, DamageCalculator.CalculateBaseDamage(5, 60), "a hugely negative result must still floor at 1");
        }

        /// <summary>
        /// 三すくみの倍率を <see cref="TypeAdvantage"/> 経由で検証する。
        /// Verifies the three-way type multipliers through <see cref="TypeAdvantage"/>.
        /// </summary>
        private static void CheckTypeMultipliers()
        {
            CheckApprox(1.5f, TypeAdvantage.GetMultiplier(ElementType.Wood, ElementType.Earth), "Wood attacking Earth is an advantage");
            CheckApprox(1.5f, TypeAdvantage.GetMultiplier(ElementType.Earth, ElementType.Water), "Earth attacking Water is an advantage");
            CheckApprox(1.5f, TypeAdvantage.GetMultiplier(ElementType.Water, ElementType.Wood), "Water attacking Wood is an advantage");

            CheckApprox(0.67f, TypeAdvantage.GetMultiplier(ElementType.Earth, ElementType.Wood), "Earth attacking Wood is a disadvantage");
            CheckApprox(0.67f, TypeAdvantage.GetMultiplier(ElementType.Water, ElementType.Earth), "Water attacking Earth is a disadvantage");
            CheckApprox(0.67f, TypeAdvantage.GetMultiplier(ElementType.Wood, ElementType.Water), "Wood attacking Water is a disadvantage");

            CheckApprox(1.0f, TypeAdvantage.GetMultiplier(ElementType.Wood, ElementType.Wood), "same element is neutral");
            CheckApprox(1.0f, TypeAdvantage.GetMultiplier(ElementType.Earth, ElementType.Earth), "same element is neutral");
            CheckApprox(1.0f, TypeAdvantage.GetMultiplier(ElementType.Water, ElementType.Water), "same element is neutral");
        }

        /// <summary>
        /// ばらつきを固定した決定論的なダメージ計算を検証する。
        /// Verifies the deterministic damage calculation with a fixed variance.
        /// </summary>
        private static void CheckDeterministicDamage()
        {
            // 基礎ダメージ = 22 - 8 / 2 = 18 / Base damage = 22 - 8 / 2 = 18
            var woodAttacker = Combatant("攻撃側ウッド", ElementType.Wood, 200, 22, 8, 20);
            var earthDefender = Combatant("防御側アース", ElementType.Earth, 200, 22, 8, 10);
            var woodDefender = Combatant("防御側ウッド", ElementType.Wood, 200, 22, 8, 10);
            var earthAttacker = Combatant("攻撃側アース", ElementType.Earth, 200, 22, 8, 20);

            CheckEqual(27, DamageCalculator.Calculate(woodAttacker, earthDefender, 1.0f), "18 base * 1.5 advantage * 1.0 variance should be 27");
            CheckEqual(18, DamageCalculator.Calculate(woodAttacker, woodDefender, 1.0f), "18 base * 1.0 neutral * 1.0 variance should be 18");
            CheckEqual(12, DamageCalculator.Calculate(earthAttacker, woodDefender, 1.0f), "18 base * 0.67 disadvantage * 1.0 variance should round to 12");

            CheckEqual(24, DamageCalculator.Calculate(woodAttacker, earthDefender, DamageCalculator.MinVariance), "27.0 * 0.9 variance should round to 24");
            CheckEqual(30, DamageCalculator.Calculate(woodAttacker, earthDefender, DamageCalculator.MaxVariance), "27.0 * 1.1 variance should round to 30");

            // 乱数版は必ず下限〜上限の範囲に入る。 / The random overload must always land inside the variance band.
            var rng = new System.Random(4242);
            bool allInBand = true;
            for (int i = 0; i < 200; i++)
            {
                int damage = DamageCalculator.Calculate(woodAttacker, earthDefender, rng);
                if (damage < 24 || damage > 30)
                {
                    allInBand = false;
                }
            }

            Check(allInBand, "the System.Random overload must stay within the deterministic 0.9-1.1 variance band (24-30 here)");
        }

        /// <summary>
        /// 防御が極端に高く属性不利でも、ダメージが1未満にならないことを検証する。
        /// Verifies damage never drops below one, even with maximal defense and an unfavourable multiplier.
        /// </summary>
        private static void CheckDamageNeverBelowOne()
        {
            var weakEarth = Combatant("よろよろアース", ElementType.Earth, 200, StatBlock.MinAttack, StatBlock.MaxDefense, 10);
            var tankyWood = Combatant("かたいウッド", ElementType.Wood, 200, StatBlock.MinAttack, StatBlock.MaxDefense, 10);

            CheckAtLeast(1, DamageCalculator.Calculate(weakEarth, tankyWood, DamageCalculator.MinVariance), "min attack vs max defense at a disadvantage must still deal at least 1");
            CheckAtLeast(1, DamageCalculator.Calculate(weakEarth, tankyWood, 0.0f), "even a degenerate variance of 0 must be clamped up to 1");
            CheckAtLeast(1, DamageCalculator.CalculateBaseDamage(StatBlock.MinAttack, StatBlock.MaxDefense), "base damage must never be below 1");
        }

        /// <summary>
        /// HPが0で下止まりし、負にならないことを検証する。
        /// Verifies HP clamps at zero and never goes negative.
        /// </summary>
        private static void CheckHpClamping()
        {
            var target = Combatant("まもり手", ElementType.Water, 30, 10, 0, 10);

            CheckEqual(30, target.MaxHp, "MaxHp should come from the card's Hp stat");
            CheckEqual(30, target.CurrentHp, "CurrentHp should start at MaxHp");
            Check(!target.IsDefeated, "a fresh combatant must not be defeated");

            CheckEqual(0, target.ApplyDamage(0), "zero damage should apply nothing");
            CheckEqual(0, target.ApplyDamage(-99), "negative damage must not heal");
            CheckEqual(30, target.CurrentHp, "HP must be untouched by zero or negative damage");

            CheckEqual(10, target.ApplyDamage(10), "ApplyDamage should return the HP actually removed");
            CheckEqual(20, target.CurrentHp, "HP should be reduced by the applied damage");
            CheckApprox(20f / 30f, target.HpRatio, "HpRatio should be CurrentHp / MaxHp");

            CheckEqual(20, target.ApplyDamage(999), "overkill should only return the HP that remained");
            CheckEqual(0, target.CurrentHp, "HP must clamp at 0, never go negative");
            Check(target.IsDefeated, "a combatant at 0 HP must report IsDefeated");
            CheckApprox(0f, target.HpRatio, "HpRatio at 0 HP must be 0");

            CheckEqual(0, target.ApplyDamage(50), "damaging an already-defeated combatant should apply nothing");
            CheckEqual(0, target.CurrentHp, "HP must stay at exactly 0");
        }

        /// <summary>
        /// 素早さ順の行動と、同値時のプレイヤー先攻を検証する。
        /// Verifies speed-ordered turns and that the player wins speed ties.
        /// </summary>
        private static void CheckTurnOrder()
        {
            // 両者ともHP最大・攻撃最小・防御最大なので、1ラウンドで倒れることはない。
            // Both sides are max HP, min attack and max defense, so nobody dies in a single round.
            var fastPlayer = TankCard("はやいプレイヤー", ElementType.Wood, 40);
            var slowCpu = TankCard("おそいCPU", ElementType.Wood, 10);

            var fasterPlayerBattle = new BattleManager(fastPlayer, slowCpu, new System.Random(1));
            Check(fasterPlayerBattle.PlayerAttacksFirst, "the higher-Speed player should act first");
            var round = fasterPlayerBattle.AdvanceRound();
            CheckEqual(2, round.Attacks.Count, "a round where nobody dies must contain exactly 2 attacks");
            if (round.Attacks.Count == 2)
            {
                Check(round.Attacks[0].AttackerIsPlayer, "with higher Speed the player must attack first");
                Check(!round.Attacks[1].AttackerIsPlayer, "the CPU must attack second when it is slower");
            }

            var fasterCpuBattle = new BattleManager(TankCard("おそいプレイヤー", ElementType.Wood, 10), TankCard("はやいCPU", ElementType.Wood, 40), new System.Random(1));
            Check(!fasterCpuBattle.PlayerAttacksFirst, "the higher-Speed CPU should act first");
            var cpuFirstRound = fasterCpuBattle.AdvanceRound();
            if (cpuFirstRound.Attacks.Count == 2)
            {
                Check(!cpuFirstRound.Attacks[0].AttackerIsPlayer, "with higher Speed the CPU must attack first");
                Check(cpuFirstRound.Attacks[1].AttackerIsPlayer, "the player must attack second when slower");
            }
            else
            {
                Check(false, "a round between two tanky combatants must contain 2 attacks");
            }

            var tieBattle = new BattleManager(TankCard("同速プレイヤー", ElementType.Wood, 25), TankCard("同速CPU", ElementType.Wood, 25), new System.Random(1));
            Check(tieBattle.PlayerAttacksFirst, "on a Speed tie the player must act first");
            var tieRound = tieBattle.AdvanceRound();
            if (tieRound.Attacks.Count >= 1)
            {
                Check(tieRound.Attacks[0].AttackerIsPlayer, "on a Speed tie the first attack must belong to the player");
            }
            else
            {
                Check(false, "a tie round must contain at least one attack");
            }

            // 先攻が倒したら後攻は攻撃しない。 / The second attack is skipped when the first one already won.
            var oneShotPlayer = MakeCard("いちげきプレイヤー", ElementType.Wood, StatBlock.MaxHp, StatBlock.MaxAttack, 0, 50);
            var glassCpu = MakeCard("ガラスのCPU", ElementType.Earth, StatBlock.MinHp, StatBlock.MaxAttack, 0, 1);
            var oneShotBattle = new BattleManager(oneShotPlayer, glassCpu, new System.Random(7));
            var oneShotRound = oneShotBattle.AdvanceRound();
            CheckEqual(1, oneShotRound.Attacks.Count, "when the first attack defeats the defender, the second attack must not happen");
            Check(oneShotBattle.IsFinished, "the battle should be finished after a one-shot kill");
            Check(oneShotBattle.Outcome == BattleOutcome.PlayerWin, "a one-shot kill by the player should be a PlayerWin");
            CheckEqual(StatBlock.MaxHp, oneShotBattle.Player.CurrentHp, "the winner must take no damage when it kills first");
        }

        /// <summary>
        /// シード固定の戦闘が決着まで終了することを検証する。
        /// Verifies a seeded battle terminates with a decisive outcome.
        /// </summary>
        private static void CheckSeededBattleTerminates()
        {
            var player = MakeCard("プレイヤーの木札", ElementType.Wood, 60, 25, 10, 20);
            var cpu = MakeCard("CPUの土札", ElementType.Earth, 60, 20, 10, 15);
            var battle = new BattleManager(player, cpu, new System.Random(20260730));

            int guard = 0;
            BattleRoundResult last = null;
            while (!battle.IsFinished && guard < BattleManager.MaxRounds + 5)
            {
                last = battle.AdvanceRound();
                guard++;
            }

            Check(battle.IsFinished, "a seeded battle must finish within the round cap");
            Check(battle.Outcome != BattleOutcome.InProgress, "a finished battle must have a decisive outcome");
            Check(last != null && last.BattleFinished, "the final round result must be flagged as finishing the battle");
            Check(last != null && !last.WasNoOp, "the final resolved round must not be flagged as a no-op");
            Check(battle.Player.CurrentHp >= 0 && battle.Cpu.CurrentHp >= 0, "HP must never be negative after a full battle");
            Check(battle.Player.IsDefeated || battle.Cpu.IsDefeated, "with these stats the battle should end by knockout, not timeout");
            Check(battle.Log.Count > 0, "the battle log must contain lines for the UI turn log");

            if (battle.Outcome == BattleOutcome.PlayerWin)
            {
                Check(battle.Cpu.IsDefeated, "a PlayerWin means the CPU is the one at 0 HP");
            }
            else
            {
                Check(battle.Player.IsDefeated, "a PlayerLose means the player is the one at 0 HP");
            }
        }

        /// <summary>
        /// 同じシードなら同じ戦闘になることを検証する (乱数注入の意味の確認)。
        /// Verifies the same seed reproduces the same battle, which is the point of injecting the random source.
        /// </summary>
        private static void CheckSeededBattleIsReproducible()
        {
            var player = MakeCard("再現プレイヤー", ElementType.Water, 90, 24, 12, 18);
            var cpu = MakeCard("再現CPU", ElementType.Wood, 90, 21, 9, 18);

            var first = RunToCompletion(new BattleManager(player, cpu, new System.Random(99)));
            var second = RunToCompletion(new BattleManager(player, cpu, new System.Random(99)));

            bool identical = first.Count == second.Count;
            if (identical)
            {
                for (int i = 0; i < first.Count; i++)
                {
                    if (first[i] != second[i])
                    {
                        identical = false;
                    }
                }
            }

            Check(identical, "two battles with the same seed and same cards must produce an identical log");
        }

        /// <summary>
        /// 硬すぎる両者の戦闘が <see cref="BattleManager.MaxRounds"/> で打ち切られることを検証する。
        /// Verifies the <see cref="BattleManager.MaxRounds"/> cap ends a battle between two absurdly tanky combatants.
        /// </summary>
        private static void CheckMaxRoundsCap()
        {
            // HP最大・攻撃最小・防御最大・同属性なので、1ラウンドの被弾は1ずつ。50ラウンドでは決着しない。
            // Max HP, min attack, max defense, same element: 1 damage per hit, so 50 rounds cannot decide it.
            var player = TankCard("鉄壁プレイヤー", ElementType.Earth, 20);
            var cpu = TankCard("鉄壁CPU", ElementType.Earth, 20);
            var battle = new BattleManager(player, cpu, new System.Random(5));

            int guard = 0;
            while (!battle.IsFinished && guard < BattleManager.MaxRounds + 10)
            {
                battle.AdvanceRound();
                guard++;
            }

            Check(battle.IsFinished, "the MaxRounds cap must terminate a battle that cannot be decided by damage");
            CheckEqual(BattleManager.MaxRounds, guard, "the cap should trigger after exactly MaxRounds resolved rounds");
            CheckEqual(BattleManager.MaxRounds, battle.RoundNumber, "RoundNumber should stop at MaxRounds");
            Check(battle.Outcome != BattleOutcome.InProgress, "a timed-out battle must still report a decisive outcome");
            Check(!battle.Player.IsDefeated && !battle.Cpu.IsDefeated, "a timeout means nobody actually reached 0 HP");
            Check(battle.Outcome == BattleOutcome.PlayerWin, "on an exact HP-ratio tie at timeout the player should win");
            Check(LogContains(battle, "時間切れ"), "the log must say the battle ended on the round cap");
        }

        /// <summary>
        /// 決着後の <c>AdvanceRound</c> が例外を投げず no-op を返すことを検証する。
        /// Verifies calling <c>AdvanceRound</c> after the battle finished returns a no-op instead of throwing.
        /// </summary>
        private static void CheckNoOpAfterFinish()
        {
            var player = MakeCard("とどめプレイヤー", ElementType.Wood, 200, StatBlock.MaxAttack, 0, 50);
            var cpu = MakeCard("うすがみCPU", ElementType.Earth, StatBlock.MinHp, StatBlock.MinAttack, 0, 1);
            var battle = new BattleManager(player, cpu, new System.Random(11));

            battle.AdvanceRound();
            Check(battle.IsFinished, "the battle should already be over after the first round here");

            int hpBefore = battle.Player.CurrentHp;
            int logLinesBefore = battle.Log.Count;
            int roundBefore = battle.RoundNumber;

            var noOp = battle.AdvanceRound();

            Check(noOp.WasNoOp, "AdvanceRound after the battle finished must return a result flagged as a no-op");
            CheckEqual(0, noOp.Attacks.Count, "a no-op round must contain no attacks");
            Check(noOp.BattleFinished, "a no-op round must report the battle as finished");
            CheckEqual(roundBefore, battle.RoundNumber, "a no-op must not advance the round number");
            CheckEqual(hpBefore, battle.Player.CurrentHp, "a no-op must not change any HP");
            CheckEqual(logLinesBefore, battle.Log.Count, "a no-op must not append log lines");
        }

        /// <summary>
        /// CPUデッキのステータス範囲と相手選定ルールを検証する。
        /// Verifies the CPU deck's stat ranges and the opponent selection rule.
        /// </summary>
        private static void CheckCpuDecks()
        {
            CheckEqual(3, CpuDecks.All.Count, "there should be exactly 3 hand-authored CPU opponents");

            var seenElements = new List<ElementType>();
            for (int i = 0; i < CpuDecks.All.Count; i++)
            {
                Card opponent = CpuDecks.All[i];
                Check(opponent != null, "no CPU opponent may be null");
                if (opponent == null)
                {
                    continue;
                }

                Check(!string.IsNullOrEmpty(opponent.Id), "every CPU opponent needs a stable id");
                Check(!string.IsNullOrEmpty(opponent.DisplayName), "every CPU opponent needs a display name");
                Check(!string.IsNullOrEmpty(opponent.FlavorText), "every CPU opponent needs flavor text");
                Check(opponent.Stats.IsWithinRange(), $"CPU opponent '{opponent.DisplayName}' has stats outside the StatBlock ranges: {opponent.Stats}");
                Check(!seenElements.Contains(opponent.Element), "each element should appear exactly once in the CPU deck");
                seenElements.Add(opponent.Element);
            }

            // 選定ルール: プレイヤーが不利になる相手は選ばれない。 / Selection rule: never pick an opponent the player is at a disadvantage against.
            var rng = new System.Random(2024);
            var playerCards = new List<Card>
            {
                MakeCard("木のプレイヤー", ElementType.Wood, 60, 20, 10, 20),
                MakeCard("土のプレイヤー", ElementType.Earth, 60, 20, 10, 20),
                MakeCard("水のプレイヤー", ElementType.Water, 60, 20, 10, 20),
            };

            bool neverDisadvantaged = true;
            for (int p = 0; p < playerCards.Count; p++)
            {
                for (int i = 0; i < 60; i++)
                {
                    Card picked = CpuDecks.PickForPlayerCard(playerCards[p], rng);
                    if (picked == null)
                    {
                        neverDisadvantaged = false;
                        continue;
                    }

                    float playerMultiplier = TypeAdvantage.GetMultiplier(playerCards[p].Element, picked.Element);
                    if (playerMultiplier < TypeAdvantage.NeutralMultiplier)
                    {
                        neverDisadvantaged = false;
                    }
                }
            }

            Check(neverDisadvantaged, "PickForPlayerCard must never return an opponent the player is at a type disadvantage against");
            Check(CpuDecks.PickForPlayerCard(null, rng) != null, "PickForPlayerCard must still return an opponent when the player card is null");
        }

        /// <summary>
        /// null 引数のガードを検証する。 / Verifies the null-argument guards.
        /// </summary>
        private static void CheckNullGuards()
        {
            bool combatantThrew = false;
            try
            {
                new BattleCombatant(null);
            }
            catch (ArgumentNullException)
            {
                combatantThrew = true;
            }

            Check(combatantThrew, "BattleCombatant must throw ArgumentNullException for a null card");

            bool managerThrew = false;
            try
            {
                new BattleManager(null, CpuDecks.All[0]);
            }
            catch (ArgumentNullException)
            {
                managerThrew = true;
            }

            Check(managerThrew, "BattleManager must throw ArgumentNullException for a null player card");
        }

        // -------------------------------------------------------------------------
        // ヘルパー / Helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// 検証用のカードを作る。 / Builds a card for verification purposes.
        /// </summary>
        private static Card MakeCard(string displayName, ElementType element, int hp, int attack, int defense, int speed)
        {
            return new Card(
                $"selftest-{displayName}",
                displayName,
                element,
                Rarity.Common,
                new StatBlock(hp, attack, defense, speed),
                "自己検証用のダミーカード。",
                null,
                DateTime.UtcNow);
        }

        /// <summary>
        /// 1ラウンドでは絶対に倒れない硬いカードを作る。 / Builds a card too tanky to die in a single round.
        /// </summary>
        private static Card TankCard(string displayName, ElementType element, int speed)
        {
            return MakeCard(displayName, element, StatBlock.MaxHp, StatBlock.MinAttack, StatBlock.MaxDefense, speed);
        }

        /// <summary>
        /// 検証用の戦闘状態を作る。 / Builds a combatant for verification purposes.
        /// </summary>
        private static BattleCombatant Combatant(string displayName, ElementType element, int hp, int attack, int defense, int speed)
        {
            return new BattleCombatant(MakeCard(displayName, element, hp, attack, defense, speed));
        }

        /// <summary>
        /// 戦闘を決着まで進め、ログのコピーを返す。 / Runs a battle to completion and returns a copy of its log.
        /// </summary>
        private static List<string> RunToCompletion(BattleManager battle)
        {
            int guard = 0;
            while (!battle.IsFinished && guard < BattleManager.MaxRounds + 10)
            {
                battle.AdvanceRound();
                guard++;
            }

            var copy = new List<string>(battle.Log.Count);
            for (int i = 0; i < battle.Log.Count; i++)
            {
                copy.Add(battle.Log[i]);
            }

            return copy;
        }

        /// <summary>
        /// ログに指定文字列を含む行があるか。 / Whether any log line contains the given text.
        /// </summary>
        private static bool LogContains(BattleManager battle, string fragment)
        {
            for (int i = 0; i < battle.Log.Count; i++)
            {
                if (battle.Log[i] != null && battle.Log[i].Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 検証1件。失敗時は理由を Debug.LogError に出す。
        /// One check; on failure the reason goes to Debug.LogError.
        /// </summary>
        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                passCount++;
                return;
            }

            failCount++;
            Debug.LogError($"Battle self-test FAILED: {message}");
        }

        /// <summary>
        /// 整数の一致を検証する。 / Checks two integers for equality.
        /// </summary>
        private static void CheckEqual(int expected, int actual, string message)
        {
            Check(expected == actual, $"{message} — expected {expected}, got {actual}");
        }

        /// <summary>
        /// 整数が下限以上かを検証する。 / Checks an integer is at least the given minimum.
        /// </summary>
        private static void CheckAtLeast(int minimum, int actual, string message)
        {
            Check(actual >= minimum, $"{message} — expected at least {minimum}, got {actual}");
        }

        /// <summary>
        /// 浮動小数の近似一致を検証する。 / Checks two floats for approximate equality.
        /// </summary>
        private static void CheckApprox(float expected, float actual, string message)
        {
            Check(Mathf.Approximately(expected, actual), $"{message} — expected {expected}, got {actual}");
        }
    }
}
#endif
