#!/usr/bin/env python3
"""Generic guard (V1, iteration 2026-09-28): no Postgres node of ANY workflow may let a query parameter or an n8n
expression land where its value stops being a quoted SQL literal.

Why this exists. The n8n Postgres node (v2, executeQuery) does NOT send `$1, $2, …` to the server as bind
parameters. It formats the query on the client — `pgp.helpers.concat([{query, values, options:{partial:true}}])`,
i.e. pg-promise's `as.format` — and pg-promise replaces every `$N` it finds by a plain regex over the WHOLE text:
in code, but equally inside `--` and `/* */` comments, inside '…' / E'…' / "…" / $$…$$ literals, with or without
backticks. A comment that names a parameter («$1 fileId, $2 gate mode…») therefore receives the raw value, and a
value carrying a newline ends the comment: the rest of the value is executed as a separate statement. That was a
real, client-reachable SQL injection in Delete File (without a token in the compat window, and with any bootstrap
token after it), Suggest Replies, Create WA/TG and the new Auth Session (review 2026-09-28, V1). The bot templates
had the same defect once (R7 part 2) and verify-reply-pipeline.py (13) pinned it for those two files only.

This checker owns the rule for EVERY `n8n-nodes-base.postgres` node in every workflow, using a Postgres-faithful
tokenizer (line comments ending at \\n or \\r, NESTED block comments, '…' with '' escapes, E'…' with backslash
escapes, "…" identifiers, $tag$…$tag$ bodies) — not a line regex. A query fails on any of:

  placeholder-in-comment   $<digit> inside a `--` or `/* */` comment (pg-promise fills it; a newline / `*/` in the
                           value escapes the comment). Write «first parameter», «fileId», never $1 — and not #1-ish
                           tricks next to a real placeholder either: `$1#` is a RAW modifier (rule below).
  placeholder-in-literal   $<digit> inside a string, quoted identifier or dollar-quoted body (the value's own quotes
                           close the literal around it).
  placeholder-modifier     $N^ / $N~ / $N# / $N:raw|:alias|:name|:json|:csv|:list|:value in code — pg-promise then
                           inserts the value raw or as an identifier, without literal quoting.
  expression-braces        `{{` anywhere in the query — n8n inlines {{ … }} into the query text (even without a
                           leading `=`) BEFORE pg-promise sees it, so it is raw interpolation, never a parameter.
  leading-equals           the query starts with `=` — the whole query is an n8n expression.
  unterminated             a comment / literal runs to the end of the query (everything after it is unaccounted for).
  not-a-string             an executeQuery node whose query is not a plain string (cannot be checked at all).

    python3 Tools/n8n/verify-sql-comments.py                 # canonical workflows/ + staging/workflows/
    python3 Tools/n8n/verify-sql-comments.py DIR_OR_FILE …   # a re-export, the derived workflows-staging/, …

Exit 0 = every Postgres node is clean (and at least one was scanned); exit 1 = findings, each named by
file / node / line / rule. Pure Python, no network. Tests: Tools/n8n/tests/test_verify_sql_comments.py (the
rules, on fixtures) and test_sql_comment_injection.py (the real pg-promise formatting of the pinned nodes).
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PATHS = (os.path.join(HERE, "workflows"), os.path.join(HERE, "staging", "workflows"))
POSTGRES_TYPE = "n8n-nodes-base.postgres"

# What pg-promise substitutes is `\$([1-9][0-9]{0,16})(?![0-9])` (+ an optional modifier). Outside code the guard is
# deliberately stricter — any `$` followed by a digit — so a `$0` / `$01` in prose never starts the debate.
PLACEHOLDER_TOKEN = re.compile(r"\$\d+")
MODIFIED_PLACEHOLDER = re.compile(r"\$[1-9][0-9]{0,16}(?![0-9])(\^|~|#|:raw|:alias|:name|:json|:csv|:list|:value)")
IDENT_CHAR = re.compile(r"[A-Za-z0-9_$\u0080-\uffff]")
DOLLAR_TAG = re.compile(r"\$(?:[A-Za-z_\u0080-\uffff][A-Za-z0-9_\u0080-\uffff]*)?\$")

COMMENT_KINDS = ("line_comment", "block_comment")
LITERAL_KINDS = ("string", "quoted_ident", "dollar_string")


def lex(q):
    """Split a query into (kind, start, end, terminated) segments the way the PostgreSQL scanner sees it.
    kind ∈ code | line_comment | block_comment | string | quoted_ident | dollar_string."""
    segs, n, i, code_start = [], len(q), 0, 0

    def flush(upto):
        if upto > code_start:
            segs.append(("code", code_start, upto, True))

    while i < n:
        c = q[i]
        if c == "-" and q.startswith("--", i):
            flush(i)
            j = i + 2
            while j < n and q[j] not in "\n\r":
                j += 1
            segs.append(("line_comment", i, j, True))   # running to the end of the query is still a complete comment
            i = code_start = j
            continue
        if c == "/" and q.startswith("/*", i):
            flush(i)
            depth, j = 1, i + 2
            while j < n and depth:
                if q.startswith("/*", j):
                    depth, j = depth + 1, j + 2
                elif q.startswith("*/", j):
                    depth, j = depth - 1, j + 2
                else:
                    j += 1
            segs.append(("block_comment", i, j, depth == 0))
            i = code_start = j
            continue
        if c == "'":
            # E'…' (backslash escapes) only when the E is not the tail of a longer identifier
            e_string = i >= 1 and q[i - 1] in "eE" and (i == 1 or not IDENT_CHAR.match(q[i - 2]))
            start = i - 1 if e_string else i
            flush(start)
            j, done = i + 1, False
            while j < n:
                if e_string and q[j] == "\\" and j + 1 < n:
                    j += 2
                    continue
                if q[j] == "'":
                    if j + 1 < n and q[j + 1] == "'":
                        j += 2
                        continue
                    j, done = j + 1, True
                    break
                j += 1
            segs.append(("string", start, j, done))
            i = code_start = j
            continue
        if c == '"':
            flush(i)
            j, done = i + 1, False
            while j < n:
                if q[j] == '"':
                    if j + 1 < n and q[j + 1] == '"':
                        j += 2
                        continue
                    j, done = j + 1, True
                    break
                j += 1
            segs.append(("quoted_ident", i, j, done))
            i = code_start = j
            continue
        if c == "$" and (i == 0 or not IDENT_CHAR.match(q[i - 1])):
            m = DOLLAR_TAG.match(q, i)
            if m:
                tag = m.group(0)
                flush(i)
                end = q.find(tag, m.end())
                j, done = (n, False) if end < 0 else (end + len(tag), True)
                segs.append(("dollar_string", i, j, done))
                i = code_start = j
                continue
        i += 1
    flush(n)
    return segs


def _line_of(q, pos):
    return q.count("\n", 0, pos) + 1


def _excerpt(q, pos):
    a = q.rfind("\n", 0, pos) + 1
    b = q.find("\n", pos)
    return q[a:(len(q) if b < 0 else b)].strip()[:140]


def check_query(q):
    """Problems of ONE query text: a list of (rule, line, token, excerpt)."""
    if not isinstance(q, str):
        return [("not-a-string", 0, type(q).__name__, repr(q)[:140])]
    out = []

    def add(rule, pos, token=""):
        out.append((rule, _line_of(q, pos), token, _excerpt(q, pos)))

    if q.startswith("="):
        add("leading-equals", 0)
    for m in re.finditer(r"\{\{", q):
        add("expression-braces", m.start(), "{{")
    for kind, a, b, done in lex(q):
        text = q[a:b]
        if not done:
            add("unterminated", a, kind)
        if kind in COMMENT_KINDS:
            for m in PLACEHOLDER_TOKEN.finditer(text):
                add("placeholder-in-comment", a + m.start(), m.group(0))
        elif kind in LITERAL_KINDS:
            for m in PLACEHOLDER_TOKEN.finditer(text):
                add("placeholder-in-literal", a + m.start(), m.group(0))
        else:
            for m in MODIFIED_PLACEHOLDER.finditer(text):
                add("placeholder-modifier", a + m.start(), m.group(0))
    return out


def postgres_nodes(wf):
    """(node, query) for every Postgres node that runs SQL text: the core node's executeQuery (its query param),
    and any other Postgres-family node that carries a `query` parameter (memory nodes carry none)."""
    for n in wf.get("nodes") or []:
        t = str(n.get("type", ""))
        p = n.get("parameters") or {}
        if t == POSTGRES_TYPE and p.get("operation", "executeQuery") == "executeQuery":
            yield n, p.get("query")
        elif "postgres" in t.lower() and "query" in p:
            yield n, p.get("query")


def check_workflow(wf):
    """[(node name, rule, line, token, excerpt)] for one workflow document, and how many Postgres nodes it holds."""
    found, count = [], 0
    for n, q in postgres_nodes(wf):
        count += 1
        for problem in check_query(q):
            found.append((n.get("name"), *problem))
    return found, count


def workflow_files(paths):
    for p in paths:
        if os.path.isdir(p):
            for f in sorted(os.listdir(p)):
                if f.endswith(".json"):
                    yield os.path.join(p, f)
        elif os.path.isfile(p):
            yield p
        else:
            raise SystemExit(f"SQL GUARD: no such file or directory: {p}")


def scan(paths):
    """(findings [(file, node, rule, line, token, excerpt)], postgres nodes scanned, workflow files read)."""
    findings, nodes, files = [], 0, 0
    for path in workflow_files(paths):
        with open(path, encoding="utf-8") as fh:
            wf = json.load(fh)
        files += 1
        found, count = check_workflow(wf)
        nodes += count
        findings += [(os.path.relpath(path, HERE), *f) for f in found]
    return findings, nodes, files


def main(argv):
    paths = argv or [p for p in DEFAULT_PATHS if os.path.exists(p)]
    findings, nodes, files = scan(paths)
    for f, node, rule, line, token, excerpt in findings:
        print(f"SQL GUARD FAIL: {f} :: {node} :: line {line} :: {rule} {token}: {excerpt}")
    if not nodes:
        print(f"SQL GUARD FAIL: no Postgres node found in {files} workflow file(s) of {paths} — wrong path?")
        return 1
    if findings:
        print(f"{len(findings)} finding(s) in {nodes} Postgres node(s) of {files} workflow file(s)")
        return 1
    print(f"OK  {nodes} Postgres node(s) in {files} workflow file(s): no placeholder outside code, no {{{{ }}}}, "
          f"no modifier, no leading =")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
