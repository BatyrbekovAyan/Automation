-- «Личные и рабочие чаты» — до миграции (только чтение): обеих таблиц ещё нет; у reply_events ровно одна CHECK-проверка —
-- та, что Postgres назвал при создании таблицы (reply_events_state_check), и в её списке ещё нет 'cancelled_personal'.
-- Миграция ищет проверку по ЭТОМУ имени: под другим именем она добавила бы вторую, а старая продолжила бы отказывать
-- 'cancelled_personal' (отмена готовящегося ответа в чате, который стал личным, не записалась бы).
-- Ожидание: {"no_audience":true,"no_connections":true,"state_checks":"1","state_check_named":true,"personal_state":false}
-- Прод: через Tools/n8n/.secrets/sql-oneoff.py (команда — README пакета, шаг 2).
select to_regclass('public.chat_audience') is null as no_audience,
       to_regclass('public.audience_connections') is null as no_connections,
       (select count(*) from pg_constraint where conrelid = 'public.reply_events'::regclass and contype = 'c') as state_checks,
       exists (select 1 from pg_constraint where conrelid = 'public.reply_events'::regclass and contype = 'c'
               and conname = 'reply_events_state_check') as state_check_named,
       exists (select 1 from pg_constraint where conrelid = 'public.reply_events'::regclass and conname = 'reply_events_state_check'
               and pg_get_constraintdef(oid) like '%''cancelled_personal''%') as personal_state;
