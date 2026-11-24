import json, pytest
from server.engine import Exchange, Instrument, Quote, SimError
from server.feeds import SyntheticFeed, parse_chart

INS = [Instrument("A.T", "A", "JPY", "stock", 100, 1000), Instrument("U", "U", "USD", "stock", 1, 100),
       Instrument("USDJPY=X", "usd", "JPY", "fx", 1000, 150)]
SP = {"stock": 10, "etf": 6, "fx": 2}


def mk(cash=1_000_000):
    ex = Exchange(INS, cash, 5, SP)
    ex.update({"A.T": Quote(1000, 1, "t"), "U": Quote(100, 1, "t"), "USDJPY=X": Quote(150, 1, "t")})
    a, tok = ex.register("alice", "human")
    return ex, a, tok


def test_market_buy_sell_fees_and_spread():
    ex, a, _ = mk()
    o = ex.place(a, "A.T", "buy", 100)
    assert o.status == "filled" and o.fill_px == pytest.approx(1000.5)  # ask = +5bps
    assert a.cash == pytest.approx(1_000_000 - 100050 * 1.0005)
    ex.place(a, "A.T", "sell", 100)
    assert "A.T" not in a.pos and a.cash < 1_000_000  # round trip costs spread + fees


def test_usd_converted_to_jpy():
    ex, a, _ = mk()
    ex.place(a, "U", "buy", 10)
    assert a.cost["U"] == pytest.approx(100.05 * 150 * 1.0005)  # fee in basis
    assert ex.equity(a) == pytest.approx(a.cash + 10 * 100 * 150)


def test_validation():
    ex, a, _ = mk()
    for args in [("X", "buy", 1), ("A.T", "buy", 50), ("A.T", "hold", 100), ("A.T", "buy", -100), ("A.T", "buy", 100, "limit", 0)]:
        with pytest.raises(SimError):
            ex.place(a, *args)
    assert ex.place(a, "A.T", "sell", 100).reason.startswith("insufficient position")
    assert ex.place(a, "A.T", "buy", 10000).reason == "insufficient cash"
    with pytest.raises(SimError):
        ex.register("alice", "ai")
    with pytest.raises(SimError):
        ex.register("<script>", "human")
    with pytest.raises(SimError):
        ex.auth("bad")


def test_limit_order_rests_then_fills_and_cancel():
    ex, a, _ = mk()
    o = ex.place(a, "A.T", "buy", 100, "limit", 990)
    assert o.status == "open"
    ex.update({"A.T": Quote(985, 2, "t")})
    assert o.status == "filled" and o.fill_px < 990
    o2 = ex.place(a, "A.T", "sell", 100, "limit", 2000)
    ex.cancel(a, o2.id)
    assert o2.status == "cancelled"
    b, _ = ex.register("bob", "ai")
    with pytest.raises(SimError):
        ex.cancel(b, o.id)  # cannot touch others' orders


def test_save_load_roundtrip(tmp_path):
    ex, a, tok = mk()
    ex.place(a, "A.T", "buy", 100)
    p = str(tmp_path / "s.json")
    ex.save(p)
    ex2 = Exchange(INS, 1_000_000, 5, SP)
    ex2.load(p)
    assert ex2.auth(tok).pos == {"A.T": 100} and tok not in open(p).read()  # only hash stored


def test_synthetic_deterministic():
    f1, f2 = SyntheticFeed(INS, 7, 60, 1), SyntheticFeed(INS, 7, 60, 1)
    for _ in range(50):
        a, b = f1.step(), f2.step()
    assert {k: v.price for k, v in a.items()} == {k: v.price for k, v in b.items()}
    assert all(v.price > 0 for v in a.values())


def test_parse_yahoo_chart():
    js = json.loads('{"chart":{"result":[{"meta":{"regularMarketPrice":2850.5,"regularMarketTime":1700000000},'
                    '"timestamp":[1,2,3],"indicators":{"quote":[{"close":[1.0,null,3.0]}]}}]}}')
    q, h = parse_chart(js)
    assert q.price == 2850.5 and q.source == "yahoo" and h == [(1.0, 1.0), (3.0, 3.0)]


def test_bad_and_stale_quotes_blocked(tmp_path):
    ex, a, _ = mk()
    ex.update({"A.T": Quote(0, 5, "t")})
    ex.update({"A.T": Quote(float("nan"), 5, "t")})
    ex.update({"A.T": Quote(1, 0, "t")})  # older ts
    assert ex.quotes["A.T"].price == 1000
    ex.quotes["A.T"].fetched -= 3600  # feed silent for an hour
    with pytest.raises(SimError, match="stale"):
        ex.place(a, "A.T", "buy", 100)
    ex.quotes["USDJPY=X"].fetched -= 3600  # USD stock needs a live FX rate too
    with pytest.raises(SimError, match="stale"):
        ex.place(a, "U", "buy", 1)


def test_state_meta_mismatch(tmp_path):
    ex = Exchange(INS, 1, 5, SP, meta={"feed": "synthetic"}); ex.register("x", "ai")
    p = str(tmp_path / "s.json"); ex.save(p)
    with pytest.raises(SimError):
        Exchange(INS, 1, 5, SP, meta={"feed": "yahoo"}).load(p)
