#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Billing consolidation SQL — ONE home for the statements that move a subscription between RevenueCat identities
(A2 «защита платящих», 2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
§3 A2, A3). The generators wire these texts; nobody keeps a copy:

  CONSOLIDATE_SQL(params)  the alias merge — a reinstall: RevenueCat names the old identity in `aliases[]` of an event
                           delivered under the new one. RevenueCat Events' `Consolidate Aliases` renders it with
                           RC_CONSOLIDATE; Task A3's identity-heal renders the same CTE chain with its own parameters.
  TRANSFER_SQL             the cross-account TRANSFER (RevenueCat Events' `Transfer Subscriber`).

A2 changed the alias merge in two ways (the TRANSFER is unchanged — Task 16/17 of Block 1):

  (1) the PLAN follows an active alias when nothing else would set it. A reinstall's first event can be a top-up
      (NON_RENEWING_PURCHASE carries no plan): the balance moved and the alias was retired, but the new identity stayed
      trial/trialing until the next renewal — up to a month of trial limits, and the lifecycle sweep deleting the bots
      created after the reinstall. Now `plan_source` takes the ACTIVE alias row of a PAID tier (start/business/network —
      never `none`, whose base quota is 0) with the latest period (Transfer's `src` order) when the event carries no plan, and `credited` hands it over in its ONE upsert (a row cannot be changed by two
      CTEs of one statement) — only to a NEVER-PAID seed without a period (TAKE: a fresh install's trialing seed, or that
      seed as the lifecycle sweep's Demote Trialing leaves it, trial/expired — nothing paid is ever downgraded or
      overwritten), and creates a missing recipient row even at a zero balance so the plan is not lost with the alias.
      A settled alias set (it rides every event for the life of the account) still writes no row version: the update runs
      only when there is money or a plan to hand over.
  (2) REKEY_CTE — the recipient takes over the live bot_profiles rows of the named install (spec §1: the row's
      app_user_id follows the owning install's billing id). RevenueCat Events renders install = SQL NULL — its webhook has
      no install, so nothing is ever re-keyed there; Task A3's identity-heal passes the token's install.

apply-billing-consolidation.py writes both texts into the canonical RevenueCat Events JSON — it owns ONLY the two
`query` fields; Map Event, the queryReplacement arrays and the wiring stay hand-authored. verify-billing-consolidation.py
pins node text == module text and the shape the behaviour rests on. Every statement names its parameters in words (V1: no
dollar + digit inside a comment or a literal); the CTE chain takes its expressions as tokens, so a caller with another
parameter order renders the same chain. Behaviour: tests/test_billing_consolidation.py (real SQL on the staging Postgres).

    python3 Tools/n8n/billing_consolidation.py --print consolidate|transfer    # the RevenueCat Events texts
"""
import re
import sys

# ------------------------------------------------------------------------------------------------- the alias merge
# The CTE chain (alias_ids → locked → plan_source → credited → retired → carried → rekeyed). Tokens: <TO> = the recipient
# app user id (text); <ALIASES> = the JSON array (text) of the alias ids, the recipient already excluded by the caller;
# <EVENT_PLAN> = the plan the event itself carries (text, NULL = none: the alias's plan may be handed over); <INSTALL> = the
# install whose live bot_profiles rows follow the recipient (text, NULL = re-key nothing).

# The recipient row may take the alias's plan: it is still a NEVER-PAID seed without a period and there IS a plan
# to hand over. A never-paid seed is Create's trial/trialing — or, since the final review of Part A (I-1,
# 2026-10-15), the same seed after the lifecycle sweep's trial expiry: `Demote Trialing` (4d17h after the first
# bot) leaves it plan 'trial', status 'expired', period NULL, while on iOS (StoreKit 1 restores nothing by itself)
# RevenueCat's alias merge — and so the heal — usually comes LATER («Восстановить», a purchase on the trial
# paywall); without this the merge retired the paid alias and the paying owner stayed locked out until his next
# renewal. A plan that was ever paid (start/business/network) or `none` never matches, nor does anything with a
# period: nothing paid is ever overwritten. Evaluated on the conflicting row under its lock (`subscribers.` in
# ON CONFLICT DO UPDATE), never on a snapshot.
TAKE = ("(subscribers.status = 'trialing' or (subscribers.plan = 'trial' and subscribers.status = 'expired'))\n"
        "                     and subscribers.current_period_end is null and exists (select 1 from plan_source)")


def REKEY_CTE(to, install, from_ids="alias_ids"):
    """The body of the re-key CTE: the live bot_profiles rows of `install` that name one of `from_ids` (a CTE of
    app_user_id values) move to the recipient `to`. `install` NULL matches no row (`owner_id = NULL` is never true)."""
    return f"""  -- A2 (спека §1): app_user_id строки bot_profiles следует за биллинг-ID владеющей установки — иначе
  -- консолидация гасит старый ID, а боты, заново привязанные к нему, замолкают. ТОЛЬКО живые строки ТОЙ установки,
  -- которую назвал вызывающий; без установки (вебхук RevenueCat её не знает) сравнение с NULL ложно для каждой
  -- строки — не перепривязывается ничего. Строки, уже записанные на получателя, не переписываются.
  update bot_profiles b set app_user_id = {to}
  where b.app_user_id in (select app_user_id from {from_ids})
    and b.app_user_id <> {to}
    and b.owner_id = {install}
    and b.deleted_at is null
  returning b.profile_id
"""


CONSOLIDATE_BODY = """with alias_ids as (
  select distinct value as app_user_id from jsonb_array_elements_text(<ALIASES>::jsonb) as t(value)
), locked as (
  -- FOR UPDATE: инстанс доставляет события одного подписчика параллельно (3150/3151 перекрылись),
  -- а снимочный sum() в такой гонке начислил бы баланс дважды. Блокировка сериализует и
  -- перечитывает свежую версию, поэтому «второй» увидит уже 0.
  select * from subscribers
  where app_user_id in (select app_user_id from alias_ids)
    and app_user_id <> <TO>
  for update
), plan_source as (
  -- A2 «защита платящих» (2026-10-14): снимок плана, который может переехать к получателю. Та же «самая живая»
  -- строка-алиас, что src в Transfer Subscriber (позднейший период, затем свежайшая запись), но ТОЛЬКО оплаченная:
  -- status = 'active' (expired/grace/trialing — не подписка) и план из платных (none — неизвестное право с базовой
  -- квотой 0 против 150 у триала: получатель потерял бы ответы), и ТОЛЬКО когда событие само плана не несёт (план
  -- события пуст: топ-ап NON_RENEWING_PURCHASE, EXPIRATION, BILLING_ISSUE …). Событие с планом кладёт свой,
  -- авторитетный, следом в Upsert Subscriber. Читается из locked — из версии под блокировкой: вторая из гонки
  -- доставок видит уже погашенную строку и не переносит ничего.
  select plan, status, current_period_end, product_id
  from locked
  where status = 'active' and plan in ('start', 'business', 'network') and <EVENT_PLAN> is null
  order by current_period_end desc nulls last, updated_at desc nulls last
  limit 1
), credited as (
  -- Баланс алиасов (сумма под блокировкой; переезжает только положительная, как и раньше) — получателю всегда;
  -- снимок plan_source — в ТОМ ЖЕ upsert'е (одну строку нельзя менять из двух CTE одного statement'а) и ТОЛЬКО
  -- никогда не платившей затравке без периода: trialing свежей установки или она же, погашенная свипом (Demote
  -- Trialing: trial/expired — склейка на iOS обычно приходит позже, финальное ревью I-1): ничего не понижает,
  -- получатель с оплаченным планом или периодом снимок не берёт, а деньги всё равно переезжают. Строки получателя
  -- ещё нет — создаём её (план алиаса, если он есть, иначе trial/trialing) даже при нулевом балансе: иначе план,
  -- который ниже гасится вместе с алиасом, пропал бы. Обновление идёт, ТОЛЬКО когда есть деньги или снимок: settled-набор алиасов (он
  -- приезжает на каждом событии до конца жизни аккаунта) не плодит версии строк.
  insert into subscribers (app_user_id, plan, status, current_period_end, product_id, topup_balance, trial_started_at, updated_at)
  select <TO>, coalesce(s.plan, 'trial'), coalesce(s.status, 'trialing'), s.current_period_end, s.product_id,
         greatest(x.total, 0), now(), now()
  from (select coalesce(sum(topup_balance), 0) as total from locked) x
  left join plan_source s on true
  on conflict (app_user_id) do update set
    topup_balance = subscribers.topup_balance + excluded.topup_balance,
    plan = case when <TAKE> then excluded.plan else subscribers.plan end,
    status = case when <TAKE> then excluded.status else subscribers.status end,
    current_period_end = case when <TAKE> then excluded.current_period_end else subscribers.current_period_end end,
    product_id = case when <TAKE> then excluded.product_id else subscribers.product_id end,
    updated_at = now()
  where excluded.topup_balance > 0 or (<TAKE>)
  returning app_user_id, plan, topup_balance
), retired as (
  update subscribers s set
    status = 'expired',   -- гасит автоответы старой личности через существующий квота-гейт
    topup_balance = 0,    -- баланс уже переехал выше
    -- тот же клэмп, что и на пути TRANSFER: не мгновенное удаление профилей (NULL стал бы
    -- кандидатом свипа сразу же), а ровно 3 дня grace, как у любого другого churn
    current_period_end = least(coalesce(s.current_period_end, now()), now()),
    updated_at = now()
  where s.app_user_id in (select app_user_id from locked)
    and (s.status <> 'expired' or s.topup_balance <> 0
         or s.current_period_end is null or s.current_period_end > now())
  returning s.app_user_id
)
, carried as (
  -- I-4 (Task 17a): ОПЛАЧЕННЫЙ РАСХОД переезжает вместе с деньгами. dialog_counts ключится
  -- по app_user_id, поэтому переустановка обнуляла месячный счётчик: подписка и баланс
  -- переезжали, а «300 из 300» превращалось в «0 из 300» — целый месяц квоты бесплатно.
  -- Копируем строки ТЕКУЩЕГО месяца (окно то же, что у Count Dialog и Get Usage) на живой id.
  -- СУЩЕСТВОВАНИЕ строки И ЕСТЬ счёт (PK user+chat+date), поэтому on conflict do nothing
  -- идемпотентен ПО ПОСТРОЕНИЮ: settled-набор алиасов приезжает на каждом событии до конца
  -- жизни аккаунта, и на повторе КАЖДАЯ строка конфликтует — ноль новых версий строк, тот же
  -- инвариант, что у `credited`/`retired` выше. distinct нужен, когда два алиаса несут один и
  -- тот же (chat, date): без него один INSERT дважды вставлял бы один ключ.
  -- Старые строки НЕ удаляем: их никто не читает (все запросы ключуются по живому
  -- app_user_id), а удаление стоило бы истории задаром.
  insert into dialog_counts (app_user_id, chat_id, d)
  select distinct <TO>, dc.chat_id, dc.d
  from dialog_counts dc
  where dc.app_user_id in (select app_user_id from locked)
    and date_trunc('month', dc.d)
        = date_trunc('month', (now() at time zone 'Asia/Almaty')::date)
  on conflict (app_user_id, chat_id, d) do nothing
  returning 1
)
, rekeyed as (
<REKEY>)
"""

# RevenueCat Events: the comment head (its parameter order) and the final select that hands the event's payload on to
# Upsert Subscriber (a Postgres node drops its input json).
RC_CONSOLIDATE_HEAD = """-- СКЛЕЙКА ИДЕНТИЧНОСТЕЙ (Task 16, fix round). RC НЕ шлёт TRANSFER при переустановке на том же
-- устройстве — он СКЛЕИВАЕТ идентичности: событие приходит под НОВЫМ app_user_id, старый лежит
-- в original_app_user_id и aliases[] (живые события 3150/3151/3153 от 2026-08-26).
-- Параметры по порядку: текущий app_user_id, JSON-массив алиасов (текущий id уже исключён в Map
-- Event), затем с третьего по восьмой — payload события (восьмой — id события RevenueCat для журнала
-- Upsert Subscriber, A1), который эта нода ОБЯЗАНА пронести дальше: Postgres-нода отдаёт
-- только результат запроса и роняет входной json (та же ловушка, что у Count Dialog в Task 9).
--
-- Перенос баланса и гашение — на каждом событии. Снимок плана строки-алиаса (A2, CTE plan_source) — ТОЛЬКО из
-- АКТИВНОЙ строки платного плана, ТОЛЬКО когда событие само плана не несёт (третий параметр пуст: топ-ап первым событием после
-- переустановки, EXPIRATION, BILLING_ISSUE …) и ТОЛЬКО никогда не платившему получателю без периода (trialing или trial/expired,
-- как его оставляет свип). Событие с планом едет следом в Upsert Subscriber и кладёт
-- свои авторитетные план/статус/период; получатель с оплаченным планом или периодом не трогается — риска понижения на
-- пути, который срабатывает ВЕЧНО, на каждом событии, по-прежнему нет. Перепривязки bot_profiles (CTE rekeyed)
-- здесь нет по построению: у вебхука RevenueCat нет установки, вместо неё — NULL.
--
-- И порядок: эта нода стоит ДО Upsert Subscriber. `topup_balance + <топ-ап события>` в апсерте НЕ идемпотентен
-- к ретраю, поэтому падение ПОСЛЕ него -> RC передоставляет -> топ-ап самого события начислится
-- дважды. Слева от апсерта безопасно в обе стороны: упали здесь — апсерт ещё не применялся;
-- упал апсерт — алиасы уже погашены и повторная склейка ничего не начислит (guard ниже).
"""
RC_CONSOLIDATE_RESULT = """select $1::text as app_user_id, $3::text as plan, $4::text as status,
       $5::timestamptz as period_end, $6::int as topup_delta, $7::text as product_id,
       (select count(*) from locked) as alias_rows,
       (select count(*) from retired) as retired_rows,
       (select count(*) from carried) as carried_rows,
       (select topup_balance from credited) as balance_after,
       (select plan from credited) as plan_after,
       (select count(*) from rekeyed) as rekeyed_rows,
       $8::text as event_id;"""
# install = SQL NULL: the RevenueCat webhook has no install — RevenueCat Events can never re-key a bot (by construction:
# no parameter feeds it).
RC_CONSOLIDATE = {"head": RC_CONSOLIDATE_HEAD, "to": "$1", "aliases": "$2", "event_plan": "$3::text", "install": "null::text",
                  "result": RC_CONSOLIDATE_RESULT}
PARAM_KEYS = ("head", "to", "aliases", "event_plan", "install", "result")
# The hand-authored queryReplacement arrays (NOT written by the generator — Map Event and these arrays stay hand-authored) that
# the RevenueCat renderings' parameter order is written for: the app user id, the alias list, then the event's payload
# (plan, status, period, top-up, SKU) and its id; TRANSFER: the two id arrays and the event id. verify-billing-consolidation.py
# pins them, so the generated SQL and its parameters cannot drift apart.
RC_CONSOLIDATE_REPLACEMENT = "={{ [$json.app_user_id, $json.alias_ids, ($json.plan ?? null), ($json.status ?? null), ($json.period_end ?? null), $json.topup_delta, ($json.product_id ?? null), ($json.event_id ?? null)] }}"
RC_TRANSFER_REPLACEMENT = "={{ [$json.to_ids, $json.from_ids, ($json.event_id ?? null)] }}"


def _render(text, **tokens):
    out = text
    for k, v in tokens.items():
        out = out.replace("<" + k + ">", v)
    left = re.findall(r"<[A-Z_]+>", out)
    assert not left, f"unrendered token(s) {left}"
    return out


def CONSOLIDATE_SQL(params):
    """The whole alias-merge statement: params = {head, to, aliases, event_plan, install, result} (RC_CONSOLIDATE for
    RevenueCat Events) — head + the CTE chain + the caller's final select."""
    missing = [k for k in PARAM_KEYS if k not in params]
    assert not missing, f"CONSOLIDATE_SQL: missing params {missing}"
    body = CONSOLIDATE_BODY.replace("<REKEY>", REKEY_CTE("<TO>", "<INSTALL>")).replace("<TAKE>", TAKE)
    return params["head"] + _render(body, TO=params["to"], ALIASES=params["aliases"], EVENT_PLAN=params["event_plan"],
                                    INSTALL=params["install"]) + params["result"]


# ------------------------------------------------------------------------------------------------- the TRANSFER
TRANSFER_SQL = """-- RC TRANSFER (Task 16): МЕЖАККАУНТНЫЙ переезд подписки. Один statement = одна транзакция,
-- поэтому перенос и гашение не могут примениться наполовину. Само событие не несёт ни плана,
-- ни SKU — всё читается из СТАРОЙ строки. Параметры по порядку: JSON-массив transferred_to,
-- затем JSON-массив transferred_from, третий — id события RevenueCat (A1): он только проносится в
-- результат; эта ветка не доходит до Upsert Subscriber, поэтому журнал идемпотентности не пишет.
with to_ids as (
  -- дедуп + позиция: снимок получают ВСЕ id из transferred_to («App User ID(s) receiving the
  -- transactions and entitlements»), а ДЕНЬГИ — только первый: баланс размножать нельзя.
  -- Дубликат в массиве без дедупа = «ON CONFLICT DO UPDATE cannot affect row a second time».
  select app_user_id, min(ord) as ord from (
    select value as app_user_id, ordinality as ord
    from jsonb_array_elements_text($1::jsonb) with ordinality as t(value, ordinality)
  ) x group by app_user_id
), src_ids as (
  select distinct value as app_user_id from jsonb_array_elements_text($2::jsonb) as t(value)
), locked as (
  -- FOR UPDATE, а не снимок: этот инстанс демонстрируемо доставляет события ОДНОГО подписчика
  -- параллельно (execs 3128/3129 и 3150/3151 перекрываются по времени), а снимочный
  -- sum(topup_balance) в такой гонке начислит баланс дважды. Блокировка строки сериализует
  -- и заставляет перечитать свежую версию, поэтому вторая доставка увидит уже 0.
  -- Получатели исключены целиком (self-transfer и любой id, который есть в обоих массивах).
  select * from subscribers
  where app_user_id in (select app_user_id from src_ids)
    and app_user_id not in (select app_user_id from to_ids)
  for update
), src as (
  -- снимок, который переезжает: самая «живая» строка-источник
  select * from locked
  order by current_period_end desc nulls last, updated_at desc nulls last
  limit 1
), moved as (
  insert into subscribers (app_user_id, plan, status, current_period_end, topup_balance, product_id, updated_at)
  select t.app_user_id, src.plan, src.status, src.current_period_end,
         case when t.ord = (select min(ord) from to_ids)
              then coalesce((select sum(topup_balance) from locked), 0) else 0 end,
         src.product_id, now()
  from to_ids t cross join src
  on conflict (app_user_id) do update set
    -- Снимок переезжает только если источник СТРОГО свежее получателя И не гасит живую строку:
    -- (1) RENEWAL под новым id законно приходит раньше TRANSFER, а RC ретраит любой не-2xx —
    -- ничья отдаётся получателю, что и делает повторную доставку идемпотентным no-op'ом;
    -- (2) СРОК — не признак жизни: истёкшая строка с более поздним period_end штамповала бы
    -- status='expired' поверх живого trialing (у которого period_end NULL = -infinity и он
    -- всегда проигрывает), а Count Dialog не считает диалоги при status in (expired,grace) —
    -- то есть свежая установка замолчала бы. Деньги при этом всё равно переезжают.
    plan = case when coalesce(subscribers.current_period_end, '-infinity'::timestamptz)
                   < coalesce(excluded.current_period_end, '-infinity'::timestamptz)
                 and not (subscribers.status in ('active','trialing')
                          and excluded.status in ('expired','grace'))
                then excluded.plan else subscribers.plan end,
    status = case when coalesce(subscribers.current_period_end, '-infinity'::timestamptz)
                   < coalesce(excluded.current_period_end, '-infinity'::timestamptz)
                 and not (subscribers.status in ('active','trialing')
                          and excluded.status in ('expired','grace'))
                  then excluded.status else subscribers.status end,
    product_id = case when coalesce(subscribers.current_period_end, '-infinity'::timestamptz)
                   < coalesce(excluded.current_period_end, '-infinity'::timestamptz)
                 and not (subscribers.status in ('active','trialing')
                          and excluded.status in ('expired','grace'))
                      then coalesce(excluded.product_id, subscribers.product_id)
                      else subscribers.product_id end,
    -- период едет ВМЕСТЕ со снимком, а не отдельно: отказали в снимке — не берём и срок
    -- (когда снимок выигрывает, он строго свежее, так что это и есть greatest из двух)
    current_period_end = case when coalesce(subscribers.current_period_end, '-infinity'::timestamptz)
                   < coalesce(excluded.current_period_end, '-infinity'::timestamptz)
                 and not (subscribers.status in ('active','trialing')
                          and excluded.status in ('expired','grace'))
                              then excluded.current_period_end else subscribers.current_period_end end,
    -- ПРИБАВЛЯЕМ, а не ПРИСВАИВАЕМ: у получателя мог быть свой топ-ап, и прибавление ложится
    -- на заблокированное живое значение строки, а не на снимок этого statement'а
    topup_balance = subscribers.topup_balance + excluded.topup_balance,
    updated_at = now()
  returning app_user_id, plan, status, current_period_end, topup_balance, product_id
), accepted as (
  -- N-1 (ревью 2026-08-26): получатели, которые ДЕЙСТВИТЕЛЬНО взяли снимок. Расход — это долг
  -- против КОНКРЕТНОЙ квоты, поэтому он обязан ехать ВМЕСТЕ с планом. Без этого гарда
  -- `carried` ниже отдавал бы обратно ровно то, что защищают freshness/liveness-термы выше:
  -- истёкший business с 300 диалогами за месяц против живого свежего trial (квота 150) —
  -- снимок справедливо отвергнут, а 300 строк расхода всё равно переехали бы и мгновенно
  -- обнулили квоту получателя (бот молчит, панель «Вместе» вместо ответов).
  -- Считаем это ИЗ РЕЗУЛЬТАТА statement'а, а не повторяя предикат в третий раз: если снимок
  -- принят, все три поля строки после записи РАВНЫ снимку; если отвергнут — получатель
  -- сохранил свои, и хотя бы одно поле разойдётся. Совпадение всех трёх при «отказе»
  -- означает, что у получателя И ТАК тот же план/статус/срок — тогда перенос расхода верен.
  select m.app_user_id
  from moved m, src
  where m.plan = src.plan
    and m.status = src.status
    and m.current_period_end is not distinct from src.current_period_end
), retired as (
  update subscribers s set
    status = 'expired',   -- убивает квоту зомби: Count Dialog не считает status in (expired,grace)
    topup_balance = 0,    -- баланс уже перенесён выше; обнуление делает перенос ровно консервативным
    -- Клэмп передаёт профили старой личности ветке A свипа Profile Lifecycle Sweep, которая
    -- смотрит на (status in ('expired','grace') and
    -- coalesce(current_period_end, now() - interval '99 days') < now() - interval '3 days').
    -- ОБА coalesce здесь обязательны: будущий оплаченный период продержал бы зомби-профили
    -- до месяца по 23₽/день, а NULL — наоборот, сделал бы строку кандидатом МГНОВЕННО (тот
    -- самый coalesce на -99 дней), и топ-ап-only личность лишилась бы реальных профилей
    -- Wappi в ближайшие ≤6 часов без всякого grace. now() даёт ровно те же 3 дня, что и
    -- любой другой churn. Значение только УМЕНЬШАЕТСЯ: просроченная строка остаётся просроченной.
    current_period_end = least(coalesce(s.current_period_end, now()), now()),
    updated_at = now()
  where s.app_user_id in (select app_user_id from locked)
    -- уже полностью погашенную строку не переписываем (ноль новых версий строки)
    and (s.status <> 'expired' or s.topup_balance <> 0
         or s.current_period_end is null or s.current_period_end > now())
  returning s.app_user_id
)
, carried as (
  -- I-4 (Task 17a): расход переезжает ВМЕСТЕ СО СНИМКОМ — буквально: только к тем id из
  -- transferred_to, которые снимок ПРИНЯЛИ (CTE `accepted` выше). Деньги по-прежнему только
  -- первому: баланс размножать нельзя, а расход — консервативно наоборот, получатель плана
  -- получает и его израсходованную часть. Окно — текущий месяц, как у
  -- Count Dialog/Get Usage. on conflict do nothing делает это идемпотентным на ретрае RC
  -- (существование строки И ЕСТЬ счёт), distinct — на случай одного (chat, date) у двух
  -- источников. Строки источников не удаляем: погашенный id никто не читает.
  insert into dialog_counts (app_user_id, chat_id, d)
  select distinct a.app_user_id, dc.chat_id, dc.d
  from accepted a, dialog_counts dc
  where dc.app_user_id in (select app_user_id from locked)
    and date_trunc('month', dc.d)
        = date_trunc('month', (now() at time zone 'Asia/Almaty')::date)
  on conflict (app_user_id, chat_id, d) do nothing
  returning 1
)
select (select count(*) from to_ids) as to_rows,
       (select count(*) from locked) as source_rows,
       (select count(*) from moved) as moved_rows,
       (select count(*) from retired) as retired_rows,
       (select count(*) from accepted) as accepted_rows,
       (select count(*) from carried) as carried_rows,
       (select topup_balance from moved order by topup_balance desc limit 1) as to_topup_balance,
       $3::text as event_id;"""


def main():
    if len(sys.argv) == 3 and sys.argv[1] == "--print" and sys.argv[2] in ("consolidate", "transfer"):
        print(CONSOLIDATE_SQL(RC_CONSOLIDATE) if sys.argv[2] == "consolidate" else TRANSFER_SQL)
        return
    sys.exit("usage: billing_consolidation.py --print consolidate|transfer")


if __name__ == "__main__":
    main()
