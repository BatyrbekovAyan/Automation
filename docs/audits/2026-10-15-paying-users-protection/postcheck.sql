-- Пакет A «защита платящих» — пост-проверка трёх миграций одним запросом (только чтение).
-- Ожидание: ledger_cols 2 | snapshot_cols 6 | heal_cols 8 | rls t | client_grants 0 | seq_client_usage f | indexes 4.
-- Прод: через Tools/n8n/.secrets/sql-oneoff.py (команда — README пакета, шаг 1).
select
  (select count(*) from information_schema.columns where table_schema = 'public' and table_name = 'rc_event_ledger')   as ledger_cols,
  (select count(*) from information_schema.columns where table_schema = 'public' and table_name = 'channel_snapshot')  as snapshot_cols,
  (select count(*) from information_schema.columns where table_schema = 'public' and table_name = 'identity_heal_log') as heal_cols,
  (select bool_and(relrowsecurity) from pg_class
     where oid in ('public.rc_event_ledger'::regclass, 'public.channel_snapshot'::regclass, 'public.identity_heal_log'::regclass)) as rls,
  (select count(*) from information_schema.role_table_grants
     where table_schema = 'public' and table_name in ('rc_event_ledger', 'channel_snapshot', 'identity_heal_log')
       and grantee in ('anon', 'authenticated')) as client_grants,
  (select has_sequence_privilege('anon', 'public.identity_heal_log_id_seq', 'USAGE')
       or has_sequence_privilege('authenticated', 'public.identity_heal_log_id_seq', 'USAGE')) as seq_client_usage,
  (select count(*) from pg_indexes where schemaname = 'public'
     and indexname in ('rc_event_ledger_pkey', 'channel_snapshot_pkey', 'identity_heal_log_pkey', 'identity_heal_log_to_idx')) as indexes;
