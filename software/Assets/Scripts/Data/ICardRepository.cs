using System.Collections.Generic;
using CitaiTokens.Cards;

namespace CitaiTokens.Data
{
    /// <summary>
    /// カードコレクションの保存先を抽象化する。MVPではローカルJSON実装のみ。
    /// Abstracts where the card collection is stored. MVP ships only a local JSON implementation.
    /// </summary>
    public interface ICardRepository
    {
        /// <summary>保存済みのカードを新しい順で返す。 / Returns saved cards, newest first.</summary>
        IReadOnlyList<Card> GetAll();

        /// <summary>IDでカードを取得する。見つからない場合は null。 / Gets a card by id, or null when absent.</summary>
        Card GetById(string id);

        /// <summary>カードを追加して永続化する。 / Adds a card and persists the collection.</summary>
        void Add(Card card);

        /// <summary>カードを削除して永続化する。削除できた場合 true。 / Removes a card and persists; true when removed.</summary>
        bool Remove(string id);

        /// <summary>ディスクから読み込み直す。 / Reloads the collection from disk.</summary>
        void Reload();
    }
}
