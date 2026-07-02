-- Price-list originals archive (2026-07-02)
--
-- The Upload File workflow archives every ingested upload (the exact bytes the
-- app sent: true original for pdf/txt, client-converted text for
-- xlsx/csv/xml/docx/rtf) to this PRIVATE bucket, object key = the app-minted
-- fileId — the same key stamped on the file's RAG chunks (documents.metadata
-- ->> 'fileId'). That makes re-indexing (re-chunk / re-embed) a server-side
-- batch job instead of "ask every user to re-upload".
-- Delete File removes the object together with the chunks.
--
-- Access model: the n8n workflows use the service_role key, which bypasses
-- RLS entirely. Deliberately NO storage.objects policies here — anon and
-- authenticated roles get nothing (default deny), matching the RAG store
-- hardening (2026-07-02-harden-rag-store.sql).
--
-- Idempotent; run in the Supabase SQL editor or via a service-role connection.

insert into storage.buckets (id, name, public)
values ('price-lists', 'price-lists', false)
on conflict (id) do nothing;
