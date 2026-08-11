#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using CitaiTokens.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CitaiTokens.EditorTools
{
    /// <summary>
    /// シーンと設定アセットを人手で組み立てずに済ませるためのエディタ用ツール。
    /// シーンファイルはリポジトリに持たない方針なので、クローン直後に一度これを実行する運用になる。
    /// Editor tooling so a human never has to hand-assemble the scene or the config asset. The scene file is
    /// deliberately not kept in the repository, so this is run once after a fresh clone.
    /// </summary>
    public static class SceneBuilder
    {
        /// <summary>メインシーンのアセットパス。 / Asset path of the main scene.</summary>
        public const string MainScenePath = "Assets/Scenes/Main.unity";

        /// <summary>設定アセットのアセットパス。 / Asset path of the config asset.</summary>
        public const string AppConfigAssetPath = "Assets/Resources/AppConfig.asset";

        /// <summary>ブートストラップGameObjectの名前。 / Name of the bootstrap GameObject.</summary>
        public const string BootstrapObjectName = "GameBootstrap";

        /// <summary>
        /// <see cref="GameBootstrap"/> だけを置いた空のシーンを作り、<c>Assets/Scenes/Main.unity</c> として
        /// 保存し、ビルド設定の先頭に登録する。UIもサービスも実行時に組み立てられるので、シーンの中身はこれだけ。
        /// Creates an empty scene containing only <see cref="GameBootstrap"/>, saves it as
        /// <c>Assets/Scenes/Main.unity</c> and registers it as the first entry of the build settings. The UI and
        /// the services are all built at runtime, so this really is the entire scene.
        /// </summary>
        [MenuItem("Tools/CITAItokens/Create Main Scene")]
        public static void CreateMainScene()
        {
            // 編集中のシーンを黙って捨てない。ユーザーが保存を拒んだ場合はここで打ち切る。
            // Never silently discard the scene being edited; abort when the user declines to save.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "[SceneBuilder] 編集中のシーンの保存がキャンセルされたため中止しました。 / "
                    + "Aborted because saving the currently open scene was cancelled.");
                return;
            }

            // 手で調整されている可能性が最も高いアセットがシーンなので、上書きは必ず確認する。
            // The scene is the asset most likely to have been customised by hand, so always confirm an overwrite.
            if (File.Exists(ToAbsolutePath(MainScenePath)))
            {
                var overwrite = EditorUtility.DisplayDialog(
                    "Create Main Scene",
                    MainScenePath + " は既に存在します。作り直すと現在の内容は失われます。続けますか?\n\n"
                    + MainScenePath + " already exists. Recreating it will discard its current contents. Continue?",
                    "上書きする / Overwrite",
                    "やめる / Cancel");

                if (!overwrite)
                {
                    Debug.Log(
                        "[SceneBuilder] 既存のシーンを保護するため中止しました。 / "
                        + "Aborted to protect the existing scene.");
                    return;
                }
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bootstrapObject = new GameObject(BootstrapObjectName);
            bootstrapObject.AddComponent<GameBootstrap>();

            EnsureFolderExists("Assets/Scenes");

            if (!EditorSceneManager.SaveScene(scene, MainScenePath))
            {
                Debug.LogError(
                    "[SceneBuilder] シーンの保存に失敗しました / Failed to save the scene to: " + MainScenePath);
                return;
            }

            AssetDatabase.Refresh();
            RegisterSceneFirstInBuildSettings(MainScenePath);

            Debug.Log(
                "[SceneBuilder] メインシーンを作成しました: " + MainScenePath
                + " (ビルド設定の先頭に登録済み)。Play を押せばタイトル画面から遊べます。 / "
                + "Created the main scene at " + MainScenePath
                + " and registered it as the first enabled entry in the build settings. Press Play to start "
                + "from the title screen.");
        }

        /// <summary>
        /// <c>Assets/Resources/AppConfig.asset</c> を用意して Project ウィンドウで選択する。既存の場合は選択のみ。
        /// Creates <c>Assets/Resources/AppConfig.asset</c> when missing and selects it in the Project window;
        /// when it already exists, it is only selected.
        /// </summary>
        /// <remarks>
        /// このアセットは環境ごとの値 (プロキシURL) を持つため、意図的に .gitignore で除外されている。
        /// 既存ファイルは絶対に上書きしない — 開発者自身のURLが入っているため。
        /// This asset holds environment-specific values (the proxy URL), so it is git-ignored on purpose.
        /// An existing file is never overwritten, because it holds the developer's own URL.
        /// </remarks>
        [MenuItem("Tools/CITAItokens/Create AppConfig Asset")]
        public static void CreateAppConfigAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AppConfig>(AppConfigAssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log(
                    "[SceneBuilder] 設定アセットは既に存在するため、選択だけ行いました (上書きしません): "
                    + AppConfigAssetPath + " / The config asset already exists, so it was only selected and "
                    + "left untouched: " + AppConfigAssetPath);
                return;
            }

            EnsureFolderExists("Assets/Resources");

            var config = ScriptableObject.CreateInstance<AppConfig>();
            AssetDatabase.CreateAsset(config, AppConfigAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // CreateAsset 後は config 自身がそのアセットの実体になるので、読み直す必要はない。
            // After CreateAsset, config itself is the asset instance, so there is nothing to reload.
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);

            Debug.Log(
                "[SceneBuilder] 設定アセットを作成しました: " + AppConfigAssetPath
                + " — Inspector で Card Proxy Url (末尾スラッシュなし) を設定し、実際のAI生成を使う場合は "
                + "Use Mock Card Generator のチェックを外してください。このアセットは .gitignore 済みです。 / "
                + "Created the config asset at " + AppConfigAssetPath
                + " — set Card Proxy Url (no trailing slash) in the Inspector and uncheck Use Mock Card "
                + "Generator to use real AI generation. This asset is git-ignored.");
        }

        /// <summary>
        /// シーンをビルド設定の先頭・有効状態で登録する。既に登録済みの場合は重複させず先頭へ移す。
        /// Registers the scene as the first, enabled entry of the build settings, moving an existing entry to the
        /// front instead of duplicating it.
        /// </summary>
        private static void RegisterSceneFirstInBuildSettings(string scenePath)
        {
            var existing = EditorBuildSettings.scenes;
            var rebuilt = new List<EditorBuildSettingsScene>(existing.Length + 1);
            rebuilt.Add(new EditorBuildSettingsScene(scenePath, true));

            for (var i = 0; i < existing.Length; i++)
            {
                if (existing[i] == null || existing[i].path == scenePath)
                {
                    continue;
                }

                rebuilt.Add(existing[i]);
            }

            EditorBuildSettings.scenes = rebuilt.ToArray();
        }

        /// <summary>
        /// <c>Assets/...</c> 形式のフォルダを実ディレクトリとして用意し、AssetDatabase に認識させる。
        /// Creates a folder given as an <c>Assets/...</c> path on disk and makes the AssetDatabase aware of it.
        /// </summary>
        private static void EnsureFolderExists(string assetFolderPath)
        {
            var absolutePath = ToAbsolutePath(assetFolderPath);
            if (Directory.Exists(absolutePath))
            {
                return;
            }

            Directory.CreateDirectory(absolutePath);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// <c>Assets/...</c> 形式のパスを絶対パスへ変換する。 / Converts an <c>Assets/...</c> path into an absolute path.
        /// </summary>
        private static string ToAbsolutePath(string assetPath)
        {
            // Application.dataPath は <project>/Assets を指すので、先頭の "Assets" を差し替える。
            // Application.dataPath points at <project>/Assets, so the leading "Assets" is replaced with it.
            var relative = assetPath.StartsWith("Assets/") ? assetPath.Substring("Assets/".Length) : assetPath;
            return Path.Combine(Application.dataPath, relative);
        }
    }
}
#endif
