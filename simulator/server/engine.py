"""Market engine: instruments, accounts, orders, fills. Pure logic, no I/O except save/load."""
import contextlib, hashlib, json, math, os, re, secrets, time
from dataclasses import dataclass, field, asdict

NAME_RE = re.compile(r"^[\w\- ]{1,24}$")  # \w is unicode: allows Japanese
MAX_OPEN = 100  # open orders per player
MAX_PLAYERS = 200


class SimError(ValueError):
    pass


@dataclass
class Instrument:
    sym: str
    name: str
    ccy: str
    kind: str
    lot: int
    seed_px: float


@dataclass
class Quote:
    price: float
    ts: float     # market data timestamp (unix)
    source: str   # yahoo | synthetic
    fetched: float = 0.0  # wall time the feed last confirmed this quote (staleness guard)


@dataclass
class Order:
    id: int
    player: str
    sym: str
    side: str     # buy | sell
    qty: int
    type: str     # market | limit
    limit: float  # 0 for market
    ts: float
    status: str = "open"  # open | filled | cancelled | rejected
    fill_px: float = 0.0
    fee: float = 0.0
    reason: str = ""


@dataclass
class Account:
    id: str
    name: str
    kind: str     # human | ai
    token_hash: str
    cash: float
    created: float
    pos: dict = field(default_factory=dict)   # sym -> qty
    cost: dict = field(default_factory=dict)  # sym -> avg cost per unit (JPY)


def _h(token):
    return hashlib.sha256(token.encode()).hexdigest()


class Exchange:
    def __init__(self, instruments, cash, fee_bps, spread_bps, stale_sec=60, meta=None):
        self.ins = {i.sym: i for i in instruments}
        self.cash0, self.fee_bps, self.spread = cash, fee_bps, spread_bps
        self.quotes: dict[str, Quote] = {}
        self.accts: dict[str, Account] = {}
        self.by_token: dict[str, str] = {}
        self.orders: dict[int, Order] = {}
        self.next_oid = 1
        self.dirty = False
        self.stale_sec = stale_sec
        self.meta = meta or {}  # saved with state; load refuses a mismatch (e.g. feed switch)
        self.open: dict[int, Order] = {}

    # ---- prices ----
    def update(self, quotes):
        now = time.time()
        for s, q in quotes.items():
            old = self.quotes.get(s)
            if s in self.ins and math.isfinite(q.price) and q.price > 0 and (not old or q.ts >= old.ts):
                q.fetched = now
                self.quotes[s] = q
        self.match()

    def live(self, sym):
        q = self.quotes.get(sym)
        return bool(q) and time.time() - q.fetched <= self.stale_sec

    def rate(self, ccy):
        if ccy == "JPY":
            return 1.0
        q = self.quotes.get(f"{ccy}JPY=X")
        if not q:
            raise SimError(f"no {ccy}JPY rate yet")
        return q.price

    def bidask(self, sym):
        q = self.quotes.get(sym)
        if not q:
            raise SimError(f"no quote for {sym} yet")
        h = q.price * self.spread[self.ins[sym].kind] / 2e4
        return q.price - h, q.price + h

    def quote_view(self, sym):
        i, q = self.ins[sym], self.quotes.get(sym)
        if not q:
            return dict(sym=sym, name=i.name, ccy=i.ccy, kind=i.kind, lot=i.lot,
                        price=0.0, bid=0.0, ask=0.0, ts=0.0, source="none")
        b, a = self.bidask(sym)
        return dict(sym=sym, name=i.name, ccy=i.ccy, kind=i.kind, lot=i.lot,
                    price=q.price, bid=b, ask=a, ts=q.ts, source=q.source)

    # ---- accounts ----
    def register(self, name, kind):
        name = name.strip()
        if not NAME_RE.match(name):
            raise SimError("name: 1-24 chars, letters/digits/_/-/space")
        if kind not in ("human", "ai"):
            raise SimError("kind must be human or ai")
        if len(self.accts) >= MAX_PLAYERS:
            raise SimError("player limit reached")
        if any(a.name.lower() == name.lower() for a in self.accts.values()):
            raise SimError("name taken")
        token = secrets.token_urlsafe(24)
        a = Account(secrets.token_hex(4), name, kind, _h(token), self.cash0, time.time())
        self.accts[a.id] = a
        self.by_token[a.token_hash] = a.id
        self.dirty = True
        return a, token

    def auth(self, token):
        aid = self.by_token.get(_h(token or ""))
        if not aid:
            raise SimError("invalid token")
        return self.accts[aid]

    def equity(self, a):
        v = a.cash
        for s, q in a.pos.items():
            with contextlib.suppress(SimError, KeyError):  # unvaluable (no quote/FX yet) -> 0
                v += q * self.quotes[s].price * self.rate(self.ins[s].ccy)
        return v

    def account_view(self, a):
        pos = []
        for s, q in a.pos.items():
            px = 0.0
            with contextlib.suppress(SimError, KeyError):
                px = self.quotes[s].price * self.rate(self.ins[s].ccy)
            pos.append(dict(sym=s, qty=q, avg_cost=a.cost.get(s, 0.0), value=q * px,
                            pnl=q * (px - a.cost.get(s, 0.0))))
        eq = self.equity(a)
        return dict(id=a.id, name=a.name, kind=a.kind, cash=a.cash, equity=eq,
                    ret_pct=(eq / self.cash0 - 1) * 100, items=pos)

    def board(self):
        rows = [dict(name=a.name, kind=a.kind, equity=self.equity(a),
                     ret_pct=(self.equity(a) / self.cash0 - 1) * 100) for a in self.accts.values()]
        return sorted(rows, key=lambda r: -r["equity"])

    # ---- orders ----
    def place(self, a, sym, side, qty, type_="market", limit=0.0):
        i = self.ins.get(sym)
        if not i:
            raise SimError(f"unknown symbol {sym}")
        if side not in ("buy", "sell"):
            raise SimError("side must be buy or sell")
        if type_ not in ("market", "limit"):
            raise SimError("type must be market or limit")
        if not isinstance(qty, int) or qty <= 0 or qty % i.lot:
            raise SimError(f"qty must be a positive multiple of {i.lot}")
        limit = float(limit or 0)
        if type_ == "limit" and not (limit > 0 and math.isfinite(limit)):
            raise SimError("limit price must be > 0")
        if type_ == "market":
            limit = 0.0
        self.bidask(sym), self.rate(i.ccy)  # fail fast if no data
        if not (self.live(sym) and (i.ccy == "JPY" or self.live(f"{i.ccy}JPY=X"))):
            raise SimError("price feed stale; trading paused for this symbol")
        if sum(o.player == a.id for o in self.open.values()) >= MAX_OPEN:
            raise SimError(f"too many open orders (max {MAX_OPEN})")
        o = Order(self.next_oid, a.id, sym, side, qty, type_, limit, time.time())
        self.next_oid += 1
        self.orders[o.id] = self.open[o.id] = o
        self.dirty = True
        self._try(o)
        if o.status == "open" and side == "buy" and limit * qty * self.rate(i.ccy) > a.cash:
            self._close(o, "rejected", "insufficient cash")
        return o

    def cancel(self, a, oid):
        o = self.orders.get(oid)
        if not o or o.player != a.id:
            raise SimError("order not found")
        if o.status != "open":
            raise SimError(f"order already {o.status}")
        self._close(o, "cancelled")
        return o

    def match(self):
        for o in list(self.open.values()):
            i = self.ins[o.sym]
            if self.live(o.sym) and (i.ccy == "JPY" or self.live(f"{i.ccy}JPY=X")):
                try:
                    self._try(o)
                except SimError:
                    pass

    def _try(self, o):
        bid, ask = self.bidask(o.sym)
        px = ask if o.side == "buy" else bid
        if o.type == "limit" and (px > o.limit if o.side == "buy" else px < o.limit):
            return
        a, i = self.accts[o.player], self.ins[o.sym]
        gross = px * o.qty * self.rate(i.ccy)
        fee = gross * self.fee_bps / 1e4
        held = a.pos.get(o.sym, 0)
        if o.side == "buy":
            if a.cash < gross + fee:
                return self._close(o, "rejected", "insufficient cash")
            a.cost[o.sym] = (a.cost.get(o.sym, 0.0) * held + gross + fee) / (held + o.qty)  # fee in basis
            a.pos[o.sym] = held + o.qty
            a.cash -= gross + fee
        else:
            if held < o.qty:
                return self._close(o, "rejected", "insufficient position (no short selling)")
            a.pos[o.sym] = held - o.qty
            a.cash += gross - fee
            if not a.pos[o.sym]:
                del a.pos[o.sym], a.cost[o.sym]
        o.fill_px, o.fee = px, fee
        self._close(o, "filled")

    def _close(self, o, status, reason=""):
        o.status, o.reason = status, reason
        if status != "open":
            self.open.pop(o.id, None)
        self.dirty = True

    def orders_of(self, a, limit=200):
        return [asdict(o) for o in self.orders.values() if o.player == a.id][-limit:]

    # ---- persistence ----
    def save(self, path):
        if not self.dirty:
            return
        os.makedirs(os.path.dirname(path), exist_ok=True)
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(dict(meta=self.meta, accts=[asdict(a) for a in self.accts.values()],
                           orders=[asdict(o) for o in self.orders.values()],
                           next_oid=self.next_oid), f, ensure_ascii=False)
        os.replace(tmp, path)
        self.dirty = False

    def load(self, path):
        if not os.path.exists(path):
            return
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        if d.get("meta", {}) != self.meta:
            raise SimError(f"state {path} was made with {d.get('meta')}, now {self.meta}; "
                           "use another SIM_STATE or delete it to start a new game")
        for x in d["accts"]:
            a = Account(**x)
            self.accts[a.id] = a
            self.by_token[a.token_hash] = a.id
        for x in d["orders"]:
            o = self.orders[x["id"]] = Order(**x)
            if o.status == "open":
                self.open[o.id] = o
        self.next_oid = d["next_oid"]
