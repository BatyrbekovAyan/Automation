-- «Личные и рабочие чаты» — canary на тестовом ИИ-сотруднике (только чтение, одни признаки: ни номеров, ни имён, ни текстов).
-- Решения по чатам, сгруппированные по профилю / виду / источнику / причине (число чатов и время самого нового решения),
-- строки подключения (известен ли номер — без самого номера), и главное правило: о личных чатах не хранится НИЧЕГО —
-- ни истории, ни памяти, ни строк журнала ответа (personal_*_rows = 0; чат, который владелец сделал личным уже после
-- рабочего периода, хранит его до удаления через 2 дня — на canary так не делать).
-- Прод: через Tools/n8n/.secrets/sql-oneoff.py (команда — README пакета, шаг 9).
select
  (select coalesce(json_agg(r order by r.profile_id, r.kind, r.source, r.reason), '[]'::json) from (
     select a.profile_id, a.kind, a.source, a.reason, count(*) as chats,
            to_char(max(a.decided_at) at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') as newest
     from public.chat_audience a group by 1, 2, 3, 4) r) as decisions,
  (select coalesce(json_agg(json_build_object(
            'profile_id', c.profile_id, 'number_known', c.phone is not null, 'previous_number_known', c.prev_phone is not null,
            'connected_at', to_char(c.connected_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI"Z"')) order by c.profile_id), '[]'::json)
     from public.audience_connections c) as connections,
  (select count(*) from public.chat_history_events h
     join public.chat_audience a on a.profile_id = h.profile_id and a.chat_id = h.chat_id where a.kind = 'personal') as personal_history_rows,
  (select count(*) from public.n8n_chat_histories m
     join public.chat_audience a on m.session_id = a.profile_id || ':' || a.chat_id where a.kind = 'personal') as personal_memory_rows,
  (select count(*) from public.reply_events e
     join public.chat_audience a on a.profile_id = e.profile_id and a.chat_id = e.chat_id where a.kind = 'personal') as personal_ledger_rows,
  (select coalesce(json_agg(r order by r.state), '[]'::json) from (
     select e.state, count(*) as events from public.reply_events e
     where e.received_at > now() - interval '1 day' group by 1) r) as ledger_last_day;
