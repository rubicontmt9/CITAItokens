"""Simple momentum bot (baseline AI). Usage: python bots/rule_bot.py NAME"""
import sys, time
import httpx

URL, NAME = "http://127.0.0.1:8000", (sys.argv[1] if len(sys.argv) > 1 else "momentum-bot")
cli = httpx.Client(base_url=URL, timeout=10)
tok = cli.post("/api/register", json={"name": NAME, "kind": "ai"}).json().get("token")
if not tok:
    sys.exit("register failed (name taken?)")
H = {"Authorization": f"Bearer {tok}"}
print("token:", tok)

def step():
    me = cli.get("/api/me", headers=H).json()
    held = {p["sym"]: p["qty"] for p in me["items"]}
    qs = cli.get("/api/quotes").json()["items"]
    fx = {"JPY": 1.0, **{q["sym"][:3]: q["price"] for q in qs if q["kind"] == "fx" and q["price"] > 0}}
    for q in qs:
        h = [x["price"] for x in cli.get(f"/api/history/{q['sym']}", params={"n": 30}).json()["items"]]
        if len(h) < 30 or q["source"] == "none" or q["ccy"] not in fx:
            continue
        mom = h[-1] / (sum(h) / len(h)) - 1
        if mom > 0.002 and q["sym"] not in held and me["cash"] > me["equity"] * 0.15:
            lots = max(1, int(me["equity"] * 0.1 / (q["ask"] * q["lot"] * fx[q["ccy"]])))
            cli.post("/api/orders", headers=H, json=dict(sym=q["sym"], side="buy", qty=q["lot"] * lots))
        elif mom < -0.002 and q["sym"] in held:
            cli.post("/api/orders", headers=H, json=dict(sym=q["sym"], side="sell", qty=held[q["sym"]]))


while True:
    try:
        step()
    except Exception as e:  # server hiccup / 429: keep running
        print("skip:", type(e).__name__, e)
    time.sleep(5)
