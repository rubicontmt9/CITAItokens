using System;
using CitaiTokens.Cards;
using UnityEngine;

namespace CitaiTokens.Battle
{
    /// <summary>
    /// 戦闘中の1体を表すラッパー。<see cref="Card"/> は不変なので、可変なHPはここだけが持つ。
    /// Wraps one side of a battle. <see cref="Card"/> is immutable, so mutable HP lives only here.
    /// </summary>
    public sealed class BattleCombatant
    {
        /// <summary>
        /// カードから戦闘用の状態を作る。HPは最大値から開始する。
        /// Creates battle state from a card. HP starts at its maximum.
        /// </summary>
        /// <param name="card">元になるカード。null は不可。 / The backing card; must not be null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="card"/> が null のとき。 / When <paramref name="card"/> is null.</exception>
        public BattleCombatant(Card card)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            Card = card;

            // Hp が 0 以下のカードでも HpRatio がゼロ除算にならないよう、最低1を保証する。
            // Guarantee at least 1 so HpRatio never divides by zero, even for a card with Hp <= 0.
            MaxHp = Mathf.Max(1, card.Stats.Hp);
            CurrentHp = MaxHp;
        }

        /// <summary>元のカード。戦闘中に書き換えてはならない。 / The backing card; never mutated during battle.</summary>
        public Card Card { get; }

        /// <summary>表示名。 / Display name.</summary>
        public string DisplayName => Card.DisplayName;

        /// <summary>属性。相性計算に使う。 / Element, used for type-advantage calculation.</summary>
        public ElementType Element => Card.Element;

        /// <summary>最大HP。 / Maximum HP.</summary>
        public int MaxHp { get; }

        /// <summary>現在HP。0未満にはならない。 / Current HP; never goes below zero.</summary>
        public int CurrentHp { get; private set; }

        /// <summary>戦闘不能かどうか。 / Whether this combatant has been defeated.</summary>
        public bool IsDefeated => CurrentHp <= 0;

        /// <summary>攻撃力。 / Attack stat.</summary>
        public int Attack => Card.Stats.Attack;

        /// <summary>防御力。 / Defense stat.</summary>
        public int Defense => Card.Stats.Defense;

        /// <summary>素早さ。行動順の決定に使う。 / Speed stat, used to decide turn order.</summary>
        public int Speed => Card.Stats.Speed;

        /// <summary>HPバー表示用の残量比 (0.0〜1.0)。 / Remaining HP ratio for HP bars (0.0 to 1.0).</summary>
        public float HpRatio => CurrentHp / (float)MaxHp;

        /// <summary>
        /// ダメージを適用する。HPは0で下止まりし、実際に減ったHP量を返す。
        /// Applies damage. HP is clamped at zero, and the HP actually lost is returned.
        /// </summary>
        /// <param name="amount">与えるダメージ。0以下は無視される。 / Damage to deal; values of zero or less are ignored.</param>
        /// <returns>実際に減少したHP量。 / The amount of HP actually removed.</returns>
        public int ApplyDamage(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int applied = Mathf.Min(amount, CurrentHp);
            CurrentHp -= applied;
            return applied;
        }

        public override string ToString()
        {
            return $"{DisplayName} ({Element}) {CurrentHp}/{MaxHp}";
        }
    }
}
