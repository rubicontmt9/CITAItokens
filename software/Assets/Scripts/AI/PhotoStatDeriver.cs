using CitaiTokens.Cards;
using UnityEngine;

namespace CitaiTokens.AI
{
    /// <summary>
    /// 特徴量からカードの中身 (ジャンル・属性・レアリティ・ステータス) を決める。ここの数値がゲームの面白さを決める。
    /// Turns features into the card's content (genre, element, rarity, stats). These numbers decide whether the
    /// game is fun.
    /// </summary>
    /// <remarks>
    /// 方針:
    ///   ・同じ写真からは必ず同じカードが出る。時刻もシードなしの乱数も使わない (再撮影による粘りを封じる)。
    ///   ・違う写真は目に見えて違うカードになる。現実の再現度より「ばらけること」を優先する。
    ///   ・調整できるように、係数はすべて名前付き定数として置き、式の中に生の数値を埋めない。
    ///
    /// Policy: the same photo must always produce the same card — no clock, no unseeded random, so re-shooting one
    /// photo cannot be used to fish for a better roll. Different photos must produce visibly different cards;
    /// spread matters more than realism. Every coefficient is a named constant so it can actually be tuned.
    /// </remarks>
    public static class PhotoStatDeriver
    {
        /// <summary>FNV-1a 32bit のオフセット基底。 / FNV-1a 32-bit offset basis.</summary>
        private const uint FnvOffsetBasis = 2166136261u;

        /// <summary>FNV-1a 32bit の素数。 / FNV-1a 32-bit prime.</summary>
        private const uint FnvPrime = 16777619u;

        // ---- ジャンル判定の重み / Genre scoring weights -------------------------------------------------
        // 各ジャンルのスコアは「0〜1 の項の重み付き平均」として求め、必ず 0〜1 に収まるようにしている。
        // こうしないと項の数が多いジャンルが常勝してしまう。
        // Each genre score is a weighted average of 0–1 terms, so every score lands in 0–1. Without this, a genre
        // with more terms would always win.

        /// <summary>槍: 方向の揃い方をどれだけ重く見るか。 / Spear: weight on edge alignment.</summary>
        public const float SpearDirectionalityWeight = 1.30f;

        /// <summary>槍: エッジが少ない (単純な形) ことをどれだけ重く見るか。 / Spear: weight on being simple (few edges).</summary>
        public const float SpearSimplicityWeight = 0.70f;

        /// <summary>杖: エッジの多さ (分岐・節) の重み。 / Staff: weight on edge quantity (forks, knots).</summary>
        public const float StaffBusynessWeight = 1.30f;

        /// <summary>杖: 方向が散らばっていることの重み。 / Staff: weight on scattered edge directions.</summary>
        public const float StaffScatterWeight = 0.70f;

        /// <summary>棍棒: 暗い塊の大きさ (太さの代理) の重み。 / Club: weight on dark mass, the thickness proxy.</summary>
        public const float ClubMassWeight = 1.10f;

        /// <summary>棍棒: エッジが少ないことの重み。 / Club: weight on having few edges.</summary>
        public const float ClubSimplicityWeight = 0.70f;

        /// <summary>棍棒: コントラストの重み。盾と分けるための項 (棍棒はメリハリがある側)。 / Club: contrast weight, the term that separates it from Shield.</summary>
        public const float ClubContrastWeight = 0.40f;

        /// <summary>盾: 暗い塊の大きさの重み。 / Shield: weight on dark mass.</summary>
        public const float ShieldMassWeight = 1.10f;

        /// <summary>盾: エッジが極端に少ないことの重み。 / Shield: weight on having very few edges.</summary>
        public const float ShieldFlatnessWeight = 0.90f;

        /// <summary>盾: コントラストが低いことの重み (平たい面は陰影が乏しい)。 / Shield: weight on low contrast, as a flat face has little shading.</summary>
        public const float ShieldLowContrastWeight = 0.60f;

        /// <summary>短剣: 暗い面積が小さいことの重み。 / Dagger: weight on a small dark area.</summary>
        public const float DaggerSmallnessWeight = 0.90f;

        /// <summary>短剣: コントラストの高さ (鋭さ) の重み。 / Dagger: weight on high contrast, read as sharpness.</summary>
        public const float DaggerContrastWeight = 0.90f;

        /// <summary>短剣: エッジの鋭さ (量) の重み。 / Dagger: weight on edge quantity, read as crispness.</summary>
        public const float DaggerEdgeWeight = 0.50f;

        /// <summary>弓: 方向が「中くらい」であることの重み。 / Bow: weight on middling directionality.</summary>
        public const float BowDirectionalityWeight = 1.20f;

        /// <summary>弓: エッジ量が「中くらい」であることの重み。 / Bow: weight on a moderate edge count.</summary>
        public const float BowEdgeWeight = 0.80f;

        /// <summary>弓: 理想的な方向の揃い方 (真っ直ぐでも散らばりでもない、しなやかな湾曲)。 / Bow: ideal directionality — neither straight nor scattered, a supple curve.</summary>
        public const float BowIdealDirectionality = 0.55f;

        /// <summary>弓: 理想的なエッジ量。 / Bow: ideal edge density.</summary>
        public const float BowIdealEdgeDensity = 0.45f;

        /// <summary>弓: 理想値からの許容幅。これ以上離れるとスコア 0。 / Bow: tolerance around the ideal; beyond this the term scores 0.</summary>
        public const float BowTolerance = 0.35f;

        // ---- レアリティ / Rarity ----------------------------------------------------------------------

        /// <summary>レアリティ: 彩度 (鮮やかで珍しい個体) の重み。 / Rarity: weight on saturation — a vivid, unusual specimen.</summary>
        public const float RaritySaturationWeight = 0.40f;

        /// <summary>レアリティ: コントラストの重み。 / Rarity: weight on contrast.</summary>
        public const float RarityContrastWeight = 0.35f;

        /// <summary>レアリティ: エッジ量 (込み入った個体) の重み。 / Rarity: weight on edge quantity — a busy specimen.</summary>
        public const float RarityEdgeWeight = 0.25f;

        /// <summary>
        /// レアリティに乗せるハッシュ由来の揺らぎの振幅 (±)。特徴量が似た2枚が同じ段に固定されないようにする。
        /// 特徴量の寄与 (0〜1) より明確に小さく保つこと。
        /// Amplitude (±) of the hash-derived jitter on rarity, so two photos with similar features are not locked
        /// to the same tier. Kept clearly smaller than the feature contribution, which spans 0–1.
        /// </summary>
        public const float RarityHashJitter = 0.12f;

        /// <summary>「上」の下限スコア。 / Score floor for Uncommon.</summary>
        public const float UncommonScoreThreshold = 0.55f;

        /// <summary>「希少」の下限スコア。 / Score floor for Rare.</summary>
        public const float RareScoreThreshold = 0.70f;

        /// <summary>「極」の下限スコア。 / Score floor for Epic.</summary>
        public const float EpicScoreThreshold = 0.85f;

        // ---- 属性 / Element ---------------------------------------------------------------------------

        /// <summary>
        /// この差以下は同点と見なす (1画素の揺れで属性が反転しないようにするための目安)。
        /// Differences at or below this count as a tie, so a single pixel cannot flip the element.
        /// </summary>
        public const float ElementTieEpsilon = 0.02f;

        // ---- ステータスの基準値 / Stat baselines ------------------------------------------------------
        // 各レンジ (HP 20-200 / ATK 5-60 / DEF 0-40 / SPD 1-50) の中央よりやや下に置き、
        // ジャンル倍率・レアリティ・特徴量の変動を掛けても上限に張り付きにくいようにしている。
        // Placed a little below the middle of each range so genre, rarity and feature variation can multiply
        // upward without everything pinning to the maximum.

        /// <summary>HP の基準値。 / Baseline HP.</summary>
        public const float BaseHp = 90f;

        /// <summary>攻撃の基準値。 / Baseline attack.</summary>
        public const float BaseAttack = 26f;

        /// <summary>防御の基準値。 / Baseline defense.</summary>
        public const float BaseDefense = 14f;

        /// <summary>速さの基準値。 / Baseline speed.</summary>
        public const float BaseSpeed = 24f;

        /// <summary>
        /// 属性によるステータス補正の幅。ジャンル倍率が 0.55〜1.65 なのに対し、こちらは意図的に 1 割未満。
        /// 属性は戦闘の相性倍率 (×1.5 / ×0.67) で既に強く効いているため、ここで大きく足すと二重取りになる。
        /// docs/game-design.md §4.0 の「属性の補正」に従い、方向づけ程度に留める。
        /// Size of the element nudge. Genre multipliers span 0.55–1.65; this is deliberately under 10%. The element
        /// already swings battles hard through the ×1.5 / ×0.67 type multipliers, so a large stat bonus on top
        /// would double-dip. Per docs/game-design.md §4.0, the element only points the stats in a direction.
        /// </summary>
        public const float ElementNudge = 0.08f;

        /// <summary>
        /// レアリティ1段ごとの倍率の増分。並 1.00 / 上 1.08 / 希少 1.16 / 極 1.24。
        /// Multiplier added per rarity tier: 1.00 / 1.08 / 1.16 / 1.24.
        /// </summary>
        public const float RarityStepMultiplier = 0.08f;

        /// <summary>
        /// 特徴量によるジャンル内の変動幅 (±)。同じ槍でも写真ごとに数値が変わるようにするための項。
        /// Amplitude (±) of the within-profile variation driven by features, so two Spears from different photos
        /// do not come out identical.
        /// </summary>
        public const float StatVariationRange = 0.20f;

        /// <summary>
        /// FNV-1a (32bit) でバイト列をハッシュする。名前生成とレアリティが同じ決定論的な種を共有できるようにする。
        /// Hashes bytes with FNV-1a (32-bit) so name generation and rarity share one deterministic source.
        /// </summary>
        /// <param name="bytes">対象のバイト列。null のときは基底値を返す。 / The bytes; returns the offset basis when null.</param>
        public static uint ComputeHash(byte[] bytes)
        {
            var hash = FnvOffsetBasis;
            if (bytes == null)
            {
                return hash;
            }

            unchecked
            {
                for (var i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
            }

            return hash;
        }

        /// <summary>
        /// ハッシュとソルトを混ぜて別の擬似乱数値を作る。1つのハッシュから独立した複数の選択を取り出すために使う。
        /// Mixes a hash with a salt to produce another pseudo-random value, so several independent choices can be
        /// drawn from a single hash.
        /// </summary>
        /// <param name="hash">元のハッシュ。 / The source hash.</param>
        /// <param name="salt">用途ごとに変える値。 / A per-use salt.</param>
        public static uint Mix(uint hash, uint salt)
        {
            unchecked
            {
                var x = hash ^ (salt * 2654435761u);
                x ^= x >> 15;
                x *= 2246822519u;
                x ^= x >> 13;
                x *= 3266489917u;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>
        /// 6ジャンルすべてを採点し、最も高いものを返す。同点のときは列挙の若い順で決着させる (決定論)。
        /// Scores all six genres and returns the highest. Ties break toward the lower enum value, deterministically.
        /// </summary>
        /// <param name="f">写真の特徴量。 / The photo's features.</param>
        public static WeaponGenre DeriveGenre(PhotoFeatures f)
        {
            var bestGenre = WeaponGenre.Club;
            var bestScore = float.MinValue;

            // 列挙値の昇順に評価する。厳密な > 比較なので、同点なら先に見た (若い) ジャンルが残る。
            // Evaluated in ascending enum order with a strict >, so on a tie the earlier genre survives.
            for (var i = 0; i <= (int)WeaponGenre.Dagger; i++)
            {
                var genre = (WeaponGenre)i;
                var score = ScoreGenre(genre, f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestGenre = genre;
                }
            }

            return bestGenre;
        }

        /// <summary>
        /// 1ジャンル分のスコア (0〜1)。代理指標の読み方は docs/game-design.md §4.0 の表に沿わせている。
        /// Score for one genre, 0–1. The reading of each proxy follows the table in docs/game-design.md §4.0.
        /// </summary>
        /// <param name="genre">採点するジャンル。 / The genre being scored.</param>
        /// <param name="f">写真の特徴量。 / The photo's features.</param>
        public static float ScoreGenre(WeaponGenre genre, PhotoFeatures f)
        {
            switch (genre)
            {
                case WeaponGenre.Spear:
                    // 真っ直ぐで単純: 方向が揃っていて、エッジが少ない。
                    // Straight and simple: aligned edges, few of them.
                    return WeightedAverage(
                        SpearDirectionalityWeight,
                        f.EdgeDirectionality,
                        SpearSimplicityWeight,
                        1f - f.EdgeDensity);

                case WeaponGenre.Staff:
                    // 込み入って散らばっている: エッジが多く、方向が揃っていない。
                    // Busy and scattered: many edges, poorly aligned.
                    return WeightedAverage(
                        StaffBusynessWeight,
                        f.EdgeDensity,
                        StaffScatterWeight,
                        1f - f.EdgeDirectionality);

                case WeaponGenre.Club:
                    // 大きな暗い塊で、エッジが少なく、それでも陰影がある (= 立体的に太い)。
                    // A large dark mass with few edges but still some shading, read as three-dimensional bulk.
                    return WeightedAverage(
                        ClubMassWeight,
                        f.DarkRatio,
                        ClubSimplicityWeight,
                        1f - f.EdgeDensity,
                        ClubContrastWeight,
                        f.Contrast);

                case WeaponGenre.Shield:
                    // 大きな暗い塊で、コントラストが低く、エッジが極端に少ない (= 平たい面)。
                    // A large dark mass, low contrast, very few edges: a flat broad face.
                    return WeightedAverage(
                        ShieldMassWeight,
                        f.DarkRatio,
                        ShieldFlatnessWeight,
                        1f - f.EdgeDensity,
                        ShieldLowContrastWeight,
                        1f - f.Contrast);

                case WeaponGenre.Dagger:
                    // 暗い面積が小さく、コントラストが高く、エッジが立っている (= 小さくて鋭い)。
                    // A small dark area, high contrast, crisp edges: small and sharp.
                    return WeightedAverage(
                        DaggerSmallnessWeight,
                        1f - f.DarkRatio,
                        DaggerContrastWeight,
                        f.Contrast,
                        DaggerEdgeWeight,
                        f.EdgeDensity);

                default:
                    // 弓: 方向もエッジ量も中くらい。極端でない写真の受け皿になる。
                    // Bow: middling on both directionality and edges; it catches the un-extreme photos.
                    return WeightedAverage(
                        BowDirectionalityWeight,
                        Tent(f.EdgeDirectionality, BowIdealDirectionality, BowTolerance),
                        BowEdgeWeight,
                        Tent(f.EdgeDensity, BowIdealEdgeDensity, BowTolerance));
            }
        }

        /// <summary>
        /// レアリティを決める。珍しい個体 (鮮やか・メリハリがある・込み入っている) が上に来る。
        /// Derives the rarity: an unusual specimen — vivid, high contrast, busy — lands higher.
        /// </summary>
        /// <param name="f">写真の特徴量。 / The photo's features.</param>
        /// <param name="photoHash">写真バイト列のハッシュ。揺らぎの種。 / Hash of the photo bytes, used as the jitter seed.</param>
        /// <remarks>
        /// 狙っている分布は docs/game-design.md §4.1 の目安に合わせた 並 55% / 上 28% / 希少 13% / 極 4%。
        /// ただし実写の彩度・コントラストの分布が分からないため、しきい値は未調整。実写を数十枚集めて
        /// スコアのヒストグラムを見てから引き直す必要がある。特に「極」が出過ぎないことは必ず確認すること。
        ///
        /// The intended distribution is 55/28/13/4 (Common/Uncommon/Rare/Epic), matching the guide numbers in
        /// docs/game-design.md §4.1. The thresholds are untuned: the real distribution of saturation and contrast
        /// in outdoor photos is unknown, so they must be re-derived from a histogram over a few dozen real photos.
        /// Verify above all that Epic stays genuinely rare.
        /// </remarks>
        public static Rarity DeriveRarity(PhotoFeatures f, uint photoHash)
        {
            var featureScore = WeightedAverage(
                RaritySaturationWeight,
                f.MeanSaturation,
                RarityContrastWeight,
                f.Contrast,
                RarityEdgeWeight,
                f.EdgeDensity);

            // ハッシュから -1〜1 の値を作り、振幅を掛けて足す。特徴量の寄与が主で、これは従。
            // Build a -1..1 value from the hash and scale it. The feature score dominates; this only nudges.
            var unit = UnitFromHash(Mix(photoHash, 0x9E3779B9u));
            var score = featureScore + (((unit * 2f) - 1f) * RarityHashJitter);

            if (score >= EpicScoreThreshold)
            {
                return Rarity.Epic;
            }

            if (score >= RareScoreThreshold)
            {
                return Rarity.Rare;
            }

            if (score >= UncommonScoreThreshold)
            {
                return Rarity.Uncommon;
            }

            return Rarity.Common;
        }

        /// <summary>
        /// 色の割合が最大のものを属性にする。緑→木、茶→土、青灰→水。
        /// The largest colour ratio wins: green to Wood, brown to Earth, blue/grey to Water.
        /// </summary>
        /// <param name="f">写真の特徴量。 / The photo's features.</param>
        /// <remarks>
        /// 差が <see cref="ElementTieEpsilon"/> 以下しかない「ほぼ同点」でも、比較順を 木 → 土 → 水 に固定しているため
        /// 結果は必ず一意に決まる。1画素の揺れで属性が反転しないことが重要なので、乱数やハッシュで割らない。
        /// Even on a near-tie within <see cref="ElementTieEpsilon"/>, the fixed comparison order (Wood, then Earth,
        /// then Water) makes the outcome unique. Nothing random or hash-based is used, because the element must not
        /// flip on a single pixel's worth of difference.
        /// </remarks>
        public static ElementType DeriveElement(PhotoFeatures f)
        {
            if (f.GreenRatio >= f.BrownRatio && f.GreenRatio >= f.BlueGrayRatio)
            {
                return ElementType.Wood;
            }

            if (f.BrownRatio >= f.BlueGrayRatio)
            {
                return ElementType.Earth;
            }

            return ElementType.Water;
        }

        /// <summary>
        /// ジャンル・属性・レアリティ・特徴量からステータスを組み立てる。戻り値は必ず <see cref="StatBlock.Clamped"/> を通す。
        /// Builds the stats from genre, element, rarity and features. The result always goes through
        /// <see cref="StatBlock.Clamped"/>.
        /// </summary>
        /// <param name="f">写真の特徴量。 / The photo's features.</param>
        /// <param name="genre">武器ジャンル (補正は大)。 / The weapon genre, the dominant modifier.</param>
        /// <param name="element">属性 (補正は小)。 / The element, a minor modifier.</param>
        /// <param name="rarity">レアリティ。 / The rarity tier.</param>
        /// <remarks>
        /// ⏸️ バランス調整は保留と決まっている (docs/game-design.md §4.1)。ステータス総量の予算制は入れていないので、
        /// 「攻撃が高く HP も高い」「1撃で終わる」といった極端な組み合わせは今も生成されうる。意図した修正案と
        /// その換算式は §4.1 に記録済み。当面は 50 ラウンドの打ち切り上限があるためゲームが停止することはない。
        ///
        /// Balance tuning is deliberately deferred (docs/game-design.md §4.1). There is no stat-budget system here,
        /// so extreme combinations — high attack alongside high HP, or a one-hit kill — remain possible. The
        /// intended fix and its point formula are recorded in §4.1. The 50-round cap keeps the game from stalling
        /// in the meantime.
        /// </remarks>
        public static StatBlock DeriveStats(
            PhotoFeatures f,
            WeaponGenre genre,
            ElementType element,
            Rarity rarity)
        {
            float hpMultiplier;
            float attackMultiplier;
            float defenseMultiplier;
            float speedMultiplier;
            GetGenreProfile(
                genre,
                out hpMultiplier,
                out attackMultiplier,
                out defenseMultiplier,
                out speedMultiplier);

            // 属性の方向づけ。木は粘る (HP)、土は硬い (防御)、水は速い (速さ)。1項目だけに小さく乗せる。
            // The element's direction: Wood endures (HP), Earth is hard (defense), Water is quick (speed).
            // Applied to a single stat, and small.
            switch (element)
            {
                case ElementType.Wood:
                    hpMultiplier *= 1f + ElementNudge;
                    break;
                case ElementType.Earth:
                    defenseMultiplier *= 1f + ElementNudge;
                    break;
                default:
                    speedMultiplier *= 1f + ElementNudge;
                    break;
            }

            var rarityMultiplier = 1f + ((int)rarity * RarityStepMultiplier);

            // 同じジャンル内でも写真ごとに違う数値になるよう、特徴量を4項目に割り当てて変動させる。
            // どの特徴量をどのステータスに当てるかは「見た目の納得感」で選んだもので、根拠は経験的。
            // Feature-driven variation so two cards of the same genre differ. Which feature drives which stat was
            // chosen for narrative plausibility, not from evidence.
            //   HP    ← 暗い塊の大きさ (太く見えるほど頑丈)          / dark mass: looks bulkier, so tougher
            //   ATK   ← コントラスト (鋭く見えるほど強く当たる)       / contrast: looks sharper, so hits harder
            //   DEF   ← 彩度 (色が濃い個体は詰まった木と見なす)        / saturation: richer colour reads as denser wood
            //   SPD   ← 方向の揃い方 (真っ直ぐなほど振りが速い)        / directionality: straighter swings faster
            var hp = BaseHp * hpMultiplier * rarityMultiplier * Variation(f.DarkRatio);
            var attack = BaseAttack * attackMultiplier * rarityMultiplier * Variation(f.Contrast);
            var defense = BaseDefense * defenseMultiplier * rarityMultiplier * Variation(f.MeanSaturation);
            var speed = BaseSpeed * speedMultiplier * rarityMultiplier * Variation(f.EdgeDirectionality);

            return new StatBlock(
                Mathf.RoundToInt(hp),
                Mathf.RoundToInt(attack),
                Mathf.RoundToInt(defense),
                Mathf.RoundToInt(speed)).Clamped();
        }

        /// <summary>
        /// ジャンルごとの4倍率。方向は <see cref="WeaponGenre"/> のドキュメントと docs/game-design.md §4.0 の表に一致させている。
        /// The four multipliers per genre. Their directions match the <see cref="WeaponGenre"/> doc comments and the
        /// table in docs/game-design.md §4.0.
        /// </summary>
        /// <param name="genre">武器ジャンル。 / The weapon genre.</param>
        /// <param name="hp">HP の倍率。 / HP multiplier.</param>
        /// <param name="attack">攻撃の倍率。 / Attack multiplier.</param>
        /// <param name="defense">防御の倍率。 / Defense multiplier.</param>
        /// <param name="speed">速さの倍率。 / Speed multiplier.</param>
        public static void GetGenreProfile(
            WeaponGenre genre,
            out float hp,
            out float attack,
            out float defense,
            out float speed)
        {
            switch (genre)
            {
                case WeaponGenre.Spear:
                    // 攻撃・速さが高く、防御が低い。 / High attack and speed, low defense.
                    hp = 0.95f;
                    attack = 1.25f;
                    defense = 0.60f;
                    speed = 1.20f;
                    return;

                case WeaponGenre.Staff:
                    // 防御・HP が高く、攻撃が低い。 / High defense and HP, low attack.
                    hp = 1.20f;
                    attack = 0.75f;
                    defense = 1.45f;
                    speed = 0.95f;
                    return;

                case WeaponGenre.Bow:
                    // 速さ・攻撃が高く、HP が低い。 / High speed and attack, low HP.
                    hp = 0.75f;
                    attack = 1.15f;
                    defense = 0.85f;
                    speed = 1.40f;
                    return;

                case WeaponGenre.Shield:
                    // 防御・HP が高く、攻撃・速さが低い。 / High defense and HP, low attack and speed.
                    hp = 1.40f;
                    attack = 0.65f;
                    defense = 1.60f;
                    speed = 0.60f;
                    return;

                case WeaponGenre.Dagger:
                    // 速さが非常に高く、HP・防御が低い。 / Very high speed, low HP and defense.
                    hp = 0.65f;
                    attack = 1.10f;
                    defense = 0.55f;
                    speed = 1.65f;
                    return;

                default:
                    // 棍棒: HP・攻撃が高く、速さが低い。 / Club: high HP and attack, low speed.
                    hp = 1.35f;
                    attack = 1.20f;
                    defense = 1.00f;
                    speed = 0.65f;
                    return;
            }
        }

        /// <summary>
        /// 0〜1 の特徴量を <see cref="StatVariationRange"/> の範囲の倍率に写す (0.5 で 1.0 倍)。
        /// Maps a 0–1 feature onto a multiplier within <see cref="StatVariationRange"/>; 0.5 maps to 1.0.
        /// </summary>
        /// <param name="feature">0〜1 の特徴量。 / A 0–1 feature value.</param>
        private static float Variation(float feature)
        {
            return 1f + ((Mathf.Clamp01(feature) - 0.5f) * 2f * StatVariationRange);
        }

        /// <summary>
        /// 理想値からの距離で 1 → 0 に落ちる三角関数状の評価 (中くらいを好む項に使う)。
        /// A tent function falling from 1 to 0 with distance from the ideal, used by terms that prefer a middle value.
        /// </summary>
        /// <param name="value">評価する値。 / The value to score.</param>
        /// <param name="ideal">最高点になる値。 / The value that scores 1.</param>
        /// <param name="tolerance">0 点になるまでの幅。 / Distance at which the score reaches 0.</param>
        private static float Tent(float value, float ideal, float tolerance)
        {
            if (tolerance <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(1f - (Mathf.Abs(value - ideal) / tolerance));
        }

        /// <summary>
        /// 重み付き平均 (2項)。重みの合計で割るのでスコアは必ず 0〜1 に収まる。
        /// Weighted average of two terms; dividing by the total weight keeps the score inside 0–1.
        /// </summary>
        private static float WeightedAverage(float weightA, float valueA, float weightB, float valueB)
        {
            var total = weightA + weightB;
            if (total <= 0f)
            {
                return 0f;
            }

            return ((weightA * valueA) + (weightB * valueB)) / total;
        }

        /// <summary>
        /// 重み付き平均 (3項)。 / Weighted average of three terms.
        /// </summary>
        private static float WeightedAverage(
            float weightA,
            float valueA,
            float weightB,
            float valueB,
            float weightC,
            float valueC)
        {
            var total = weightA + weightB + weightC;
            if (total <= 0f)
            {
                return 0f;
            }

            return ((weightA * valueA) + (weightB * valueB) + (weightC * valueC)) / total;
        }

        /// <summary>
        /// ハッシュ値を 0〜1 の実数に写す。 / Maps a hash value onto a 0–1 float.
        /// </summary>
        /// <param name="hash">混合済みのハッシュ。 / A mixed hash value.</param>
        private static float UnitFromHash(uint hash)
        {
            return (hash % 10000u) / 9999f;
        }
    }
}
