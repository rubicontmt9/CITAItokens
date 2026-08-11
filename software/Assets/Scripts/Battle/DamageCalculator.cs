using System;
using CitaiTokens.Cards;
using UnityEngine;

namespace CitaiTokens.Battle
{
    /// <summary>
    /// ダメージ計算。乱数は外から渡す設計にして、計算式だけを単体で検証できるようにしている。
    /// Damage maths. Randomness is injected from outside so the formula can be verified on its own.
    /// </summary>
    /// <remarks>
    /// 計算式: baseDamage = max(1, attack - defense / 2) ※ defense / 2 は整数除算。
    /// finalDamage = max(1, round(baseDamage * typeMultiplier * variance))。
    /// Formula: baseDamage = max(1, attack - defense / 2) with integer division for defense / 2;
    /// finalDamage = max(1, round(baseDamage * typeMultiplier * variance)).
    /// </remarks>
    public static class DamageCalculator
    {
        /// <summary>ばらつきの下限。 / Lower bound of the damage variance.</summary>
        public const float MinVariance = 0.9f;

        /// <summary>ばらつきの上限。 / Upper bound of the damage variance.</summary>
        public const float MaxVariance = 1.1f;

        /// <summary>ダメージの下限。戦闘が0ダメージで停滞しないようにする。 / Damage floor, so a battle can never stall on zero damage.</summary>
        public const int MinDamage = 1;

        /// <summary>
        /// 属性倍率とばらつきを掛ける前の基礎ダメージ。防御の割り算は整数除算 (切り捨て)。
        /// Base damage before the type multiplier and variance. The defense division is integer (truncating).
        /// </summary>
        /// <param name="attack">攻撃側の攻撃力。 / The attacker's attack stat.</param>
        /// <param name="defense">防御側の防御力。 / The defender's defense stat.</param>
        /// <returns>1以上の基礎ダメージ。 / Base damage, never less than one.</returns>
        public static int CalculateBaseDamage(int attack, int defense)
        {
            return Mathf.Max(MinDamage, attack - (defense / 2));
        }

        /// <summary>
        /// ばらつきを引数で受け取る決定論的なダメージ計算。テストで検証できる本体。
        /// Deterministic damage calculation with the variance passed in; this is the testable core.
        /// </summary>
        /// <param name="attacker">攻撃側。 / The attacking combatant.</param>
        /// <param name="defender">防御側。 / The defending combatant.</param>
        /// <param name="variance">ばらつき係数。通常は <see cref="MinVariance"/>〜<see cref="MaxVariance"/>。 / Variance factor, normally between <see cref="MinVariance"/> and <see cref="MaxVariance"/>.</param>
        /// <returns>1以上の最終ダメージ。 / Final damage, never less than one.</returns>
        public static int Calculate(BattleCombatant attacker, BattleCombatant defender, float variance)
        {
            if (attacker == null)
            {
                throw new ArgumentNullException(nameof(attacker));
            }

            if (defender == null)
            {
                throw new ArgumentNullException(nameof(defender));
            }

            int baseDamage = CalculateBaseDamage(attacker.Attack, defender.Defense);
            float multiplier = TypeAdvantage.GetMultiplier(attacker.Element, defender.Element);
            int finalDamage = RoundHalfUp(baseDamage * multiplier * variance);
            return Mathf.Max(MinDamage, finalDamage);
        }

        /// <summary>
        /// 0.5 を常に切り上げる丸め。<c>Mathf.RoundToInt</c> は偶数丸め (22.5 → 22) のため、
        /// 調整時に直感と食い違わないよう明示的にこちらを使う。
        /// Rounds halves up. <c>Mathf.RoundToInt</c> rounds half to even (22.5 becomes 22),
        /// which is counter-intuitive when tuning damage numbers, so this is used explicitly instead.
        /// </summary>
        /// <param name="value">丸める値。 / The value to round.</param>
        /// <returns>丸めた整数。 / The rounded integer.</returns>
        public static int RoundHalfUp(float value)
        {
            return (int)Mathf.Floor(value + 0.5f);
        }

        /// <summary>
        /// 乱数からばらつきを引いてダメージを計算する。決定論版に委譲するだけ。
        /// Draws the variance from the random source and delegates to the deterministic overload.
        /// </summary>
        /// <param name="attacker">攻撃側。 / The attacking combatant.</param>
        /// <param name="defender">防御側。 / The defending combatant.</param>
        /// <param name="random">乱数源。シードを固定すれば再現できる。 / Random source; seed it to reproduce a battle.</param>
        /// <returns>1以上の最終ダメージ。 / Final damage, never less than one.</returns>
        public static int Calculate(BattleCombatant attacker, BattleCombatant defender, System.Random random)
        {
            return Calculate(attacker, defender, DrawVariance(random));
        }

        /// <summary>
        /// <see cref="MinVariance"/> 以上 <see cref="MaxVariance"/> 以下の一様乱数を引く。
        /// Draws a uniform random value between <see cref="MinVariance"/> and <see cref="MaxVariance"/>.
        /// </summary>
        /// <param name="random">乱数源。 / The random source.</param>
        /// <returns>ばらつき係数。 / A variance factor.</returns>
        public static float DrawVariance(System.Random random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            return MinVariance + ((float)random.NextDouble() * (MaxVariance - MinVariance));
        }
    }
}
