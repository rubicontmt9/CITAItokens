using System;
using UnityEngine;

namespace CitaiTokens.Cards
{
    /// <summary>
    /// 撮影した自然物から生成される1枚のカード。永続化される正規のデータ形。
    /// A single card generated from a photographed natural object. This is the canonical persisted shape.
    /// </summary>
    [Serializable]
    public class Card
    {
        /// <summary>
        /// ジャンル未指定で作られたカードのジャンル。enum の既定値 (0) と一致させてある。
        /// The genre used for a card created without one. Kept equal to the enum's default value (0).
        /// </summary>
        /// <remarks>
        /// ジャンル導入前に保存されたカードを読み込むと、この値が入った状態になる。移行の判定に使えるよう公開している。
        /// Cards saved before the genre existed deserialize with this value, so it is public to let migration
        /// code recognise them.
        /// </remarks>
        public const WeaponGenre DefaultWeaponGenre = WeaponGenre.Club;

        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private WeaponGenre weaponGenre;
        [SerializeField] private ElementType element;
        [SerializeField] private Rarity rarity;
        [SerializeField] private StatBlock stats;
        [SerializeField] private string flavorText;
        [SerializeField] private string imagePath;
        [SerializeField] private string captureTimestampUtc;
        [SerializeField] private bool hasLocation;
        [SerializeField] private double latitude;
        [SerializeField] private double longitude;
        [SerializeField] private string sourcePhotoHash;

        /// <summary>Newtonsoft.Json / Unity のシリアライズ用。 / For Newtonsoft.Json and Unity serialization.</summary>
        public Card()
        {
        }

        /// <summary>
        /// 武器ジャンルを指定しない旧シグネチャ。<see cref="DefaultWeaponGenre"/> が入る。
        /// The legacy signature that takes no weapon genre; <see cref="DefaultWeaponGenre"/> is used instead.
        /// </summary>
        /// <remarks>
        /// ジャンル追加前に書かれた呼び出し側 (<c>CpuDecks</c> など) をそのまま通すためだけに残してある。
        /// 新しいコードはジャンルを受け取る方のコンストラクタを使うこと。ジャンルはカードの同一性の一部であり、
        /// 既定値のまま作られたカードは「ジャンル未設定」と見分けが付かない。
        /// This exists only so callers written before the genre was added (such as <c>CpuDecks</c>) keep
        /// compiling. New code must use the constructor that takes a genre: the genre is part of a card's
        /// identity, and a card left on the default is indistinguishable from one whose genre was never set.
        /// </remarks>
        public Card(
            string id,
            string displayName,
            ElementType element,
            Rarity rarity,
            StatBlock stats,
            string flavorText,
            string imagePath,
            DateTime captureTimestampUtc)
            : this(
                id,
                displayName,
                DefaultWeaponGenre,
                element,
                rarity,
                stats,
                flavorText,
                imagePath,
                captureTimestampUtc)
        {
        }

        /// <summary>
        /// 武器ジャンルまで含めた完全なコンストラクタ。新しいコードはこちらを使う。
        /// The full constructor including the weapon genre. This is the one new code should use.
        /// </summary>
        public Card(
            string id,
            string displayName,
            WeaponGenre weaponGenre,
            ElementType element,
            Rarity rarity,
            StatBlock stats,
            string flavorText,
            string imagePath,
            DateTime captureTimestampUtc)
        {
            this.id = id;
            this.displayName = displayName;
            this.weaponGenre = weaponGenre;
            this.element = element;
            this.rarity = rarity;
            this.stats = stats;
            this.flavorText = flavorText;
            this.imagePath = imagePath;
            this.captureTimestampUtc = captureTimestampUtc.ToUniversalTime().ToString("o");
        }

        /// <summary>一意なID (GUID)。 / Unique id (GUID).</summary>
        public string Id => id;

        /// <summary>カード名 (写真から生成)。 / Card name, generated from the photo.</summary>
        public string DisplayName => displayName;

        /// <summary>
        /// 武器ジャンル。ステータスの形を決める主要因で、図鑑の軸のひとつ。
        /// The weapon genre: the main driver of the stat shape and one axis of the collection.
        /// </summary>
        public WeaponGenre WeaponGenre => weaponGenre;

        public ElementType Element => element;

        public Rarity Rarity => rarity;

        public StatBlock Stats => stats;

        /// <summary>フレーバーテキスト (写真から生成)。 / Flavor text, generated from the photo.</summary>
        public string FlavorText => flavorText;

        /// <summary>
        /// 保存済みサムネイル画像への、永続化ディレクトリ基準の相対パス。
        /// Path to the saved thumbnail image, relative to the persistent data directory.
        /// </summary>
        public string ImagePath
        {
            get => imagePath;
            set => imagePath = value;
        }

        /// <summary>撮影時刻 (UTC, ISO 8601)。 / Capture time (UTC, ISO 8601).</summary>
        public string CaptureTimestampUtc => captureTimestampUtc;

        /// <summary>撮影位置を持つか。 / Whether a capture location is recorded.</summary>
        public bool HasLocation => hasLocation;

        public double Latitude => latitude;

        public double Longitude => longitude;

        /// <summary>元写真のハッシュ。重複提出の検出用 (任意)。 / Hash of the source photo, for duplicate detection (optional).</summary>
        public string SourcePhotoHash
        {
            get => sourcePhotoHash;
            set => sourcePhotoHash = value;
        }

        /// <summary>
        /// 撮影時刻をパースして返す。不正な値の場合は <see cref="DateTime.MinValue"/>。
        /// Parses the capture time; returns <see cref="DateTime.MinValue"/> when the stored value is invalid.
        /// </summary>
        public DateTime GetCaptureTimeUtc()
        {
            return DateTime.TryParse(
                captureTimestampUtc,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : DateTime.MinValue;
        }

        public void SetLocation(double latitudeValue, double longitudeValue)
        {
            latitude = latitudeValue;
            longitude = longitudeValue;
            hasLocation = true;
        }

        public void ClearLocation()
        {
            latitude = 0d;
            longitude = 0d;
            hasLocation = false;
        }
    }
}
