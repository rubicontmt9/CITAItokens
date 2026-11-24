// CITAI Market web client. All server text is rendered via textContent (XSS-safe).
const $ = (s) => document.querySelector(s);
const yen = (v) => Math.round(v).toLocaleString("ja-JP");
const num = (v) => (v >= 1000 ? v.toLocaleString("ja-JP", { maximumFractionDigits: 1 }) : v.toFixed(v >= 10 ? 2 : 4));
const ago = (ts) => (ts ? `${Math.round((Date.now() / 1000 - ts) / 60)}分前` : "-");
let token = localStorage.getItem("simToken") || "";
let sel = null, quotes = {}, hist = [];

function row(tbody, cells, cls = []) {
  const tr = document.createElement("tr");
  cells.forEach((c, i) => {
    const td = document.createElement("td");
    if (c instanceof Node) td.append(c); else td.textContent = c;
    if (cls[i]) td.className = cls[i];
    tr.append(td);
  });
  tbody.append(tr);
  return tr;
}

async function api(path, opt = {}) {
  const r = await fetch(path, {
    ...opt,
    headers: { "Content-Type": "application/json", ...(token && { Authorization: `Bearer ${token}` }) },
  });
  const j = await r.json().catch(() => ({}));
  if (!r.ok) throw new Error(typeof j.detail === "string" ? j.detail : JSON.stringify(j.detail));
  return j;
}

// ---- quotes / chart ----
function renderQuotes(items) {
  const tb = $("#quotes tbody");
  tb.replaceChildren();
  for (const q of items) {
    const prev = quotes[q.sym];
    const cls = prev && q.price !== prev.price ? (q.price > prev.price ? "up" : "down") : "";
    const tr = row(tb, [q.sym, q.name, q.source === "none" ? "-" : num(q.bid), q.source === "none" ? "-" : num(q.ask),
      q.lot, ago(q.ts)], ["", "l", cls, cls]);
    if (q.sym === sel) tr.className = "sel";
    tr.onclick = () => select(q.sym);
    quotes[q.sym] = q;
    if (q.sym === sel && q.ts && (!hist.length || hist.at(-1).ts !== q.ts)) hist.push({ ts: q.ts, price: q.price });
  }
  if (!sel && items.length) select(items[0].sym);
  draw();
}

async function select(sym) {
  sel = sym;
  const q = quotes[sym];
  $("#chartTitle").textContent = `${sym} ${q?.name ?? ""} (${q?.ccy ?? ""}・単位 ${q?.lot ?? ""})`;
  $("#oQty").step = $("#oQty").min = q?.lot ?? 1;
  document.querySelectorAll("#quotes tbody tr").forEach((tr) => tr.classList.toggle("sel", tr.firstChild.textContent === sym));
  hist = (await api(`/api/history/${encodeURIComponent(sym)}?n=600`)).items;
  draw();
}

function draw() {
  const c = $("#chart"), g = c.getContext("2d");
  const w = (c.width = c.clientWidth * devicePixelRatio), h = (c.height = 260 * devicePixelRatio);
  g.clearRect(0, 0, w, h);
  const css = getComputedStyle(document.documentElement);
  if (hist.length < 2) {
    g.fillStyle = css.getPropertyValue("--muted"); g.font = `${13 * devicePixelRatio}px sans-serif`;
    g.fillText("データ待ち…", 10, 30); return;
  }
  const ps = hist.map((p) => p.price), lo = Math.min(...ps), hi = Math.max(...ps), pad = 50 * devicePixelRatio;
  const x = (i) => (i / (hist.length - 1)) * (w - pad), y = (p) => h - 10 - ((p - lo) / (hi - lo || 1)) * (h - 20);
  g.strokeStyle = css.getPropertyValue(ps.at(-1) >= ps[0] ? "--up" : "--down"); g.lineWidth = 1.5 * devicePixelRatio;
  g.beginPath(); ps.forEach((p, i) => (i ? g.lineTo(x(i), y(p)) : g.moveTo(x(i), y(p)))); g.stroke();
  g.fillStyle = css.getPropertyValue("--muted"); g.font = `${11 * devicePixelRatio}px sans-serif`;
  g.fillText(num(hi), w - pad + 4, 14 * devicePixelRatio); g.fillText(num(lo), w - pad + 4, h - 4);
}

// ---- account ----
async function refreshAccount() {
  if (!token) return;
  try {
    const [me, od] = await Promise.all([api("/api/me"), api("/api/orders")]);
    $("#login").style.display = "none";
    $("#who").textContent = `${me.name} (${me.kind === "ai" ? "AI" : "人間"})`;
    $("#summary").textContent = `現金 ${yen(me.cash)}円 / 総資産 ${yen(me.equity)}円 / 損益率 ${me.ret_pct.toFixed(2)}%`;
    const pb = $("#pos tbody"); pb.replaceChildren();
    me.items.forEach((p) => row(pb, [p.sym, p.qty, yen(p.avg_cost), yen(p.value), yen(p.pnl)], ["", "", "", "", p.pnl >= 0 ? "up" : "down"]));
    const ob = $("#ord tbody"); ob.replaceChildren();
    od.items.slice(-30).reverse().forEach((o) => {
      let btn = "";
      if (o.status === "open") {
        btn = document.createElement("button"); btn.textContent = "取消";
        btn.onclick = () => api(`/api/orders/${o.id}`, { method: "DELETE" }).then(refreshAccount).catch((e) => msg(e.message));
      }
      row(ob, [o.id, o.sym, o.side === "buy" ? "買" : "売", o.qty, o.type === "limit" ? `指 ${num(o.limit)}` : "成",
        o.reason || o.status, o.fill_px ? num(o.fill_px) : "-", btn]);
    });
  } catch (e) {
    if (e.message === "invalid token") { token = ""; localStorage.removeItem("simToken"); $("#login").style.display = ""; }
  }
}

function renderBoard(items) {
  const tb = $("#board tbody"); tb.replaceChildren();
  items.forEach((r, i) => row(tb, [i + 1, r.name, r.kind === "ai" ? "AI" : "人間", yen(r.equity), `${r.ret_pct.toFixed(2)}%`],
    ["", "l", "", "", r.ret_pct >= 0 ? "up" : "down"]));
}

const msg = (t) => ($("#msg").textContent = t);

// ---- forms ----
$("#regForm").onsubmit = async (e) => {
  e.preventDefault();
  try {
    const r = await api("/api/register", { method: "POST", body: JSON.stringify({ name: $("#regName").value, kind: $("#regKind").value }) });
    token = r.token; localStorage.setItem("simToken", token);
    alert(`登録完了。トークンを控えてください:\n${token}`);
    refreshAccount();
  } catch (err) { alert(err.message); }
};
$("#tokForm").onsubmit = (e) => { e.preventDefault(); token = $("#tokIn").value.trim(); localStorage.setItem("simToken", token); refreshAccount(); };
$("#oType").onchange = () => ($("#oLimit").disabled = $("#oType").value !== "limit");
$("#orderForm").onsubmit = async (e) => {
  e.preventDefault();
  if (!token) return msg("先に登録してください");
  try {
    const o = await api("/api/orders", { method: "POST", body: JSON.stringify({
      sym: sel, side: $("#oSide").value, qty: +$("#oQty").value, type: $("#oType").value, limit: +$("#oLimit").value || 0 }) });
    msg(`#${o.id} ${o.status}${o.reason ? ": " + o.reason : ""}${o.fill_px ? " @" + num(o.fill_px) : ""}`);
    refreshAccount();
  } catch (err) { msg(err.message); }
};

// ---- live ----
function connect() {
  const ws = new WebSocket(`${location.protocol === "https:" ? "wss" : "ws"}://${location.host}/ws`);
  ws.onopen = () => ($("#status").textContent = "接続中 ●");
  ws.onclose = () => { $("#status").textContent = "切断 — 再接続中…"; setTimeout(connect, 2000); };
  ws.onmessage = (ev) => { const m = JSON.parse(ev.data); renderQuotes(m.quotes); renderBoard(m.board); refreshAccount(); };
}
api("/api/info").then((i) => ($("#status").title = `feed=${i.feed} poll=${i.poll}s fee=${i.fee_bps}bps`));
api("/api/quotes").then((r) => renderQuotes(r.items));
api("/api/leaderboard").then((r) => renderBoard(r.items));
refreshAccount();
connect();
window.onresize = draw;
