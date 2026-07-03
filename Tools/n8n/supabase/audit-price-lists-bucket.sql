-- Bucket audit: price-lists originals vs RAG chunks (2026-07-03)
--
-- Supersedes the 2026-07-02 rollout invariant "bucket audit: 0 orphans".
-- Since the image OCR tier (2026-07-03), an unreferenced object is no longer
-- automatically a bug: when the vision gate rejects a photo (HTTP 422
-- no_price_data), the workflow has ALREADY archived the original to
-- price-lists/{fileId} — by design, rejected photos are future re-OCR
-- candidates (see docs/superpowers/specs/2026-07-03-image-ocr-price-lists-design.md).
-- The app discards the fileId on 422, so no chunks and no UploadedFilesStore
-- entry reference the object. Expected, not an orphan to clean up.
--
-- Classification (query 1):
--   referenced             object's fileId has chunks in public.documents — healthy
--   orphaned-by-rejection  no chunks + image/* mimetype — expected (422-rejected
--                          photo kept for re-OCR; also covers the rare case of a
--                          photo whose vision pass succeeded but embed/insert
--                          failed — indistinguishable in SQL, and equally a
--                          re-OCR candidate)
--   orphaned-unexpected    no chunks + non-image mimetype — INVESTIGATE. Document
--                          branches never 422, so this means ingest failed after
--                          archive, or DeleteFile removed chunks but the storage
--                          delete was skipped/failed.
--
-- INVARIANT: zero 'orphaned-unexpected'. Any count of 'orphaned-by-rejection'
-- is fine. If every image object lands in 'orphaned-unexpected' with mimetype
-- application/octet-stream, the Store Original File node stopped forwarding the
-- binary's mime type — fix the workflow, not this audit.
--
-- How to run: Supabase SQL editor, or the temp-workflow pattern (manual n8n
-- workflow + Postgres node on the Session-pooler credential) — storage.objects
-- needs a service-role/postgres connection, PostgREST won't expose it.

-- 1. Summary — the invariant check.
with objects as (
  select o.name                                as file_id,
         o.metadata ->> 'mimetype'             as mimetype,
         exists (select 1 from public.documents d
                 where d.metadata ->> 'fileId' = o.name) as has_chunks
  from storage.objects o
  where o.bucket_id = 'price-lists'
)
select case
         when has_chunks               then 'referenced'
         when mimetype like 'image/%'  then 'orphaned-by-rejection'
         else                               'orphaned-unexpected'
       end     as classification,
       count(*) as objects
from objects
group by 1
order by 1;

-- 2. Detail — every non-referenced object, worst first.
with objects as (
  select o.name                                as file_id,
         o.metadata ->> 'mimetype'             as mimetype,
         o.created_at,
         (o.metadata ->> 'size')::bigint       as size_bytes,
         exists (select 1 from public.documents d
                 where d.metadata ->> 'fileId' = o.name) as has_chunks
  from storage.objects o
  where o.bucket_id = 'price-lists'
)
select case
         when mimetype like 'image/%' then 'orphaned-by-rejection'
         else                              'orphaned-unexpected'
       end as classification,
       file_id, mimetype, size_bytes, created_at
from objects
where not has_chunks
order by 1 desc, created_at;

-- 3. Reverse check — chunks whose fileId has no stored original. Expected for
--    files uploaded before the bucket existed (2026-07-02); they can't be
--    re-indexed without a user re-upload. New entries here after that date
--    mean Store Original File is failing silently (it's onError: continue).
select d.metadata ->> 'fileId' as file_id,
       count(*)                as chunks
from public.documents d
where d.metadata ? 'fileId'
  and not exists (select 1 from storage.objects o
                  where o.bucket_id = 'price-lists'
                    and o.name = d.metadata ->> 'fileId')
group by 1
order by 1;
