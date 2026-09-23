// deploy.sh --go EXECUTED against a fake VPS on this disk (повторное ревью, N-1/N-3).
//
// deploy.test.mjs only greps the EXIT trap and runs --go as a dry run, and that is how a trap that
// never rolled anything back reached the re-review: macOS /bin/bash 3.2 in a UTF-8 locale (Terminal.app's
// default) reads the 0xC2 byte of «»» as part of a variable name, so `«$PHASE»` became `$PHASE\xC2` and
// `set -u` killed the trap before its ssh. Here the real script runs, under that locale, with /bin/bash.
//
// No network, no ssh: tools/test/fake-vps/ holds ssh/scp/rsync/curl/docker/sha256sum stand-ins that are
// first on PATH. ssh runs the "remote" command locally with HOME = a fake VPS directory; docker maps the
// container's /etc/caddy/Caddyfile, /srv/site and /tmp onto it; curl answers like a file_server over that
// fake site/ and refuses every other host. Each scenario checks the END STATE on the fake VPS: site/ and
// Caddyfile bytes and inodes, the config the fake Caddy runs, what the operator was told, the local tmp dir.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const TOOLS = path.resolve(HERE, '..');
// The stand-ins run from HERE, never from a fresh copy: macOS scans every newly created executable on its
// first exec (~0.8 s each), which made one scenario take ~5 s and eight in parallel ~45 s.
const SHIMS = path.join(HERE, 'fake-vps');
for (const f of fs.readdirSync(SHIMS)) {
  const p = path.join(SHIMS, f);
  if (!(fs.statSync(p).mode & 0o100)) fs.chmodSync(p, 0o755);   // a checkout that lost the exec bit
}
const BASH = '/bin/bash';                          // the only bash on the owner's Mac (3.2)
const UTF8 = { LC_ALL: 'en_US.UTF-8', LANG: 'en_US.UTF-8' };
const REVIEW_DIR = 'review/923843a986255cba/';
const VIDEO = `${REVIEW_DIR}ChooseReply-demo-2026-09.mp4`;
const NOTES = `${REVIEW_DIR}notes v1.txt`;         // a space in a name: the URL list must survive it

const GLOBAL = '{\n\temail ops@example.com\n}\n\n';
const V1_SITE = 'choosereply.com {\n\troot * /srv/site\n\tencode gzip\n\tfile_server\n}\n';
const WWW = '\nwww.choosereply.com {\n\tredir https://choosereply.com{uri} permanent\n}\n';
const N8N = '\nn8n.choosereply.com {\n\treverse_proxy n8n:5678 {\n\t\tflush_interval -1\n\t}\n}\n';
const V1 = GLOBAL + V1_SITE + WWW + N8N;
const SITE_TEXT = fs.readFileSync(path.join(TOOLS, 'Caddyfile.site'), 'utf8');

function put(root, rel, data) {
  const p = path.join(root, rel);
  fs.mkdirSync(path.dirname(p), { recursive: true });
  fs.writeFileSync(p, data);
  return p;
}

// each scenario's world is removed when the run ends (CR_KEEP_FAKE_VPS=1 keeps them for a post-mortem)
const worlds = [];
process.on('exit', () => { if (!process.env.CR_KEEP_FAKE_VPS) for (const w of worlds) fs.rmSync(w, { recursive: true, force: true }); });

// One throw-away world: byte copies of the real tools (deploy.sh finds dist/ as ../dist from its own
// directory), a small dist/, the stand-ins, and a fake VPS laid out like the real one (v1 site with the
// App Review video, the Caddyfile that also serves n8n).
function world({ live = V1 } = {}) {
  const T = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-go-'));
  worlds.push(T);
  for (const f of ['deploy.sh', 'caddy-install.sh', 'caddy-block.py', 'Caddyfile.site']) {
    fs.mkdirSync(path.join(T, 'tools'), { recursive: true });
    fs.copyFileSync(path.join(TOOLS, f), path.join(T, 'tools', f));
  }
  put(T, 'dist/index.html', '<html>v2</html>\n');
  put(T, 'dist/privacy.html', '<html>privacy v2</html>\n');
  put(T, 'dist/terms.html', '<html>terms v2</html>\n');
  put(T, 'dist/og.png', 'v2png');
  put(T, 'dist/assets/0e18f1f94a/site.css', '/* v2 */\n');
  put(T, 'dist/assets/0e18f1f94a/main.js', '// v2\n');
  const V = path.join(T, 'vps');
  put(V, 'choosereply/site/index.html', '<html>v1</html>\n');
  put(V, 'choosereply/site/privacy.html', '<html>privacy v1</html>\n');
  put(V, 'choosereply/site/terms.html', '<html>terms v1</html>\n');
  put(V, 'choosereply/site/og.png', 'v1png');
  put(V, 'choosereply/site/assets/oldv1/site.css', '/* v1 */\n');
  put(V, `choosereply/site/${VIDEO}`, Buffer.alloc(4096, 7));
  put(V, `choosereply/site/${NOTES}`, 'for the reviewer\n');
  // v1 went up on 2026-08-27: rsync's quick check (size + mtime) must see the new files as newer — same
  // size and the same second would make it skip them, which is a property of the test, not of a deploy
  const v1Time = new Date('2026-08-27T12:00:00Z');
  const age = (d) => { for (const e of fs.readdirSync(d, { withFileTypes: true })) { const p = path.join(d, e.name); if (e.isDirectory()) age(p); fs.utimesSync(p, v1Time, v1Time); } };
  age(path.join(V, 'choosereply/site'));
  put(V, 'choosereply/Caddyfile', live);
  put(V, 'running-config', live);          // what the fake Caddy runs before the deploy
  put(V, 'log', '');
  fs.mkdirSync(path.join(V, 'rtmp'));
  fs.mkdirSync(path.join(V, 'ctmp'));
  const ltmp = path.join(T, 'ltmp');
  fs.mkdirSync(ltmp);
  return { T, V, ltmp, site: path.join(V, 'choosereply/site'), cf: path.join(V, 'choosereply/Caddyfile') };
}

function tree(root) {
  const out = {};
  const walk = (d) => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const p = path.join(d, e.name), rel = path.relative(root, p);
      if (e.isDirectory()) { out[rel + '/'] = 'dir'; walk(p); } else out[rel] = crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
    }
  };
  walk(root);
  return out;
}

function snapshot(w) {
  return { tree: tree(w.site), cf: fs.readFileSync(w.cf, 'utf8'), siteIno: fs.statSync(w.site).ino, cfIno: fs.statSync(w.cf).ino };
}

// Runs the copied deploy.sh --go in its own process group (so a Ctrl-C can go to the whole foreground
// group, like a terminal's). sigintOnMarker: wait for that file in the fake VPS, then SIGINT the group.
function deploy(w, env = {}, { locale = UTF8, sigintOnMarker = null } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(BASH, [path.join(w.T, 'tools', 'deploy.sh'), '--go'], {
      cwd: w.T, detached: true,
      env: { ...process.env, ...locale, PATH: `${SHIMS}:${process.env.PATH}`, TMPDIR: w.ltmp, SIM_V: w.V, SIM_BLOCK: path.join(w.T, 'tools', 'caddy-block.py'), ...env },
    });
    let stdout = '', stderr = '';
    child.stdout.on('data', (d) => { stdout += d; });
    child.stderr.on('data', (d) => { stderr += d; });
    const kill = setTimeout(() => { try { process.kill(-child.pid, 'SIGKILL'); } catch {} }, 90000);
    let poll = null;
    if (sigintOnMarker) {
      const t0 = Date.now();
      poll = setInterval(() => {
        if (fs.existsSync(path.join(w.V, sigintOnMarker))) {
          clearInterval(poll); poll = null;
          setTimeout(() => { try { process.kill(-child.pid, 'SIGINT'); } catch {} }, 200);
        } else if (Date.now() - t0 > 60000) { clearInterval(poll); poll = null; }
      }, 50);
    }
    child.on('error', reject);
    child.on('close', (status, signal) => {
      clearTimeout(kill); if (poll) clearInterval(poll);
      resolve({ status, signal, stdout, stderr, log: fs.readFileSync(path.join(w.V, 'log'), 'utf8') });
    });
  });
}

const leftovers = (w) => fs.readdirSync(w.ltmp);
const backups = (w) => fs.readdirSync(path.join(w.V, 'choosereply')).filter((n) => /\.bak-/.test(n));

// Every failure after the backup must end exactly where it started: same bytes, same inodes (a bind
// mount keeps seeing the old ones), the fake Caddy running what is on disk, the operator told so.
function assertRolledBack(w, pre, r, phase) {
  const said = r.stdout + r.stderr;
  assert.notEqual(r.status, 0, 'a failed deploy must not exit 0');
  assert.doesNotMatch(r.stderr, /unbound variable/, 'the EXIT trap itself must not die (N-1)');
  assert.match(r.stderr, new RegExp(`FAILED during «${phase}» \\(exit \\d+\\) — rolling back automatically`), said);
  assert.match(r.stderr, /rolled back: site\/ and Caddyfile restored in place/, said);
  assert.deepEqual(tree(w.site), pre.tree, 'site/ byte for byte as before the deploy');
  assert.equal(fs.readFileSync(w.cf, 'utf8'), pre.cf, 'Caddyfile as before the deploy');
  assert.equal(fs.statSync(w.site).ino, pre.siteIno, 'site/ restored in place (same inode)');
  assert.equal(fs.statSync(w.cf).ino, pre.cfIno, 'Caddyfile restored in place (same inode)');
  assert.equal(fs.readFileSync(path.join(w.V, 'running-config'), 'utf8'), pre.cf, 'the fake Caddy runs the file on disk');
  assert.deepEqual(leftovers(w), [], 'the local tmp dir is removed on the failure path too');
}

test('premise: /bin/bash 3.2 in en_US.UTF-8 reads «»» as part of a variable name (why this file exists)', (t) => {
  const r = spawnSync(BASH, ['-c', 'set -u; P=x; echo "«$P»"'], { encoding: 'utf8', env: { ...process.env, ...UTF8 } });
  if (r.status === 0) t.diagnostic('this bash/locale does not reproduce the 0xC2 trap — the scenarios below still run');
  else assert.match(r.stderr, /unbound variable/);
  const ok = spawnSync(BASH, ['-c', 'set -u; P=x; echo "«${P}»"'], { encoding: 'utf8', env: { ...process.env, ...UTF8 } });
  assert.equal(ok.status, 0, ok.stderr);
  assert.equal(ok.stdout, '«x»\n');
});

test('the deploy scripts never put a non-ASCII byte right after a bare $NAME (use ${NAME})', () => {
  for (const f of fs.readdirSync(TOOLS).filter((n) => n.endsWith('.sh'))) {
    const bad = fs.readFileSync(path.join(TOOLS, f), 'utf8').split('\n')
      .map((l, i) => [i + 1, l]).filter(([, l]) => !/^\s*#/.test(l) && /\$[A-Za-z_][A-Za-z0-9_]*[^\x00-\x7F]/.test(l));
    assert.deepEqual(bad, [], `${f}: ${bad.map(([n, l]) => `${n}: ${l.trim()}`).join(' | ')}`);
  }
});

test('deploy.sh --go against a fake VPS (en_US.UTF-8, /bin/bash)', { concurrency: true }, async (t) => {
  t.test('happy path: only the choosereply.com block changes; assets first; review/ checked file by file', async () => {
    const w = world(), pre = snapshot(w);
    const r = await deploy(w);
    assert.equal(r.status, 0, r.stdout + r.stderr);
    const cf = fs.readFileSync(w.cf, 'utf8');
    assert.equal(cf, GLOBAL + SITE_TEXT.trimEnd() + '\n' + WWW + N8N, 'global, www and n8n byte for byte');
    assert.equal(fs.statSync(w.cf).ino, pre.cfIno);
    assert.equal(fs.readFileSync(path.join(w.V, 'running-config'), 'utf8'), cf);
    const post = tree(w.site);
    for (const [rel, h] of Object.entries(tree(path.join(w.T, 'dist')))) assert.equal(post[rel], h, `deployed: ${rel}`);
    for (const rel of [VIDEO, NOTES, 'assets/oldv1/site.css']) assert.equal(post[rel], pre.tree[rel], `kept: ${rel}`);
    // (openrsync starts its local receiver as `rsync --server …` through PATH — the stand-in again)
    const rs = r.log.split('\n').filter((l) => l.startsWith('rsync: ') && !l.includes('--server') && l.includes('/choosereply/site/'));
    assert.ok(rs.length === 2 && rs[0].includes('/assets/ ') && rs[1].includes('--exclude /assets/'), rs.join('\n'));
    // N-3: the App Review files themselves are recorded before and compared after — by HEAD (no video download)
    const vid = 'https://choosereply.com/review/923843a986255cba/ChooseReply-demo-2026-09.mp4';
    assert.match(r.stdout, new RegExp(`before:.* ${vid.replace(/[./]/g, '\\$&')}=200`));
    assert.match(r.stdout, /before:.* https:\/\/choosereply\.com\/review\/923843a986255cba\/notes%20v1\.txt=200/);
    assert.match(r.stdout, /before:.* https:\/\/choosereply\.com\/review\/923843a986255cba\/=404/, 'the directory too (v1: no browse)');
    assert.equal(r.log.split('\n').filter((l) => l === `curl: HEAD ${vid} -> 200`).length, 2, 'HEAD before and after');
    assert.ok(!r.log.includes(`curl: GET ${vid}`), 'the video is never downloaded');
    assert.match(r.stdout, /^done — rollback, if ever needed: /m);
    assert.deepEqual(leftovers(w), []);
  });

  for (const [name, phase, env, opts] of [
    ['smoke miss (terms.html 500 after the reload)', 'smoke', { SIM_CURL_FAIL: 'https://choosereply.com/terms.html=500' }],
    ['smoke miss in LC_ALL=C too', 'smoke', { SIM_CURL_FAIL: 'https://choosereply.com/terms.html=500' }, { locale: { LC_ALL: 'C', LANG: 'C' } }],
    ['the App Review video stops answering after the reload (N-3)', 'smoke', { SIM_CURL_FAIL: 'https://choosereply.com/review/923843a986255cba/ChooseReply-demo-2026-09.mp4=404' }],
    ['rsync of the root files dies half-way', 'files', { SIM_RSYNC_ROOT_FAIL: '1' }],
    ['Ctrl-C during the rsync', 'files', { SIM_RSYNC_ROOT_SLOW: '20' }, { sigintOnMarker: '.rsync-root' }],
    ['caddy reload fails', 'caddy', { SIM_FAIL_RELOAD_N: '1' }],
    ['ssh drops on the Caddy step', 'caddy', { SIM_SSH_DROP: 'caddy-install.sh' }],
  ]) {
    t.test(`${name} → rolled back automatically`, async () => {
      const w = world(), pre = snapshot(w);
      const r = await deploy(w, env, opts);
      assertRolledBack(w, pre, r, phase);
      if (opts && opts.sigintOnMarker) assert.equal(r.status, 130, 'Ctrl-C exits 130');
      if (env.SIM_FAIL_RELOAD_N) assert.match(r.stderr, /caddy-install: restored Caddyfile from Caddyfile\.bak-/);
    });
  }

  t.test('preflight stop (live block has file_server browse) → nothing changed, and it says so', async () => {
    const live = V1.replace('\tfile_server\n}', '\tfile_server browse\n}');
    const w = world({ live }), pre = snapshot(w);
    const r = await deploy(w);
    assert.equal(r.status, 1, r.stdout + r.stderr);
    assert.doesNotMatch(r.stderr, /unbound variable/);
    assert.match(r.stderr, /STOP: the live choosereply\.com block does not match/);
    assert.match(r.stderr, /FAILED during «preflight» \(exit 1\) — nothing on the VPS was changed/);
    assert.deepEqual(backups(w), [], 'no backup made');
    assert.deepEqual(tree(w.site), pre.tree);
    assert.equal(fs.readFileSync(w.cf, 'utf8'), live);
    assert.deepEqual(leftovers(w), []);
  });
});
