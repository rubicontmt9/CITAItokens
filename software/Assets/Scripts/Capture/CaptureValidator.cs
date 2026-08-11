using System;
using CitaiTokens.Data;

namespace CitaiTokens.Capture
{
    /// <summary>
    /// 「今撮った写真か」「前回から移動したか」を判定する検証器。屋外に出ることがこのゲームの目的なので、
    /// この2つのルールがゲーム性の中心になる。UnityにもAppConfigにも依存させず、単体で検証できる形にしてある。
    /// Validates "was this photo taken just now" and "did you move since last time". Getting the player
    /// outdoors is the point of the game, so these two rules carry the design. This class depends on neither
    /// Unity nor AppConfig, so it stays checkable on its own.
    /// </summary>
    public sealed class CaptureValidator
    {
        /// <summary>地球半径 (メートル)。ハバサイン計算に使う。 / Earth radius in metres, used by the haversine formula.</summary>
        public const double EarthRadiusMeters = 6371000d;

        private readonly ICaptureHistory history;
        private readonly int freshPhotoMaxAgeMinutes;
        private readonly float minMetersBetweenCaptures;
        private readonly bool requireLocationCheck;

        /// <summary>
        /// 検証器を作る。3つの調整値はオーケストレータ側の設定から渡される。
        /// Creates the validator. The three tunables are supplied by the orchestrator's configuration.
        /// </summary>
        /// <param name="history">直近の撮影記録。null 可 (その場合は移動チェックを通す)。 / Last-capture record; null is allowed and passes the movement check.</param>
        /// <param name="freshPhotoMaxAgeMinutes">写真として許容する最大経過時間 (分)。 / Maximum allowed age of a photo, in minutes.</param>
        /// <param name="minMetersBetweenCaptures">前回撮影地点から離れるべき最小距離 (メートル)。 / Minimum distance to move from the previous capture, in metres.</param>
        /// <param name="requireLocationCheck">移動チェックを行うか。 / Whether to enforce the movement check at all.</param>
        public CaptureValidator(
            ICaptureHistory history,
            int freshPhotoMaxAgeMinutes,
            float minMetersBetweenCaptures,
            bool requireLocationCheck)
        {
            this.history = history;
            this.freshPhotoMaxAgeMinutes = freshPhotoMaxAgeMinutes;
            this.minMetersBetweenCaptures = minMetersBetweenCaptures;
            this.requireLocationCheck = requireLocationCheck;
        }

        /// <summary>許容する写真の最大経過時間 (分)。 / Maximum allowed photo age, in minutes.</summary>
        public int FreshPhotoMaxAgeMinutes => freshPhotoMaxAgeMinutes;

        /// <summary>前回撮影地点から離れるべき最小距離 (メートル)。 / Minimum distance from the previous capture, in metres.</summary>
        public float MinMetersBetweenCaptures => minMetersBetweenCaptures;

        /// <summary>移動チェックが有効か。 / Whether the movement check is enabled.</summary>
        public bool RequireLocationCheck => requireLocationCheck;

        /// <summary>
        /// 写真が「今撮ったもの」かを検証する。許容時間を超えて古い写真は拒否する。
        /// 端末時計のずれで未来時刻になっている場合は拒否しない (プレイヤーの責任ではない)。
        /// Validates that the photo was taken just now, rejecting anything older than the allowed age.
        /// A capture time in the future (device clock skew) is not rejected: that is not the player's fault.
        /// </summary>
        /// <param name="capturedAtUtc">撮影時刻 (UTC)。 / Capture time (UTC).</param>
        /// <param name="nowUtc">現在時刻 (UTC)。 / Current time (UTC).</param>
        public CaptureValidationResult ValidateFreshness(DateTime capturedAtUtc, DateTime nowUtc)
        {
            if (freshPhotoMaxAgeMinutes <= 0)
            {
                return CaptureValidationResult.Valid();
            }

            if (capturedAtUtc == DateTime.MinValue)
            {
                return CaptureValidationResult.Invalid("撮影時刻が確認できませんでした。もう一度撮影してください。");
            }

            var ageMinutes = (nowUtc - capturedAtUtc).TotalMinutes;
            if (ageMinutes <= freshPhotoMaxAgeMinutes)
            {
                return CaptureValidationResult.Valid();
            }

            return CaptureValidationResult.Invalid(
                "この写真は古すぎます。今この場で撮った写真を使ってください (" + freshPhotoMaxAgeMinutes + "分以内)。");
        }

        /// <summary>
        /// 前回の撮影地点から十分に移動したかを検証する。位置情報が無い状況では必ず通す:
        /// GPSが取れないことでプレイヤーを詰まらせてはいけない。
        /// Validates that the player has moved far enough from the previous capture. It always passes when
        /// location is unavailable: a missing GPS fix must never block the player.
        /// </summary>
        /// <param name="haveLocation">今回の撮影に位置情報があるか。 / Whether this capture carries a location.</param>
        /// <param name="latitude">今回の緯度 (度)。 / Latitude of this capture, in degrees.</param>
        /// <param name="longitude">今回の経度 (度)。 / Longitude of this capture, in degrees.</param>
        public CaptureValidationResult ValidateMovement(bool haveLocation, double latitude, double longitude)
        {
            if (!requireLocationCheck)
            {
                return CaptureValidationResult.Valid();
            }

            if (!haveLocation)
            {
                return CaptureValidationResult.Valid();
            }

            if (history == null || !history.HasPreviousCapture || !history.HasLastLocation)
            {
                return CaptureValidationResult.Valid();
            }

            if (minMetersBetweenCaptures <= 0f)
            {
                return CaptureValidationResult.Valid();
            }

            var distance = DistanceMeters(
                history.LastLatitude,
                history.LastLongitude,
                latitude,
                longitude);

            if (distance >= minMetersBetweenCaptures)
            {
                return CaptureValidationResult.Valid();
            }

            var remaining = (int)Math.Ceiling(minMetersBetweenCaptures - distance);
            if (remaining < 1)
            {
                remaining = 1;
            }

            return CaptureValidationResult.Invalid(
                "前回と同じ場所のようです。あと約" + remaining + "m 移動して、別の自然物を撮ってみてください。");
        }

        /// <summary>
        /// 2点間の距離をハバサインの公式で求める (メートル)。地球半径は 6371000 m。
        /// Returns the distance between two points in metres using the haversine formula, earth radius 6371000 m.
        /// </summary>
        public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            var lat1Rad = ToRadians(lat1);
            var lat2Rad = ToRadians(lat2);
            var deltaLatRad = ToRadians(lat2 - lat1);
            var deltaLonRad = ToRadians(lon2 - lon1);

            var sinHalfLat = Math.Sin(deltaLatRad / 2d);
            var sinHalfLon = Math.Sin(deltaLonRad / 2d);

            var a = (sinHalfLat * sinHalfLat)
                + (Math.Cos(lat1Rad) * Math.Cos(lat2Rad) * sinHalfLon * sinHalfLon);

            if (a < 0d)
            {
                a = 0d;
            }
            else if (a > 1d)
            {
                a = 1d;
            }

            var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
            return EarthRadiusMeters * c;
        }

        /// <summary>
        /// 度をラジアンに変換する。 / Converts degrees to radians.
        /// </summary>
        private static double ToRadians(double degrees)
        {
            return degrees * (Math.PI / 180d);
        }
    }

    /// <summary>
    /// 撮影検証の結果。無効な場合はそのままプレイヤーに見せられる日本語メッセージを持つ。
    /// Result of a capture validation. When invalid, it carries a Japanese message ready to show the player.
    /// </summary>
    public sealed class CaptureValidationResult
    {
        private CaptureValidationResult(bool isValid, string message)
        {
            IsValid = isValid;
            Message = message;
        }

        /// <summary>検証を通ったか。 / Whether the validation passed.</summary>
        public bool IsValid { get; }

        /// <summary>プレイヤーに提示するメッセージ。有効時は null。 / Player-facing message; null when valid.</summary>
        public string Message { get; }

        /// <summary>検証成功の結果を作る。 / Creates a passing result.</summary>
        public static CaptureValidationResult Valid()
        {
            return new CaptureValidationResult(true, null);
        }

        /// <summary>検証失敗の結果を作る。 / Creates a failing result.</summary>
        public static CaptureValidationResult Invalid(string message)
        {
            return new CaptureValidationResult(false, message);
        }
    }
}
