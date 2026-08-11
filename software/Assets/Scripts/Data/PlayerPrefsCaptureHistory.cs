using System;
using System.Globalization;
using UnityEngine;

namespace CitaiTokens.Data
{
    /// <summary>
    /// 直近の撮影記録を <see cref="PlayerPrefs"/> に保存する <see cref="ICaptureHistory"/> 実装。
    /// 1件だけしか保持しないため、専用のファイルを持つより PlayerPrefs のほうが簡単。
    /// An <see cref="ICaptureHistory"/> backed by <see cref="PlayerPrefs"/>. Only one record is ever
    /// kept, so PlayerPrefs is simpler than owning a dedicated file.
    /// </summary>
    public sealed class PlayerPrefsCaptureHistory : ICaptureHistory
    {
        private const string KeyPrefix = "citai.capture.";
        private const string TimestampKey = KeyPrefix + "lastUtc";
        private const string HasLocationKey = KeyPrefix + "hasLocation";
        private const string LatitudeKey = KeyPrefix + "latitude";
        private const string LongitudeKey = KeyPrefix + "longitude";

        /// <summary>過去に撮影記録があるか。 / Whether any capture has been recorded yet.</summary>
        public bool HasPreviousCapture => LastCaptureUtc > DateTime.MinValue;

        /// <summary>
        /// 直近の撮影時刻 (UTC)。記録がない、または値が壊れている場合は <see cref="DateTime.MinValue"/>。
        /// Last capture time (UTC), or <see cref="DateTime.MinValue"/> when absent or unparseable.
        /// </summary>
        public DateTime LastCaptureUtc
        {
            get
            {
                var stored = PlayerPrefs.GetString(TimestampKey, string.Empty);
                if (string.IsNullOrEmpty(stored))
                {
                    return DateTime.MinValue;
                }

                DateTime parsed;
                if (!DateTime.TryParse(
                        stored,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out parsed))
                {
                    return DateTime.MinValue;
                }

                return parsed.ToUniversalTime();
            }
        }

        /// <summary>直近の撮影に位置情報があるか。 / Whether the last capture carried a location.</summary>
        public bool HasLastLocation => PlayerPrefs.GetInt(HasLocationKey, 0) == 1;

        /// <summary>直近の撮影の緯度。 / Latitude of the last capture.</summary>
        public double LastLatitude => ReadDouble(LatitudeKey);

        /// <summary>直近の撮影の経度。 / Longitude of the last capture.</summary>
        public double LastLongitude => ReadDouble(LongitudeKey);

        /// <summary>
        /// 位置情報なしで撮影を記録して永続化する。以前の位置情報は消去される。
        /// Records a capture without a location and persists it. Any previously stored location is cleared.
        /// </summary>
        public void RecordCapture(DateTime capturedAtUtc)
        {
            WriteTimestamp(capturedAtUtc);
            PlayerPrefs.SetInt(HasLocationKey, 0);
            PlayerPrefs.DeleteKey(LatitudeKey);
            PlayerPrefs.DeleteKey(LongitudeKey);
            PlayerPrefs.Save();
        }

        /// <summary>位置情報付きで撮影を記録して永続化する。 / Records a capture with a location and persists it.</summary>
        public void RecordCapture(DateTime capturedAtUtc, double latitude, double longitude)
        {
            WriteTimestamp(capturedAtUtc);
            PlayerPrefs.SetInt(HasLocationKey, 1);
            WriteDouble(LatitudeKey, latitude);
            WriteDouble(LongitudeKey, longitude);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 記録を全て消す。デバッグ用。 / Clears every stored value. Intended for debugging.
        /// </summary>
        public void Clear()
        {
            PlayerPrefs.DeleteKey(TimestampKey);
            PlayerPrefs.DeleteKey(HasLocationKey);
            PlayerPrefs.DeleteKey(LatitudeKey);
            PlayerPrefs.DeleteKey(LongitudeKey);
            PlayerPrefs.Save();
        }

        private static void WriteTimestamp(DateTime capturedAtUtc)
        {
            PlayerPrefs.SetString(
                TimestampKey,
                capturedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        }

        private static void WriteDouble(string key, double value)
        {
            PlayerPrefs.SetString(key, value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static double ReadDouble(string key)
        {
            var stored = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(stored))
            {
                return 0d;
            }

            double parsed;
            if (!double.TryParse(
                    stored,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out parsed))
            {
                return 0d;
            }

            return parsed;
        }
    }
}
