/**
 * card-proxy — Cloudflare Worker sitting between the Unity client and the Anthropic API.
 *
 * Why this exists: an API key shipped inside a mobile app binary can be extracted from it.
 * This game is carried around outdoors on real devices, so the key lives here, server-side,
 * and the client only ever talks to this Worker.
 *
 * Wire contract (fixed — the Unity client is written against exactly this):
 *   POST /generate  { "image_base64": "<base64 JPEG, no data: URI prefix>" }
 *     200 -> { name, element, rarity, stats:{hp,attack,defense,speed}, flavorText, reasoning }
 *     400 -> missing / invalid / oversized image_base64, or unparseable JSON body
 *     405 -> anything that is not POST /generate
 *     422 -> the photo is not a natural object (error string is player-facing Japanese)
 *     502 -> upstream AI call failed or returned something unusable
 *   GET /health -> { "ok": true }
 */

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

/** Model id and request shape come from the `claude-api` skill, not from memory. */
const MODEL = "claude-opus-5";
const ANTHROPIC_URL = "https://api.anthropic.com/v1/messages";
const ANTHROPIC_VERSION = "2023-06-01";

/**
 * Claude Opus 5 can decline a request via safety classifiers (HTTP 200 with
 * stop_reason "refusal"). Opting into server-side fallbacks means such a request is
 * re-served by another model inside the same call instead of failing.
 *
 * This is a beta feature. If the account is not enrolled the upstream call will 400 and
 * every request here becomes a 502 — flip this to `false` (the only change needed) if
 * that happens. Photos of branches are very unlikely to trip a classifier, so losing
 * this costs almost nothing.
 */
const ENABLE_REFUSAL_FALLBACK = true;
const FALLBACK_BETA = "server-side-fallback-2026-07-01";

/**
 * Thinking is on by default on Claude Opus 5 and `max_tokens` caps thinking *plus* the
 * response text, so leave headroom well above the ~250 tokens of JSON we actually want.
 * Truncated output would surface as unparseable JSON and burn the retry.
 */
const MAX_TOKENS = 4096;

/**
 * `effort: "low"` keeps latency and cost down; this is a short visual-classification task,
 * not deep reasoning. Note that `temperature` is *not* available on Claude Opus 5 (sending
 * it returns 400), so consistency is steered through the prompt and low effort instead.
 */
const EFFORT = "low";

/** ~5 MB of base64. Checked before spending an upstream call. */
const MAX_IMAGE_BASE64_LENGTH = 5 * 1024 * 1024;

/** The client sends JPEG bytes per the contract. */
const IMAGE_MEDIA_TYPE = "image/jpeg";

/** Shown to the player verbatim when the photographed subject is not a natural object. */
const NOT_NATURE_MESSAGE =
  "自然のものが写っていないみたい。木の枝や葉っぱ、草花、石など、外にある自然物を撮ってみてね。";

/** Used when the model returns a blank name. Never ship an empty card name to the client. */
const FALLBACK_NAME = "名もなき枝";

const MAX_NAME_CHARS = 24;
const MAX_FLAVOR_CHARS = 80;
const MAX_REASONING_CHARS = 300;

const ELEMENTS = ["Wood", "Earth", "Water"] as const;
const RARITIES = ["Common", "Uncommon", "Rare", "Epic"] as const;

type Element = (typeof ELEMENTS)[number];
type Rarity = (typeof RARITIES)[number];

/** Ranges mirror StatBlock in the Unity client, which clamps again on its side. */
const STAT_RANGES = {
  hp: { min: 20, max: 200, fallback: 60 },
  attack: { min: 5, max: 60, fallback: 20 },
  defense: { min: 0, max: 40, fallback: 10 },
  speed: { min: 1, max: 50, fallback: 20 },
} as const;

type StatName = keyof typeof STAT_RANGES;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface Env {
  /** Set with `npx wrangler secret put ANTHROPIC_API_KEY`. Never hardcoded, never logged. */
  ANTHROPIC_API_KEY?: string;
}

/** Request body of POST /generate. */
interface GenerateRequest {
  image_base64: string;
}

interface CardStats {
  hp: number;
  attack: number;
  defense: number;
  speed: number;
}

/** Success response body of POST /generate. */
interface CardResponse {
  name: string;
  element: Element;
  rarity: Rarity;
  stats: CardStats;
  flavorText: string;
  reasoning: string;
}

/** What we ask the model for: a card, plus its own verdict on whether the photo qualifies. */
interface ModelCard {
  rejected?: unknown;
  rejectReason?: unknown;
  name?: unknown;
  element?: unknown;
  rarity?: unknown;
  stats?: unknown;
  flavorText?: unknown;
  reasoning?: unknown;
}

/** Outcome of one upstream attempt, so the caller can decide whether to retry. */
type UpstreamOutcome =
  | { kind: "card"; card: ModelCard }
  | { kind: "retryable"; detail: string }
  | { kind: "fatal"; detail: string };

// ---------------------------------------------------------------------------
// HTTP helpers
// ---------------------------------------------------------------------------

/**
 * Permissive CORS. Harmless for a dev-only proxy with no cookies or credentials, and it
 * makes testing from a browser console possible.
 */
const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "POST, GET, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type",
  "Access-Control-Max-Age": "86400",
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", ...CORS_HEADERS },
  });
}

function fail(message: string, status: number): Response {
  return json({ error: message }, status);
}

// ---------------------------------------------------------------------------
// Prompt
// ---------------------------------------------------------------------------

/**
 * The instruction block. Kept as a fixed string (no interpolation) so it can be prompt-cached
 * and so results stay comparable between calls.
 */
function buildPrompt(): string {
  return `You are the card-generation engine for a Japanese smartphone game. The player photographs a natural object outdoors — usually a tree branch — and you turn that photo into a battle card.

STEP 1 — Judge the subject first.
Decide whether the photo really shows a natural object: a branch, twig, stick, plant, leaf, flower, grass, tree, bark, moss, lichen, stone, soil, or water. Living or fallen makes no difference; what matters is that a real natural object is the subject of the photo.
If the main subject is NOT a natural object — a desk, a floor, a wall, a computer or phone screen, a photo of a photo, a person, a pet, food, a vehicle, a printed picture, packaging, a toy — set "rejected": true and write a one-sentence English explanation in "rejectReason". Do not invent a card for it. Still fill the remaining fields with harmless placeholder values; they are discarded.
Otherwise set "rejected": false and "rejectReason": "".

STEP 2 — Derive the stats from concrete visual features.
- Thickness / girth: thicker → higher hp and higher defense. A thin twig gets low hp.
- Bark texture: rough, cracked, gnarled, knotty → higher defense and hp. Smooth or bare → lower.
- Length: long and slender → higher speed. Short and stubby → lower speed.
- Forks and branching: more forks, and sharp or spiky tips → higher attack. A single straight stick gets low attack.
- Colour and condition: vivid, healthy, unusual colouring nudges stats up; pale, dry, plain, or rotten nudges them down.
Weigh several features together rather than fixating on one. Ranges (integers only): hp 20-200, attack 5-60, defense 0-40, speed 1-50.
Use the whole range across different specimens: an ordinary twig belongs near the low end, an impressively thick forked mossy branch near the high end. Do not default everything to the middle.

STEP 3 — Element, from what is actually visible.
- "Wood": woody, leafy, green, living plant material.
- "Earth": soil, sand, stone, rock, dust, dry or dead material, brown and earthy tones.
- "Water": wet, damp, mossy, lichen-covered, riverside, rain-soaked, or clearly growing near water.
Pick the single best fit.

STEP 4 — Rarity, from how unusual or striking the specimen is.
- "Common": an ordinary twig or leaf, nothing remarkable.
- "Uncommon": a decent specimen with one clearly interesting feature.
- "Rare": genuinely striking — unusual shape, heavy moss, many forks, notable size.
- "Epic": reserve for something truly extraordinary. Most photos are not Epic. If you hesitate, it is not Epic.

STEP 5 — Text fields.
- "name": a Japanese card name, at most 24 characters, evocative and specific to this specimen. Japanese only — no English, no quotation marks.
- "flavorText": one Japanese sentence, at most 80 characters, in the voice of the card's world.
- "reasoning": a short English note (under 150 characters) listing the visual features that drove the stats, e.g. "thick bark, heavy moss, three forks". This is for prompt debugging.

Be consistent: judge conservatively, and let the same specimen produce the same numbers each time.
Reply with a single JSON object matching the schema and nothing else — no prose, no explanation, no markdown code fences.`;
}

/**
 * JSON Schema for structured outputs. Only keywords the API supports are used: no
 * minimum/maximum (numeric constraints are unsupported), no maxLength — the server clamps
 * and truncates in normalizeCard() anyway. Every field is required so the model cannot
 * omit one; on rejection it fills the card fields with placeholders we throw away.
 */
function outputSchema(): Record<string, unknown> {
  const statProps: Record<string, unknown> = {};
  for (const key of Object.keys(STAT_RANGES)) {
    statProps[key] = { type: "integer" };
  }

  return {
    type: "object",
    properties: {
      rejected: {
        type: "boolean",
        description: "true when the photo does not show a natural object",
      },
      rejectReason: { type: "string", description: "English reason when rejected, else empty" },
      name: { type: "string", description: "Japanese card name, <= 24 characters" },
      element: { type: "string", enum: [...ELEMENTS] },
      rarity: { type: "string", enum: [...RARITIES] },
      stats: {
        type: "object",
        properties: statProps,
        required: Object.keys(STAT_RANGES),
        additionalProperties: false,
      },
      flavorText: { type: "string", description: "One Japanese sentence, <= 80 characters" },
      reasoning: { type: "string", description: "Short English note on the visual features used" },
    },
    required: [
      "rejected",
      "rejectReason",
      "name",
      "element",
      "rarity",
      "stats",
      "flavorText",
      "reasoning",
    ],
    additionalProperties: false,
  };
}

// ---------------------------------------------------------------------------
// Upstream call
// ---------------------------------------------------------------------------

/**
 * One call to the Messages API with the image plus the prompt. Returns the parsed model
 * object, or a classification of the failure so the caller can decide about retrying.
 */
async function callClaude(apiKey: string, imageBase64: string): Promise<UpstreamOutcome> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "x-api-key": apiKey,
    "anthropic-version": ANTHROPIC_VERSION,
  };
  if (ENABLE_REFUSAL_FALLBACK) {
    headers["anthropic-beta"] = FALLBACK_BETA;
  }

  const body: Record<string, unknown> = {
    model: MODEL,
    max_tokens: MAX_TOKENS,
    // The prompt never varies, so cache it; repeated calls then read it instead of re-billing it.
    system: [
      {
        type: "text",
        text: buildPrompt(),
        cache_control: { type: "ephemeral" },
      },
    ],
    output_config: {
      effort: EFFORT,
      format: { type: "json_schema", schema: outputSchema() },
    },
    messages: [
      {
        role: "user",
        content: [
          {
            type: "image",
            source: { type: "base64", media_type: IMAGE_MEDIA_TYPE, data: imageBase64 },
          },
          { type: "text", text: "この写真を判定して、カードのJSONを1つだけ返してください。" },
        ],
      },
    ],
  };
  if (ENABLE_REFUSAL_FALLBACK) {
    // "default" lets Anthropic route by refusal category instead of us pinning a model.
    body.fallbacks = "default";
  }

  let upstream: Response;
  try {
    upstream = await fetch(ANTHROPIC_URL, {
      method: "POST",
      headers,
      body: JSON.stringify(body),
    });
  } catch (err) {
    return { kind: "retryable", detail: `network error: ${describe(err)}` };
  }

  if (!upstream.ok) {
    // Response bodies from the API do not contain the key, but keep the log terse anyway.
    const detail = `upstream HTTP ${upstream.status}: ${truncate(await safeText(upstream), 400)}`;
    // 429 and 5xx are worth one retry; other 4xx (bad key, bad request) will not fix themselves.
    const retryable = upstream.status === 429 || upstream.status >= 500;
    return retryable ? { kind: "retryable", detail } : { kind: "fatal", detail };
  }

  let payload: {
    content?: Array<{ type?: string; text?: string }>;
    stop_reason?: string;
  };
  try {
    payload = (await upstream.json()) as typeof payload;
  } catch (err) {
    return { kind: "retryable", detail: `upstream body was not JSON: ${describe(err)}` };
  }

  // A refusal is an HTTP 200 with empty or partial content — check it before reading content.
  if (payload.stop_reason === "refusal") {
    return { kind: "fatal", detail: "upstream declined the request (stop_reason=refusal)" };
  }
  if (payload.stop_reason === "max_tokens") {
    // The JSON is almost certainly cut off; retrying is the cheapest fix.
    return { kind: "retryable", detail: "upstream hit max_tokens; output likely truncated" };
  }

  // Thinking blocks may precede the answer, so pick out text blocks specifically.
  const text = (payload.content ?? [])
    .filter((block) => block?.type === "text" && typeof block.text === "string")
    .map((block) => block.text as string)
    .join("");

  if (text.trim() === "") {
    return { kind: "retryable", detail: "upstream returned no text content" };
  }

  const card = extractJson(text);
  if (card === null) {
    return { kind: "retryable", detail: `could not parse model output: ${truncate(text, 300)}` };
  }

  return { kind: "card", card };
}

/**
 * Defensive parse of the model's text. Structured outputs should already guarantee bare JSON,
 * but this proxy must not fall over if that ever changes: strip markdown fences, then fall back
 * to the outermost {...} span if there is stray prose around the object.
 */
function extractJson(raw: string): ModelCard | null {
  let text = raw.trim();

  // ```json ... ``` or ``` ... ```
  const fenced = /^```(?:json|JSON)?\s*([\s\S]*?)\s*```$/.exec(text);
  if (fenced !== null) {
    text = fenced[1].trim();
  }

  const candidates: string[] = [text];

  const first = text.indexOf("{");
  const last = text.lastIndexOf("}");
  if (first !== -1 && last > first) {
    const span = text.slice(first, last + 1);
    if (span !== text) {
      candidates.push(span);
    }
  }

  for (const candidate of candidates) {
    try {
      const parsed = JSON.parse(candidate);
      if (typeof parsed === "object" && parsed !== null && !Array.isArray(parsed)) {
        return parsed as ModelCard;
      }
    } catch {
      // Try the next candidate.
    }
  }

  return null;
}

// ---------------------------------------------------------------------------
// Normalisation
// ---------------------------------------------------------------------------

/**
 * Turn whatever the model produced into a response the client can trust. Raw model output is
 * never forwarded as-is: stats are coerced to integers and clamped, unknown element/rarity
 * values fall back to the safest option, over-long strings are truncated, and a blank name is
 * replaced. The Unity client validates the same ranges, and the two must never disagree.
 */
function normalizeCard(card: ModelCard): CardResponse {
  const rawStats = (typeof card.stats === "object" && card.stats !== null ? card.stats : {}) as Record<
    string,
    unknown
  >;

  const stats = {} as CardStats;
  for (const key of Object.keys(STAT_RANGES) as StatName[]) {
    stats[key] = clampStat(key, rawStats[key]);
  }

  const name = truncateChars(asString(card.name).trim(), MAX_NAME_CHARS);

  return {
    name: name === "" ? FALLBACK_NAME : name,
    element: ELEMENTS.includes(card.element as Element) ? (card.element as Element) : "Wood",
    rarity: RARITIES.includes(card.rarity as Rarity) ? (card.rarity as Rarity) : "Common",
    stats,
    flavorText: truncateChars(asString(card.flavorText).trim(), MAX_FLAVOR_CHARS),
    reasoning: truncateChars(asString(card.reasoning).trim(), MAX_REASONING_CHARS),
  };
}

function clampStat(key: StatName, value: unknown): number {
  const range = STAT_RANGES[key];
  const numeric = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(numeric)) {
    return range.fallback;
  }
  return Math.min(range.max, Math.max(range.min, Math.round(numeric)));
}

function asString(value: unknown): string {
  return typeof value === "string" ? value : "";
}

/** Truncate by code point, so a multi-byte character is never cut in half. */
function truncateChars(value: string, limit: number): string {
  const chars = Array.from(value);
  return chars.length <= limit ? value : chars.slice(0, limit).join("");
}

function truncate(value: string, limit: number): string {
  return value.length <= limit ? value : `${value.slice(0, limit)}…`;
}

async function safeText(response: Response): Promise<string> {
  try {
    return await response.text();
  } catch {
    return "<unreadable body>";
  }
}

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

// ---------------------------------------------------------------------------
// Payload validation
// ---------------------------------------------------------------------------

const BASE64_PATTERN = /^[A-Za-z0-9+/]+={0,2}$/;

/** Returns the cleaned base64 string, or an error message suitable for a 400. */
function validateImage(value: unknown): { ok: true; image: string } | { ok: false; error: string } {
  if (typeof value !== "string" || value.trim() === "") {
    return { ok: false, error: "image_base64 is required and must be a non-empty string." };
  }

  if (value.trimStart().startsWith("data:")) {
    return {
      ok: false,
      error: "image_base64 must be raw base64 without a data: URI prefix.",
    };
  }

  if (value.length > MAX_IMAGE_BASE64_LENGTH) {
    return {
      ok: false,
      error: `image_base64 is too large (limit ${MAX_IMAGE_BASE64_LENGTH} characters).`,
    };
  }

  // Tolerate line breaks from base64 encoders that wrap output.
  const cleaned = value.replace(/\s+/g, "");
  if (cleaned.length % 4 !== 0 || !BASE64_PATTERN.test(cleaned)) {
    return { ok: false, error: "image_base64 is not a valid base64 string." };
  }

  return { ok: true, image: cleaned };
}

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

async function handleGenerate(request: Request, env: Env): Promise<Response> {
  const apiKey = env.ANTHROPIC_API_KEY;
  if (apiKey === undefined || apiKey === "") {
    // Clear server-side signal; the client learns nothing about our configuration.
    console.error(
      "[card-proxy] ANTHROPIC_API_KEY is not set. Run: npx wrangler secret put ANTHROPIC_API_KEY",
    );
    return fail("Server is not configured.", 500);
  }

  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return fail("Request body must be valid JSON.", 400);
  }

  if (typeof body !== "object" || body === null || Array.isArray(body)) {
    return fail("Request body must be a JSON object.", 400);
  }

  const validation = validateImage((body as Partial<GenerateRequest>).image_base64);
  if (!validation.ok) {
    return fail(validation.error, 400);
  }

  // One retry: the failure modes we retry on (transport blip, truncated or unparseable output)
  // are usually transient, and a second full attempt is far cheaper than failing the player's
  // photo. Beyond that we give up rather than multiplying cost and latency.
  const attempts = 2;
  let lastDetail = "unknown upstream failure";

  for (let attempt = 1; attempt <= attempts; attempt++) {
    const outcome = await callClaude(apiKey, validation.image);

    if (outcome.kind === "card") {
      if (outcome.card.rejected === true) {
        // The 422 path: the whole point of the game is getting people outdoors looking at
        // nature, so a desk or a screen is refused rather than turned into a card. The model's
        // own wording is logged but never shown — the player sees our fixed Japanese message.
        console.log(
          `[card-proxy] rejected non-natural subject: ${truncate(asString(outcome.card.rejectReason), 200)}`,
        );
        return fail(NOT_NATURE_MESSAGE, 422);
      }
      return json(normalizeCard(outcome.card));
    }

    lastDetail = outcome.detail;
    console.error(`[card-proxy] attempt ${attempt}/${attempts} failed: ${outcome.detail}`);

    if (outcome.kind === "fatal") {
      break;
    }
  }

  console.error(`[card-proxy] giving up after ${attempts} attempt(s): ${lastDetail}`);
  return fail("Card generation failed upstream. Please try again.", 502);
}

export default {
  async fetch(request: Request, env: Env, _ctx: ExecutionContext): Promise<Response> {
    const { pathname } = new URL(request.url);

    // Preflight, so the endpoint can be exercised from a browser during development.
    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: CORS_HEADERS });
    }

    if (request.method === "GET" && pathname === "/health") {
      return json({ ok: true });
    }

    if (request.method === "POST" && pathname === "/generate") {
      return handleGenerate(request, env);
    }

    return fail("Not found. Use POST /generate.", 405);
  },
} satisfies ExportedHandler<Env>;
