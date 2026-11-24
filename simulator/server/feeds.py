"""Price feeds. YahooFeed = real delayed prices; SyntheticFeed = seeded random walk."""
import asyncio, math, random, time
from urllib.parse import quote
import httpx
from .engine import Quote

YAHOO = "https://query1.finance.yahoo.com/v8/finance/chart/{}"
VOL = {"stock": 0.30, "etf": 0.18, "fx": 0.08}  # annualized
YEAR_SEC = 252 * 6.5 * 3600                     # trading seconds per year


def parse_chart(js):
    """Yahoo chart JSON -> (Quote, [(ts, close), ...])."""
    r = js["chart"]["result"][0]
    m = r["meta"]
    closes = (r.get("indicators", {}).get("quote") or [{}])[0].get("close") or []
    hist = [(float(t), float(c)) for t, c in zip(r.get("timestamp") or [], closes) if c is not None]
    return Quote(float(m["regularMarketPrice"]), float(m["regularMarketTime"]), "yahoo"), hist


class YahooFeed:
    """Unofficial Yahoo Finance endpoint; delayed (~15-20 min for TSE). Personal/local use only."""

    def __init__(self, instruments):
        self.syms = [i.sym for i in instruments]
        self.cli = httpx.AsyncClient(timeout=10, headers={"User-Agent": "Mozilla/5.0"})
        self.errors: dict[str, str] = {}

    async def _one(self, sym, rng):
        try:
            r = await self.cli.get(YAHOO.format(quote(sym, safe="=")), params={"interval": "1m", "range": rng})
            r.raise_for_status()
            self.errors.pop(sym, None)
            return sym, parse_chart(r.json())
        except Exception as e:  # network/format error: keep last price
            self.errors[sym] = f"{type(e).__name__}: {e}"[:200]
            return sym, None

    async def poll(self, with_hist=False):
        res = await asyncio.gather(*(self._one(s, "1d") for s in self.syms))
        q = {s: v[0] for s, v in res if v}
        return (q, {s: v[1] for s, v in res if v}) if with_hist else q


class SyntheticFeed:
    """Geometric Brownian motion + rare jumps. Same SEED => same market path."""

    def __init__(self, instruments, seed, speed, poll):
        self.rng = random.Random(seed)
        self.ins = instruments
        self.px = {i.sym: float(i.seed_px) for i in instruments}
        self.dt = poll * speed / YEAR_SEC
        self.errors: dict[str, str] = {}

    def step(self):
        for i in self.ins:
            s = VOL[i.kind]
            z = self.rng.gauss(0, 1)
            j = self.rng.gauss(0, 0.02) if self.rng.random() < 0.002 else 0.0
            self.px[i.sym] *= math.exp(-0.5 * s * s * self.dt + s * math.sqrt(self.dt) * z + j)
        return {k: Quote(round(v, 4), time.time(), "synthetic") for k, v in self.px.items()}

    async def poll(self, with_hist=False):
        q = self.step()
        return (q, {}) if with_hist else q
