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
        /// このビルドが書き出すセーブデータのスキーマ版。ツール側から参照できるよう公開している。
        /// The save schema version this build writes. Public so tooling can read it.
        /// </summary>
        /// <remarks>
        /// 1 = <c>{ "schemaVersion": 1, "cards": [ ... ] }</c> のラッパー形式。
        /// 0 = バージョン導入前の、裸の <c>[ ... ]</c> 配列 (ファイル中に番号を持たない)。
        /// <see cref="Card"/> に項目を足すたびにこの値を上げ、<see cref="Reload"/> に移行処理を足す。
        /// 1 = the wrapper form <c>{ "schemaVersion": 1, "cards": [ ... ] }</c>.
        /// 0 = the pre-versioning bare <c>[ ... ]</c> array, which carries no number in the file at all.
        /// Bump this whenever a field is added to <see cref="Card"/> and add the migration to <see cref="Reload"/>.
        /// </remarks>
        public const int CurrentSchemaVersion = 1;

        /// <summary>バージョン導入前のセーブを表す版番号。 / The version number standing for a pre-versioning save.</summary>
        public const int PreVersioningSchemaVersion = 0;

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
        /// 読み込んだファイルがこのビルドより新しかったため、保存を禁止している状態。
        /// True when the file on disk was newer than this build, so saving is forbidden.
        /// </summary>
        private bool saveBlocked;

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
        /// 保存が禁止されているか。ディスク上のセーブがこのビルドより新しい場合に true になる。
        /// Whether saving is forbidden, which happens when the save on disk is newer than this build.
        /// </summary>
        /// <remarks>
        /// この状態では追加・削除はメモリ上でしか起こらない。UI 側は「このセーブは新しいアプリで作られている」旨を
        /// 出して、変更が残らないことをプレイヤーに伝えるべき。
        /// While this is true, additions and removals only affect memory. The UI should tell the player that the
        /// save was written by a newer build and that their changes will not persist.
        /// </remarks>
        public bool IsSaveBlocked => saveBlocked;

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
        /// <remarks>
        /// 読み込みは絶対に書き込まない (壊れたファイルの退避だけが例外)。バージョン0のセーブを読んだ場合も
        /// その場で書き戻さず、次の <see cref="Add"/> / <see cref="Remove"/> の保存でラッパー形式に上がる。
        /// Loading never writes, the one exception being quarantining a corrupt file. A version-0 save is not
        /// rewritten on the spot either: it is upgraded to the wrapper form by the next save from
        /// <see cref="Add"/> or <see cref="Remove"/>.
        /// </remarks>
        public void Reload()
        {
            cards.Clear();
            saveBlocked = false;

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
                if (IsBareCardArray(json))
                {
                    LoadPreVersioningArray(json);
                }
                else if (!LoadVersionedWrapper(json))
                {
                    // より新しいセーブだったので、読まずに何も書かずに終える。 / A newer save: nothing read, nothing written.
                    return;
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

        /// <summary>
        /// 中身が裸のJSON配列かどうかを、最初の非空白文字だけを見て判定する。
        /// Decides whether the contents are a bare JSON array by looking only at the first non-whitespace character.
        /// </summary>
        /// <remarks>
        /// バージョン導入前のファイルは <c>[ ... ]</c>、導入後は <c>{ "schemaVersion": ... }</c> で始まる。
        /// ここでパースを試して失敗を見るのではなく1文字で分けるのは、「どちらの形として読むか」の判断と
        /// 「壊れている」の判断を混ぜないため。混ぜると、壊れたラッパーを配列として読み直そうとして
        /// 二重に失敗し、退避の理由が分からなくなる。
        /// A pre-versioning file starts with <c>[</c>; a versioned one starts with <c>{</c>. Splitting on one
        /// character rather than by trying a parse and catching the failure keeps "which shape is this" separate
        /// from "is this corrupt": mixing them means a damaged wrapper gets retried as an array, fails twice, and
        /// the reason it was quarantined becomes unreadable.
        /// </remarks>
        private static bool IsBareCardArray(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            for (var i = 0; i < json.Length; i++)
            {
                // BOM も読み飛ばす。File.ReadAllText は通常取り除くが、残っていても判定を誤らせない。
                // The BOM is skipped too: File.ReadAllText normally strips it, but a surviving one must not
                // change the verdict.
                if (char.IsWhiteSpace(json[i]) || json[i] == '\uFEFF')
                {
                    continue;
                }

                return json[i] == '[';
            }

            return false;
        }

        /// <summary>
        /// バージョン導入前の裸の配列を読み込む。プレイテストで既に溜まっているデータを捨てないための経路。
        /// Loads a pre-versioning bare array. This path exists so playtest data already on disk is not discarded.
        /// </summary>
        private void LoadPreVersioningArray(string json)
        {
            var loaded = JsonConvert.DeserializeObject<List<Card>>(json, SerializerSettings);
            AddLoadedCards(loaded);

            Debug.LogWarning(
                "[LocalCardRepository] バージョン番号を持たない旧形式のセーブ (v"
                + PreVersioningSchemaVersion + ") を読み込み、v" + CurrentSchemaVersion
                + " として移行しました。枚数: " + cards.Count
                + " (武器ジャンルは既定値になります)。次の保存でファイルが新形式に置き換わります。 / "
                + "Migrated a pre-versioning save (v" + PreVersioningSchemaVersion + ") to v"
                + CurrentSchemaVersion + "; " + cards.Count
                + " card(s) loaded, with the weapon genre falling back to its default. The file is rewritten in "
                + "the new form on the next save.");
        }

        /// <summary>
        /// ラッパー形式を読み込む。読み込んだ場合 true、より新しい版で読まなかった場合 false。
        /// Loads the wrapper form; returns true when loaded, false when it was skipped for being newer.
        /// </summary>
        private bool LoadVersionedWrapper(string json)
        {
            var file = JsonConvert.DeserializeObject<CollectionFile>(json, SerializerSettings);
            if (file == null)
            {
                // "null" だけのファイルなど。壊れている扱いにする。 / A file containing just "null"; treat as corrupt.
                throw new InvalidDataException(
                    "コレクションのラッパーが null として読み込まれました / The collection wrapper deserialized to null.");
            }

            if (file.SchemaVersion > CurrentSchemaVersion)
            {
                // 新しいアプリで作られたセーブを、古いアプリが読める形に落として書き戻すと、
                // 新しい版だけが持つ項目が黙って消える。読まない・触らないのが唯一安全な選択。
                // Downgrading a save written by a newer build and writing it back would silently drop whatever
                // fields only the newer version knows about. Refusing to read or touch it is the only safe move.
                saveBlocked = true;
                cards.Clear();
                Debug.LogError(
                    "[LocalCardRepository] セーブデータのスキーマ版 " + file.SchemaVersion
                    + " はこのビルドが扱える " + CurrentSchemaVersion
                    + " より新しいため、読み込まず、ファイルにも一切書き込みません。空のコレクションで起動します。"
                    + "新しい方のアプリで開いてください。対象ファイル: " + filePath + " / "
                    + "Save schema version " + file.SchemaVersion + " is newer than the " + CurrentSchemaVersion
                    + " this build understands, so it was not loaded and the file is left completely untouched. "
                    + "Starting with an empty in-memory collection; open it with the newer build instead. File: "
                    + filePath);
                return false;
            }

            AddLoadedCards(file.Cards);

            if (file.SchemaVersion < CurrentSchemaVersion)
            {
                Debug.LogWarning(
                    "[LocalCardRepository] スキーマ版 " + file.SchemaVersion + " のセーブを v"
                    + CurrentSchemaVersion + " として移行しました。枚数: " + cards.Count + " / "
                    + "Migrated a v" + file.SchemaVersion + " save to v" + CurrentSchemaVersion + "; "
                    + cards.Count + " card(s) loaded.");
            }

            return true;
        }

        /// <summary>
        /// 読み込んだリストから null を除いて取り込む。 / Takes the loaded list, skipping null entries.
        /// </summary>
        private void AddLoadedCards(List<Card> loaded)
        {
            if (loaded == null)
            {
                return;
            }

            for (var i = 0; i < loaded.Count; i++)
            {
                if (loaded[i] != null)
                {
                    cards.Add(loaded[i]);
                }
            }
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
            // 古いビルドが新しいセーブを壊すことは絶対に許さない。読めなかったものは書き戻さない。
            // An older build must never clobber a newer save: what could not be read is never written back.
            if (saveBlocked)
            {
                Debug.LogError(
                    "[LocalCardRepository] このビルドより新しいセーブデータを保護するため、保存を中止しました。"
                    + "メモリ上の変更はアプリ終了時に失われます。対象ファイル: " + filePath + " / "
                    + "Save aborted to protect a save file newer than this build. In-memory changes will be lost "
                    + "when the app exits. File: " + filePath);
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var payload = new CollectionFile
                {
                    SchemaVersion = CurrentSchemaVersion,
                    Cards = cards,
                };

                var json = JsonConvert.SerializeObject(payload, SerializerSettings);
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

        /// <summary>
        /// <c>collection.json</c> の最上位。カード配列をバージョン番号で包むためだけの型。
        /// The top level of <c>collection.json</c>: a type that exists only to wrap the card array in a version.
        /// </summary>
        /// <remarks>
        /// <see cref="Card"/> と違い [Serializable] を付けていない。付けると
        /// <see cref="SerializerSettings"/> のフィールド契約が効いてしまい、自動プロパティのバッキング
        /// フィールド名 (<c>&lt;SchemaVersion&gt;k__BackingField</c>) がそのままJSONのキーになる。
        /// JSON上のキー名は <see cref="JsonPropertyAttribute"/> で明示的に固定してある。
        /// Unlike <see cref="Card"/> this is deliberately not [Serializable]: the field-based contract in
        /// <see cref="SerializerSettings"/> would then apply and emit auto-property backing field names
        /// (<c>&lt;SchemaVersion&gt;k__BackingField</c>) as the JSON keys. The key names are pinned explicitly
        /// with <see cref="JsonPropertyAttribute"/>.
        /// </remarks>
        public sealed class CollectionFile
        {
            /// <summary>セーブデータのスキーマ版。 / Schema version of the save data.</summary>
            [JsonProperty("schemaVersion")]
            public int SchemaVersion { get; set; }

            /// <summary>保存されているカード。 / The saved cards.</summary>
            [JsonProperty("cards")]
            public List<Card> Cards { get; set; }
        }
    }
}
