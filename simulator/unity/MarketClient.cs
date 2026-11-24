// CITAI Market client for Unity (virtual economy integration).
// Copy into software/Assets/Scripts/Economy/. Requires the simulator server running locally.
// Unity 2021.2+ (C# 8). Set Player Settings > Allow downloads over HTTP = Always allowed (localhost).
// Author: Rubiconian Destomato
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace CitaiEconomy
{
    [Serializable] public class Quote { public string sym, name, ccy, kind, source; public int lot; public double price, bid, ask, ts; }
    [Serializable] public class Quotes { public Quote[] items; }
    [Serializable] public class Position { public string sym; public long qty; public double avg_cost, value, pnl; }
    [Serializable] public class Account { public string id, name, kind; public double cash, equity, ret_pct; public Position[] items; }
    [Serializable] public class Order { public int id; public string sym, side, type, status, reason; public long qty; public double limit, fill_px, fee, ts; }
    [Serializable] public class Rank { public string name, kind; public double equity, ret_pct; }
    [Serializable] public class Ranking { public Rank[] items; }
    [Serializable] class RegisterReq { public string name, kind; }
    [Serializable] class RegisterRes { public string id, name, kind, token; }
    [Serializable] class OrderReq { public string sym, side, type; public long qty; public double limit; }
    [Serializable] class Err { public string detail; }

    public class MarketClient : MonoBehaviour
    {
        public string baseUrl = "http://127.0.0.1:8000";
        const string TokenKey = "citai.market.token";
        public string Token { get => PlayerPrefs.GetString(TokenKey, ""); set { PlayerPrefs.SetString(TokenKey, value); PlayerPrefs.Save(); } }

        public IEnumerator Register(string playerName, Action<string> ok, Action<string> err) =>
            Send("POST", "/api/register", JsonUtility.ToJson(new RegisterReq { name = playerName, kind = "human" }),
                 j => { Token = JsonUtility.FromJson<RegisterRes>(j).token; ok?.Invoke(Token); }, err);

        public IEnumerator GetQuotes(Action<Quote[]> ok, Action<string> err) =>
            Send("GET", "/api/quotes", null, j => ok?.Invoke(JsonUtility.FromJson<Quotes>(j).items), err);

        public IEnumerator GetAccount(Action<Account> ok, Action<string> err) =>
            Send("GET", "/api/me", null, j => ok?.Invoke(JsonUtility.FromJson<Account>(j)), err);

        public IEnumerator GetRanking(Action<Rank[]> ok, Action<string> err) =>
            Send("GET", "/api/leaderboard", null, j => ok?.Invoke(JsonUtility.FromJson<Ranking>(j).items), err);

        /// <summary>side: "buy"|"sell". limit &lt;= 0 means market order.</summary>
        public IEnumerator PlaceOrder(string sym, string side, long qty, double limit, Action<Order> ok, Action<string> err) =>
            Send("POST", "/api/orders", JsonUtility.ToJson(new OrderReq { sym = sym, side = side, qty = qty, type = limit > 0 ? "limit" : "market", limit = Math.Max(0, limit) }),
                 j => ok?.Invoke(JsonUtility.FromJson<Order>(j)), err);

        public IEnumerator CancelOrder(int id, Action<Order> ok, Action<string> err) =>
            Send("DELETE", $"/api/orders/{id}", null, j => ok?.Invoke(JsonUtility.FromJson<Order>(j)), err);

        IEnumerator Send(string method, string path, string body, Action<string> ok, Action<string> err)
        {
            using var req = new UnityWebRequest(baseUrl + path, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = 10 };
            if (body != null) { req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body)); req.SetRequestHeader("Content-Type", "application/json"); }
            if (!string.IsNullOrEmpty(Token)) req.SetRequestHeader("Authorization", "Bearer " + Token);
            yield return req.SendWebRequest();
            var text = req.downloadHandler.text;
            if (req.result == UnityWebRequest.Result.Success) ok?.Invoke(text);
            else
            {
                string d = null;
                try { d = JsonUtility.FromJson<Err>(text)?.detail; } catch { /* validation errors are not a plain string */ }
                err?.Invoke(d ?? $"{req.responseCode} {req.error}");
            }
        }
    }
}
