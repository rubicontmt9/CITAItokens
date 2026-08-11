using System;
using CitaiTokens.Cards;

namespace CitaiTokens.AI
{
    /// <summary>
    /// 特徴量から完成した <see cref="Card"/> を組み立てる。導出は <see cref="PhotoStatDeriver"/> に委ね、ここは
    /// 「言葉」の担当 (カード名とフレーバー) と組み立ての担当。
    /// Assembles a finished <see cref="Card"/> from features. Derivation is delegated to
    /// <see cref="PhotoStatDeriver"/>; this class owns the words — the name and the flavor text — and the assembly.
    /// </summary>
    /// <remarks>
    /// 名前は「接頭辞 × 属性語 × ジャンル語」で組む。属性とジャンルが名前に出るので、水属性の槍は名前だけで
    /// 水っぽい槍だと分かる。組み合わせ数は 24 × 5 × 4 = 480 (属性・ジャンルの組ごと) あり、同じ名前を引く体験は稀。
    /// 選択はすべて写真のハッシュから引くため、同じ写真からは必ず同じ名前が出る。
    ///
    /// Names are built as prefix x element word x genre word, so the element and genre are both visible in the name
    /// — a Water spear reads as a water-ish spear. That is 24 x 5 x 4 = 480 combinations per element/genre pair, so
    /// repeats are rare. Every choice is drawn from the photo hash, so one photo always yields one name.
    /// </remarks>
    public static class PhotoCardComposer
    {
        /// <summary>
        /// 名前の接頭辞。すべて後続の名詞句に自然に繋がる連体修飾にしてある。
        /// Name prefixes, all written so they attach naturally to the noun phrase that follows.
        /// </summary>
        private static readonly string[] NamePrefixes =
        {
            "苔むした",
            "風化した",
            "雨に濡れた",
            "ひび割れた",
            "陽を浴びた",
            "朝霧の",
            "夕暮れの",
            "静かな",
            "うねる",
            "ささやく",
            "凍えた",
            "野ざらしの",
            "名も無き",
            "古びた",
            "艶やかな",
            "くすんだ",
            "軋む",
            "はるかな",
            "土を抱いた",
            "霜の降りた",
            "幾夏を越えた",
            "木漏れ日の",
            "折れざる",
            "眠れる",
        };

        /// <summary>木属性の語。 / Words for the Wood element.</summary>
        private static readonly string[] WoodWords =
        {
            "若葉の",
            "常緑の",
            "青苔の",
            "新芽の",
            "樹霊の",
        };

        /// <summary>土属性の語。 / Words for the Earth element.</summary>
        private static readonly string[] EarthWords =
        {
            "赤土の",
            "岩根の",
            "土塊の",
            "黄土の",
            "砂礫の",
        };

        /// <summary>水属性の語。 / Words for the Water element.</summary>
        private static readonly string[] WaterWords =
        {
            "水面の",
            "雨露の",
            "清流の",
            "渚の",
            "深淵の",
        };

        /// <summary>棍棒の語。 / Words for Club.</summary>
        private static readonly string[] ClubWords = { "棍", "棍棒", "打棒", "金剛棒" };

        /// <summary>槍の語。 / Words for Spear.</summary>
        private static readonly string[] SpearWords = { "槍", "長槍", "穂槍", "投げ槍" };

        /// <summary>杖の語。 / Words for Staff.</summary>
        private static readonly string[] StaffWords = { "杖", "錫杖", "呪杖", "長杖" };

        /// <summary>弓の語。 / Words for Bow.</summary>
        private static readonly string[] BowWords = { "弓", "短弓", "長弓", "弦枝" };

        /// <summary>盾の語。 / Words for Shield.</summary>
        private static readonly string[] ShieldWords = { "盾", "木盾", "樹皮盾", "大盾" };

        /// <summary>短剣の語。 / Words for Dagger.</summary>
        private static readonly string[] DaggerWords = { "短剣", "小刀", "棘剣", "牙刃" };

        /// <summary>木属性のフレーバー前半。 / First clause of the flavor text for Wood.</summary>
        private static readonly string[] WoodFlavors =
        {
            "葉の匂いがまだ抜けない。",
            "春を何度も見送ってきた。",
            "折れても、また芽吹くのだろう。",
        };

        /// <summary>土属性のフレーバー前半。 / First clause of the flavor text for Earth.</summary>
        private static readonly string[] EarthFlavors =
        {
            "握ると乾いた土がこぼれた。",
            "地面の冷たさを覚えている。",
            "根のあった場所が黒く残る。",
        };

        /// <summary>水属性のフレーバー前半。 / First clause of the flavor text for Water.</summary>
        private static readonly string[] WaterFlavors =
        {
            "濡れた面が鈍く光る。",
            "雨の重さを吸っている。",
            "水音の近くに落ちていた。",
        };

        /// <summary>棍棒のフレーバー後半。 / Second clause of the flavor text for Club.</summary>
        private static readonly string[] ClubFlavors =
        {
            "振ると空気が唸る。",
            "重さだけで用が足りる。",
            "片手では持ち上がらない。",
        };

        /// <summary>槍のフレーバー後半。 / Second clause of the flavor text for Spear.</summary>
        private static readonly string[] SpearFlavors =
        {
            "先が思ったより鋭い。",
            "真っ直ぐさに迷いがない。",
            "突けば、それで足りる。",
        };

        /// <summary>杖のフレーバー後半。 / Second clause of the flavor text for Staff.</summary>
        private static readonly string[] StaffFlavors =
        {
            "節のひとつずつに年がある。",
            "掲げると、風が向きを変えた。",
            "分かれた枝先が空を指す。",
        };

        /// <summary>弓のフレーバー後半。 / Second clause of the flavor text for Bow.</summary>
        private static readonly string[] BowFlavors =
        {
            "しなって、また戻る。",
            "弦を張れば鳴きそうだ。",
            "撓むほど強くなる。",
        };

        /// <summary>盾のフレーバー後半。 / Second clause of the flavor text for Shield.</summary>
        private static readonly string[] ShieldFlavors =
        {
            "打たれた跡が幾つも残る。",
            "広い面が身体を隠す。",
            "受けるために生まれた形。",
        };

        /// <summary>短剣のフレーバー後半。 / Second clause of the flavor text for Dagger.</summary>
        private static readonly string[] DaggerFlavors =
        {
            "掌に収まるが、油断はできない。",
            "短いぶんだけ速い。",
            "先端だけが異様に鋭い。",
        };

        /// <summary>接頭辞を選ぶソルト。 / Salt for the prefix choice.</summary>
        private const uint PrefixSalt = 101u;

        /// <summary>属性語を選ぶソルト。 / Salt for the element-word choice.</summary>
        private const uint ElementWordSalt = 211u;

        /// <summary>ジャンル語を選ぶソルト。 / Salt for the genre-word choice.</summary>
        private const uint GenreWordSalt = 307u;

        /// <summary>属性フレーバーを選ぶソルト。 / Salt for the element flavor choice.</summary>
        private const uint ElementFlavorSalt = 401u;

        /// <summary>ジャンルフレーバーを選ぶソルト。 / Salt for the genre flavor choice.</summary>
        private const uint GenreFlavorSalt = 509u;

        /// <summary>
        /// 特徴量とハッシュからカード1枚を組み立てる。同じ入力なら (ID と時刻を除いて) 必ず同じ結果になる。
        /// Builds one card from the features and hash. The same input always yields the same result, apart from the
        /// id and the timestamp.
        /// </summary>
        /// <param name="features">写真の特徴量。 / The photo's features.</param>
        /// <param name="photoHash">写真バイト列のハッシュ。語の選択とレアリティの種。 / Hash of the photo bytes; seeds the word choices and the rarity.</param>
        /// <param name="imagePath">保存済みサムネイルの相対パス。未保存なら null。 / Relative path of the saved thumbnail, or null if not saved yet.</param>
        public static Card Compose(PhotoFeatures features, uint photoHash, string imagePath)
        {
            var genre = PhotoStatDeriver.DeriveGenre(features);
            var element = PhotoStatDeriver.DeriveElement(features);
            var rarity = PhotoStatDeriver.DeriveRarity(features, photoHash);
            var stats = PhotoStatDeriver.DeriveStats(features, genre, element, rarity);

            // ジャンルを受け取る完全なコンストラクタを使う (docs/game-design.md §4.0 の「データモデルへの影響」)。
            // ジャンル無しの旧シグネチャは既定値の棍棒が入ってしまうため、生成側では使ってはいけない。
            // Uses the full constructor that takes the genre (docs/game-design.md §4.0, "Impact on the Data Model").
            // The legacy signature silently substitutes the default Club, so generation code must never use it.
            var card = new Card(
                Guid.NewGuid().ToString(),
                BuildName(genre, element, photoHash),
                genre,
                element,
                rarity,
                stats,
                BuildFlavorText(genre, element, photoHash),
                imagePath,
                DateTime.UtcNow);

            // 重複提出の検出に使えるよう、元写真のハッシュを文字列で残す。
            // Keep the source photo's hash as a string, so duplicate submissions can be detected later.
            card.SourcePhotoHash = photoHash.ToString("x8");

            return card;
        }

        /// <summary>
        /// カード名を組む。「接頭辞 + 属性語 + ジャンル語」で、属性とジャンルが名前から読み取れる形にする。
        /// Builds the card name as prefix + element word + genre word, so both the element and the genre are
        /// readable from the name alone.
        /// </summary>
        /// <param name="genre">武器ジャンル。 / The weapon genre.</param>
        /// <param name="element">属性。 / The element.</param>
        /// <param name="photoHash">写真のハッシュ。語の選択の種。 / The photo hash, seeding the word choices.</param>
        public static string BuildName(WeaponGenre genre, ElementType element, uint photoHash)
        {
            var elementWords = GetElementWords(element);
            var genreWords = GetGenreWords(genre);

            // 3つの選択が連動しないよう、それぞれ別のソルトで混ぜたハッシュから引く。
            // Each of the three choices is drawn from a differently salted mix, so they do not move together.
            var prefix = Pick(NamePrefixes, photoHash, PrefixSalt);
            var elementWord = Pick(elementWords, photoHash, ElementWordSalt);
            var genreWord = Pick(genreWords, photoHash, GenreWordSalt);

            return prefix + elementWord + genreWord;
        }

        /// <summary>
        /// フレーバーテキストを組む。属性の一文とジャンルの一文を並べ、短い二文にする。
        /// Builds the flavor text: one sentence about the element followed by one about the genre.
        /// </summary>
        /// <param name="genre">武器ジャンル。 / The weapon genre.</param>
        /// <param name="element">属性。 / The element.</param>
        /// <param name="photoHash">写真のハッシュ。文の選択の種。 / The photo hash, seeding the sentence choices.</param>
        public static string BuildFlavorText(WeaponGenre genre, ElementType element, uint photoHash)
        {
            var elementFlavor = Pick(GetElementFlavors(element), photoHash, ElementFlavorSalt);
            var genreFlavor = Pick(GetGenreFlavors(genre), photoHash, GenreFlavorSalt);
            return elementFlavor + genreFlavor;
        }

        /// <summary>属性に対応する語の表。 / The word table for an element.</summary>
        private static string[] GetElementWords(ElementType element)
        {
            switch (element)
            {
                case ElementType.Wood:
                    return WoodWords;
                case ElementType.Earth:
                    return EarthWords;
                default:
                    return WaterWords;
            }
        }

        /// <summary>ジャンルに対応する語の表。 / The word table for a genre.</summary>
        private static string[] GetGenreWords(WeaponGenre genre)
        {
            switch (genre)
            {
                case WeaponGenre.Spear:
                    return SpearWords;
                case WeaponGenre.Staff:
                    return StaffWords;
                case WeaponGenre.Bow:
                    return BowWords;
                case WeaponGenre.Shield:
                    return ShieldWords;
                case WeaponGenre.Dagger:
                    return DaggerWords;
                default:
                    return ClubWords;
            }
        }

        /// <summary>属性に対応するフレーバーの表。 / The flavor table for an element.</summary>
        private static string[] GetElementFlavors(ElementType element)
        {
            switch (element)
            {
                case ElementType.Wood:
                    return WoodFlavors;
                case ElementType.Earth:
                    return EarthFlavors;
                default:
                    return WaterFlavors;
            }
        }

        /// <summary>ジャンルに対応するフレーバーの表。 / The flavor table for a genre.</summary>
        private static string[] GetGenreFlavors(WeaponGenre genre)
        {
            switch (genre)
            {
                case WeaponGenre.Spear:
                    return SpearFlavors;
                case WeaponGenre.Staff:
                    return StaffFlavors;
                case WeaponGenre.Bow:
                    return BowFlavors;
                case WeaponGenre.Shield:
                    return ShieldFlavors;
                case WeaponGenre.Dagger:
                    return DaggerFlavors;
                default:
                    return ClubFlavors;
            }
        }

        /// <summary>
        /// ハッシュから表の1要素を決定論的に選ぶ。空の表には never 当たらない前提だが、念のため空文字を返す。
        /// Deterministically picks one entry from a table using the hash. The tables are never empty, but an empty
        /// string is returned if one ever is.
        /// </summary>
        /// <param name="table">選択元の表。 / The table to pick from.</param>
        /// <param name="photoHash">写真のハッシュ。 / The photo hash.</param>
        /// <param name="salt">用途ごとのソルト。 / A per-use salt.</param>
        private static string Pick(string[] table, uint photoHash, uint salt)
        {
            if (table == null || table.Length == 0)
            {
                return string.Empty;
            }

            var mixed = PhotoStatDeriver.Mix(photoHash, salt);
            return table[(int)(mixed % (uint)table.Length)];
        }
    }
}
