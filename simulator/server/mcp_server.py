"""MCP server (stdio) letting an AI agent trade via the simulator HTTP API.

Env: SIM_URL (default http://127.0.0.1:8000), SIM_NAME (player name, default "claude"),
     SIM_TOKEN (optional; otherwise registered once and cached in data/mcp-<hash>.token, mode 600).
"""
import hashlib, os
import httpx

try:  # mcp >= 2
    from mcp.server.mcpserver import MCPServer as Server
except ImportError:  # mcp 1.x
    from mcp.server.fastmcp import FastMCP as Server

URL = os.environ.get("SIM_URL", "http://127.0.0.1:8000")
NAME = os.environ.get("SIM_NAME", "claude")
TOKEN_FILE = os.path.join(os.path.dirname(__file__), "..", "data", f"mcp-{hashlib.sha256(NAME.encode()).hexdigest()[:16]}.token")
cli = httpx.Client(base_url=URL, timeout=10)
mcp = Server("citai-market", instructions=(
    "Virtual stock/ETF/FX trading game (no real money). You compete against humans and other AIs; "
    "goal: maximize equity (JPY). Prices are delayed real quotes. No short selling. "
    "Check lot sizes in quotes before ordering."))


def _token():
    t = os.environ.get("SIM_TOKEN")
    if t:
        return t
    if os.path.exists(TOKEN_FILE):
        return open(TOKEN_FILE).read().strip()
    r = cli.post("/api/register", json={"name": NAME, "kind": "ai"})
    r.raise_for_status()
    t = r.json()["token"]
    os.makedirs(os.path.dirname(TOKEN_FILE), exist_ok=True)
    with os.fdopen(os.open(TOKEN_FILE, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600), "w") as f:
        f.write(t)
    return t


def _call(method, path, auth=True, **kw):
    try:
        h = {"Authorization": f"Bearer {_token()}"} if auth else {}
        r = cli.request(method, path, headers=h, **kw)
    except httpx.HTTPError as e:
        return {"error": f"simulator unreachable at {URL}: {e}"}
    if r.status_code == 401:
        return {"error": f"token rejected (server reset?). Delete {TOKEN_FILE} or set SIM_TOKEN / another SIM_NAME."}
    if r.status_code >= 400:
        return {"error": r.json().get("detail", r.text)}
    return r.json()


@mcp.tool()
def get_quotes() -> dict:
    """All instruments with price, bid, ask, currency, lot size, data timestamp."""
    return _call("GET", "/api/quotes", auth=False)


@mcp.tool()
def get_history(sym: str, n: int = 120) -> dict:
    """Recent price history (ts, price) for a symbol, newest last."""
    return _call("GET", f"/api/history/{sym}", auth=False, params={"n": n})


@mcp.tool()
def get_account() -> dict:
    """Your cash, equity (JPY), return %, and positions."""
    return _call("GET", "/api/me")


@mcp.tool()
def place_order(sym: str, side: str, qty: int, type: str = "market", limit: float = 0.0) -> dict:
    """Place an order. side: buy|sell. type: market|limit (limit needs price in the instrument currency)."""
    return _call("POST", "/api/orders", json=dict(sym=sym, side=side, qty=qty, type=type, limit=limit))


@mcp.tool()
def cancel_order(order_id: int) -> dict:
    """Cancel an open limit order."""
    return _call("DELETE", f"/api/orders/{order_id}")


@mcp.tool()
def list_orders() -> dict:
    """Your recent orders and their status."""
    return _call("GET", "/api/orders")


@mcp.tool()
def leaderboard() -> dict:
    """Ranking of all players (human and AI) by equity."""
    return _call("GET", "/api/leaderboard", auth=False)


if __name__ == "__main__":
    mcp.run()
