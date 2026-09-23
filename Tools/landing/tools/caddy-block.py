#!/usr/bin/env python3
"""The choosereply.com site block of the VPS Caddyfile — a pure text transform, used by tools/deploy.sh.

  caddy-block.py lint   <new-block>                    the new block file is exactly one `choosereply.com {…}`
  caddy-block.py show   <Caddyfile>                    print the live site block
  caddy-block.py check  <Caddyfile> <new-block>        print the live block, a diff live → new, and every
                                                       live directive the new block would DROP; exit 3 if any
  caddy-block.py render <Caddyfile> <new-block> <out>  write the whole new Caddyfile to <out>: the live file
                                                       with ONLY the site block replaced. Refuses (exit 3)
                                                       exactly when `check` does. Never writes <Caddyfile>.

deploy.sh runs this LOCALLY on a read-only copy of the live file (ssh cat), so the transform is tested
here without a server (tools/test/deploy.test.mjs) and nothing on the VPS is written before the new
config has been validated by Caddy itself (tools/caddy-install.sh).

Why `check` refuses instead of merging (финальное ревью, I-3): the site block also serves /review/<hash>/
(the App Review video) and may carry a `file_server browse`, a `handle`, headers or a redirect that
tools/Caddyfile.site does not know about. Replacing the block would drop them silently and `caddy validate`
cannot know they were wanted; merging line by line cannot be right for nested blocks and matchers. So a live
line survives only if the new block carries it too, or it is one of the KNOWN_V1 lines the new block
supersedes on purpose. Anything else stops the deploy with the diff printed: move it into
tools/Caddyfile.site (then it is carried over) and run again.

Parsing follows the Caddyfile grammar: a block opens with `{` as the LAST token of a line and closes with a
line that is just `}`; `#` starts a comment at a token boundary outside quotes; `{uri}`-style placeholders
are ordinary tokens. Heredocs (`<<MARKER`) and one-line blocks are refused (exit 2) rather than guessed.

Exit codes: 0 ok · 2 shape not understood (block missing, duplicated, combined with other addresses,
unbalanced braces, heredoc) · 3 the new block would drop live directives · 64 usage.
"""
import difflib
import pathlib
import sys

SITE = 'choosereply.com'

# Lines of the v1 site block that tools/Caddyfile.site supersedes on purpose (whitespace-normalised).
# Keep this list SHORT: a line that is not here and not in the new block stops the deploy.
KNOWN_V1 = frozenset({
    'root * /srv/site',
    'file_server',
    'encode gzip',
    'encode zstd gzip',
    'encode gzip zstd',
})


class Shape(Exception):
    """The file is not a Caddyfile shape this tool understands — refuse, never guess."""


def strip_comment(line):
    """The code part of a line: a `#` at a token boundary, outside quotes, starts a comment."""
    quote, i = None, 0
    while i < len(line):
        ch = line[i]
        if quote:
            if ch == '\\' and quote == '"':
                i += 2                      # an escaped char never closes the quote
                continue
            if ch == quote:
                quote = None
        elif ch in '"`':
            quote = ch
        elif ch == '#' and (i == 0 or line[i - 1] in ' \t'):
            return line[:i]
        i += 1
    return line


def blocks(text):
    """Top-level blocks as dicts {head, start, end} (character offsets; end includes the closing line's \\n)."""
    out, depth, cur, pos = [], 0, None, 0
    for line in text.splitlines(keepends=True):
        code = strip_comment(line.rstrip('\r\n')).strip()
        if '<<' in code:
            raise Shape('heredoc syntax (<<) is not supported by this tool')
        toks = code.split()
        if code == '}':
            if depth == 0:
                raise Shape('unbalanced braces: a `}` closes nothing')
            depth -= 1
            if depth == 0:
                cur['end'] = pos + len(line)
                out.append(cur)
                cur = None
        elif toks and toks[-1] == '{':
            if depth == 0:
                cur = {'head': ' '.join(toks[:-1]), 'start': pos}
            depth += 1
        elif depth == 0 and code and toks[0] != 'import':
            # outside a block only `import …` is expected; anything else is the single-site shorthand
            # or a one-line block — refuse rather than guess what it serves
            raise Shape(f'top-level line outside a block: {code!r}')
        pos += len(line)
    if depth != 0:
        raise Shape('unbalanced braces: a block is never closed')
    return out


def addresses(head):
    return [a for a in head.replace(',', ' ').split() if a]


def site_block(text, what):
    """The one block whose address list is exactly `choosereply.com`; refuse any other arrangement."""
    try:
        bs = blocks(text)
    except Shape as e:
        raise Shape(f'{what}: {e}')
    exact, mixed = [], []
    for b in bs:
        addrs = addresses(b['head'])
        hosts = [a.split('://')[-1].split(':')[0].split('/')[0] for a in addrs]
        if addrs == [SITE]:
            exact.append(b)
        elif SITE in hosts:
            mixed.append(b['head'])
    if mixed:
        raise Shape(f'{what}: {SITE} is served by a block with other addresses or a scheme/port ({mixed[0]!r}) — reconcile tools/Caddyfile.site by hand')
    if len(exact) != 1:
        raise Shape(f'{what}: `{SITE} {{` block found {len(exact)} times, expected exactly 1')
    b = exact[0]
    return b, text[b['start']:b['end']]


def directives(block_text):
    """Directive lines of a block, whitespace-normalised; the head, braces-only lines, blanks and comments do not count."""
    out = []
    for line in block_text.splitlines()[1:]:
        s = ' '.join(strip_comment(line).split())
        if not s or s == '}':
            continue
        out.append(s)
    return out


def dropped(live_block, new_block):
    """Live directive lines the new block would drop: not carried by it and not a known v1 line."""
    have = set(directives(new_block))
    return [d for d in directives(live_block) if d not in have and d not in KNOWN_V1]


def new_block_of(new_text, what):
    """The new block file must be exactly one site block (comments/blank lines around it are fine)."""
    b, block = site_block(new_text, what)
    if len(blocks(new_text)) != 1:
        raise Shape(f'{what}: must contain only the {SITE} block')
    return block


def render(live_text, new_text, what_live='Caddyfile', what_new='new block'):
    """The live file with only the site block replaced. Everything before and after it stays byte-identical."""
    b, _live = site_block(live_text, what_live)
    new = new_block_of(new_text, what_new).rstrip('\n') + '\n'
    out = live_text[:b['start']] + new + live_text[b['end']:]
    # prove it: the result parses, holds the new block once, and every other block is untouched
    nb, got = site_block(out, 'rendered Caddyfile')
    assert got == new, 'rendered block differs from the new block'
    assert out[:nb['start']] == live_text[:b['start']] and out[nb['end']:] == live_text[b['end']:], 'text outside the block changed'
    return out


def ensure_nl(s):
    return s if s.endswith('\n') else s + '\n'


def main(argv):
    usage = (not argv or argv[0] not in ('lint', 'show', 'check', 'render')
             or len(argv) != {'lint': 2, 'show': 2, 'check': 3, 'render': 4}[argv[0]])
    if usage:
        print(__doc__, file=sys.stderr)
        return 64
    cmd = argv[0]
    try:
        if cmd == 'lint':
            p = pathlib.Path(argv[1])
            block = new_block_of(p.read_text(), str(p))
            print(f'{p.name}: one {SITE} block, {len(directives(block))} directive lines')
            return 0
        live_path = pathlib.Path(argv[1])
        live_text = live_path.read_text()
        _b, live = site_block(live_text, str(live_path))
        if cmd == 'show':
            sys.stdout.write(ensure_nl(live))
            return 0
        new_path = pathlib.Path(argv[2])
        new_text = new_path.read_text()
        new = new_block_of(new_text, str(new_path))
        lost = dropped(live, new)
        if cmd == 'check':
            print(f'--- live {SITE} block ({live_path}):')
            sys.stdout.write(ensure_nl(live))
            print(f'--- diff live → {new_path.name}:')
            diff = list(difflib.unified_diff(ensure_nl(live).splitlines(True), ensure_nl(new).splitlines(True), 'live', new_path.name))
            sys.stdout.write(''.join(diff) if diff else '    (identical)\n')
        if lost:
            print(f'--- STOP: the live block carries directives that {new_path.name} would DROP:')
            for d in lost:
                print('    ' + d)
            print(f'    move them into {new_path.name} (then they are carried over) and run again')
            return 3
        if cmd == 'check':
            print(f'--- nothing is dropped: every live directive is in {new_path.name} or a known v1 line it supersedes')
            return 0
        out = render(live_text, new_text, str(live_path), str(new_path))
        out_path = pathlib.Path(argv[3])
        if out_path.resolve() == live_path.resolve():
            print('render never writes the live Caddyfile', file=sys.stderr)
            return 64
        out_path.write_text(out)
        return 0
    except Shape as e:
        print(f'STOP: {e}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
