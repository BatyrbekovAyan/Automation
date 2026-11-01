-- «Личные и рабочие чаты» — проверка здоровья Decide Audience (только чтение). Его сбой не оставляет следа: вызов из шаблона
-- идёт дальше при ошибке, а успешные выполнения не сохраняются — и тогда каждый новый клиент молча остаётся без ответа.
-- Признак системного сбоя: строку подключения создал сам Decide Audience (Ensure Connection: номер не известен — приложение о
-- подключении не сообщало), прошло больше часа (окно первых 30 минут позади), а решений у профиля нет ни одного.
-- Профиль в списке — повод посмотреть ошибочные выполнения его клона и crAudienceDec001 (watch.py); профиль, в чат которого
-- после первых 30 минут никто не писал, тоже попадёт в список — это не сбой.
-- Ожидание сразу после выкладки: {"suspicious":"0","profiles":[]}; печатаются только id профилей Wappi и время.
-- Прод: через Tools/n8n/.secrets/sql-oneoff.py (команда — README пакета, шаг 8).
select count(*) as suspicious,
       coalesce(json_agg(json_build_object(
         'profile_id', c.profile_id,
         'connected_at', to_char(c.connected_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI"Z"'),
         'row_created', to_char(c.updated_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI"Z"')) order by c.updated_at), '[]'::json) as profiles
from public.audience_connections c
where c.phone is null
  and c.updated_at < now() - interval '1 hour'
  and not exists (select 1 from public.chat_audience a where a.profile_id = c.profile_id);
