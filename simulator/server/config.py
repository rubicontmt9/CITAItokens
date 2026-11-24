"""Settings via environment variables (SIM_*)."""
import os

def _env(k, d, t=str):
    return t(os.environ.get(f"SIM_{k}", d))

FEED = _env("FEED", "yahoo")            # yahoo | synthetic
POLL = _env("POLL", 20 if FEED == "yahoo" else 1, float)  # seconds between ticks
SPEED = _env("SPEED", 60, float)        # synthetic: market-seconds per real second
SEED = int(os.environ["SIM_SEED"]) if "SIM_SEED" in os.environ else None  # unset = unpredictable
CASH = _env("CASH", 10_000_000, float)  # starting cash (virtual JPY)
FEE_BPS = _env("FEE_BPS", 5, float)     # commission, basis points of notional
HOST = _env("HOST", "127.0.0.1")
PORT = _env("PORT", 8000, int)
HOSTS = _env("HOSTS", "127.0.0.1,localhost").split(",")  # allowed Host headers
RATE = _env("RATE", 50, float)          # req/s per player (DoS guard only)
STATE = _env("STATE", os.path.join(os.path.dirname(__file__), "..", "data", "state.json"))

# kind -> full bid/ask spread in bps
SPREAD_BPS = {"stock": 10, "etf": 6, "fx": 2}

# sym, name, quote ccy, kind, lot size, synthetic seed price
INSTRUMENTS = [
    ("7203.T", "トヨタ自動車", "JPY", "stock", 100, 2800),
    ("6758.T", "ソニーG", "JPY", "stock", 100, 3500),
    ("8306.T", "三菱UFJ FG", "JPY", "stock", 100, 1800),
    ("9984.T", "ソフトバンクG", "JPY", "stock", 100, 9000),
    ("1321.T", "日経225連動型ETF", "JPY", "etf", 1, 40000),
    ("AAPL", "Apple", "USD", "stock", 1, 230),
    ("MSFT", "Microsoft", "USD", "stock", 1, 420),
    ("NVDA", "NVIDIA", "USD", "stock", 1, 140),
    ("SPY", "S&P500 ETF", "USD", "etf", 1, 580),
    ("USDJPY=X", "米ドル/円", "JPY", "fx", 1000, 150),
    ("EURJPY=X", "ユーロ/円", "JPY", "fx", 1000, 160),
]
