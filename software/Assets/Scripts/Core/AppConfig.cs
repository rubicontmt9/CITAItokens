using UnityEngine;

namespace CitaiTokens.Core
{
    /// <summary>
    /// ビルドに焼き込まない調整値をまとめた設定アセット。プロキシURLなどをコードから分離するために使う。
    /// A settings asset holding tunable values, keeping things like the proxy URL out of the code.
    /// </summary>
    [CreateAssetMenu(fileName = "AppConfig", menuName = "CITAItokens/App Config")]
    public sealed class AppConfig : ScriptableObject
    {
        /// <summary><see cref="LoadOrDefault"/> が探す Resources 内のアセット名。 / Asset name searched by <see cref="LoadOrDefault"/>.</summary>
        public const string ResourceName = "AppConfig";

        [SerializeField] private string cardProxyUrl = string.Empty;
        [SerializeField] private bool useMockCardGenerator = true;
        [SerializeField] private int freshPhotoMaxAgeMinutes = 10;
        [SerializeField] private bool requireLocationCheck = false;
        [SerializeField] private float minMetersBetweenCaptures = 30f;

        /// <summary>
        /// カード生成プロキシサービスのベースURL。リポジトリにはコミットしない。
        /// Base URL of the card-generation proxy service. Not committed to the repository.
        /// </summary>
        public string CardProxyUrl => cardProxyUrl;

        /// <summary>
        /// true の間は通信せずローカルでカードを生成する。プロキシ未デプロイでもEditorで一周遊べるようにするため。
        /// While true, cards are generated locally with no network call, so the loop is playable in the
        /// Editor before the proxy is deployed.
        /// </summary>
        public bool UseMockCardGenerator => useMockCardGenerator;

        /// <summary>
        /// 「今撮った写真」と認めるまでの分数。これより古い写真は拒否する。
        /// How many minutes a photo may be old and still count as "taken just now"; older photos are rejected.
        /// </summary>
        public int FreshPhotoMaxAgeMinutes => freshPhotoMaxAgeMinutes;

        /// <summary>
        /// GPSによる移動チェックを有効にするか (任意機能)。 / Whether the GPS movement check is enabled (opt-in).
        /// </summary>
        public bool RequireLocationCheck => requireLocationCheck;

        /// <summary>
        /// 連続撮影を認めるために必要な最小移動距離 (メートル)。
        /// Minimum distance in metres the player must move between captures.
        /// </summary>
        public float MinMetersBetweenCaptures => minMetersBetweenCaptures;

        /// <summary>
        /// <c>Assets/Resources/AppConfig.asset</c> を読み込む。存在しない場合は警告を出して既定値のインスタンスを返す。
        /// 設定アセットが無くてもゲームは必ず起動できる。
        /// Loads <c>Assets/Resources/AppConfig.asset</c>. When it is missing, logs a warning and returns an
        /// instance holding the defaults, so the game always remains playable without a config asset.
        /// </summary>
        public static AppConfig LoadOrDefault()
        {
            var loaded = Resources.Load<AppConfig>(ResourceName);
            if (loaded != null)
            {
                return loaded;
            }

            Debug.LogWarning(
                "[AppConfig] Assets/Resources/AppConfig.asset が見つかりません。既定値で起動し、"
                + "カード生成はモック (通信なし) になります。 / "
                + "Assets/Resources/AppConfig.asset is missing. Starting with default values; "
                + "card generation will use the mock generator with no network calls.");

            return CreateInstance<AppConfig>();
        }
    }
}
