#!/usr/bin/env python3
"""The graph walker itself (tests/graph_walk.py, shared by test_chat_audience.py and test_decide_audience.py): where it
stands in for n8n, it must answer what n8n 2.36 answers — each rule below is read off the staging install's own source
(n8n-core 2.36.5, n8n-nodes-base 2.36.4, n8n-workflow), the version prod runs.

  postgres_zero_rows_answer             a Postgres node (typeVersion 2.6, single batching) whose statements return no row:
                                        nothing when every query is a SELECT by n8n's own isSelectQuery (a data-modifying
                                        CTE under a top-level SELECT counts as one), else ONE {success: true} item; the
                                        older rule (typeVersion < 2.3): {success: true} for every executeQuery
  passthrough_hands_the_input_on        a node that throws OUTSIDE its runner (credentials, connection setup, an expression)
                                        and continues on error hands on its INPUT items unchanged — no error item; a node
                                        that does not continue on error ends the execution
  unexecuted_node_answers_isexecuted_false   $('Node') of a node of the workflow that has not run: isExecuted false, and
                                        first() throws; a node that is not in the workflow throws at $()
  trigger_keeps_only_the_declared_fields     an Execute Workflow trigger hands on the declared fields — null when left out
                                        — and nothing else
  execute_workflow_like_n8n             an Execute Workflow node type-checks its mapped inputs with n8n's own validateFieldType
                                        (a numeric string for a number field throws outside the runner: the INPUT is handed on,
                                        the sub-workflow never starts), runs the sub-workflow once, hands on its last node's
                                        items; a failing sub-workflow is an {error} item; a Code node sees $prevNode.name
  postgres_string_form_params           queryReplacement in n8n's string form (`={{ a }},{{ b }}`): each {{ }} on its own,
                                        JSON kept whole, anything else split at commas
  stop_ends_the_walk                    a node named in `stop` ends the walk where it stands, unexecuted

    python3 Tools/n8n/tests/test_graph_walk.py
"""
from graph_walk import walk_graph
from harness import Case, report
from rc_events_chain import Session

SETUP = "create temp table gw_t (id int primary key, v text);\ninsert into gw_t values (1, 'a');"
TRIGGER = 'When Executed by Another Workflow'


def trigger(*fields):
    return {'name': TRIGGER, 'type': 'n8n-nodes-base.executeWorkflowTrigger', 'typeVersion': 1.1, 'position': [0, 0],
            'parameters': {'inputSource': 'workflowInputs',
                           'workflowInputs': {'values': [{'name': f, 'type': 'string'} for f in fields]}}}


def postgres(name, query, version=2.6, on_error='continueRegularOutput', always=True):
    n = {'name': name, 'type': 'n8n-nodes-base.postgres', 'typeVersion': version, 'position': [200, 0],
         'parameters': {'operation': 'executeQuery', 'query': query, 'options': {'queryReplacement': '={{ [ $json.x ] }}'}}}
    if on_error:
        n['onError'] = on_error
    if always:
        n['alwaysOutputData'] = True
    return n


def code(name, js, x=400):
    return {'name': name, 'type': 'n8n-nodes-base.code', 'typeVersion': 2, 'position': [x, 0], 'parameters': {'jsCode': js}}


def chain(*nodes):
    conns = {}
    for a, b in zip(nodes, nodes[1:]):
        conns[a['name']] = {'main': [[{'node': b['name'], 'type': 'main', 'index': 0}]]}
    return {'nodes': list(nodes), 'connections': conns}


def out(run, name):
    return [i['json'] for i in ((run.outputs.get(name) or [[]])[0])]


def walk(pg, w, item, throws=None):
    return walk_graph(pg, w, TRIGGER, items=[{'json': item}], throws=throws)


with Case('postgres_zero_rows_answer: no row back → nothing for SELECT queries (alwaysOutputData then hands on one empty '
          'item), ONE {success: true} item for anything else; the pre-2.3 rule answers {success: true} for every executeQuery') as c:
    with Session(SETUP) as pg:
        cases = (
            ('select, no row', postgres('Q', 'select id from gw_t where v = $1'), [{}]),
            ('select, no row, no alwaysOutputData', postgres('Q', 'select id from gw_t where v = $1', always=False), []),
            ('update, no row', postgres('Q', 'update gw_t set v = v where v = $1'), [{'success': True}]),
            ('a data-modifying CTE under a top-level select', postgres(
                'Q', 'with up as (update gw_t set v = v where v = $1 returning id) select id from up'), [{}]),
            ('a comment, then an update', postgres('Q', '-- the select in this comment does not count\nupdate gw_t set v = v where v = $1'),
             [{'success': True}]),
            ('select, no row, typeVersion 2.2', postgres('Q', 'select id from gw_t where v = $1', version=2.2), [{'success': True}]),
            ('a row back', postgres('Q', 'select id from gw_t where v = $1 or true'), [{'id': 1}]),
            ('update returning, a row back', postgres('Q', "update gw_t set v = v where id = 1 and $1 <> '' returning id"), [{'id': 1}]),
            ('update returning, no row', postgres('Q', 'update gw_t set v = v where v = $1 returning id'), [{'success': True}]),
            ('a CTE that returns under a top-level update', postgres(
                'Q', 'with c as (select id from gw_t where v = $1) update gw_t set v = v where id in (select id from c)'),
             [{'success': True}]),
        )
        for label, n, want in cases:
            r = walk(pg, chain(trigger('x'), n), {'x': 'zzz'})
            c.check(r.halt is None and out(r, 'Q') == want, f'{label}: {out(r, "Q")} {r}')

with Case('passthrough_hands_the_input_on: a continue-on-error node that throws outside its runner hands on its INPUT items '
          'unchanged (n8n-core: nodeSuccessData = the input); a node without onError ends the execution') as c:
    with Session(SETUP) as pg:
        w = chain(trigger('x', 'kind'), postgres('Q', 'select id from gw_t where v = $1'), code('After', 'return $input.all();'))
        r = walk(pg, w, {'x': 'a', 'kind': 'business'}, throws={'Q': 'getCredentials: credential not found'})
        c.check(r.halt is None and out(r, 'Q') == [{'x': 'a', 'kind': 'business'}] and out(r, 'After') == [{'x': 'a', 'kind': 'business'}]
                and ('sql', 'Q') not in r.events and ('throw', 'Q') in r.events, f'{out(r, "Q")} {r.events} {r}')
        r = walk(pg, w, {'x': 'a', 'kind': 'business'})
        c.check(out(r, 'Q') == [{'id': 1}], f'without the throw the statement answers: {out(r, "Q")}')
        stop = chain(trigger('x'), postgres('Q', 'select 1', on_error=None), code('After', 'return $input.all();'))
        r = walk(pg, stop, {'x': 'a'}, throws={'Q': 'getCredentials: credential not found'})
        c.check(r.halt is not None and 'Q' in r.halt and not r.ran('After'), f'no onError: {r}')

with Case('unexecuted_node_answers_isexecuted_false: $() of a node of the workflow that has not run answers isExecuted '
          'false and throws on first(); a name that is not a node throws at $()') as c:
    with Session(SETUP) as pg:
        probe = code('Probe', "const ex = $('Idle').isExecuted;\nlet threw = '';\ntry { $('Idle').first(); } catch (e) { threw = String(e.message); }\n"
                              "let missing = '';\ntry { $('Nope'); } catch (e) { missing = String(e.message); }\n"
                              "return [{ json: { ex, threw, missing } }];")
        idle = code('Idle', 'return $input.all();', x=800)
        w = chain(trigger('x'), probe)
        w['nodes'].append(idle)
        r = walk(pg, w, {'x': 'a'})
        got = out(r, 'Probe')
        c.check(got and got[0]['ex'] is False and "hasn't been executed" in got[0]['threw'] and "doesn't exist" in got[0]['missing'],
                f'{got} {r}')

with Case('trigger_keeps_only_the_declared_fields: an Execute Workflow trigger hands on every declared field — null when the '
          'caller left it out — and nothing else') as c:
    with Session(SETUP) as pg:
        r = walk(pg, chain(trigger('a', 'b')), {'a': 1, 'extra': 'dropped'})
        c.check(out(r, TRIGGER) == [{'a': 1, 'b': None}], f'{out(r, TRIGGER)}')


def execute(name, values, types, convert=False, on_error='continueRegularOutput', always=False):
    """An Execute Workflow node (typeVersion 1.2, the shape the bot templates' Decide Audience has) calling 'subWorkflow01'."""
    n = {'name': name, 'type': 'n8n-nodes-base.executeWorkflow', 'typeVersion': 1.2, 'position': [200, 0],
         'parameters': {'workflowId': {'__rl': True, 'value': 'subWorkflow01', 'mode': 'id'},
                        'workflowInputs': {'mappingMode': 'defineBelow', 'value': values, 'matchingColumns': [],
                                           'schema': [{'id': k, 'displayName': k, 'required': False, 'defaultMatch': False, 'display': True,
                                                       'canBeUsedToMatch': True, 'type': t, 'removed': False} for k, t in types],
                                           'attemptToConvertTypes': convert, 'convertFieldsToString': False},
                        'options': {}}}
    if on_error:
        n['onError'] = on_error
    if always:
        n['alwaysOutputData'] = True
    return n


def sub_workflow(js='return [{ json: { got: $input.first().json } }];'):
    """The called workflow: a trigger declaring n (number) and s (string), then one Code node — its LAST node."""
    t = trigger('n', 's')
    t['parameters']['workflowInputs']['values'] = [{'name': 'n', 'type': 'number'}, {'name': 's', 'type': 'string'}]
    return chain(t, code('Echo', js))


with Case('execute_workflow_like_n8n: the mapped inputs are type-checked by n8n\'s own validateFieldType (strict unless '
          'attemptToConvertTypes) — a number passes, null passes, a numeric STRING for a number field throws outside the node\'s '
          'runner (it hands its INPUT on, the sub-workflow never starts), with conversion it passes as a number; the '
          'sub-workflow\'s trigger keeps its declared fields, its LAST node\'s items come back; a failing sub-workflow is an '
          '{error} item; $prevNode names the node that called a Code node') as c:
    with Session(SETUP) as pg:
        subs = {'subWorkflow01': sub_workflow()}
        after = code('After', 'return [{ json: { prev: $prevNode.name, item: $input.first().json } }];', x=600)
        mapping = {'n': '={{ $json.n }}', 's': 'literal'}
        types = (('n', 'number'), ('s', 'string'))

        def run_with(item, **kw):
            w = chain(trigger('n', 'extra'), execute('Call', mapping, types, **kw), after)
            return walk_graph(pg, w, TRIGGER, items=[{'json': item}], subs=subs)

        r = run_with({'n': 12, 'extra': 'x'})
        c.check(out(r, 'Call') == [{'got': {'n': 12, 's': 'literal'}}] and out(r, 'After') == [{'prev': 'Call', 'item': {'got': {'n': 12, 's': 'literal'}}}]
                and ('sub', 'Call') in r.events and r.subs.get('Call'), f'a number: {out(r, "Call")} {out(r, "After")} {r}')
        r = run_with({'n': None, 'extra': 'x'})
        c.check(out(r, 'Call') == [{'got': {'n': None, 's': 'literal'}}], f'null: {out(r, "Call")}')
        r = run_with({'n': '12', 'extra': 'x'})
        c.check(out(r, 'Call') == [{'n': '12', 'extra': 'x'}] and ('throw', 'Call') in r.events and not r.subs.get('Call'),
                f'a numeric string, strict: {out(r, "Call")} {r.events}')
        r = run_with({'n': '12', 'extra': 'x'}, convert=True)
        c.check(out(r, 'Call') == [{'got': {'n': 12, 's': 'literal'}}], f'a numeric string, converted: {out(r, "Call")}')
        r = run_with({'n': '12', 'extra': 'x'}, on_error=None)
        c.check(r.halt is not None and not r.ran('After'), f'without onError the execution ends: {r}')
        failing = {'subWorkflow01': sub_workflow("throw new Error('the sub-workflow failed');")}
        w = chain(trigger('n'), execute('Call', mapping, types), after)
        r = walk_graph(pg, w, TRIGGER, items=[{'json': {'n': 1}}], subs=failing)
        got = out(r, 'Call')
        c.check(len(got) == 1 and 'failed' in str(got[0].get('error')), f'a failing sub-workflow: {got} {r}')

with Case('postgres_string_form_params: a queryReplacement in n8n\'s string form (`={{ a }},{{ b }}`, typeVersion ≥ 2.5) — '
          'each {{ }} on its own, a JSON value kept whole, anything else split at commas') as c:
    with Session(SETUP) as pg:
        n = postgres('Q', 'select $1::text as a, $2::text as b, $3::text as c')
        n['parameters']['options']['queryReplacement'] = '={{ $json.x }},{{ $json.y }}'
        r = walk(pg, chain(trigger('x', 'y'), n), {'x': 'one', 'y': 'two,three'})
        c.check(out(r, 'Q') == [{'a': 'one', 'b': 'two', 'c': 'three'}], f'a comma inside a value splits (n8n\'s own rule): {out(r, "Q")}')
        two = postgres('Q', 'select $1::text as a, $2::text as b')
        two['parameters']['options']['queryReplacement'] = '={{ $json.x }},{{ $json.y }}'
        r = walk(pg, chain(trigger('x', 'y'), two), {'x': 'wa-prof', 'y': '5550001112'})
        c.check(out(r, 'Q') == [{'a': 'wa-prof', 'b': '5550001112'}], f'a number-like value stays whole: {out(r, "Q")}')
        old = postgres('Q', 'select $1::text as a', version=2.2)
        old['parameters']['options']['queryReplacement'] = '={{ $json.x }}'
        try:
            walk(pg, chain(trigger('x'), old), {'x': 'one'})
            c.check(False, 'the text form before typeVersion 2.5 (another rule in n8n) must be refused, not guessed')
        except NotImplementedError:
            pass

with Case('stop_ends_the_walk: a node named in `stop` is recorded where the walk ends and does not run') as c:
    with Session(SETUP) as pg:
        w = chain(trigger('x'), code('A', 'return $input.all();'), code('B', "throw new Error('must not run');"))
        r = walk_graph(pg, w, TRIGGER, items=[{'json': {'x': 1}}], stop=('B',))
        c.check(r.halt is None and r.trail == [TRIGGER, 'A', 'B'] and ('stop', 'B') in r.events and 'B' not in r.outputs, f'{r} {r.events}')

report()
