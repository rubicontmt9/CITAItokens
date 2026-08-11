using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace CitaiTokens.Capture
{
    /// <summary>
    /// 撮影した元画像を <c>Application.persistentDataPath/captures/</c> 以下で所有するストア。
    /// パスの決定・新規保存・古いファイルの削除だけを担当し、Unityのライフサイクルには依存しない素のC#クラス。
    /// Owns the full-resolution capture files under <c>Application.persistentDataPath/captures/</c>.
    /// It only decides paths, writes new captures and prunes old ones; it is a plain C# class with no
    /// dependency on Unity's lifecycle.
    /// </summary>
    /// <remarks>
    /// 【保持方式に「枚数上限」を選んだ理由】
    /// 元画像はどの <c>Card</c> からも参照されない (カードが持つのはサムネイルの相対パスだけ) ため、
    /// 実質はデバッグ用の成果物で、価値は「直近の数枚を実機から吸い出せること」に尽きる。
    /// つまり欲しい保証は「直近N枚が残っていること」であって「合計Nバイト以内」でも「N日以内」でもない。
    /// ・容量上限: 1枚のサイズが端末のカメラ解像度で大きく変わるので、残る枚数が端末ごとにばらつき、
    ///   「直近何枚を確認できるか」を保証できない。判定のたびに全ファイルのサイズ取得も要る。
    /// ・日数上限: 久しぶりに起動したプレイヤーでは直前の1枚まで消えうる。今撮った1枚を失わないことが
    ///   最優先なので採らない。
    /// ・枚数上限: 最悪サイズを 20枚 × 約3MB ≒ 60MB と見積もれ、削除対象は書き込み時刻の並べ替えだけで決まる。
    /// また削除は「撮影が成功した後」にしか走らせない。サムネイルの書き出し前に消すと、今撮った1枚が
    /// 取り返しのつかない形で失われるため。最新の1枚は常に最も新しいので、上限が1以上なら決して消えない。
    /// Why a count-based cap: the originals are referenced by no <c>Card</c> (cards store only the thumbnail's
    /// relative path), so they are effectively debug artefacts whose only value is "the last few can be pulled
    /// off the device". The guarantee wanted is therefore "the N most recent survive", not "at most N bytes"
    /// and not "nothing older than N days". A size cap makes the surviving count vary per device because JPEG
    /// size follows camera resolution, and it needs every file's size on every check. An age cap can delete the
    /// single most recent photo of a player who has not played for a while, which is exactly the file that must
    /// never be lost. A count cap bounds the worst case at roughly 20 × 3 MB and needs only a sort by write time.
    /// Pruning also runs strictly after a successful capture, never before: deleting anything while the just
    /// taken photo has not yet been turned into a thumbnail could lose it irrecoverably. The newest file is by
    /// definition the one just written, so with a cap of 1 or more it is never a deletion candidate.
    /// </remarks>
    public sealed class CaptureFileStore
    {
        /// <summary>撮影画像を置くサブディレクトリ名。 / Name of the sub-directory holding captured images.</summary>
        public const string DirectoryName = "captures";

        /// <summary>
        /// 残す元画像の最大枚数。これを超えた分を古い順に削除する。1以上であること。
        /// Maximum number of originals to keep; anything beyond this is deleted oldest-first. Must be 1 or more.
        /// </summary>
        public const int MaxRetainedFiles = 20;

        /// <summary>撮影ファイル名の接頭辞。 / Prefix of a capture file name.</summary>
        public const string FileNamePrefix = "capture-";

        /// <summary>撮影ファイルの拡張子。 / Extension of a capture file.</summary>
        public const string FileNameExtension = ".jpg";

        /// <summary>
        /// このストアが自分で書いたファイルだけに一致する検索パターン。
        /// 知らないファイルを巻き込んで消さないよう、削除も集計もこのパターン越しにしか行わない。
        /// A search pattern matching only the files this store wrote itself. Deletion and accounting both go
        /// through it, so a file the store does not recognise is never touched.
        /// </summary>
        private const string SearchPattern = FileNamePrefix + "*" + FileNameExtension;

        private readonly string rootPath;

        /// <summary>
        /// 既定の保存先 (<see cref="Application.persistentDataPath"/>) を使って初期化する。
        /// Creates the store rooted at the default location (<see cref="Application.persistentDataPath"/>).
        /// </summary>
        public CaptureFileStore()
            : this(Application.persistentDataPath)
        {
        }

        /// <summary>
        /// 保存先のルートを明示して初期化する。テスト用。
        /// Creates the store with an explicit root directory. Intended for tests.
        /// </summary>
        /// <param name="persistentRootPath">保存先ルートの絶対パス。 / Absolute path of the root directory.</param>
        public CaptureFileStore(string persistentRootPath)
        {
            rootPath = persistentRootPath;
        }

        /// <summary>撮影画像を置くディレクトリの絶対パス。 / Absolute path of the directory holding captured images.</summary>
        public string DirectoryPath => Path.Combine(rootPath, DirectoryName);

        /// <summary>
        /// JPEGバイト列を新しいファイルとして保存し、絶対パスを返す。失敗時は null を返してログを出す
        /// (呼び出し側は撮影失敗として扱える)。例外は外へ投げない。
        /// Saves the JPEG bytes as a new file and returns its absolute path. On failure it logs and returns
        /// null so the caller can report a failed capture; it never throws.
        /// </summary>
        /// <param name="jpegBytes">保存するJPEGバイト列。 / JPEG bytes to save.</param>
        /// <param name="capturedAtUtc">撮影時刻 (UTC)。ファイル名に使う。 / Capture time (UTC), used as the file name.</param>
        public string Write(byte[] jpegBytes, DateTime capturedAtUtc)
        {
            if (jpegBytes == null || jpegBytes.Length == 0)
            {
                Debug.LogError("[CaptureFileStore] 保存するデータが空です。 / The bytes to save are empty.");
                return null;
            }

            var directory = DirectoryPath;
            var fileName = FileNamePrefix
                + capturedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)
                + FileNameExtension;
            var absolutePath = Path.Combine(directory, fileName);

            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(absolutePath, jpegBytes);
                return absolutePath;
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[CaptureFileStore] 撮影画像の保存に失敗しました / Failed to save the capture '"
                    + absolutePath + "': " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 新しい方から <see cref="MaxRetainedFiles"/> 枚を残し、残りを書き込み時刻の古い順に削除する。
        /// 削除に失敗してもログを出して続行するだけで、撮影の流れを止めない。必ず撮影成功の後に呼ぶこと。
        /// Keeps the <see cref="MaxRetainedFiles"/> most recent files and deletes the rest, oldest first, by
        /// file write time. A failed deletion is logged and ignored so pruning can never break capture.
        /// Call this only after a capture has succeeded.
        /// </summary>
        public void Prune()
        {
            string[] files;
            try
            {
                var directory = DirectoryPath;
                if (!Directory.Exists(directory))
                {
                    return;
                }

                files = Directory.GetFiles(directory, SearchPattern);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[CaptureFileStore] 撮影ディレクトリを列挙できませんでした / "
                    + "Could not list the capture directory: " + e.Message);
                return;
            }

            if (files == null || files.Length <= MaxRetainedFiles)
            {
                return;
            }

            // 書き込み時刻を先にまとめて取得する。取得できないファイルは DateTime.MaxValue = 「最も新しい」
            // 扱いにして削除候補から外す。素性の分からないものを消さない側に倒すため。
            // Collect the write times up front. A file whose time cannot be read is treated as MaxValue, i.e.
            // the newest, so it is never a deletion candidate: when in doubt, keep the file.
            var times = new DateTime[files.Length];
            for (var i = 0; i < files.Length; i++)
            {
                try
                {
                    times[i] = File.GetLastWriteTimeUtc(files[i]);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        "[CaptureFileStore] 更新時刻を取得できませんでした / Could not read the write time of '"
                        + files[i] + "': " + e.Message);
                    times[i] = DateTime.MaxValue;
                }
            }

            Array.Sort(times, files);

            var deleteCount = files.Length - MaxRetainedFiles;
            var deleted = 0;
            for (var i = 0; i < deleteCount; i++)
            {
                if (DeleteFile(files[i]))
                {
                    deleted++;
                }
            }

            Debug.Log(
                "[CaptureFileStore] 古い撮影画像を整理しました / Pruned old captures: "
                + deleted + "/" + deleteCount + " deleted, "
                + (files.Length - deleted) + " kept (cap " + MaxRetainedFiles + ").");
        }

        /// <summary>
        /// 撮影ディレクトリが使っている合計バイト数を返す。診断用 (docs/android-testing.md §4.6 の実測に対応)。
        /// 読めないファイルは 0 として数える。例外は投げない。
        /// Returns the total number of bytes used by the capture files, for diagnostics (matching the storage
        /// figure in docs/android-testing.md §4.6). Unreadable files count as zero; never throws.
        /// </summary>
        public long GetTotalBytes()
        {
            string[] files;
            try
            {
                var directory = DirectoryPath;
                if (!Directory.Exists(directory))
                {
                    return 0L;
                }

                files = Directory.GetFiles(directory, SearchPattern);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[CaptureFileStore] 撮影ディレクトリを列挙できませんでした / "
                    + "Could not list the capture directory: " + e.Message);
                return 0L;
            }

            if (files == null)
            {
                return 0L;
            }

            var total = 0L;
            for (var i = 0; i < files.Length; i++)
            {
                try
                {
                    var info = new FileInfo(files[i]);
                    if (info.Exists)
                    {
                        total += info.Length;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        "[CaptureFileStore] ファイルサイズを取得できませんでした / Could not read the size of '"
                        + files[i] + "': " + e.Message);
                }
            }

            return total;
        }

        /// <summary>
        /// このストアが書いた撮影画像をすべて削除する。診断・容量計測のリセット用。例外は投げない。
        /// Deletes every capture file this store wrote, for diagnostics and for resetting a storage measurement.
        /// Never throws.
        /// </summary>
        public void DeleteAll()
        {
            string[] files;
            try
            {
                var directory = DirectoryPath;
                if (!Directory.Exists(directory))
                {
                    return;
                }

                files = Directory.GetFiles(directory, SearchPattern);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[CaptureFileStore] 撮影ディレクトリを列挙できませんでした / "
                    + "Could not list the capture directory: " + e.Message);
                return;
            }

            if (files == null)
            {
                return;
            }

            var deleted = 0;
            for (var i = 0; i < files.Length; i++)
            {
                if (DeleteFile(files[i]))
                {
                    deleted++;
                }
            }

            Debug.Log(
                "[CaptureFileStore] 撮影画像をすべて削除しました / Deleted all captures: "
                + deleted + "/" + files.Length + ".");
        }

        /// <summary>
        /// ファイルを1つ削除する。失敗しても警告ログだけで握りつぶす。
        /// Deletes a single file, swallowing any failure with a warning log.
        /// </summary>
        /// <param name="absolutePath">削除するファイルの絶対パス。 / Absolute path of the file to delete.</param>
        private static bool DeleteFile(string absolutePath)
        {
            try
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[CaptureFileStore] 撮影画像の削除に失敗しました / Failed to delete the capture '"
                    + absolutePath + "': " + e.Message);
                return false;
            }
        }
    }
}
