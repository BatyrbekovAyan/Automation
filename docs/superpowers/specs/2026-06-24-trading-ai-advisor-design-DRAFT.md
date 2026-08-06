# Trading AI Advisor — Design Draft (PENDING APPROVAL)

> Status: **DRAFT** — brainstorming paused mid-session on 2026-06-24. Not yet approved, no spec finalized, no implementation started. Resume by re-reading this file.

This is a **new, separate product** from the existing `Automation` (WhatsApp/Telegram bot) app — captured here only because that's where the conversation happened.

## Idea
An AI chat app "like ChatGPT but specialized in trading" — user asks what to buy/sell, chats, gets recommendations.

## Decisions locked so far (via brainstorming Q&A)
1. **Product type:** Signal advisor — names concrete trades (BUY/SELL), not just education/analysis.
   - Hard constraint surfaced & accepted: an LLM **cannot be the source** of a signal (no live prices, can't predict). The signal must come from a real system; the LLM only explains it.
2. **Signal source:** Live market data + technical indicators (deterministic rules, backtestable). LLM = "the voice."
3. **Market:** US / global **stocks**.
   - Data reality: free-tier APIs (Finnhub, Twelve Data, Alpha Vantage, FMP) give ~15-min-delayed data with rate limits; real-time costs money. Fine for hourly/daily (swing) timeframes.
4. **Where it lives:** A **separate new product** (not inside `Automation`, not reusing the Unity chat client).
5. **Stack:** **Web / PWA first** (fastest MVP; wrap native later).

## Proposed architecture (presented, awaiting feedback)
Core principle: **the LLM never invents the signal; the engine computes it, the LLM explains it.**

- **Layer 1 — Signal engine (backend):** scheduler pulls OHLCV → computes indicators (RSI, MACD, moving averages, volume) → deterministic rules → `BUY/SELL/HOLD` + strength + which rules fired + risk (stop/target) → stored in DB. Includes a **backtest** module (win-rate) for trust + rule tuning.
- **Layer 2 — LLM (Claude):** backend feeds Claude the **real computed signals/indicators** (via tool-use so it fetches fresh real numbers, never hallucinates), Claude explains the signal, answers follow-ups, discusses strategy/risk.
- **Layer 3 — Frontend (Web/PWA):** ChatGPT-style chat + watchlist/signal dashboard (cards: BUY/SELL/HOLD + mini chart) + detail view (chart w/ indicators, reasoning, backtest stats). Disclaimers everywhere ("educational, not financial advice").

Data flow: `schedule → data-API → indicators → rules → signals (DB)` → dashboard shows them; on a user question the backend hands Claude the real data → Claude explains → chat.

## Open recommendations (not yet confirmed by user)
- **Backend: Python (FastAPI)** — native for indicators/backtest (`pandas-ta`). (Alt: existing n8n — awkward for indicator math.)
- **LLM: Claude (Sonnet 4.6 for chat)** with tool-use grounding.

## Proposed MVP scope (awaiting confirmation)
- 15–30 popular US stocks; one timeframe (daily); 3 indicators + simple rules (RSI, MACD, MA crossover); signals dashboard + chat grounded in those signals; simple per-signal backtest; free-tier data API; disclaimers.

## Honest effort estimate (the user's original question)
- "Wrap an LLM in a chat": days.
- Full MVP, focused solo: ~3–6 weeks (engine+data 1–2 wk, grounded chat ~1 wk, web UI 1–2 wk, backtest+polish ~1 wk).
- Hard-forever parts: tuning rules so signals are genuinely decent; ops cost (real-time data + LLM tokens).
- Responsibility layer is mandatory: disclaimers, "ideas not advice," show reasoning + risk, no guaranteed returns (financial-advice regulation).

## NEXT STEP when resuming
Three questions were on the table for the user:
1. OK with **backend = Python (FastAPI)** and **LLM = Claude**?
2. Is the MVP scope right, or wider/narrower?
3. Anything important missing?

After answers → finalize spec at `docs/superpowers/specs/2026-06-24-trading-ai-advisor-design.md` → writing-plans skill.
