namespace CitaiTokens.Cards
{
    /// <summary>
    /// 武器のジャンル。撮った枝の見た目から決まり、ステータスに大きな補正をかける。
    /// The weapon genre. Derived from how the photographed branch looks, and the main driver of stat shape.
    /// </summary>
    /// <remarks>
    /// 属性(<see cref="ElementType"/>)は戦闘の相性倍率でも効くため、ステータス補正は
    /// ジャンルを主、属性を従とする。詳細は docs/game-design.md の 4.0 を参照。
    /// The element also drives the battle multipliers, so the genre is the primary source of stat
    /// modifiers and the element only nudges them. See docs/game-design.md §4.0.
    /// </remarks>
    public enum WeaponGenre
    {
        /// <summary>棍棒。太く短い。HP・攻撃が高く、速さが低い。 / Club: thick and short. High HP and attack, low speed.</summary>
        Club = 0,

        /// <summary>槍。細長く真っ直ぐ。攻撃・速さが高く、防御が低い。 / Spear: long and straight. High attack and speed, low defense.</summary>
        Spear = 1,

        /// <summary>杖。分岐が多く節がある。防御・HPが高く、攻撃が低い。 / Staff: forked and gnarled. High defense and HP, low attack.</summary>
        Staff = 2,

        /// <summary>弓。湾曲してしなやか。速さ・攻撃が高く、HPが低い。 / Bow: curved and supple. High speed and attack, low HP.</summary>
        Bow = 3,

        /// <summary>盾。平たく幅が広い。防御・HPが高く、攻撃・速さが低い。 / Shield: flat and broad. High defense and HP, low attack and speed.</summary>
        Shield = 4,

        /// <summary>短剣。小さく鋭い。速さが非常に高く、HP・防御が低い。 / Dagger: small and sharp. Very high speed, low HP and defense.</summary>
        Dagger = 5,
    }
}
