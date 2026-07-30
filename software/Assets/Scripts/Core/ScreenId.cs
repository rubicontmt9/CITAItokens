namespace CitaiTokens.Core
{
    /// <summary>
    /// 画面の識別子。MVPは1シーン内でこれらを切り替える。
    /// Identifies a screen. The MVP switches between these inside a single Unity scene.
    /// </summary>
    public enum ScreenId
    {
        /// <summary>タイトル画面。 / Title screen.</summary>
        Title = 0,

        /// <summary>撮影画面。 / Capture screen.</summary>
        Capture = 1,

        /// <summary>生成されたカードの表示画面。 / Screen showing the generated card.</summary>
        CardResult = 2,

        /// <summary>コレクション一覧画面。 / Collection list screen.</summary>
        Collection = 3,

        /// <summary>バトル画面。 / Battle screen.</summary>
        Battle = 4,

        /// <summary>バトル結果画面。 / Battle result screen.</summary>
        Result = 5,
    }
}
