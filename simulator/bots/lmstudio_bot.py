"""LLM trader via LM Studio (OpenAI-compatible API, local). Usage: python bots/lmstudio_bot.py NAME [MODEL]
Start LM Studio's local server first (default http://localhost:1234)."""
import json, os, re, sys, time
import httpx

SIM, LLM = "http://127.0.0.1:8000", os.environ.get("LMSTUDIO_URL", "http://localhost:1234/v1")
NAME = sys.argv[1] if len(sys.argv) > 1 else "lmstudio-bot"
MODEL = sys.argv[2] if len(sys.argv) > 2 else "local-model"
INTERVAL = float(os.environ.get("BOT_INTERVAL", 30))
sim = httpx.Client(base_url=SIM, timeout=10)
tok = os.environ.get("SIM_TOKEN") or sim.post("/api/register", json={"name": NAME, "kind": "ai"}).json().get("token")
if not tok:
    sys.exit("register failed (name taken? set SIM_TOKEN)")
H = {"Authorization": f"Bearer {tok}"}
print("token:", tok)

PROMPT = """You are a trader in a virtual market game (no real money). Maximize equity in JPY. No short selling.
qty must be a multiple of lot. Reply ONLY with JSON: {"orders":[{"sym":"...","side":"buy|sell","qty":N}],"why":"..."}
Empty orders list = hold."""

while True:
    try:
        state = dict(account=sim.get("/api/me", headers=H).json(),
                     quotes=[{k: q[k] for k in ("sym", "name", "ccy", "lot", "bid", "ask", "ts")} for q in sim.get("/api/quotes").json()["items"]])
        r = httpx.post(f"{LLM}/chat/completions", timeout=120, json=dict(model=MODEL, temperature=0.3, messages=[
            {"role": "system", "content": PROMPT}, {"role": "user", "content": json.dumps(state, ensure_ascii=False)}]))
        text = r.json()["choices"][0]["message"]["content"]
        plan = json.loads(re.search(r"\{.*\}", text, re.S).group())
        print("why:", plan.get("why"))
        for o in plan.get("orders", [])[:5]:  # cap per turn
            res = sim.post("/api/orders", headers=H, json=dict(sym=str(o["sym"]), side=str(o["side"]), qty=int(o["qty"])))
            print(o, "->", res.json())
    except Exception as e:  # bad LLM output must not crash the bot
        print("skip:", type(e).__name__, e)
    time.sleep(INTERVAL)
