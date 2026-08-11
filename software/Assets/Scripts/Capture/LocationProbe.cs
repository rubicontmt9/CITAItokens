using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace CitaiTokens.Capture
{
    /// <summary>
    /// 位置情報を1回だけ読む補助コンポーネント。設定で明示的に有効化されたときにしか使われない。
    /// 位置情報は完全に任意であり、どの失敗経路でもゲームを止めない (非成功の結果を返すだけ)。
    /// A helper component that reads the location once. It is only used when the config opts in.
    /// Location is strictly optional: every failure path returns a non-success result instead of blocking the game.
    /// </summary>
    public sealed class LocationProbe : MonoBehaviour
    {
        /// <summary>権限ダイアログの応答を待つ上限秒数。 / Seconds to wait for the permission dialog to be answered.</summary>
        private const float PermissionTimeoutSeconds = 30f;

        /// <summary>
        /// 位置情報を1回取得しようと試みる。コルーチンとして実行する。
        /// タイムアウト・権限拒否・端末側で無効、いずれの場合も非成功の結果をコールバックに返す。
        /// Tries once to obtain the current location. Run as a coroutine.
        /// Timeouts, denied permission and a location service disabled by the user all deliver a
        /// non-success result to the callback rather than blocking.
        /// </summary>
        /// <param name="timeoutSeconds">測位を待つ上限秒数。 / Maximum seconds to wait for a fix.</param>
        /// <param name="onComplete">結果を受け取るコールバック。 / Callback receiving the result.</param>
        public IEnumerator TryGetLocation(float timeoutSeconds, Action<LocationProbeResult> onComplete)
        {
            if (timeoutSeconds <= 0f)
            {
                timeoutSeconds = 1f;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Permission.RequestUserPermission(Permission.FineLocation);

                var permissionWaited = 0f;
                while (permissionWaited < PermissionTimeoutSeconds
                    && !Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                {
                    yield return null;
                    permissionWaited += Time.unscaledDeltaTime;
                }
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Report(onComplete, LocationProbeResult.Fail("位置情報の利用が許可されていません。"));
                yield break;
            }
#endif

            if (!Input.location.isEnabledByUser)
            {
                Report(onComplete, LocationProbeResult.Fail("端末の位置情報サービスが無効です。"));
                yield break;
            }

            string startError = null;
            try
            {
                Input.location.Start();
            }
            catch (Exception e)
            {
                startError = e.Message;
            }

            if (startError != null)
            {
                Debug.LogWarning(
                    "[LocationProbe] 位置情報サービスの開始に失敗しました / "
                    + "Failed to start the location service: " + startError);
                Report(onComplete, LocationProbeResult.Fail("位置情報の取得を開始できませんでした。"));
                yield break;
            }

            var waited = 0f;
            while (waited < timeoutSeconds && Input.location.status == LocationServiceStatus.Initializing)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }

            var status = Input.location.status;

            if (status == LocationServiceStatus.Running)
            {
                double latitude = Input.location.lastData.latitude;
                double longitude = Input.location.lastData.longitude;
                StopLocationService();
                Report(onComplete, LocationProbeResult.Ok(latitude, longitude));
                yield break;
            }

            StopLocationService();

            if (status == LocationServiceStatus.Failed)
            {
                Report(onComplete, LocationProbeResult.Fail("位置情報の取得に失敗しました。"));
                yield break;
            }

            Report(onComplete, LocationProbeResult.Fail("位置情報の取得がタイムアウトしました。"));
        }

        /// <summary>
        /// 位置情報サービスを止める。停止の失敗はログのみで、外へは投げない。
        /// Stops the location service; a failure to stop is logged only, never thrown.
        /// </summary>
        private static void StopLocationService()
        {
            try
            {
                Input.location.Stop();
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[LocationProbe] 位置情報サービスの停止中に例外が発生しました / "
                    + "Exception while stopping the location service: " + e.Message);
            }
        }

        /// <summary>
        /// コールバックが null でも安全に結果を渡す。 / Delivers the result, tolerating a null callback.
        /// </summary>
        private static void Report(Action<LocationProbeResult> onComplete, LocationProbeResult result)
        {
            if (onComplete != null)
            {
                onComplete(result);
            }
        }
    }

    /// <summary>
    /// 位置情報取得の結果。失敗しても撮影自体は続行できる。
    /// Result of a location probe. Capture continues even when this fails.
    /// </summary>
    public sealed class LocationProbeResult
    {
        private LocationProbeResult(bool success, double latitude, double longitude, string errorMessage)
        {
            Success = success;
            Latitude = latitude;
            Longitude = longitude;
            ErrorMessage = errorMessage;
        }

        /// <summary>測位に成功したか。 / Whether a fix was obtained.</summary>
        public bool Success { get; }

        /// <summary>緯度 (度)。失敗時は 0。 / Latitude in degrees; 0 on failure.</summary>
        public double Latitude { get; }

        /// <summary>経度 (度)。失敗時は 0。 / Longitude in degrees; 0 on failure.</summary>
        public double Longitude { get; }

        /// <summary>失敗理由。成功時は null。 / Reason for the failure; null on success.</summary>
        public string ErrorMessage { get; }

        /// <summary>測位成功の結果を作る。 / Creates a successful result.</summary>
        public static LocationProbeResult Ok(double latitude, double longitude)
        {
            return new LocationProbeResult(true, latitude, longitude, null);
        }

        /// <summary>測位失敗の結果を作る。 / Creates a failed result.</summary>
        public static LocationProbeResult Fail(string errorMessage)
        {
            return new LocationProbeResult(false, 0d, 0d, errorMessage);
        }
    }
}
