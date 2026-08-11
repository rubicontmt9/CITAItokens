using System;

namespace CitaiTokens.Data
{
    /// <summary>
    /// 直近の撮影記録。「移動して撮る」チェックのために参照される。
    /// The most recent capture record, consulted by the "move before you capture again" check.
    /// </summary>
    public interface ICaptureHistory
    {
        /// <summary>過去に撮影記録があるか。 / Whether any capture has been recorded yet.</summary>
        bool HasPreviousCapture { get; }

        /// <summary>直近の撮影時刻 (UTC)。記録がない場合は <see cref="DateTime.MinValue"/>。 / Last capture time (UTC), or <see cref="DateTime.MinValue"/>.</summary>
        DateTime LastCaptureUtc { get; }

        /// <summary>直近の撮影に位置情報があるか。 / Whether the last capture carried a location.</summary>
        bool HasLastLocation { get; }

        double LastLatitude { get; }

        double LastLongitude { get; }

        /// <summary>撮影を記録して永続化する。 / Records a capture and persists it.</summary>
        void RecordCapture(DateTime capturedAtUtc);

        /// <summary>位置情報付きで撮影を記録して永続化する。 / Records a capture with a location and persists it.</summary>
        void RecordCapture(DateTime capturedAtUtc, double latitude, double longitude);
    }
}
