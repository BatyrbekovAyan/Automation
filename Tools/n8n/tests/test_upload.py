#!/usr/bin/env python3
"""R3 (iteration 2026-09-27): a price-list upload must survive the Auth Gate and reach extraction, the upload
registry must receive the real decoded values, and a session may only upload under ITS OWN bot workflow ids.

Runs the canonical Upload File graph (Tools/n8n/tests/upload_chain.js — real n8n 2.36 Code-result validation,
Extract From File and binary helpers) from the multipart shape Unity's WWWForm sends: every AddField part is a
text/plain part, which n8n files under item.binary, and the body is empty. Then runs the REAL Register Upload SQL
with the evaluated parameters against a temp `uploaded_files` in the staging Postgres (BEGIN … ROLLBACK).
Also: every generated Auth Gate (all gated webhooks, the proxy, Handoffs, Test Dialog) keeps an input's binary.
"""
import base64
import json
import subprocess
import sys
from pathlib import Path

from harness import Case, N8N, WF, bind, report, sql_json

DRIVER = N8N / 'tests' / 'upload_chain.js'
UPLOAD = WF / 'KoTuIlk4LMrlvnWI-Upload_File.json'
FILE_TEXT = 'Товар А — 1000 ₸\nТовар Б — 2500 ₸\n'
GUID = '3f2b8c1e-9d4a-4b6f-8e21-5a7c0d9e1f42'
OWN_WA, OWN_TG, FOREIGN = 'wfOwnWA00000001', 'wfOwnTG00000001', 'wfVictimWA00001'
CLAIMS = {'i': 'inst-A', 'u': '$RCAnonymousID:0123456789abcdef0123456789abcdef', 'p': ['profA'], 'w': [OWN_WA, OWN_TG], 'c': []}


def drive(mode, files, payload):
    r = subprocess.run(['node', str(DRIVER), mode, *map(str, files)], input=json.dumps(payload), text=True, capture_output=True)
    if r.returncode != 0:
        raise RuntimeError('upload_chain.js failed: ' + r.stderr.strip()[-2000:])
    return json.loads(r.stdout)


REGISTERED = {'Register Upload': {'allowed': True}}          # the registry's verdict row (SQL tested below)


def scenario(claims, wa, tg, file_id, compat_open=False, pg=REGISTERED, file=None):
    return {'compatOpen': compat_open, 'claims': claims, 'pg': pg,
            'fields': {'whatsappWorkflowId': wa, 'telegramWorkflowId': tg, 'contentType': 'products', 'fileId': file_id},
            'file': file or {'text': FILE_TEXT, 'name': 'price.txt', 'ext': 'txt', 'mime': 'text/plain'}}


def tiny_pdf(text):
    """A minimal valid one-page PDF carrying `text` (ASCII), with a correct xref — for the PDF branch."""
    objs = ['<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
            '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 144] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
            None, '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>']
    stream = f'BT /F1 18 Tf 20 100 Td ({text}) Tj ET'
    objs[3] = f'<< /Length {len(stream)} >>\nstream\n{stream}\nendstream'
    out, offs = '%PDF-1.4\n', []
    for i, o in enumerate(objs, 1):
        offs.append(len(out.encode('latin-1')))
        out += f'{i} 0 obj\n{o}\nendobj\n'
    xref = len(out.encode('latin-1'))
    out += f'xref\n0 {len(objs) + 1}\n0000000000 65535 f \n' + ''.join(f'{x:010d} 00000 n \n' for x in offs)
    out += f'trailer\n<< /Size {len(objs) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n'
    return base64.b64encode(out.encode('latin-1')).decode()


SCENARIOS = {
    'legacy_own': scenario(None, OWN_WA, '-1', GUID, compat_open=True),
    'session_own_wa': scenario(CLAIMS, OWN_WA, '-1', GUID),
    'session_own_both': scenario(CLAIMS, OWN_WA, OWN_TG, GUID),
    'session_foreign_wa': scenario(CLAIMS, FOREIGN, '-1', GUID),
    'session_foreign_tg': scenario(CLAIMS, OWN_WA, FOREIGN, GUID),
    'session_pathlike_fileid': scenario(CLAIMS, OWN_WA, '-1', '../other-bucket/x'),
    'legacy_pathlike_fileid': scenario(None, OWN_WA, '-1', '../other-bucket/x', compat_open=True),
    'session_compat_closed_no_token': scenario(None, OWN_WA, '-1', GUID),
    'session_foreign_fileid': scenario(CLAIMS, OWN_WA, '-1', GUID, pg={'Register Upload': {'allowed': False}}),
    'session_registry_fault': scenario(CLAIMS, OWN_WA, '-1', GUID, pg={'Register Upload': {'message': 'Connection terminated', 'error': {'message': 'Connection terminated'}}}),
    'session_valid_pdf': scenario(CLAIMS, OWN_WA, '-1', GUID, file={'b64': tiny_pdf('Rose 1000 KZT'), 'name': 'price.pdf', 'ext': 'pdf', 'mime': 'application/pdf'}),
    'session_corrupt_pdf': scenario(CLAIMS, OWN_WA, '-1', GUID, file={'b64': base64.b64encode(b'%PDF-garbage, not a pdf').decode(), 'name': 'price.pdf', 'ext': 'pdf', 'mime': 'application/pdf'}),
}
R = drive('chain', [UPLOAD], SCENARIOS)
REGISTER_SQL = next(n for n in json.loads(UPLOAD.read_text(encoding='utf-8'))['nodes'] if n['name'] == 'Register Upload')['parameters']['query']
TEMP_TABLE = ("create temp table uploaded_files (file_id text primary key, owner_id text, bot_wa_id text, bot_tg_id text, content_type text, "
              "file_name text, created_at timestamptz not null default now(), deleted_at timestamptz);")


def registered_row(params):
    return sql_json(bind(REGISTER_SQL, params) + "\nselect row_to_json(u) from (select file_id, owner_id, bot_wa_id, bot_tg_id, content_type, file_name from uploaded_files) u;",
                    TEMP_TABLE)


def own_upload(key, owner, wa, tg):
    r = R[key]
    with Case(f'R3 {key}: the file survives the gate and reaches extraction + the vector-store insert') as c:
        c.check(r['error'] is None, f"chain failed: {r['error']} trace={r['trace']}")
        c.check(r['gate'] and r['gate']['ok'] and 'data' in r['gate']['binaryKeys'], f"gate output lost the file: {r['gate']}")
        c.check(r['extractedText'] == FILE_TEXT, f"Extract from TXT got {r['extractedText']!r}")
        c.check((r['vectorStoreInput'] or {}).get('content', '').strip() == FILE_TEXT.strip(), f"vector store input {r['vectorStoreInput']}")
        c.check((r['storeOriginal'] or {}).get('gotFile') is True and str((r['storeOriginal'] or {}).get('url', '')).endswith('/price-lists/' + GUID),
                f"Store Original File {r['storeOriginal']}")
        c.check(r['response'] is None or r['response']['code'] == 200, f"unexpected response {r['response']}")
    with Case(f'R3 {key}: Register Upload receives the decoded values and the real SQL stores the row') as c:
        p = (r['registerUpload'] or {}).get('params')
        c.check(p == [GUID, owner, wa, tg, 'products', 'price.txt'], f"params {p}")
        if p:
            row = registered_row(p)
            c.check(row == {'file_id': GUID, 'owner_id': owner or None, 'bot_wa_id': None if wa in ('', '-1') else wa,
                            'bot_tg_id': None if tg in ('', '-1') else tg, 'content_type': 'products', 'file_name': 'price.txt'}, f"row {row}")
        c.check(r['sideEffects'][:1] == ['Register Upload'], f"Register Upload must be the first side effect: {r['sideEffects']}")
        c.check(r['trace'].index('Register Upload') < r['trace'].index('Switch'),
                f"the registry verdict must come BEFORE the ingest (sequential, not a sibling branch): {r['trace']}")


own_upload('legacy_own', '', OWN_WA, '-1')
own_upload('session_own_wa', 'inst-A', OWN_WA, '-1')
own_upload('session_own_both', 'inst-A', OWN_WA, OWN_TG)

for key, why in (('session_foreign_wa', 'a WhatsApp workflow id the token does not own'),
                 ('session_foreign_tg', 'a Telegram workflow id the token does not own'),
                 ('session_pathlike_fileid', 'a fileId that is not a GUID (it is spliced into the service-role storage URL)'),
                 ('legacy_pathlike_fileid', 'a non-GUID fileId in legacy mode too')):
    r = R[key]
    with Case(f'R3 {key}: {why} is refused with 403 before ANY write') as c:
        c.check(r['error'] is None, f"chain failed instead of refusing: {r['error']}")
        c.check((r['response'] or {}).get('code') == 403, f"response {r['response']}")
        c.check(r['sideEffects'] == [], f"side effects ran: {r['sideEffects']}")

with Case('R3 session_foreign_fileid: a fileId the registry says belongs to another install -> 403, nothing but the registry check ran') as c:
    r = R['session_foreign_fileid']
    c.check((r['response'] or {}).get('code') == 403 and r['sideEffects'] == ['Register Upload'], f"{r['response']} {r['sideEffects']}")

with Case('R3 session_registry_fault: a registry DB fault -> 503 db_unavailable, no ingest') as c:
    r = R['session_registry_fault']
    c.check((r['response'] or {}).get('code') == 503 and (r['response'] or {}).get('body', {}).get('error') == 'db_unavailable'
            and r['sideEffects'] == ['Register Upload'], f"{r['response']} {r['sideEffects']}")

with Case('R3 session_valid_pdf: a real PDF is extracted and reaches the vector-store insert') as c:
    r = R['session_valid_pdf']
    c.check(r['error'] is None and 'Rose 1000 KZT' in str((r['vectorStoreInput'] or {}).get('content', '')), f"{r['error']} {r['vectorStoreInput']}")

with Case('R3 session_corrupt_pdf: an unreadable PDF answers 422 unreadable_file — never a 200 the app records as uploaded') as c:
    r = R['session_corrupt_pdf']
    c.check(r['error'] is None and (r['response'] or {}).get('code') == 422
            and (r['response'] or {}).get('body', {}).get('error') == 'unreadable_file', f"{r['error']} {r['response']}")
    c.check('Supabase Vector Store' not in r['sideEffects'], f"nothing may be ingested: {r['sideEffects']}")

# ---- the registry SQL decides: the caller's own / a new fileId -> allowed; another install's -> refused, row untouched ----
REG_SETUP = TEMP_TABLE + """
insert into uploaded_files (file_id, owner_id, bot_wa_id) values
  ('11111111-1111-4111-8111-111111111111', 'inst-B', 'wfB'),
  ('22222222-2222-4222-8222-222222222222', null, 'wfL'),
  ('33333333-3333-4333-8333-333333333333', 'inst-A', 'wfOwnWA00000001');"""


def reg(file_id, owner):
    return sql_json(f"create temp table r as {bind(REGISTER_SQL.rstrip().rstrip(';'), [file_id, owner, OWN_WA, '-1', 'products', 'price.txt'])};\n"
                    "select json_build_object('allowed', (select allowed from r), 'rows', (select json_agg(json_build_object('f', file_id, 'o', owner_id, 'wa', bot_wa_id) order by file_id) from uploaded_files));",
                    REG_SETUP)


with Case('R3 Register Upload SQL: a new fileId is registered to the caller (session) / to nobody (legacy) and allowed') as c:
    a, b = reg(GUID, 'inst-A'), reg(GUID, '')
    c.check(a['allowed'] is True and {'f': GUID, 'o': 'inst-A', 'wa': OWN_WA} in a['rows'], f"session {a}")
    c.check(b['allowed'] is True and {'f': GUID, 'o': None, 'wa': OWN_WA} in b['rows'], f"legacy {b}")

with Case('R3 Register Upload SQL: another install\'s fileId is refused and its row is not changed') as c:
    a = reg('11111111-1111-4111-8111-111111111111', 'inst-A')
    b = reg('11111111-1111-4111-8111-111111111111', '')
    row = {'f': '11111111-1111-4111-8111-111111111111', 'o': 'inst-B', 'wa': 'wfB'}
    c.check(a['allowed'] is False and row in a['rows'], f"session {a}")
    c.check(b['allowed'] is False and row in b['rows'], f"legacy caller {b}")

with Case('R3 Register Upload SQL: a repeat of the caller\'s own fileId and an owner-less legacy row are allowed (the legacy row is adopted)') as c:
    a = reg('33333333-3333-4333-8333-333333333333', 'inst-A')
    b = reg('22222222-2222-4222-8222-222222222222', 'inst-A')
    c.check(a['allowed'] is True and len([x for x in a['rows'] if x['f'].startswith('3333')]) == 1, f"repeat {a}")
    c.check(b['allowed'] is True and {'f': '22222222-2222-4222-8222-222222222222', 'o': 'inst-A', 'wa': 'wfL'} in b['rows'], f"legacy row {b}")

with Case('R3 compat window closed: a token-less upload is 401 and nothing runs') as c:
    r = R['session_compat_closed_no_token']
    c.check((r['response'] or {}).get('code') == 401 and r['sideEffects'] == [], f"{r['response']} {r['sideEffects']}")

TEMP_DIRS = []


def mutated_copy(mutate):
    """The canonical workflows in a temp dir with Upload File mutated — for the structural guards' negative cases."""
    import shutil
    import tempfile
    d = Path(tempfile.mkdtemp(prefix='r3-neg-'))
    TEMP_DIRS.append(d)
    for p in WF.glob('*.json'):
        shutil.copy(p, d / p.name)
    w = json.loads((d / UPLOAD.name).read_text(encoding='utf-8'))
    mutate(w)
    (d / UPLOAD.name).write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    return d


def bypass(w):   # the pre-R3 wiring: registry + ingest straight off the last extract, no field check
    w['connections']['Extract Telegram Workflow Id'] = {'main': [[{'node': 'Register Upload', 'type': 'main', 'index': 0},
                                                                  {'node': 'Switch', 'type': 'main', 'index': 0},
                                                                  {'node': 'Merge', 'type': 'main', 'index': 1}]]}


def sibling_registry(w):   # R3 part 1 wiring: the registry as a SIBLING of the ingest (n8n v1 runs it last)
    w['connections']['Upload Fields Valid?']['main'][0] = [{'node': 'Register Upload', 'type': 'main', 'index': 0},
                                                          {'node': 'Switch', 'type': 'main', 'index': 0},
                                                          {'node': 'Merge', 'type': 'main', 'index': 1}]


def old_gate(w):  # the pre-R3 gate that re-emitted json only
    g = next(n for n in w['nodes'] if n['name'] == 'Auth Gate')
    g['parameters']['jsCode'] = g['parameters']['jsCode'].replace('...(inItem.binary ? { binary: inItem.binary } : {}), ', '')


with Case('R3 negative: verify-auth-gate.py rejects the pre-R3 wiring (no field check) and the binary-dropping gate') as c:
    for label, m in (('bypass', bypass), ('old gate', old_gate), ('registry as a sibling of the ingest', sibling_registry)):
        d = mutated_copy(m)
        r = subprocess.run([sys.executable, str(N8N / 'verify-auth-gate.py'), '--dir', str(d)], capture_output=True, text=True)
        c.check(r.returncode != 0 and 'AUTH GATE FAIL' in r.stdout, f"{label}: verifier passed a broken Upload File: {r.stdout[-300:]}")
    r = subprocess.run([sys.executable, str(N8N / 'verify-auth-gate.py')], capture_output=True, text=True)
    c.check(r.returncode == 0, f"verifier fails on the canonical set: {r.stdout[-300:]}")

with Case('R3 negative: verify_rag.py rejects an ingest edge that bypasses Upload Fields Valid?') as c:
    d = mutated_copy(lambda w: w['connections']['Extract Telegram Workflow Id']['main'][0].append({'node': 'Merge', 'type': 'main', 'index': 1}))
    import importlib.util
    spec = importlib.util.spec_from_file_location('verify_rag_neg', N8N / 'verify_rag.py')
    vr = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(vr)
    vr.WF = str(d)
    try:
        vr.check_upload_no_put_chain()
        c.check(False, 'verify_rag passed a bypass edge')
    except AssertionError as e:
        c.check('ONLY Upload Fields Valid?' in str(e), str(e))

with Case('R3 staging isolation: the derived staging copies can no longer write to the real Supabase project') as c:
    from harness import module
    d = module('staging/derive-staging.py')
    writers = {}
    for p in sorted(WF.glob('*.json')):
        w = json.loads(d.derive_file(p.read_text(encoding='utf-8')))
        before = d.supabase_writers_left(w)
        d.isolate_supabase_writers(w)
        if before:
            writers[p.name] = (before, d.supabase_writers_left(w), w)
    c.check(set(writers) == {UPLOAD.name, 'ZTqpumOpL1rNDOp6-Delete_File.json', 'lmjYsdNcQA2IE5rl-Delete_Bot_Files.json'},
            f"canonical Supabase writers: { {k: v[0] for k, v in writers.items()} }")
    c.check(all(not v[1] for v in writers.values()), f"writers left after isolation: { {k: v[1] for k, v in writers.items()} }")
    up = writers.get(UPLOAD.name, (None, None, {'nodes': []}))[2]
    stub = next((n for n in up['nodes'] if n['name'] == 'Supabase Vector Store'), {})
    qr = ((stub.get('parameters') or {}).get('options') or {}).get('queryReplacement', '')
    c.check(stub.get('type') == 'n8n-nodes-base.postgres' and all(k in qr for k in ('"botWaId"', '"botTgId"', '"fileId"', '"contentType"')),
            f"the staging stub must carry the chunk metadata: {stub.get('type')} {qr[:200]}")
    c.check(not any(n['name'] in ('Embeddings OpenAI', 'Data Loader') for n in up['nodes']), 'embedding sub-nodes must go with the insert')

with Case('R3 gate contract: every generated Auth Gate re-emits the input binary (no webhook can lose a file)') as c:
    files = sorted(p for p in WF.glob('*.json') if '"name": "Auth Gate"' in p.read_text(encoding='utf-8'))
    g = drive('gates', files, {'text': FILE_TEXT})
    c.check(len(g) >= 16, f"expected >= 16 gated workflows, found {len(g)}")
    lost = [f for f, v in g.items() if not (v.get('binaryKept') and v.get('crAuth') and v.get('bodyKept'))]
    c.check(not lost, f"gate drops binary in {lost}")

import shutil
for d in TEMP_DIRS:
    shutil.rmtree(d, ignore_errors=True)
report()
