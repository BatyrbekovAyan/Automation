-- «Личные и рабочие чаты» — canary на тестовом ИИ-сотруднике (README пакета, шаг 9.5; только чтение, одни признаки: ни номеров,
-- ни имён, ни текстов). Главное правило выкладки: о личном чате после решения по нему не записано ничего. Поэтому история и
-- журнал ответа личного чата сверяются со временем решения по ЭТОМУ чату (chat_audience.decided_at): решения идут с самой
-- выкладки, а шаг 9 бывает днями позже — так видна и строка, записанная между решением и отметкой 9.0. Законно после решения о
-- личном чате не пишет никто (исключение — гонка в секунды на самом переходе: ответ или запись прежнего клона, начатые, пока чат
-- ещё не был личным). Записанное ДО решения — до выкладки ИИ-сотрудник записывал каждый свой чат один на один, до шага 6.2 так
-- же пишут прежние клоны — остаётся на сервере и в оценку не входит: решение «личный» для чата, который никогда не был рабочим,
-- часов удаления не запускает (README пакета, «Сохранённое до выкладки»). У памяти (n8n_chat_histories) нет колонки времени:
-- «после» для неё — id больше отметки шага 9.0 (canary-baseline.sql, прямо перед 9.1). Каждый, кто пишет память, в том же
-- выполнении пишет историю или журнал ответа, поэтому запись о личном чате между решением и отметкой покажут и они. От отметки
-- судится и остальное: решения, рабочий чат 9.1, журнал ответа по состояниям.
-- Отметку подставляет canary.py (из canary-baseline.txt шага 9.0). Без подстановки запрос падает на приведении типа — нарочно.
--   decisions             решения, принятые после отметки: профиль / вид / источник / причина / число чатов / самое новое
--   connections           строки подключения (известен ли номер — без самого номера)
--   personal_after        о чатах, которые сейчас личные, записано после решения по ним: история / память (после отметки) /
--                         журнал ответа — ждём 0 / 0 / 0
--   business_after        о чатах, решённых рабочими после отметки, после неё записано: история / память — ждём > 0
--   ledger_after          журнал ответа после отметки, по состояниям
--   personal_rows_before  о нынешних личных чатах записано до решения по ним (память — до отметки) — только для сведения
--                         (остаётся до удаления ИИ-сотрудника)
-- Прод: через Tools/n8n/.secrets/sql-oneoff.py, текст собирает canary.py (команда — README пакета, шаг 9.5).
with baseline as (
  select '__BASELINE_AT__'::timestamptz as at, '__MEMORY_MAX_ID__'::bigint as memory_max_id
), personal as (
  select a.profile_id, a.chat_id, a.decided_at from public.chat_audience a where a.kind = 'personal'
), business_new as (
  select a.profile_id, a.chat_id from public.chat_audience a, baseline b where a.kind = 'business' and a.decided_at >= b.at
)
select
  (select coalesce(json_agg(r order by r.profile_id, r.kind, r.source, r.reason), '[]'::json) from (
     select a.profile_id, a.kind, a.source, a.reason, count(*) as chats,
            to_char(max(a.decided_at) at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') as newest
     from public.chat_audience a, baseline b where a.decided_at >= b.at group by 1, 2, 3, 4) r) as decisions,
  (select coalesce(json_agg(json_build_object(
            'profile_id', c.profile_id, 'number_known', c.phone is not null, 'previous_number_known', c.prev_phone is not null,
            'connected_at', to_char(c.connected_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI"Z"')) order by c.profile_id), '[]'::json)
     from public.audience_connections c) as connections,
  json_build_object(
    'history', (select count(*) from public.chat_history_events h join personal p using (profile_id, chat_id)
                where h.created_at >= p.decided_at),
    'memory',  (select count(*) from public.n8n_chat_histories m join personal p on m.session_id = p.profile_id || ':' || p.chat_id,
                  baseline b where m.id > b.memory_max_id),
    'ledger',  (select count(*) from public.reply_events e join personal p using (profile_id, chat_id)
                where e.received_at >= p.decided_at)) as personal_after,
  json_build_object(
    'history', (select count(*) from public.chat_history_events h join business_new n using (profile_id, chat_id), baseline b
                where h.created_at >= b.at),
    'memory',  (select count(*) from public.n8n_chat_histories m join business_new n on m.session_id = n.profile_id || ':' || n.chat_id,
                  baseline b where m.id > b.memory_max_id)) as business_after,
  (select coalesce(json_object_agg(s.state, s.events order by s.state), '{}'::json) from (
     select e.state, count(*) as events from public.reply_events e, baseline b where e.received_at >= b.at group by 1) s) as ledger_after,
  json_build_object(
    'history', (select count(*) from public.chat_history_events h join personal p using (profile_id, chat_id)
                where h.created_at < p.decided_at),
    'memory',  (select count(*) from public.n8n_chat_histories m join personal p on m.session_id = p.profile_id || ':' || p.chat_id,
                  baseline b where m.id <= b.memory_max_id),
    'ledger',  (select count(*) from public.reply_events e join personal p using (profile_id, chat_id)
                where e.received_at < p.decided_at)) as personal_rows_before;
