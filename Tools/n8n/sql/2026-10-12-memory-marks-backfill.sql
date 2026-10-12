-- M3 (T7, follow-ups 2026-10-11, docs/audits/2026-10-09-followups/F1F2-plan.md §2/§4): the memory marks for rows written
-- BEFORE the T6 writers. The bot's memory model sees role + content only (additional_kwargs never reach it), so from
-- T6 on the writers put the facts into the content: the owner's replies (source owner_app / owner_phone) open with
-- «Владелец: », the bot's reply whose delivery is unknown with «[доставка не подтверждена] ». This backfill gives the
-- rows already in n8n_chat_histories the same marks, so an older conversation (and the memory F2 moves to a
-- reconnected bot) reads the same way. The strings are apply-history-handoff.py's OWNER_MEMORY_PREFIX /
-- UNDELIVERED_MEMORY_PREFIX — tests/test_reply_lost_send.py pins the parity and runs this block.
-- Only the memory content changes: chat_history_events.body (what the app shows), source and delivery stay as they are.
-- Idempotent: a row that already opens with its mark is not touched (the T6 writers' own rows included).
-- Apply AFTER deploying the T6 workflows (the Wappi Proxy, both bot templates and Dashboard Outcomes — the «Сводка»
-- transcript must already read the content without the marks). Not a schema change; old code ignores the marks
-- (without the prompt rule a marked row reads like the variant B1 probe of Q1 — measured no worse than prod).
-- BACKFILL-BEGIN
update public.n8n_chat_histories
   set message = message || jsonb_build_object('content', 'Владелец: ' || coalesce(message->>'content', ''))
 where message->>'type' = 'ai'
   and message->'additional_kwargs'->>'source' in ('owner_app', 'owner_phone')
   and left(coalesce(message->>'content', ''), 10) <> 'Владелец: ';
update public.n8n_chat_histories
   set message = message || jsonb_build_object('content', '[доставка не подтверждена] ' || coalesce(message->>'content', ''))
 where message->>'type' = 'ai'
   and (message->'additional_kwargs'->>'source') is null
   and message->'additional_kwargs'->>'delivery' = 'unknown'
   and left(coalesce(message->>'content', ''), 27) <> '[доставка не подтверждена] ';
-- BACKFILL-END

-- Post-checks (expect 0 and 0):
--   select count(*) from public.n8n_chat_histories
--    where message->>'type' = 'ai' and message->'additional_kwargs'->>'source' in ('owner_app', 'owner_phone')
--      and left(coalesce(message->>'content', ''), 10) <> 'Владелец: ';
--   select count(*) from public.n8n_chat_histories
--    where message->>'type' = 'ai' and (message->'additional_kwargs'->>'source') is null
--      and message->'additional_kwargs'->>'delivery' = 'unknown'
--      and left(coalesce(message->>'content', ''), 27) <> '[доставка не подтверждена] ';

-- Reverse (take the marks off again — only if the T6 workflows are rolled back AND the marks must go; kept commented):
-- update public.n8n_chat_histories
--    set message = message || jsonb_build_object('content', substr(message->>'content', 11))
--  where message->>'type' = 'ai'
--    and message->'additional_kwargs'->>'source' in ('owner_app', 'owner_phone')
--    and left(coalesce(message->>'content', ''), 10) = 'Владелец: ';
-- update public.n8n_chat_histories
--    set message = message || jsonb_build_object('content', substr(message->>'content', 28))
--  where message->>'type' = 'ai'
--    and (message->'additional_kwargs'->>'source') is null
--    and message->'additional_kwargs'->>'delivery' = 'unknown'
--    and left(coalesce(message->>'content', ''), 27) = '[доставка не подтверждена] ';
