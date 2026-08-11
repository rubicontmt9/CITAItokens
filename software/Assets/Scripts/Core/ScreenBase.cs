using UnityEngine;

namespace CitaiTokens.Core
{
    /// <summary>
    /// 1画面の基底クラス。MVPではシーンファイルを6つ持つ代わりに、1シーン内の兄弟GameObjectを
    /// <see cref="ScreenRouter"/> が有効/無効に切り替えることで画面遷移を表現する。
    /// Base class for a single screen. Instead of six scene files, the MVP keeps screens as sibling
    /// GameObjects in one scene and lets <see cref="ScreenRouter"/> activate and deactivate them.
    /// </summary>
    public abstract class ScreenBase : MonoBehaviour
    {
        /// <summary>この画面の識別子。 / The identifier of this screen.</summary>
        public abstract ScreenId Id { get; }

        /// <summary>
        /// 画面が表示される直前に呼ばれる。GameObjectは既に有効化されている。
        /// Called just after this screen is shown; the GameObject is already active.
        /// </summary>
        /// <param name="payload">遷移元から渡された任意のデータ。無い場合は null。 / Optional data handed over by the caller; null when there is none.</param>
        public virtual void OnShow(object payload)
        {
        }

        /// <summary>
        /// 画面が隠される直前に呼ばれる。 / Called just before this screen is hidden.
        /// </summary>
        public virtual void OnHide()
        {
        }
    }
}
