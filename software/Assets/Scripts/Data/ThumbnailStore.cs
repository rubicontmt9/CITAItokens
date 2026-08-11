using System;
using System.IO;
using UnityEngine;

namespace CitaiTokens.Data
{
    /// <summary>
    /// カード画像ファイルを <c>Application.persistentDataPath/cards/</c> 以下で管理する。
    /// 保存されるパスは相対パスなので、端末が変わってもセーブデータを持ち運べる。
    /// Owns card image files under <c>Application.persistentDataPath/cards/</c>.
    /// Stored paths are relative, so the save file stays portable across devices.
    /// </summary>
    public sealed class ThumbnailStore
    {
        /// <summary>画像を置くサブディレクトリ名。 / Name of the sub-directory holding card images.</summary>
        public const string DirectoryName = "cards";

        /// <summary>長辺の最大ピクセル数。 / Maximum length of the longest edge, in pixels.</summary>
        public const int MaxEdgePixels = 512;

        /// <summary>JPEGエンコード品質。 / JPEG encoding quality.</summary>
        public const int JpegQuality = 85;

        private readonly string rootPath;

        /// <summary>
        /// 既定の保存先 (<see cref="Application.persistentDataPath"/>) を使って初期化する。
        /// Creates the store rooted at the default location (<see cref="Application.persistentDataPath"/>).
        /// </summary>
        public ThumbnailStore()
            : this(Application.persistentDataPath)
        {
        }

        /// <summary>
        /// 保存先のルートを明示して初期化する。テスト用。
        /// Creates the store with an explicit root directory. Intended for tests.
        /// </summary>
        public ThumbnailStore(string persistentRootPath)
        {
            rootPath = persistentRootPath;
        }

        /// <summary>
        /// 相対パスを絶対パスに変換する。 / Converts a relative path into an absolute path.
        /// </summary>
        public string GetAbsolutePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            return Path.Combine(rootPath, relativePath);
        }

        /// <summary>
        /// 元画像を読み込み、長辺512pxまで縮小してJPEGとして保存し、相対パスを返す。
        /// 失敗時は null を返してエラーログを出す (呼び出し側は画像なしで続行できる)。
        /// Loads the source image, downscales it so the longest edge is at most 512 px, saves it as JPEG,
        /// and returns the relative path. Returns null and logs an error on failure, so the caller can
        /// continue without an image.
        /// </summary>
        /// <param name="cardId">保存先ファイル名に使うカードID。 / Card id, used as the file name.</param>
        /// <param name="sourceImagePath">元画像の絶対パス。 / Absolute path of the source image.</param>
        public string SaveThumbnail(string cardId, string sourceImagePath)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                Debug.LogError("[ThumbnailStore] カードIDが空です。 / The card id is empty.");
                return null;
            }

            if (string.IsNullOrEmpty(sourceImagePath) || !File.Exists(sourceImagePath))
            {
                Debug.LogError(
                    "[ThumbnailStore] 元画像が見つかりません / Source image not found: " + sourceImagePath);
                return null;
            }

            byte[] sourceBytes;
            try
            {
                sourceBytes = File.ReadAllBytes(sourceImagePath);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[ThumbnailStore] 元画像の読み込みに失敗しました / Failed to read the source image '"
                    + sourceImagePath + "': " + e.Message);
                return null;
            }

            var source = new Texture2D(2, 2);
            Texture2D scaled = null;
            try
            {
                if (!source.LoadImage(sourceBytes))
                {
                    Debug.LogError(
                        "[ThumbnailStore] 画像としてデコードできませんでした / Could not decode the image: "
                        + sourceImagePath);
                    return null;
                }

                int targetWidth;
                int targetHeight;
                CalculateTargetSize(source.width, source.height, out targetWidth, out targetHeight);

                byte[] jpegBytes;
                if (targetWidth == source.width && targetHeight == source.height)
                {
                    jpegBytes = source.EncodeToJPG(JpegQuality);
                }
                else
                {
                    scaled = Downscale(source, targetWidth, targetHeight);
                    if (scaled == null)
                    {
                        return null;
                    }

                    jpegBytes = scaled.EncodeToJPG(JpegQuality);
                }

                if (jpegBytes == null || jpegBytes.Length == 0)
                {
                    Debug.LogError("[ThumbnailStore] JPEGエンコードに失敗しました / JPEG encoding failed.");
                    return null;
                }

                var relativePath = DirectoryName + "/" + cardId + ".jpg";
                var absolutePath = GetAbsolutePath(relativePath);
                var directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(absolutePath, jpegBytes);
                return relativePath;
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[ThumbnailStore] サムネイルの保存に失敗しました / Failed to save the thumbnail: " + e.Message);
                return null;
            }
            finally
            {
                if (scaled != null)
                {
                    UnityEngine.Object.Destroy(scaled);
                }

                UnityEngine.Object.Destroy(source);
            }
        }

        /// <summary>
        /// 保存済みサムネイルを読み込む。存在しない・読めない場合は null。
        /// Loads a saved thumbnail; returns null when the file is missing or unreadable.
        /// </summary>
        public Texture2D LoadThumbnail(string relativePath)
        {
            var absolutePath = GetAbsolutePath(relativePath);
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
            {
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(absolutePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[ThumbnailStore] サムネイルの読み込みに失敗しました / Failed to read the thumbnail '"
                    + absolutePath + "': " + e.Message);
                return null;
            }

            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(texture);
                Debug.LogWarning(
                    "[ThumbnailStore] サムネイルをデコードできませんでした / Could not decode the thumbnail: "
                    + absolutePath);
                return null;
            }

            return texture;
        }

        /// <summary>
        /// サムネイルを削除する。失敗しても例外は投げない。
        /// Deletes a thumbnail on a best-effort basis; never throws.
        /// </summary>
        public void DeleteThumbnail(string relativePath)
        {
            var absolutePath = GetAbsolutePath(relativePath);
            if (string.IsNullOrEmpty(absolutePath))
            {
                return;
            }

            try
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[ThumbnailStore] サムネイルの削除に失敗しました / Failed to delete the thumbnail '"
                    + absolutePath + "': " + e.Message);
            }
        }

        /// <summary>
        /// アスペクト比を保ったまま長辺が <see cref="MaxEdgePixels"/> 以下になるサイズを求める。
        /// Computes a size whose longest edge is at most <see cref="MaxEdgePixels"/>, preserving aspect ratio.
        /// </summary>
        private static void CalculateTargetSize(int width, int height, out int targetWidth, out int targetHeight)
        {
            var longestEdge = Mathf.Max(width, height);
            if (longestEdge <= MaxEdgePixels)
            {
                targetWidth = width;
                targetHeight = height;
                return;
            }

            var scale = (float)MaxEdgePixels / longestEdge;
            targetWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            targetHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
        }

        /// <summary>
        /// GPU で縮小した新しい <see cref="Texture2D"/> を返す。呼び出し側が破棄する責任を持つ。
        /// Returns a new downscaled <see cref="Texture2D"/>; the caller owns and must destroy it.
        /// </summary>
        private static Texture2D Downscale(Texture2D source, int targetWidth, int targetHeight)
        {
            var renderTexture = RenderTexture.GetTemporary(targetWidth, targetHeight, 0);
            var previousActive = RenderTexture.active;
            Texture2D result = null;
            try
            {
                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;

                result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0f, 0f, targetWidth, targetHeight), 0, 0);
                result.Apply();
                return result;
            }
            catch (Exception e)
            {
                if (result != null)
                {
                    UnityEngine.Object.Destroy(result);
                }

                Debug.LogError(
                    "[ThumbnailStore] 画像の縮小に失敗しました / Failed to downscale the image: " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }
    }
}
