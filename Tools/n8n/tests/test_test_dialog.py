#!/usr/bin/env python3
"""R15 (iteration 2026-09-27): the owner's «Проверить бота» must talk to the prompt the bot ANSWERS CLIENTS with.

n8n 2.x keeps two copies of a workflow: `nodes` (the latest saved DRAFT, `versionId`) and `activeVersion.nodes` (the
PUBLISHED graph a live webhook executes, `activeVersionId`). Deactivation clears `activeVersionId`/`activeVersion`, and
activation without a versionId publishes the current draft. Test Dialog reads the bot's workflow through the public API
(`GET /api/v1/workflows/{id}`, which returns both) — these cases run the REAL Compose / Sources / Build Response Code
bodies of the canonical Test Dialog workflow on responses in the exact n8n 2.36.7 DTO shape (fullResponse wrapper),
built from the REAL WhatsApp and Telegram bot templates' nodes.

Contract (review R15 + the iteration README decision):
  * an ACTIVE bot is tested by its PUBLISHED version: draft «DRAFT PRICE 999» vs published «PUBLISHED PRICE 3200» → 3200;
  * an INACTIVE bot is tested by its draft — the version activation will publish — and the response SAYS so
    (promptVersion.source = 'draft'); this is not an error;
  * the response names the tested version: promptVersion {source, versionId, draftPending};
  * an active bot whose published version cannot be read, or whose tested version has no «AI Agent» node, gets an honest
    `bot_prompt_unavailable` — never a silent fallback to the draft or to whatever node sits at index 5.

    python3 Tools/n8n/tests/test_test_dialog.py
"""
import copy

from harness import Case, code, expr, node, report, wf

TD = wf('crTestDialog0001-Test_Dialog.json')
TEMPLATES = {'WA': wf('4wYitz5ek30SVNlT-WhatsApp_Bot.json'), 'TG': wf('4VN3gsFaC2HUYmcc-Telegram_Bot.json')}
DRAFT, PUB = 'DRAFT PRICE 999', 'PUBLISHED PRICE 3200'
VALIDATE = {'ok': True, 'profileId': 'r15-prof', 'workflowId': 'r15WorkflowId01', 'channel': 'whatsapp', 'text': 'Сколько стоит букет?',
            'history': [], 'runId': '4242', 'filterKey': 'botWaId'}


def bot_nodes(ch, marker, agent_name='AI Agent'):
    """The template's own node list with the AI Agent's system message carrying `marker` (a clone's composed prompt)."""
    nodes = copy.deepcopy(TEMPLATES[ch]['nodes'])
    agent = next(n for n in nodes if n['name'] == 'AI Agent')
    agent['parameters']['options']['systemMessage'] = f'Ты бот цветочного магазина. Цена букета из 25 роз: {marker} тенге.'
    agent['name'] = agent_name
    return nodes


def dto(ch, active, draft_marker, pub_marker=None, draft_id='v-draft', pub_id='v-pub', include_active_version=True,
        pub_agent_name='AI Agent', draft_agent_name='AI Agent'):
    """GET /api/v1/workflows/{id} as n8n 2.36.7 answers it (toWorkflowPublicDto), wrapped like fullResponse."""
    body = {'id': 'r15WorkflowId01', 'name': 'Bot', 'active': active, 'isArchived': False, 'versionId': draft_id,
            'nodes': bot_nodes(ch, draft_marker, draft_agent_name), 'connections': {}}
    if active:
        body['activeVersionId'] = pub_id
        if include_active_version:
            body['activeVersion'] = {'versionId': pub_id, 'workflowId': body['id'], 'nodes': bot_nodes(ch, pub_marker, pub_agent_name),
                                     'connections': {}, 'autosaved': False}
    else:
        body['activeVersionId'] = None
        body['activeVersion'] = None
    return {'json': {'body': body, 'statusCode': 200, 'headers': {}}}


def compose(read):
    return code(TD, 'Compose', read, named={'Validate': VALIDATE})[0]['json']


for ch in TEMPLATES:
    with Case(f'R15 [{ch}] A: an ACTIVE bot is tested by its PUBLISHED prompt (draft 999 vs published 3200 → 3200)') as c:
        out = compose(dto(ch, True, DRAFT, PUB))
        sm = out.get('systemMessage', '')
        c.check(out.get('ok') is True, f'the test must run: {out}')
        c.check(PUB in sm and DRAFT not in sm, f'tested the wrong version: …{sm[-80:]!r}')
        c.check(out.get('promptVersion') == {'source': 'published', 'versionId': 'v-pub', 'draftPending': True},
                f'the response must name the tested (published) version and that a draft differs: {out.get("promptVersion")}')

    with Case(f'R15 [{ch}] B: an INACTIVE bot is tested by its draft (what activation publishes) and says so — not an error') as c:
        out = compose(dto(ch, False, DRAFT))
        sm = out.get('systemMessage', '')
        c.check(out.get('ok') is True, f'an inactive bot must stay testable: {out}')
        c.check(DRAFT in sm, f'the draft is the only version an inactive bot has: …{sm[-80:]!r}')
        c.check(out.get('promptVersion') == {'source': 'draft', 'versionId': 'v-draft', 'draftPending': False},
                f'the response must say the draft was tested: {out.get("promptVersion")}')

    with Case(f'R15 [{ch}] C: an active bot in sync (draft == published) keeps working, labelled published') as c:
        out = compose(dto(ch, True, PUB, PUB, draft_id='v-same', pub_id='v-same'))
        c.check(out.get('ok') is True and PUB in out.get('systemMessage', ''), f'{out}')
        c.check(out.get('promptVersion') == {'source': 'published', 'versionId': 'v-same', 'draftPending': False}, f'{out.get("promptVersion")}')

    with Case(f'R15 [{ch}] D: an active bot whose published version is NOT in the response fails closed (never the draft)') as c:
        out = compose(dto(ch, True, DRAFT, PUB, include_active_version=False))
        c.check(out.get('ok') is False and out.get('error') == 'bot_prompt_unavailable',
                f'a published version that cannot be read must not be replaced by the draft: ok={out.get("ok")} '
                f'sm=…{str(out.get("systemMessage", ""))[-40:]!r} err={out.get("error")}')

    with Case(f'R15 [{ch}] E: the published version has no «AI Agent» node → honest error, not the draft') as c:
        out = compose(dto(ch, True, DRAFT, PUB, pub_agent_name='Old Agent'))
        c.check(out.get('ok') is False and out.get('error') == 'bot_prompt_unavailable', f'{out.get("ok")} {out.get("error")} {str(out.get("systemMessage", ""))[-40:]!r}')

    with Case(f'R15 [{ch}] F: no «AI Agent» by name → no prompt taken from whatever node sits at index 5') as c:
        out = compose(dto(ch, False, DRAFT, draft_agent_name='Renamed Agent'))
        c.check(out.get('ok') is False and out.get('error') == 'bot_prompt_unavailable',
                f'nodes[5] must not stand in for the agent: ok={out.get("ok")} sm=…{str(out.get("systemMessage", ""))[-40:]!r}')

    with Case(f'R15 [{ch}] G: a failed read (404) stays an honest bot_prompt_unavailable') as c:
        out = compose({'json': {'body': {'message': 'Not Found'}, 'statusCode': 404, 'headers': {}}})
        c.check(out.get('ok') is False and out.get('error') == 'bot_prompt_unavailable', f'{out}')

    with Case(f'R15 [{ch}] H: the owner is told which version answered — promptVersion reaches the response; nothing sent/counted') as c:
        comp = compose(dto(ch, True, DRAFT, PUB))
        src = code(TD, 'Sources', {'json': {}}, named={'Compose': comp})[0]['json']
        c.check(PUB in src.get('systemMessage', '') and DRAFT not in src.get('systemMessage', ''), 'Sources must hand the tested prompt to the agent')
        resp = code(TD, 'Build Response', {'json': {'actions': []}},
                    named={'Validate': VALIDATE, 'Compose': comp, 'Sources': src, 'AI Agent': {'output': 'Букет стоит 3200 тенге.'}})[0]['json']
        c.check(resp.get('success') is True and resp.get('sent') is False and resp.get('quotaCounted') is False, f'{resp}')
        c.check(resp.get('promptVersion') == {'source': 'published', 'versionId': 'v-pub', 'draftPending': True},
                f'the response must carry the tested version: {resp.get("promptVersion")}')

with Case('R15 I: an unreadable published version answers 502 bot_prompt_unavailable through the existing branch') as c:
    out = compose(dto('WA', True, DRAFT, PUB, include_active_version=False))
    un = node(TD, 'Respond Unavailable')['parameters']
    body = expr(un['responseBody'], out)
    c.check(expr(node(TD, 'Prompt OK?')['parameters']['conditions']['conditions'][0]['leftValue'], out) is not True, 'Prompt OK? must route it to the error branch')
    c.check(isinstance(body, dict) and body.get('error') == 'bot_prompt_unavailable' and body.get('success') is False, f'{body}')

report()
