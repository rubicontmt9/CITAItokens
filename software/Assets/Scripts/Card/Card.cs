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
        [SerializeField] private string id;
        [SerializeField] private string displayName;
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

        public Card(
            string id,
            string displayName,
            ElementType element,
            Rarity rarity,
            StatBlock stats,
            string flavorText,
            string imagePath,
            DateTime captureTimestampUtc)
        {
            this.id = id;
            this.displayName = displayName;
            this.element = element;
            this.rarity = rarity;
            this.stats = stats;
            this.flavorText = flavorText;
            this.imagePath = imagePath;
            this.captureTimestampUtc = captureTimestampUtc.ToUniversalTime().ToString("o");
        }

        /// <summary>一意なID (GUID)。 / Unique id (GUID).</summary>
        public string Id => id;

        /// <summary>カード名 (AI生成)。 / Card name (AI-generated).</summary>
        public string DisplayName => displayName;

        public ElementType Element => element;

        public Rarity Rarity => rarity;

        public StatBlock Stats => stats;

        /// <summary>フレーバーテキスト (AI生成)。 / Flavor text (AI-generated).</summary>
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
