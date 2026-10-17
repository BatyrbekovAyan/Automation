-- Пакет B «расписание бота» — пост-проверка миграции одним запросом (только чтение).
-- Ожидание: schedule_cols 8 | rls t | client_grants 0 | tz_trigger t | triggers 1 | tz_fn t | rows 0
-- (tz_trigger: триггер bot_schedule_tz_check срабатывает BEFORE INSERT OR UPDATE OF tz — исправление ревью B1, d34a3f81).
-- Прод: через Tools/n8n/.secrets/sql-oneoff.py (команда — README пакета, шаг 1).
select
  (select count(*) from information_schema.columns where table_schema = 'public' and table_name = 'bot_schedule') as schedule_cols,
  (select relrowsecurity from pg_class where oid = 'public.bot_schedule'::regclass) as rls,
  (select count(*) from information_schema.role_table_grants
     where table_schema = 'public' and table_name = 'bot_schedule' and grantee in ('anon', 'authenticated')) as client_grants,
  (select bool_and(pg_get_triggerdef(t.oid) like '%BEFORE INSERT OR UPDATE OF tz ON public.bot_schedule%') from pg_trigger t
     where t.tgrelid = 'public.bot_schedule'::regclass and t.tgname = 'bot_schedule_tz_check') as tz_trigger,
  (select count(*) from pg_trigger t where t.tgrelid = 'public.bot_schedule'::regclass and not t.tgisinternal) as triggers,
  to_regprocedure('public.bot_schedule_tz_check()') is not null as tz_fn,
  (select count(*) from public.bot_schedule) as rows;
