-- Пакет B «расписание бота» — до миграции (только чтение): таблицы и функции триггера ещё нет → t | t.
-- Прод: через Tools/n8n/.secrets/sql-oneoff.py (команда — README пакета, шаг 1).
select to_regclass('public.bot_schedule') is null as no_schedule,
       to_regprocedure('public.bot_schedule_tz_check()') is null as no_tz_fn;
