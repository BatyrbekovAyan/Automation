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

report()
