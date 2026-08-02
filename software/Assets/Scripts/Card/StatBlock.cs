using System;
using UnityEngine;

namespace CitaiTokens.Cards
{
    /// <summary>
    /// カードの戦闘ステータス。生成された値は必ず <see cref="Clamped"/> を通して正規化する。
    /// Battle stats for a card. Generated values must always be normalized through <see cref="Clamped"/>.
    /// </summary>
    [Serializable]
    public struct StatBlock
    {
        public const int MinHp = 20;
        public const int MaxHp = 200;
        public const int MinAttack = 5;
        public const int MaxAttack = 60;
        public const int MinDefense = 0;
        public const int MaxDefense = 40;
        public const int MinSpeed = 1;
        public const int MaxSpeed = 50;

        [SerializeField] private int hp;
        [SerializeField] private int attack;
        [SerializeField] private int defense;
        [SerializeField] private int speed;

        public StatBlock(int hp, int attack, int defense, int speed)
        {
            this.hp = hp;
            this.attack = attack;
            this.defense = defense;
            this.speed = speed;
        }

        public int Hp => hp;
        public int Attack => attack;
        public int Defense => defense;
        public int Speed => speed;

        /// <summary>
        /// 各値を許容範囲に収めた新しい <see cref="StatBlock"/> を返す。
        /// Returns a new <see cref="StatBlock"/> with every value clamped into its allowed range.
        /// </summary>
        public StatBlock Clamped()
        {
            return new StatBlock(
                Mathf.Clamp(hp, MinHp, MaxHp),
                Mathf.Clamp(attack, MinAttack, MaxAttack),
                Mathf.Clamp(defense, MinDefense, MaxDefense),
                Mathf.Clamp(speed, MinSpeed, MaxSpeed));
        }

        /// <summary>
        /// すべての値が許容範囲内かどうか。 / Whether every value is already within range.
        /// </summary>
        public bool IsWithinRange()
        {
            return hp >= MinHp && hp <= MaxHp
                && attack >= MinAttack && attack <= MaxAttack
                && defense >= MinDefense && defense <= MaxDefense
                && speed >= MinSpeed && speed <= MaxSpeed;
        }

        public override string ToString()
        {
            return $"HP {hp} / ATK {attack} / DEF {defense} / SPD {speed}";
        }
    }
}
