-- Пакет A — до миграций (только чтение): все три таблицы ещё отсутствуют → t | t | t.
-- Прод: через Tools/n8n/.secrets/sql-oneoff.py (команда — README пакета, шаг 1).
select to_regclass('public.rc_event_ledger') is null as no_ledger, to_regclass('public.channel_snapshot') is null as no_snapshot,
       to_regclass('public.identity_heal_log') is null as no_heal_log;
