-- «Личные и рабочие чаты» — пост-проверка миграции одним запросом (только чтение).
-- Ожидание: {"audience_cols":"11","connection_cols":"6","restore_cols":"3","rls":true,"client_grants":"0","indexes":"3",
--            "wipe_idx":true,"state_checks":"1","personal_state":true,"rows":"0"}
--   audience_cols 11 / connection_cols 6 — все колонки обеих таблиц; restore_cols 3 — три колонки возврата к прежнему номеру
--   (chat_audience.decided_phone, audience_connections.prev_phone / prev_connected_at), все допускают NULL;
--   rls — RLS включён у обеих; client_grants 0 — у anon / authenticated нет прав; indexes 3 — два первичных ключа и список
--   работы удаления (chat_audience_wipe_idx); wipe_idx — его условие без source (личное решение и владельца, и правила);
--   state_checks 1 — у reply_events по-прежнему ОДНА CHECK-проверка; personal_state — в ней есть 'cancelled_personal';
--   rows 0 — строк ещё нет (их пишут только новые workflow).
-- Прод: через Tools/n8n/.secrets/sql-oneoff.py (команда — README пакета, шаг 2).
select
  (select count(*) from information_schema.columns where table_schema = 'public' and table_name = 'chat_audience') as audience_cols,
  (select count(*) from information_schema.columns where table_schema = 'public' and table_name = 'audience_connections') as connection_cols,
  (select count(*) from information_schema.columns where table_schema = 'public' and is_nullable = 'YES'
     and ((table_name = 'chat_audience' and column_name = 'decided_phone')
       or (table_name = 'audience_connections' and column_name in ('prev_phone', 'prev_connected_at')))) as restore_cols,
  (select bool_and(relrowsecurity) from pg_class
     where oid in ('public.chat_audience'::regclass, 'public.audience_connections'::regclass)) as rls,
  (select count(*) from information_schema.role_table_grants where table_schema = 'public'
     and table_name in ('chat_audience', 'audience_connections') and grantee in ('anon', 'authenticated')) as client_grants,
  (select count(*) from pg_indexes where schemaname = 'public'
     and indexname in ('chat_audience_pkey', 'audience_connections_pkey', 'chat_audience_wipe_idx')) as indexes,
  (select pg_get_indexdef('public.chat_audience_wipe_idx'::regclass) like '%(personal_since) WHERE%'
          and pg_get_indexdef('public.chat_audience_wipe_idx'::regclass) not like '%source%') as wipe_idx,
  (select count(*) from pg_constraint where conrelid = 'public.reply_events'::regclass and contype = 'c') as state_checks,
  exists (select 1 from pg_constraint where conrelid = 'public.reply_events'::regclass and conname = 'reply_events_state_check'
          and pg_get_constraintdef(oid) like '%''cancelled_personal''%') as personal_state,
  (select count(*) from public.chat_audience) + (select count(*) from public.audience_connections) as rows;
