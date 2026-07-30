using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CitaiTokens.Cards;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace CitaiTokens.Data
{
    /// <summary>
    /// カードコレクションを端末内のJSONファイルとして保存する <see cref="ICardRepository"/> 実装。
    /// アカウントもサーバーも使わないMVPの唯一の保存先。
    /// An <see cref="ICardRepository"/> that stores the collection as a JSON file on the device.
    /// This is the only storage backend in the MVP: no accounts, no server.
    /// </summary>
    public sealed class LocalCardRepository : ICardRepository
    {
        /// <summary>コレクションファイル名。 / File name of the collection file.</summary>
        public const string FileName = "collection.json";

        /// <summary>
        /// <see cref="Card"/> と <see cref="StatBlock"/> は [Serializable] な private フィールドにデータを持ち、
        /// 公開プロパティは読み取り専用なので、Newtonsoft の既定設定 (publicメンバーのみ) では往復できない。
        /// <c>IgnoreSerializableAttribute = false</c> にしてフィールドベースの契約を使う。
        /// <see cref="Card"/> and <see cref="StatBlock"/> keep their data in private [Serializable] fields
        /// behind read-only properties, so Newtonsoft's default public-members-only contract cannot round-trip
        /// them. Setting <c>IgnoreSerializableAttribute = false</c> switches to a field-based contract.
        /// </summary>
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { IgnoreSerializableAttribute = false },
            Formatting = Formatting.Indented,
        };

        private readonly string filePath;
        private readonly string tempFilePath;
        private readonly List<Card> cards = new List<Card>();
        private readonly ReadOnlyCollection<Card> readOnlyView;

        /// <summary>
        /// 既定の保存先 (<see cref="Application.persistentDataPath"/>) から読み込んで初期化する。
        /// Creates the repository and loads from the default location (<see cref="Application.persistentDataPath"/>).
        /// </summary>
        public LocalCardRepository()
            : this(Path.Combine(Application.persistentDataPath, FileName))
        {
        }

        /// <summary>
        /// 保存先を明示して初期化する。テスト用。
        /// Creates the repository with an explicit file path. Intended for tests.
        /// </summary>
        public LocalCardRepository(string collectionFilePath)
        {
            filePath = collectionFilePath;
            tempFilePath = collectionFilePath + ".tmp";
            readOnlyView = new ReadOnlyCollection<Card>(cards);
            Reload();
        }

        /// <summary>コレクションファイルの絶対パス。 / Absolute path of the collection file.</summary>
        public string FilePath => filePath;

        /// <summary>保持しているカード枚数。 / Number of cards currently held.</summary>
        public int Count => cards.Count;

        /// <summary>
        /// 保存済みのカードを新しい順 (撮影時刻の降順) で返す。返り値は読み取り専用ビュー。
        /// Returns saved cards newest first (capture time descending). The result is a read-only view.
        /// </summary>
        public IReadOnlyList<Card> GetAll()
        {
            return readOnlyView;
        }

        /// <summary>IDでカードを取得する。見つからない場合は null。 / Gets a card by id, or null when absent.</summary>
        public Card GetById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null && cards[i].Id == id)
                {
                    return cards[i];
                }
            }

            return null;
        }

        /// <summary>カードを追加して即座に永続化する。 / Adds a card and persists immediately.</summary>
        public void Add(Card card)
        {
            if (card == null)
            {
                Debug.LogError("[LocalCardRepository] null のカードは追加できません。 / Cannot add a null card.");
                return;
            }

            cards.Add(card);
            SortNewestFirst();
            Save();
        }

        /// <summary>カードを削除して即座に永続化する。削除できた場合 true。 / Removes a card and persists immediately; true when removed.</summary>
        public bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null && cards[i].Id == id)
                {
                    cards.RemoveAt(i);
                    Save();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// ディスクから読み込み直す。ファイルが無いのは異常ではなく「まだ0枚」を意味する。
        /// Reloads from disk. A missing file is not an error: it simply means "no cards yet".
        /// </summary>
        public void Reload()
        {
            cards.Clear();

            if (!File.Exists(filePath))
            {
                return;
            }

            string json;
            try
            {
                json = File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[LocalCardRepository] コレクションの読み込みに失敗しました / Failed to read the collection from '"
                    + filePath + "': " + e.Message);
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<List<Card>>(json, SerializerSettings);
                if (loaded != null)
                {
                    for (var i = 0; i < loaded.Count; i++)
                    {
                        if (loaded[i] != null)
                        {
                            cards.Add(loaded[i]);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                cards.Clear();
                Debug.LogError(
                    "[LocalCardRepository] コレクションファイルが壊れていたため退避して空から再開します / "
                    + "The collection file was corrupt; quarantining it and continuing with an empty collection: "
                    + e.Message);
                QuarantineCorruptFile();
                return;
            }

            SortNewestFirst();
        }

        /// <summary>撮影時刻の降順に並べ替える。 / Sorts the in-memory list by capture time, descending.</summary>
        private void SortNewestFirst()
        {
            cards.Sort(CompareNewestFirst);
        }

        private static int CompareNewestFirst(Card left, Card right)
        {
            return right.GetCaptureTimeUtc().CompareTo(left.GetCaptureTimeUtc());
        }

        /// <summary>
        /// 一時ファイルへ書いてから差し替えることで、書き込み中のクラッシュでコレクションを失わないようにする。
        /// 保存の失敗は例外として外に出さず、ログに残すだけにする。
        /// Writes to a temporary file and then swaps it in, so a crash mid-write cannot destroy the collection.
        /// Save failures are logged, never thrown out of this method.
        /// </summary>
        private void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(cards, SerializerSettings);
                File.WriteAllText(tempFilePath, json, Encoding.UTF8);

                if (File.Exists(filePath))
                {
                    ReplaceExistingFile(tempFilePath, filePath);
                }
                else
                {
                    File.Move(tempFilePath, filePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[LocalCardRepository] コレクションの保存に失敗しました / Failed to save the collection to '"
                    + filePath + "': " + e.Message);
            }
        }

        /// <summary>
        /// 既存ファイルを一時ファイルで置き換える。<see cref="File.Replace(string,string,string)"/> が
        /// 使えない環境では削除してから移動する方式にフォールバックする。
        /// Replaces an existing file with the temporary file, falling back to delete-then-move on
        /// platforms where <see cref="File.Replace(string,string,string)"/> is unavailable.
        /// </summary>
        private static void ReplaceExistingFile(string source, string destination)
        {
            try
            {
                File.Replace(source, destination, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[LocalCardRepository] File.Replace が使えないため削除して移動します / "
                    + "File.Replace is unavailable here; falling back to delete-then-move: " + e.Message);
                File.Delete(destination);
                File.Move(source, destination);
            }
        }

        /// <summary>
        /// 壊れたファイルをタイムスタンプ付きの名前に退避する。プレイヤーが永久に詰まらないようにするための処理。
        /// Moves the corrupt file aside under a timestamped name, so the player is never permanently stuck.
        /// </summary>
        private void QuarantineCorruptFile()
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                var quarantineName = "collection.corrupt-"
                    + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
                var quarantinePath = string.IsNullOrEmpty(directory)
                    ? quarantineName
                    : Path.Combine(directory, quarantineName);

                if (File.Exists(quarantinePath))
                {
                    File.Delete(quarantinePath);
                }

                File.Move(filePath, quarantinePath);
                Debug.LogError(
                    "[LocalCardRepository] 壊れたコレクションを退避しました / Corrupt collection moved to: "
                    + quarantinePath);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[LocalCardRepository] 壊れたコレクションの退避に失敗しました / "
                    + "Failed to quarantine the corrupt collection: " + e.Message);
            }
        }
    }
}
