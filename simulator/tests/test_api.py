import os, tempfile
os.environ.update(SIM_FEED="synthetic", SIM_POLL="0.05", SIM_HOSTS="testserver,127.0.0.1",
                  SIM_STATE=os.path.join(tempfile.mkdtemp(), "s.json"))
import time
from fastapi.testclient import TestClient
from server.app import app


def test_flow():
    with TestClient(app) as c:
        time.sleep(0.3)
        qs = c.get("/api/quotes").json()["items"]
        assert all(q["source"] == "synthetic" for q in qs)
        tok = c.post("/api/register", json={"name": "テスト太郎", "kind": "human"}).json()["token"]
        H = {"Authorization": f"Bearer {tok}"}
        assert c.get("/api/me").status_code == 401
        o = c.post("/api/orders", headers=H, json={"sym": "7203.T", "side": "buy", "qty": 100}).json()
        assert o["status"] == "filled"
        assert c.post("/api/orders", headers=H, json={"sym": "7203.T", "side": "buy", "qty": 100, "limit": "nan"}).status_code == 422
        me = c.get("/api/me", headers=H).json()
        assert me["items"][0]["sym"] == "7203.T"
        assert c.get("/api/leaderboard").json()["items"][0]["name"] == "テスト太郎"
        assert len(c.get("/api/history/7203.T").json()["items"]) > 1
        with c.websocket_connect("/ws") as ws:
            assert ws.receive_json()["type"] == "tick"
        assert c.get("/api/quotes", headers={"Host": "evil.example"}).status_code == 400  # DNS rebinding guard
        assert "CITAI Market" in c.get("/").text
