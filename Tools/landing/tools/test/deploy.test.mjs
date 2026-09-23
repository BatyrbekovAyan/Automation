// Final review (2026-09-22), I-2/I-3/M-3: the deploy's Caddyfile transform and failure paths, with NO
// network and NO ssh: caddy-block.py (the pure transform deploy.sh runs locally on a read-only copy of
// the live file) is run on sample Caddyfiles; caddy-install.sh (the one step that writes the Caddyfile on
// the VPS) runs on temp files against a fake `docker`; deploy.sh runs only as its default DRY RUN, with
// ssh/scp/rsync/curl stubbed to fail loudly if anything calls them.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const TOOLS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const BLOCK = path.join(TOOLS, 'caddy-block.py');
const SITE = path.join(TOOLS, 'Caddyfile.site');
const SITE_TEXT = fs.readFileSync(SITE, 'utf8');
const tmp = () => fs.mkdtempSync(path.join(os.tmpdir(), 'crl-deploy-'));
const py = (...args) => spawnSync('python3', [BLOCK, ...args], { encoding: 'utf8' });
function file(dir, name, text) { const p = path.join(dir, name); fs.writeFileSync(p, text); return p; }

// The VPS layout as described when it was set up (2026-08-27): global options, the static site from
// ./site, www → redirect, n8n behind reverse_proxy. The exact live text is unknown here — which is why
// deploy.sh reads it and refuses on anything this transform would drop.
const GLOBAL = '{\n\temail ops@example.com\n}\n\n';
const V1_SITE = 'choosereply.com {\n\troot * /srv/site\n\tencode gzip\n\tfile_server\n}\n';
const WWW = '\nwww.choosereply.com {\n\tredir https://choosereply.com{uri} permanent\n}\n';
const N8N = '\nn8n.choosereply.com {\n\t# the app backend — must never change on a landing deploy\n\treverse_proxy n8n:5678 {\n\t\tflush_interval -1\n\t}\n}\n';
const V1 = GLOBAL + V1_SITE + WWW + N8N;

test('render: v1 (global, site, www redirect, n8n proxy) — меняется только блок choosereply.com', () => {
  const d = tmp(), live = file(d, 'Caddyfile', V1), out = path.join(d, 'Caddyfile.new');
  const r = py('render', live, SITE, out);
  assert.equal(r.status, 0, r.stderr);
  const got = fs.readFileSync(out, 'utf8');
  assert.equal(got, GLOBAL + SITE_TEXT.trimEnd() + '\n' + WWW + N8N, 'всё вне блока — байт в байт');
  assert.ok(got.includes(N8N) && got.includes(WWW) && got.startsWith(GLOBAL));
  assert.equal(fs.readFileSync(live, 'utf8'), V1, 'render не пишет живой файл');
  // the result is itself a well-formed file whose block is the new one
  assert.equal(py('check', out, SITE).status, 0);
  assert.equal(py('show', out).stdout, SITE_TEXT.trimEnd() + '\n');
});

test('check: v1-строки, которые новый блок заменяет (encode gzip, file_server, root), не останавливают', () => {
  const d = tmp(), live = file(d, 'Caddyfile', V1);
  const r = py('check', live, SITE);
  assert.equal(r.status, 0, r.stdout + r.stderr);
  assert.match(r.stdout, /--- diff live → Caddyfile\.site:\n--- live\n\+\+\+ Caddyfile\.site\n/, 'печатает diff');
  assert.match(r.stdout, /-\tencode gzip\n/);
  assert.match(r.stdout, /nothing is dropped/);
});

test('check/render: директивы живого блока, которых нет в новом, — СТОП (exit 3), ничего не записано', () => {
  const d = tmp();
  const live = file(d, 'Caddyfile', GLOBAL + 'choosereply.com {\n\troot * /srv/site\n\tencode gzip\n\theader X-Frame-Options DENY\n\thandle /review/* {\n\t\tfile_server browse\n\t}\n\tfile_server\n}\n' + WWW + N8N);
  const r = py('check', live, SITE);
  assert.equal(r.status, 3);
  assert.match(r.stdout, /would DROP:\n\s+header X-Frame-Options DENY\n\s+handle \/review\/\* \{\n\s+file_server browse\n/);
  const out = path.join(d, 'Caddyfile.new');
  assert.equal(py('render', live, SITE, out).status, 3, 'render отказывает ровно там же, где check');
  assert.ok(!fs.existsSync(out), 'кандидат не создан');
  // …and once the directive is moved into the new block, it is carried over
  const site2 = file(d, 'Caddyfile.site', SITE_TEXT.replace(/\n\}\s*$/, '\n\theader X-Frame-Options DENY\n\thandle /review/* {\n\t\tfile_server browse\n\t}\n}\n'));
  assert.equal(py('check', live, site2).status, 0);
  assert.equal(py('render', live, site2, out).status, 0);
  assert.match(fs.readFileSync(out, 'utf8'), /handle \/review\/\* \{\n\t\tfile_server browse\n\t\}/);
});

test('форма, которую инструмент не понимает, — отказ (exit 2), файл не тронут', () => {
  const cases = {
    'сайт вместе с www': 'choosereply.com, www.choosereply.com {\n\tfile_server\n}\n',
    'со схемой': 'https://choosereply.com {\n\tfile_server\n}\n',
    'блока нет': WWW + N8N,
    'блок дважды': V1_SITE + '\n' + V1_SITE,
    'скобки не сходятся': 'choosereply.com {\n\thandle {\n\t\tfile_server\n}\n',
    'лишняя }': V1_SITE + '}\n',
    'heredoc': 'choosereply.com {\n\trespond <<EOF\n\t}\n\tEOF\n}\n',
    'однострочный блок': 'choosereply.com { file_server }\n',
  };
  for (const [name, text] of Object.entries(cases)) {
    const d = tmp(), live = file(d, 'Caddyfile', text), out = path.join(d, 'new');
    for (const cmd of ['check', 'render']) {
      const r = cmd === 'check' ? py(cmd, live, SITE) : py(cmd, live, SITE, out);
      assert.equal(r.status, 2, `${name} / ${cmd}: ${r.stdout}${r.stderr}`);
    }
    assert.equal(fs.readFileSync(live, 'utf8'), text, name);
    assert.ok(!fs.existsSync(out), name);
  }
});

test('разбор: комментарии, кавычки и плейсхолдеры не ломают границы блоков', () => {
  const d = tmp();
  const text = '# choosereply.com { — не блок, комментарий\n' + 'choosereply.com {\n\troot * /srv/site # хвост\n\theader X-Note "a # b {"\n\tfile_server\n}\n' + WWW;
  const live = file(d, 'Caddyfile', text), out = path.join(d, 'new');
  const r = py('check', live, SITE);
  assert.equal(r.status, 3, 'header X-Note — не из нового блока');
  assert.match(r.stdout, /would DROP:\n\s+header X-Note "a # b \{"\n\s+move/);
  const ok = file(d, 'ok', 'import snippets/*\n\n' + text.replace('\theader X-Note "a # b {"\n', ''));
  assert.equal(py('render', ok, SITE, out).status, 0);
  assert.ok(fs.readFileSync(out, 'utf8').startsWith('import snippets/*\n\n# choosereply.com {'));
  assert.ok(fs.readFileSync(out, 'utf8').endsWith(WWW));
});

test('lint: Caddyfile.site — ровно один блок choosereply.com; render никогда не пишет в живой путь', () => {
  assert.equal(py('lint', SITE).status, 0);
  const d = tmp();
  assert.equal(py('lint', file(d, 'two', SITE_TEXT + WWW)).status, 2);
  const live = file(d, 'Caddyfile', V1);
  assert.equal(py('render', live, SITE, live).status, 64);
  assert.equal(fs.readFileSync(live, 'utf8'), V1);
});

// ---- caddy-install.sh (step 3 on the VPS) against a fake `docker compose exec -T caddy …` ----
const FAKE_DOCKER = `#!/bin/sh
[ "$1" = compose ] && [ "$2" = exec ] && [ "$3" = -T ] && [ "$4" = caddy ] || { echo "fake docker: $*" >&2; exit 90; }
shift 4
echo "$*" >> "$FAKE_LOG"
map() { case "$1" in /etc/caddy/Caddyfile) echo "$FAKE_ETC" ;; /tmp/*) echo "$FAKE_CTMP/\${1#/tmp/}" ;; *) echo "$1" ;; esac; }
case "$1" in
  sh) t=$(echo "$3" | sed 's/^cat > //'); cat > "$(map "$t")" ;;
  rm) [ "$2" = -f ] && rm -f "$(map "$3")" ;;
  sha256sum) f=$(map "$2"); shasum -a 256 "$f" | cut -d' ' -f1 | sed "s|$| $2|" ;;
  caddy)
    if [ "$2" = validate ]; then
      cfg=""; prev=""; for a in "$@"; do [ "$prev" = --config ] && cfg=$a; prev=$a; done
      grep -q INVALID "$(map "$cfg")" && { echo "Error: INVALID" >&2; exit 1; }
      echo "Valid configuration"
    elif [ "$2" = reload ]; then
      [ -n "$FAIL_RELOAD" ] && { echo "reload failed" >&2; exit 1; }
      echo reloaded
    fi ;;
esac
`;
function installEnv({ candidate, stale = false, failReload = false, changed = false }) {
  const d = tmp(), bin = path.join(d, 'bin'), ctmp = path.join(d, 'ctmp');
  fs.mkdirSync(bin); fs.mkdirSync(ctmp);
  fs.writeFileSync(path.join(bin, 'docker'), FAKE_DOCKER, { mode: 0o755 });
  const live = file(d, 'Caddyfile', V1);
  file(d, 'Caddyfile.bak-1', V1);
  const cand = file(d, 'Caddyfile.new', candidate);
  const sha = spawnSync('shasum', ['-a', '256', live], { encoding: 'utf8' }).stdout.split(' ')[0];
  if (changed) fs.appendFileSync(live, '# edited meanwhile\n');
  const etc = stale ? file(d, 'stale-copy', V1) : live;   // a stale inode mount: the container reads another file
  const log = path.join(d, 'docker.log');
  const ino = fs.statSync(live).ino;
  const r = spawnSync('/bin/bash', [path.join(TOOLS, 'caddy-install.sh'), cand, 'Caddyfile.bak-1', sha], {
    cwd: d, encoding: 'utf8',
    env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, FAKE_ETC: etc, FAKE_CTMP: ctmp, FAKE_LOG: log, FAIL_RELOAD: failReload ? '1' : '' },
  });
  const calls = fs.existsSync(log) ? fs.readFileSync(log, 'utf8').trim().split('\n') : [];
  return { r, live, ino, calls, text: () => fs.readFileSync(live, 'utf8'), ctmp };
}
const NEW = GLOBAL + SITE_TEXT.trimEnd() + '\n' + WWW + N8N;

test('caddy-install: кандидат валиден → записан НА МЕСТЕ (тот же inode), проверен, перезагружен', () => {
  const e = installEnv({ candidate: NEW });
  assert.equal(e.r.status, 0, e.r.stderr);
  assert.equal(e.text(), NEW);
  assert.equal(fs.statSync(e.live).ino, e.ino, 'одиночный bind-mount видит тот же inode');
  const v = e.calls.findIndex((c) => /validate --adapter caddyfile --config \/tmp\/cr-Caddyfile\.candidate/.test(c));
  const rl = e.calls.findIndex((c) => /^caddy reload/.test(c));
  assert.ok(v >= 0 && rl > v, 'кандидат проверен до перезагрузки: ' + e.calls.join(' | '));
  assert.ok(e.calls.some((c) => c === 'sha256sum /etc/caddy/Caddyfile'), 'контейнер видит ровно новые байты');
  assert.deepEqual(fs.readdirSync(e.ctmp), [], 'копия кандидата в контейнере убрана');
});

test('caddy-install: кандидат НЕвалиден → живой файл не тронут, reload не вызван (exit 5)', () => {
  const e = installEnv({ candidate: NEW.replace('encode', 'INVALID encode') });
  assert.equal(e.r.status, 5, e.r.stderr);
  assert.equal(e.text(), V1);
  assert.ok(!e.calls.some((c) => /reload/.test(c)));
  assert.deepEqual(fs.readdirSync(e.ctmp), []);
});

test('caddy-install: reload упал → бэкап возвращён на место (тот же inode), exit 6', () => {
  const e = installEnv({ candidate: NEW, failReload: true });
  assert.equal(e.r.status, 6, e.r.stderr);
  assert.equal(e.text(), V1, 'на диске снова тот конфиг, который Caddy и исполняет');
  assert.equal(fs.statSync(e.live).ino, e.ino);
});

test('caddy-install: контейнер читает не этот файл (устаревший inode) → откат, reload не вызван', () => {
  const e = installEnv({ candidate: NEW, stale: true });
  assert.equal(e.r.status, 6, e.r.stderr);
  assert.equal(e.text(), V1);
  assert.match(e.r.stderr, /does not see the new file/);
  assert.ok(!e.calls.some((c) => /reload/.test(c)));
});

test('caddy-install: живой Caddyfile изменился после предпроверки → ничего не пишет (exit 4)', () => {
  const e = installEnv({ candidate: NEW, changed: true });
  assert.equal(e.r.status, 4, e.r.stderr);
  assert.ok(e.text().endsWith('# edited meanwhile\n'));
  assert.deepEqual(e.calls, [], 'даже контейнер не трогали');
});

// ---- deploy.sh: dry run only ----
test('deploy.sh без --go: только DRY-строки, ни одного вызова ssh/scp/rsync/curl; порядок шагов', () => {
  if (!fs.existsSync(path.join(TOOLS, '../dist/index.html'))) return;   // no dist/ yet — the script refuses before anything
  const bin = tmp();
  for (const c of ['ssh', 'scp', 'rsync', 'curl']) {
    fs.writeFileSync(path.join(bin, c), `#!/bin/sh\necho "LIVE CALL: ${c}" >&2\nexit 99\n`, { mode: 0o755 });
  }
  const r = spawnSync('/bin/bash', [path.join(TOOLS, 'deploy.sh')], { encoding: 'utf8', env: { ...process.env, PATH: `${bin}:${process.env.PATH}` } });
  assert.equal(r.status, 0, r.stderr);
  assert.ok(!/LIVE CALL/.test(r.stderr + r.stdout));
  const out = r.stdout;
  const at = (s) => { const i = out.indexOf(s); assert.ok(i >= 0, `нет в плане: ${s}`); return i; };
  // the new config is validated (and the live block compared) BEFORE the backup and any write
  assert.ok(at('caddy-block.py check') < at('== 1. backup'));
  assert.ok(at('validate the NEW file in the container') < at('== 1. backup'));
  // the rollback line comes right after the backup, before any file or Caddy change
  assert.ok(at('== 1. backup') < at('rollback (keep this line):') && at('rollback (keep this line):') < at('== 2. files'));
  assert.match(out, /rsync -a --delete site\.bak-\d{8}-\d{6}\/ site\//, 'откат на месте, без rm -rf && mv');
  assert.ok(at('dist/assets/ choosereply:choosereply/site/assets/') < at("--exclude /assets/"), 'сначала assets/, потом корень');
  assert.ok(out.search(/DRY: ssh choosereply cd ~\/choosereply && bash \/tmp\/cr-deploy-\d{8}-\d{6}\/caddy-install\.sh /) > at('== 3. Caddy'), 'Caddyfile пишет только caddy-install.sh, на шаге 3');
  assert.match(out, /Caddyfile\.site: one choosereply\.com block/, 'локальная проверка нового блока выполняется и в dry-run');
});

test('deploy.sh: любой выход после бэкапа без DONE откатывает автоматически; нет --ack и rm -rf', () => {
  const src = fs.readFileSync(path.join(TOOLS, 'deploy.sh'), 'utf8');
  const code = src.split('\n').filter((l) => !/^\s*#/.test(l)).join('\n');
  assert.match(code, /set -Eeuo pipefail/);
  assert.match(code, /trap finish EXIT/);
  assert.match(code, /trap 'exit 130' INT; trap 'exit 143' TERM; trap 'exit 129' HUP/);
  const fin = code.slice(code.indexOf('finish() {'), code.indexOf('trap finish EXIT'));
  assert.match(fin, /if \[\[ \$BACKED_UP == 1 \]\]; then[\s\S]*ssh "\$HOST" "\$ROLLBACK_REMOTE"/);
  assert.ok(code.indexOf('BACKED_UP=1') > code.indexOf('cp -a site site.bak-'), 'флаг бэкапа — только после успешного бэкапа');
  assert.ok(code.indexOf('DONE=1') > code.indexOf('stop "smoke check failed"'), 'DONE — только после дымовой проверки');
  assert.ok(!/rm -rf site/.test(code) && !/mv site\.bak/.test(code), 'откат не подменяет inode каталога');
  assert.ok(!/ack-caddy-diff/.test(code), 'выбросить живые директивы нельзя — только перенести в Caddyfile.site');
  assert.ok(!/caddy-block\.py[^\n]*swap/.test(code), 'живой Caddyfile не правится до проверки кандидата');
  const inst = fs.readFileSync(path.join(TOOLS, 'caddy-install.sh'), 'utf8');
  assert.ok(inst.indexOf('--adapter caddyfile --config "$PROBE"') < inst.indexOf('cat "$CAND" > "$LIVE"'), 'кандидат проверяется ДО записи');
  assert.ok(inst.indexOf('trap restore ERR') < inst.indexOf('cat "$CAND" > "$LIVE"'));
});
