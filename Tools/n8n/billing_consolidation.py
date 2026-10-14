#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Billing consolidation SQL — ONE home for the statements that move a subscription between RevenueCat identities
(A2 «защита платящих», 2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
§3 A2, A3). The generators wire these texts; nobody keeps a copy:

  CONSOLIDATE_SQL(params)  the alias merge — a reinstall: RevenueCat names the old identity in `aliases[]` of an event
                           delivered under the new one. RevenueCat Events' `Consolidate Aliases` renders it with
                           RC_CONSOLIDATE; Task A3's identity-heal renders the same CTE chain with its own parameters.
  TRANSFER_SQL             the cross-account TRANSFER (RevenueCat Events' `Transfer Subscriber`).

apply-billing-consolidation.py writes both texts into the canonical RevenueCat Events JSON — it owns ONLY the two
`query` fields; Map Event, the queryReplacement arrays and the wiring stay hand-authored. verify-billing-consolidation.py
pins node text == module text. Every statement names its parameters in words (V1: no dollar + digit inside a comment or a
literal); the CTE chain takes its recipient / alias-list expressions as tokens, so a caller with another parameter order
renders the same chain.

    python3 Tools/n8n/billing_consolidation.py --print consolidate|transfer    # the RevenueCat Events texts
"""
import re
import sys

# ------------------------------------------------------------------------------------------------- the alias merge
# The CTE chain (alias_ids → locked → credited → retired → carried). Tokens: <TO> = the recipient app user id (text),
# <ALIASES> = the JSON array (text) of the alias ids, the recipient already excluded by the caller.
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
), credited as (
  -- Пишем ТОЛЬКО когда есть что переносить: settled-набор алиасов (а он приезжает на каждом
  -- событии до конца жизни аккаунта) не должен плодить версии строк. Если строки получателя
  -- ещё нет — создаём её с trial/trialing по умолчанию, а идущий следом Upsert Subscriber
  -- положит поверх настоящие план/статус, не трогая баланс.
  insert into subscribers (app_user_id, plan, status, topup_balance, updated_at)
  select <TO>, 'trial', 'trialing', x.total, now()
  from (select coalesce(sum(topup_balance), 0) as total from locked) x
  where x.total > 0
  on conflict (app_user_id) do update set
    topup_balance = subscribers.topup_balance + excluded.topup_balance,
    updated_at = now()
  returning app_user_id, topup_balance
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
-- ТОЛЬКО перенос баланса и гашение — НИКАКОГО копирования снимка. Событие, которое едет следом
-- в Upsert Subscriber, уже несёт авторитетные план/статус/период; копировать сюда снимок строки-
-- алиаса значило бы поставить риск понижения на путь, который срабатывает ВЕЧНО, на каждом событии.
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
       $8::text as event_id;"""
RC_CONSOLIDATE = {"head": RC_CONSOLIDATE_HEAD, "to": "$1", "aliases": "$2", "result": RC_CONSOLIDATE_RESULT}
PARAM_KEYS = ("head", "to", "aliases", "result")


def _render(text, **tokens):
    out = text
    for k, v in tokens.items():
        out = out.replace("<" + k + ">", v)
    left = re.findall(r"<[A-Z_]+>", out)
    assert not left, f"unrendered token(s) {left}"
    return out


def CONSOLIDATE_SQL(params):
    """The whole alias-merge statement: params = {head, to, aliases, result} (RC_CONSOLIDATE for RevenueCat Events)."""
    missing = [k for k in PARAM_KEYS if k not in params]
    assert not missing, f"CONSOLIDATE_SQL: missing params {missing}"
    return params["head"] + _render(CONSOLIDATE_BODY, TO=params["to"], ALIASES=params["aliases"]) + params["result"]


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
