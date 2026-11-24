"""HTTP/WebSocket API + static web UI. Run: python -m server.app (from simulator/)."""
import asyncio, contextlib, json, logging, os, secrets, time
from collections import deque
from typing import Literal
from fastapi import Depends, FastAPI, Header, HTTPException, Request, WebSocket, WebSocketDisconnect
from fastapi.middleware.trustedhost import TrustedHostMiddleware
from fastapi.responses import JSONResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field
from . import config as C
from .engine import Exchange, Instrument, Quote, SimError
from .feeds import SyntheticFeed, YahooFeed

log = logging.getLogger("sim")
INS = [Instrument(*x) for x in C.INSTRUMENTS]
ex = Exchange(INS, C.CASH, C.FEE_BPS, C.SPREAD_BPS, stale_sec=max(60, 3 * C.POLL),
              meta=dict(feed=C.FEED, cash=C.CASH))
hist: dict[str, deque] = {i.sym: deque(maxlen=3000) for i in INS}
clients: set[WebSocket] = set()
MAX_WS = 100
feed = None


def restore_quotes():
    """Resume from saved prices of the same feed so positions are not revalued on restart."""
    p = C.STATE + ".quotes"
    try:
        with open(p, encoding="utf-8") as f:
            for s, q in json.load(f).items():
                if s in ex.ins and q["source"] == C.FEED:
                    ex.quotes[s] = Quote(**{**q, "fetched": 0.0})  # stale until the feed confirms
    except FileNotFoundError:
        pass
    except (ValueError, TypeError, KeyError):
        log.warning("ignoring corrupt %s", p)


_saved = [0.0]


def save(force=False):
    os.makedirs(os.path.dirname(C.STATE) or ".", exist_ok=True)
    ex.save(C.STATE)
    if force or time.time() - _saved[0] > 10:  # prices: at most every 10 s
        tmp = C.STATE + ".quotes.tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump({s: q.__dict__ for s, q in ex.quotes.items()}, f)
        os.replace(tmp, C.STATE + ".quotes")
        _saved[0] = time.time()


def push_hist(quotes):
    for s, q in quotes.items():
        h = hist[s]
        if not h or h[-1][0] != q.ts:
            h.append((q.ts, q.price))


async def broadcast():
    msg = json.dumps(dict(type="tick", quotes=[ex.quote_view(s) for s in ex.ins], board=ex.board()))


    async def send(ws):  # a stalled client must not block the tick loop
        try:
            await asyncio.wait_for(ws.send_text(msg), 2)
        except Exception:
            clients.discard(ws)
    await asyncio.gather(*(send(ws) for ws in list(clients)))


async def loop():
    first = True
    while True:
        try:
            q, h = await feed.poll(with_hist=True) if first else (await feed.poll(), {})
            for s, rows in h.items():
                hist[s].extend(rows)
            first = first and not q
            ex.update(q)
            push_hist(q)
            await broadcast()
            save()
            if feed.errors:
                log.warning("feed errors: %s", feed.errors)
        except Exception:
            log.exception("tick failed")
        await asyncio.sleep(C.POLL)


@contextlib.asynccontextmanager
async def lifespan(_):
    global feed
    ex.load(C.STATE)
    restore_quotes()
    if C.FEED == "synthetic":
        feed = SyntheticFeed(INS, C.SEED if C.SEED is not None else secrets.randbits(64), C.SPEED, C.POLL)
        feed.px.update({s: q.price for s, q in ex.quotes.items()})
    else:
        feed = YahooFeed(INS)
    task = asyncio.create_task(loop())
    yield
    task.cancel()
    save(force=True)


app = FastAPI(title="CITAI Market Simulator", lifespan=lifespan)
app.add_middleware(TrustedHostMiddleware, allowed_hosts=C.HOSTS)


@app.exception_handler(SimError)
async def _sim_err(_, e):
    return JSONResponse({"detail": str(e)}, status_code=400)


# ---- auth + DoS guard (token bucket per key; speed differences otherwise allowed) ----
_bucket: dict[str, list] = {}


def throttle(key):
    now = time.monotonic()
    if len(_bucket) > 10000:
        _bucket.clear()
    tok, last = _bucket.get(key, [C.RATE, now])
    tok = min(C.RATE, tok + (now - last) * C.RATE)
    if tok < 1:
        raise HTTPException(429, "rate limit")
    _bucket[key] = [tok - 1, now]


async def player(req: Request, authorization: str = Header("")):
    throttle("ip:" + (req.client.host if req.client else "?"))
    try:
        a = ex.auth(authorization.removeprefix("Bearer ").strip())
    except SimError as e:
        raise HTTPException(401, str(e))
    throttle(a.id)
    return a


# ---- schemas ----
class RegisterIn(BaseModel):
    name: str = Field(min_length=1, max_length=24)
    kind: Literal["human", "ai"]


class OrderIn(BaseModel):
    sym: str = Field(max_length=16)
    side: Literal["buy", "sell"]
    qty: int = Field(gt=0, le=10**9)
    type: Literal["market", "limit"] = "market"
    limit: float = Field(0.0, ge=0, lt=1e12, allow_inf_nan=False)


# ---- public ----
@app.get("/api/info")
async def info():
    return dict(feed=C.FEED, poll=C.POLL, cash=C.CASH, fee_bps=C.FEE_BPS, spread_bps=C.SPREAD_BPS,
                errors=feed.errors if feed else {}, server_time=time.time())


@app.get("/api/quotes")
async def quotes():
    return {"items": [ex.quote_view(s) for s in ex.ins]}


@app.get("/api/history/{sym}")
async def history(sym: str, n: int = 500):
    if sym not in hist:
        raise HTTPException(404, "unknown symbol")
    rows = list(hist[sym])[-max(1, min(n, 3000)):]
    return {"sym": sym, "items": [{"ts": t, "price": p} for t, p in rows]}


@app.get("/api/leaderboard")
async def leaderboard():
    return {"items": ex.board()}


@app.post("/api/register")
async def register(body: RegisterIn, req: Request):
    throttle("reg:" + (req.client.host if req.client else "?"))
    a, token = ex.register(body.name, body.kind)
    save()
    return dict(id=a.id, name=a.name, kind=a.kind, token=token)


# ---- authenticated ----
@app.get("/api/me")
async def me(a=Depends(player)):
    return ex.account_view(a)


@app.get("/api/orders")
async def orders(a=Depends(player)):
    return {"items": ex.orders_of(a)}


@app.post("/api/orders")
async def place(body: OrderIn, a=Depends(player)):
    o = ex.place(a, body.sym, body.side, body.qty, body.type, body.limit)
    return o.__dict__


@app.delete("/api/orders/{oid}")
async def cancel(oid: int, a=Depends(player)):
    return ex.cancel(a, oid).__dict__


@app.websocket("/ws")
async def ws(sock: WebSocket):
    if len(clients) >= MAX_WS:
        return await sock.close(code=1013)
    await sock.accept()
    clients.add(sock)
    try:
        while True:
            await sock.receive_text()  # ignore input; keepalive only
    except WebSocketDisconnect:
        pass
    finally:
        clients.discard(sock)


app.mount("/", StaticFiles(directory=os.path.join(os.path.dirname(__file__), "..", "web"), html=True))

if __name__ == "__main__":
    import uvicorn
    logging.basicConfig(level=logging.INFO)
    uvicorn.run(app, host=C.HOST, port=C.PORT)
