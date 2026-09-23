// Task 12: RU typography of the copy — a short preposition/conjunction never ends a line and a dash
// never starts one. Checked on the section sources' TEXT (tags, comments, svg/script stripped): after
// в, и, с, на, для, без, или… comes a hard space (&nbsp;), and a dash follows a hard space.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../src/sections');
const SHORT = '(?:в|во|и|а|с|со|к|ко|о|об|у|на|по|за|до|из|от|не|но|для|без|или|при|про|под|над)';
const hanging = new RegExp(`(?<![A-Za-z\\u0400-\\u04FF-])(${SHORT}) (?=[A-Za-z\\u0400-\\u04FF«"(\\d])`, 'gi');

function textOf(html) {
  return html
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/<(svg|script|style)\b[\s\S]*?<\/\1>/g, ' ')
    .replace(/<\/?(em|span|b|strong|a|i)\b[^>]*>/g, '')   // inline tags do not break a line
    .replace(/<[^>]+>/g, '\n');
}

for (const f of fs.readdirSync(DIR).filter((n) => n.endsWith('.html'))) {
  test(`${f}: короткие слова и тире не висят на краю строки`, () => {
    const t = textOf(fs.readFileSync(path.join(DIR, f), 'utf8'));
    const hang = [...t.matchAll(hanging)].map((m) => t.slice(Math.max(0, m.index - 12), m.index + 14).replace(/\n/g, ' '));
    assert.deepEqual(hang, [], 'после предлога/союза — &nbsp;');
    const dash = [...t.matchAll(/[^\s>] —/g)].map((m) => t.slice(Math.max(0, m.index - 12), m.index + 8));
    assert.deepEqual(dash, [], 'перед тире — &nbsp;');
  });
}
