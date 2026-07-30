using System;
using CitaiTokens.Cards;
using Newtonsoft.Json;
using UnityEngine;

namespace CitaiTokens.AI
{
    /// <summary>
    /// カード生成プロキシの成功レスポンス。プロキシ側と共有する固定のワイヤ形式であり、勝手に変えてはならない。
    /// AIの出力は信用せず、<see cref="TryToCard"/> がここで一括して検証・正規化する。
    /// The success response from the card-generation proxy. This is a fixed wire format shared with the proxy
    /// and must not be redesigned. The model's output is not trusted: <see cref="TryToCard"/> is the single
    /// seam where it is validated and normalized.
    /// </summary>
    [Serializable]
    public sealed class CardProxyResponse
    {
        /// <summary>名前が空だったときの代替名。見た目の問題でプレイヤーを行き止まりにしないための保険。 / Fallback name when the model returns none, so cosmetics never dead-end the player.</summary>
        public const string FallbackName = "名もなき自然物";

        /// <summary>カード名の最大文字数。 / Maximum length of a card name.</summary>
        public const int MaxNameLength = 24;

        /// <summary>フレーバーテキストの最大文字数。 / Maximum length of the flavor text.</summary>
        public const int MaxFlavorTextLength = 80;

        /// <summary>AIが付けたカード名。 / Card name assigned by the AI.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>属性名の文字列 ("Wood" / "Earth" / "Water")。 / Element name as a string ("Wood" / "Earth" / "Water").</summary>
        [JsonProperty("element")]
        public string Element { get; set; }

        /// <summary>レアリティ名の文字列 ("Common" / "Uncommon" / "Rare" / "Epic")。 / Rarity name as a string.</summary>
        [JsonProperty("rarity")]
        public string Rarity { get; set; }

        /// <summary>戦闘ステータス。欠けている場合はエラー扱い。 / Battle stats; a missing object is treated as an error.</summary>
        [JsonProperty("stats")]
        public CardProxyStats Stats { get; set; }

        /// <summary>フレーバーテキスト。 / Flavor text.</summary>
        [JsonProperty("flavorText")]
        public string FlavorText { get; set; }

        /// <summary>AIの判断根拠。デバッグ用途のみで、プレイヤーには見せない。 / The AI's reasoning; for debugging only, never shown to the player.</summary>
        [JsonProperty("reasoning")]
        public string Reasoning { get; set; }

        /// <summary>
        /// レスポンスを検証して <see cref="Card"/> に変換する。ここが唯一の検証の関所。
        /// 名前やフレーバーの欠落は補って続行し、ステータスの欠落だけをエラーとする。
        /// Validates the response and converts it into a <see cref="Card"/>. This is the one validation seam.
        /// A missing name or flavor text is patched up and the flow continues; only missing stats is an error.
        /// </summary>
        /// <param name="card">生成されたカード。失敗時は null。 / The generated card; null on failure.</param>
        /// <param name="error">失敗理由。成功時は null。 / Reason for the failure; null on success.</param>
        /// <returns>変換に成功したか。 / Whether the conversion succeeded.</returns>
        public bool TryToCard(out Card card, out string error)
        {
            card = null;
            error = null;

            if (Stats == null)
            {
                error = "カードのステータスを受け取れませんでした。もう一度お試しください。";
                return false;
            }

            var displayName = Trim(Name, MaxNameLength);
            if (string.IsNullOrEmpty(displayName))
            {
                Debug.LogWarning(
                    "[CardProxyResponse] 名前が空だったため代替名を使います / "
                    + "The response had no name; falling back to a generic one.");
                displayName = FallbackName;
            }

            var element = ParseElement(Element);
            var rarity = ParseRarity(Rarity);

            var stats = new StatBlock(Stats.Hp, Stats.Attack, Stats.Defense, Stats.Speed).Clamped();

            var flavorText = Trim(FlavorText, MaxFlavorTextLength);
            if (flavorText == null)
            {
                flavorText = string.Empty;
            }

            card = new Card(
                Guid.NewGuid().ToString(),
                displayName,
                element,
                rarity,
                stats,
                flavorText,
                null,
                DateTime.UtcNow);

            return true;
        }

        /// <summary>
        /// 属性名を大文字小文字を無視して解釈する。解釈できなければ警告を出して Wood にする。
        /// Parses the element name case-insensitively, warning and defaulting to Wood on anything unrecognised.
        /// </summary>
        private static ElementType ParseElement(string raw)
        {
            if (!string.IsNullOrEmpty(raw)
                && Enum.TryParse<ElementType>(raw.Trim(), true, out var parsed)
                && Enum.IsDefined(typeof(ElementType), parsed))
            {
                return parsed;
            }

            Debug.LogWarning(
                "[CardProxyResponse] 属性を解釈できなかったため Wood にします / "
                + "Unrecognised element '" + raw + "'; defaulting to Wood.");
            return ElementType.Wood;
        }

        /// <summary>
        /// レアリティ名を大文字小文字を無視して解釈する。解釈できなければ警告を出して Common にする。
        /// Parses the rarity name case-insensitively, warning and defaulting to Common on anything unrecognised.
        /// </summary>
        private static CitaiTokens.Cards.Rarity ParseRarity(string raw)
        {
            if (!string.IsNullOrEmpty(raw)
                && Enum.TryParse<CitaiTokens.Cards.Rarity>(raw.Trim(), true, out var parsed)
                && Enum.IsDefined(typeof(CitaiTokens.Cards.Rarity), parsed))
            {
                return parsed;
            }

            Debug.LogWarning(
                "[CardProxyResponse] レアリティを解釈できなかったため Common にします / "
                + "Unrecognised rarity '" + raw + "'; defaulting to Common.");
            return CitaiTokens.Cards.Rarity.Common;
        }

        /// <summary>
        /// 前後の空白を除き、長すぎる文字列を切り詰める。モデルが暴走した文章を送ってきても崩れないようにする。
        /// Trims surrounding whitespace and truncates absurdly long strings, so a runaway model cannot break the UI.
        /// </summary>
        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            return trimmed.Length > maxLength ? trimmed.Substring(0, maxLength) : trimmed;
        }
    }

    /// <summary>
    /// プロキシレスポンスの stats オブジェクト。値は必ず <see cref="StatBlock.Clamped"/> を通してから使う。
    /// The stats object of the proxy response. Its values are only ever used after <see cref="StatBlock.Clamped"/>.
    /// </summary>
    [Serializable]
    public sealed class CardProxyStats
    {
        /// <summary>体力。 / Hit points.</summary>
        [JsonProperty("hp")]
        public int Hp { get; set; }

        /// <summary>攻撃力。 / Attack.</summary>
        [JsonProperty("attack")]
        public int Attack { get; set; }

        /// <summary>防御力。 / Defense.</summary>
        [JsonProperty("defense")]
        public int Defense { get; set; }

        /// <summary>素早さ。 / Speed.</summary>
        [JsonProperty("speed")]
        public int Speed { get; set; }
    }

    /// <summary>
    /// プロキシのエラーレスポンス (2xx以外のとき返る本文)。422 の説明文はそのままプレイヤーに見せる。
    /// The proxy's error response body, returned with any non-2xx status. The explanation carried by a 422
    /// is shown to the player verbatim.
    /// </summary>
    [Serializable]
    public sealed class CardProxyError
    {
        /// <summary>サーバーが返したエラーメッセージ。 / The error message returned by the server.</summary>
        [JsonProperty("error")]
        public string Error { get; set; }
    }
}
