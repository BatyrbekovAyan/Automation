-- Пакет B — проба tzdata Postgres прода (только чтение; ревью B1). Ожидание: almaty_offset 05:00:00 | almaty_known t
-- (moscow_offset 03:00:00 — справочно). Казахстан перешёл на единый UTC+5 1 марта 2024 (tzdata 2024a): 06:00:00 значит,
-- что база часовых поясов Postgres старше — тогда СТОП (окно бота в Asia/Almaty сдвинулось бы на час, а триггер
-- bot_schedule_tz_check отказывал бы зонам новее этой базы). Прод: через Tools/n8n/.secrets/sql-oneoff.py (README, шаг 1).
select ((now() at time zone 'Asia/Almaty') - (now() at time zone 'UTC'))::text as almaty_offset,
       exists (select 1 from pg_catalog.pg_timezone_names z where z.name = 'Asia/Almaty') as almaty_known,
       ((now() at time zone 'Europe/Moscow') - (now() at time zone 'UTC'))::text as moscow_offset;
