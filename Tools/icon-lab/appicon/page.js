// Emits the icon review page. Every mark on it is the SAME markup the PNG
// sheets are rendered from, so the page can never drift from the assets.
const fs = require('fs');
const path = require('path');
const { concepts } = require(process.env.SET === 'v1' ? './concepts' : process.env.SET === 'v2' ? './concepts_choice' : process.env.SET === 'v3' ? './concepts_round3' : process.env.SET === 'v4' ? './concepts_round4' : process.env.SET === 'v5' ? './concepts_round5' : process.env.SET === 'v6' ? './concepts_round6' : './concepts_round7');
const { iconSvg, palettes } = require('./build');

const DIRS = [
  {
    key: 'S', name: 'Ваша четвёрка из прошлого раунда',
    bet: 'T1, T4, T12 и S8 различаются ТОЛЬКО цветом. Значит, каркас зафиксирован: три полосы во всю ширину, средняя толще, она и есть выбранная, никаких отметок. Дальше меняется не форма, а рецепт цвета на этом каркасе.',
  },
  {
    key: 'A', name: 'Во что одеты соседние',
    bet: 'Принятая полоса яркая — вопрос, что носят отклонённые: приглушённые чернила, градиент вполсилы, тот же цвет темнее или два разных оттенка.',
  },
  {
    key: 'B', name: 'Отделка выбранной',
    bet: 'Соседние не трогаем — полируем принятую: цветное ребро из-под белой, стеклянный градиент, неоновая трубка с белой сердцевиной.',
  },
  {
    key: 'C', name: 'Инверсия и тонировка',
    bet: 'Наследники S8. Полная заливка с глубиной — и компромисс: чернила лишь подкрашены цветом, так что заметность S8 сочетается с тёмным фоном, который вы просили.',
  },
  {
    key: 'D', name: 'Пропорции и углы',
    bet: 'Три ручки, которые применимы к любому рецепту выше: контраст толщин выкручен сильнее, поля вдвое меньше, углы прямее. Выбираются независимо от цвета.',
  },
];

const VERDICT = {
  T1:  ['R11 и S8 в одном, без цветного фона', 'Толщина и цвет дублируют друг друга'],
  T4:  ['Единственный с живым, не плоским цветом', 'Градиент почти не виден на 29px'],
  T12: ['Один цвет — максимально фирменный', 'Приглушённые полосы сливаются с фоном'],
  S8:  ['Самый заметный: единственный яркий среди тёмных', 'Цветная заливка — дальше всех от чернил'],
  U1:  ['T4 и T12 вместе — самый фирменный рецепт', 'На слабом экране градиент уходит в плоский'],
  U2:  ['Цвет в соседях, свет в выбранной', 'Полусила градиента требует точной настройки'],
  U3:  ['Монолит: ни одного белого пикселя', 'Контраст тона слабее контраста белого'],
  U4:  ['Глубина вместо плоского повтора', 'Разница оттенков почти пропадает на мелком'],
  U5:  ['Один намёк цвета на весь знак', 'Ребро в пару пикселей исчезает первым'],
  U6:  ['Стекло дорого выглядит на большом', 'На мелком неотличим от просто белой'],
  U7:  ['Неон честно светится на чернилах', 'Белая сердцевина утончает полосу'],
  U8:  ['Глубина заливки делает S8 объёмным', 'Всё ещё цветной фон, не чернила'],
  U9:  ['Заметность S8 на тёмном фоне — компромисс найден', 'Тонировку в 20% видно не на всех экранах'],
  U10: ['Иерархия читается с любого расстояния', 'Тонкие соседние на 29px худеют до линий'],
  U11: ['Знак крупнее в той же иконке', 'Малые поля спорят со скруглением маски'],
  U12: ['Строки интерфейса, а не таблетки', 'Прямее углы — суше характер'],
};

const icons = {};
for (const c of concepts) {
  icons[c.id] = {};
  for (const p of palettes) icons[c.id][p.id] = iconSvg(c, p, { bare: true });
}

const meta = concepts.map((c) => ({ id: c.id, dir: c.dir, ru: c.ru, note: c.note, verdict: VERDICT[c.id] }));
const pals = palettes.map((p) => ({ id: p.id, ru: p.ru, bg: p.bg, accent: p.accent, mark: p.mark }));

const DATA = `const ICONS=${JSON.stringify(icons)};const META=${JSON.stringify(meta)};const PALS=${JSON.stringify(pals)};const DIRS=${JSON.stringify(DIRS)};`;

const html = String.raw`<title>Иконка Choose Reply</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Unbounded:wght@500;600&family=Manrope:wght@400;500;600;700&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>
:root{
  --ground:#EFF3F9; --panel:#FFFFFF; --panel-2:#F6F8FC; --line:#DFE5EF; --line-2:#CBD4E2;
  --ink:#0E1524; --ink-2:#586477; --ink-3:#8C96A7;
  --accent:#2C6BF0; --accent-soft:rgba(44,107,240,.10);
  --wall:linear-gradient(160deg,#D9E4F6 0%,#BFD0EA 60%,#AEC2E2 100%);
  --wall-ink:#152238; --shadow:0 1px 2px rgba(16,26,45,.06),0 12px 28px rgba(16,26,45,.07);
  --ring:0 0 0 1px var(--line);
}
:root:not([data-theme="light"]){ color-scheme:light dark; }
@media (prefers-color-scheme:dark){
  :root:not([data-theme="light"]){
    --ground:#080B11; --panel:#111722; --panel-2:#0D131C; --line:#1D2634; --line-2:#2A3446;
    --ink:#E7EBF2; --ink-2:#98A2B4; --ink-3:#6B7588;
    --accent:#22D3EE; --accent-soft:rgba(34,211,238,.12);
    --wall:linear-gradient(160deg,#17253F 0%,#0C1220 55%,#05080E 100%);
    --wall-ink:#E7EBF2; --shadow:0 1px 2px rgba(0,0,0,.5),0 14px 34px rgba(0,0,0,.45);
  }
}
:root[data-theme="dark"]{
  --ground:#080B11; --panel:#111722; --panel-2:#0D131C; --line:#1D2634; --line-2:#2A3446;
  --ink:#E7EBF2; --ink-2:#98A2B4; --ink-3:#6B7588;
  --accent:#22D3EE; --accent-soft:rgba(34,211,238,.12);
  --wall:linear-gradient(160deg,#17253F 0%,#0C1220 55%,#05080E 100%);
  --wall-ink:#E7EBF2; --shadow:0 1px 2px rgba(0,0,0,.5),0 14px 34px rgba(0,0,0,.45);
}

*{box-sizing:border-box}
body{
  margin:0; background:var(--ground); color:var(--ink);
  font:400 16px/1.6 Manrope,-apple-system,"Segoe UI",Roboto,sans-serif;
  -webkit-font-smoothing:antialiased;
}
.wrap{max-width:1180px;margin:0 auto;padding:0 22px 96px}
h1,h2,h3{font-family:Unbounded,Manrope,sans-serif;font-weight:600;letter-spacing:-.01em;text-wrap:balance;margin:0}
.mono{font-family:"IBM Plex Mono",ui-monospace,SFMono-Regular,Menlo,monospace;font-variant-numeric:tabular-nums}
.eyebrow{font-family:"IBM Plex Mono",ui-monospace,monospace;font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:var(--ink-3)}

/* ---------- masthead ---------- */
header.top{padding:56px 0 34px}
.rec{margin-top:26px;padding:18px 20px;border-radius:14px;background:var(--accent-soft);
     border:1px solid color-mix(in srgb,var(--accent) 32%,transparent);max-width:74ch}
.rec p{margin:8px 0 0;font-size:15px;line-height:1.65;color:var(--ink-2)}
.rec p b{color:var(--ink);font-weight:700}
header.top h1{font-size:clamp(28px,4.2vw,42px);line-height:1.12;margin:14px 0 16px}
header.top p{color:var(--ink-2);max-width:62ch;font-size:17px;margin:0}
.facts{display:flex;flex-wrap:wrap;gap:10px;margin-top:24px}
.fact{border:1px solid var(--line);border-radius:999px;padding:7px 14px;font-size:13px;color:var(--ink-2);background:var(--panel)}
.fact b{color:var(--ink);font-weight:600}

/* ---------- palette rail ---------- */
.rail{position:sticky;top:0;z-index:40;border-top:1px solid var(--line);background:color-mix(in srgb,var(--ground) 88%,transparent);
      backdrop-filter:blur(12px);-webkit-backdrop-filter:blur(12px);
      border-bottom:1px solid var(--line);margin-bottom:34px}
.rail .inner{max-width:1180px;margin:0 auto;padding:12px 22px;display:flex;align-items:center;gap:16px;flex-wrap:wrap}
.rail .lbl{font-family:"IBM Plex Mono",monospace;font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:var(--ink-3)}
.sw{display:flex;gap:8px;flex-wrap:wrap}
.sw button{border:1px solid var(--line-2);background:var(--panel);border-radius:10px;padding:5px 5px 5px 5px;
           display:flex;align-items:center;gap:8px;cursor:pointer;color:var(--ink);font:500 13px Manrope,sans-serif;
           padding-right:12px;transition:border-color .16s,box-shadow .16s}
.sw button:hover{border-color:var(--line-2)}
.sw button[aria-pressed="true"]{border-color:var(--accent);box-shadow:0 0 0 1px var(--accent)}
.sw button:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
.chip{width:24px;height:24px;border-radius:7px;flex:none}

/* ---------- layout ---------- */
.cols{display:grid;grid-template-columns:1fr;gap:40px}
@media(min-width:1020px){ .cols{grid-template-columns:minmax(0,1fr) 316px;gap:48px;align-items:start} }

/* ---------- direction sections ---------- */
section.dir{margin:0 0 52px;scroll-margin-top:86px}
section.dir > .head{margin-bottom:22px;padding-left:16px;border-left:3px solid var(--accent)}
section.dir h2{font-size:22px;margin-bottom:8px}
section.dir .bet{color:var(--ink-2);font-size:15px;max-width:64ch;margin:0}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(238px,1fr));gap:18px}

.card{background:var(--panel);border:1px solid var(--line);border-radius:18px;padding:18px;
      display:flex;flex-direction:column;gap:12px;text-align:left;cursor:pointer;color:inherit;
      font:inherit;box-shadow:var(--shadow);transition:transform .18s ease,border-color .18s ease,box-shadow .18s ease}
.card:hover{transform:translateY(-2px);border-color:var(--line-2)}
.card[aria-pressed="true"]{border-color:var(--accent);box-shadow:0 0 0 1px var(--accent),var(--shadow)}
.card:focus-visible{outline:2px solid var(--accent);outline-offset:3px}
.card .big{width:100%;aspect-ratio:1;max-width:150px;margin:0 auto}
.card .row1{display:flex;align-items:baseline;gap:8px}
.card .code{font-family:"IBM Plex Mono",monospace;font-size:11px;letter-spacing:.1em;color:var(--ink-3)}
.card h3{font-size:15.5px;font-family:Manrope,sans-serif;font-weight:700;letter-spacing:0}
.card .note{font-size:13px;line-height:1.5;color:var(--ink-2);margin:0}
.sizes{display:flex;align-items:flex-end;gap:12px;padding-top:12px;margin-top:auto;border-top:1px solid var(--line)}
.sizes figure{margin:0;display:flex;flex-direction:column;align-items:center;gap:5px}
.sizes figcaption{font-family:"IBM Plex Mono",monospace;font-size:10px;color:var(--ink-3)}
.sizes .cap{font-size:11px;color:var(--ink-3);margin-left:auto;text-align:right;line-height:1.35;max-width:96px}
.pros{display:flex;flex-direction:column;gap:5px;font-size:12.5px;line-height:1.45}
.pros div{display:flex;gap:7px;color:var(--ink-2)}
.pros .m{color:var(--ink-3);flex:none;font-family:"IBM Plex Mono",monospace;font-size:12px}

/* ---------- home screen preview ---------- */
.preview{position:relative}
@media(min-width:1020px){ .preview{position:sticky;top:78px} }
.phone{background:var(--wall);border-radius:26px;padding:20px 16px 14px;border:1px solid var(--line-2);
       box-shadow:var(--shadow);overflow:hidden}
.hs{display:grid;grid-template-columns:repeat(4,1fr);gap:16px 12px}
.hs .app{display:flex;flex-direction:column;align-items:center;gap:5px}
.hs .app span{font-size:9.5px;color:var(--wall-ink);text-shadow:0 1px 3px rgba(0,0,0,.35);
              max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
:root[data-theme="light"] .hs .app span,
:root:not([data-theme="dark"]) .hs .app span{text-shadow:0 1px 2px rgba(255,255,255,.6)}
.hs .app.me span{font-weight:700}
.tile{width:100%;aspect-ratio:1;border-radius:22.5%;display:block}
.dock{margin-top:16px;background:rgba(255,255,255,.22);border-radius:18px;padding:9px;
      display:grid;grid-template-columns:repeat(4,1fr);gap:10px;backdrop-filter:blur(6px)}
.pv-head{display:flex;align-items:center;justify-content:space-between;gap:10px;margin-bottom:12px}
.pv-head h3{font-size:14px;font-family:Manrope,sans-serif;font-weight:700}
.toggle{display:flex;border:1px solid var(--line-2);border-radius:9px;overflow:hidden}
.toggle button{border:0;background:var(--panel);color:var(--ink-2);font:500 11.5px Manrope,sans-serif;
               padding:5px 10px;cursor:pointer}
.toggle button[aria-pressed="true"]{background:var(--accent-soft);color:var(--accent)}
.toggle button:focus-visible{outline:2px solid var(--accent);outline-offset:-2px}
.pv-meta{margin-top:14px;font-size:13px;color:var(--ink-2);line-height:1.5}
.pv-meta b{color:var(--ink)}
.pv-note{margin-top:10px;font-size:12px;color:var(--ink-3);line-height:1.5}

/* ---------- colourways ---------- */
.cw{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:16px}
.cw .item{background:var(--panel);border:1px solid var(--line);border-radius:16px;padding:14px;
          display:flex;flex-direction:column;gap:10px;box-shadow:var(--shadow)}
.cw .item .nm{display:flex;align-items:baseline;justify-content:space-between;gap:8px}
.cw .item .nm b{font-size:13.5px;font-weight:700}
.cw .item .nm span{font-family:"IBM Plex Mono",monospace;font-size:10.5px;color:var(--ink-3)}
.cw .item p{margin:0;font-size:12px;color:var(--ink-2);line-height:1.45}

/* ---------- closing ---------- */
.next{background:var(--panel);border:1px solid var(--line);border-radius:18px;padding:24px 26px;box-shadow:var(--shadow)}
.next h2{font-size:19px;margin-bottom:14px}
.next ol{margin:0;padding-left:20px;color:var(--ink-2);font-size:14.5px;line-height:1.7}
.next ol b{color:var(--ink);font-weight:600}
.next .warn{margin-top:18px;padding:12px 14px;border-radius:12px;background:var(--accent-soft);
            color:var(--ink-2);font-size:13.5px;line-height:1.55;border:1px solid color-mix(in srgb,var(--accent) 30%,transparent)}
.next .warn b{color:var(--ink)}
hr.sep{border:0;border-top:1px solid var(--line);margin:44px 0 34px}
h2.big{font-size:24px;margin-bottom:8px}
p.lede{color:var(--ink-2);max-width:64ch;margin:0 0 22px;font-size:15px}
@media(prefers-reduced-motion:reduce){*{transition:none!important;animation:none!important}}
</style>

<div class="wrap">
  <header class="top">
    <div class="eyebrow">Choose Reply · иконка приложения · 28 августа 2026</div>
    <h1>Один каркас, двенадцать рецептов цвета</h1>
    <p>Седьмой заход — и он особенный: T1, T4, T12 и S8 различаются <b>только цветом</b>. Каркас зафиксирован — три полосы, средняя толще и она выбранная, без отметок. Так что здесь двенадцать <b>рецептов цвета и отделки</b> на одном каркасе плюс три ручки пропорций, совместимые с любым рецептом. Это финишная прямая: выбираете рецепт — я довожу знак и готовлю файлы для сторов.</p>
    <div class="facts">
      <span class="fact">Каркас зафиксирован</span>
      <span class="fact"><b>12</b> рецептов цвета</span>
      <span class="fact"><b>3</b> ручки пропорций</span>
      <span class="fact"><b>6</b> палитр</span>
      <span class="fact">Каждый проверен на <b>29px</b></span>
    </div>
    <div class="rec">
      <div class="eyebrow">Если коротко</div>
      <p>Из нового я бы взял <b>Q12 «Цветная реплика»</b> — это вывернутая P5, которая вам понравилась: реплика залита цветом, отклонённые варианты прорезаны до фона, принятый белый. На домашнем экране она заметнее всех остальных и при этом остаётся читаемой на 29 пикселях, где большинство знаков со строками уже плывут. Второй кандидат — <b>Q6 «Три реплики»</b>: развитие вашей P15, и это самый честный знак набора, потому что ровно так варианты и выглядят в «Вместе». Третий — <b>Q1 «Выбранный обретает хвост»</b>: единственный, где нарисован сам момент отправки, а не его результат.</p>
    </div>
  </header>
</div>

<div class="rail">
  <div class="inner">
    <span class="lbl">Палитра</span>
    <div class="sw" id="sw"></div>
  </div>
</div>

<div class="wrap">

  <div class="cols">
    <main id="main"></main>
    <aside class="preview">
      <div class="pv-head">
        <h3>На домашнем экране</h3>
        <div class="toggle" id="wallToggle">
          <button type="button" data-wall="dark" aria-pressed="true">Тёмные</button>
          <button type="button" data-wall="light" aria-pressed="false">Светлые</button>
        </div>
      </div>
      <div class="phone" id="phone"></div>
      <div class="pv-meta" id="pvMeta"></div>
      <p class="pv-note">Соседи подобраны так, как выглядит настоящий экран: пёстро. Если знак теряется здесь — он потеряется и у клиента.</p>
    </aside>
  </div>

  <hr class="sep">
  <h2 class="big" id="cwTitle">Палитры</h2>
  <p class="lede" id="cwLede"></p>
  <div class="cw" id="cw"></div>

  <hr class="sep">
  <div class="next">
    <h2>Что дальше, когда направление выбрано</h2>
    <ol>
      <li><b>Довожу выбранный знак.</b> Оптический центр, толщины, посадка в квадрат — на финальном варианте это делается по-настоящему, а не на глаз.</li>
      <li><b>Экспорт под сторы.</b> 1024×1024 без альфы и без скруглений для App Store; для Android — adaptive icon: отдельный фон и отдельный передний слой с запасом 33% по краям, иначе Google обрежет знак по кругу.</li>
      <li><b>Прописываю в проект.</b> <span class="mono">ProjectSettings</span> для iOS и Android, замена <span class="mono">Assets/Images/Icon.png</span>, плюс favicon и og-картинка на choosereply.com — чтобы сайт, стор и приложение говорили одним знаком.</li>
      <li><b>Скриншоты для стора.</b> Иконка задаёт цвет обложек — их проще делать после, чем переделывать.</li>
    </ol>
    <div class="warn"><b>Что происходит после выбора рецепта.</b> Осталось три решения: рецепт цвета (эта страница), палитра — циановая «Ночная» против фирменного индиго «Чернильный» (переключатель вверху красит все знаки сразу), и при желании ручка пропорций из последней секции. Дальше я довожу знак: оптическая посадка на сетке, толщины и радиусы на реальных размерах, экспорт 1024×1024 без альфы для App Store, adaptive icon для Android с запасом 33% по краям, замена favicon и og-картинки на choosereply.com — чтобы сайт, стор и приложение говорили одним знаком.</div>
  </div>
</div>

<script>
${DATA}

const SQ = (id) => '<defs><clipPath id="' + id + '"><path d="M 227.8 0 L 796.2 0 C 852.5 0 1024 171.5 1024 227.8 L 1024 796.2 C 1024 852.5 852.5 1024 796.2 1024 L 227.8 1024 C 171.5 1024 0 852.5 0 796.2 L 0 227.8 C 0 171.5 171.5 0 227.8 0 Z"/></clipPath></defs>';

// Apple's continuous corner, as a real superellipse-ish path (matches the PNG sheets).
function squirclePath(){
  const s = 1024, k = s * 0.2225, c = k * 0.55;
  return 'M ' + k + ' 0 L ' + (s-k) + ' 0 C ' + (s-c) + ' 0 ' + s + ' ' + c + ' ' + s + ' ' + k +
         ' L ' + s + ' ' + (s-k) + ' C ' + s + ' ' + (s-c) + ' ' + (s-c) + ' ' + s + ' ' + (s-k) + ' ' + s +
         ' L ' + k + ' ' + s + ' C ' + c + ' ' + s + ' 0 ' + (s-c) + ' 0 ' + (s-k) +
         ' L 0 ' + k + ' C 0 ' + c + ' ' + c + ' 0 ' + k + ' 0 Z';
}
let n = 0;
function svgFor(conceptId, palId, cls){
  const id = 'sq' + (++n);
  return '<svg viewBox="0 0 1024 1024" class="' + (cls||'') + '" role="img" aria-label="Иконка ' + conceptId + '">' +
    '<defs><clipPath id="' + id + '"><path d="' + squirclePath() + '"/></clipPath></defs>' +
    '<g clip-path="url(#' + id + ')">' + ICONS[conceptId][palId] + '</g></svg>';
}

let palId = 'night';
let sel = 'U1';
let wall = 'dark';

/* ---------- palette rail ---------- */
const sw = document.getElementById('sw');
sw.innerHTML = PALS.map(p =>
  '<button type="button" data-pal="' + p.id + '" aria-pressed="' + (p.id===palId) + '">' +
  '<i class="chip" style="background:linear-gradient(140deg,' + p.bg[0] + ',' + p.bg[1] + ')"></i>' + p.ru + '</button>').join('');
sw.addEventListener('click', e => {
  const b = e.target.closest('button[data-pal]'); if (!b) return;
  palId = b.dataset.pal;
  [...sw.querySelectorAll('button')].forEach(x => x.setAttribute('aria-pressed', String(x.dataset.pal === palId)));
  renderAll();
});

/* ---------- concept sections ---------- */
function cardHtml(m){
  return '<button type="button" class="card" data-id="' + m.id + '" aria-pressed="' + (m.id===sel) + '">' +
    svgFor(m.id, palId, 'big') +
    '<div class="row1"><span class="code">' + m.id + '</span><h3>' + m.ru + '</h3></div>' +
    '<p class="note">' + m.note + '</p>' +
    '<div class="pros">' +
      '<div><span class="m">+</span><span>' + m.verdict[0] + '</span></div>' +
      '<div><span class="m">&minus;</span><span>' + m.verdict[1] + '</span></div>' +
    '</div>' +
    '<div class="sizes">' +
      [60,40,29].map(s => '<figure><div style="width:' + s + 'px">' + svgFor(m.id, palId) + '</div>' +
        '<figcaption>' + s + '</figcaption></figure>').join('') +
      '<div class="cap">реальный размер на телефоне</div>' +
    '</div></button>';
}
function renderMain(){
  document.getElementById('main').innerHTML = DIRS.map(d =>
    '<section class="dir"><div class="head"><div class="eyebrow">Направление ' + d.key + '</div>' +
    '<h2>' + d.name + '</h2><p class="bet">' + d.bet + '</p></div>' +
    '<div class="grid">' + META.filter(m => m.dir === d.key).map(cardHtml).join('') + '</div></section>').join('');
}
document.addEventListener('click', e => {
  const c = e.target.closest('.card'); if (!c) return;
  sel = c.dataset.id;
  document.querySelectorAll('.card').forEach(x => x.setAttribute('aria-pressed', String(x.dataset.id === sel)));
  renderPreview(); renderCw();
});

/* ---------- home screen ---------- */
const NEIGHBOURS = [
  ['#3C8CF0','#1F5FD0','M 512 300 a 212 212 0 1 0 1 0 Z M 512 392 a 120 120 0 1 1 -1 0 Z'],
  ['#4BC96A','#189B45','M 300 512 h 424 M 512 300 v 424'],
  ['#F2A33C','#DE7A16','M 330 640 l 130 -190 110 130 84 -110 100 170 Z'],
  ['#8E7BF0','#5B45C9','M 512 296 l 190 330 h -380 Z'],
  ['#E5606A','#C23440','M 340 380 h 344 v 300 h -344 Z M 340 470 h 344'],
  ['#39B7C4','#1B8794','M 512 320 v 384 M 372 460 h 280'],
  ['#9AA3B2','#6A7484','M 350 400 h 324 M 350 512 h 324 M 350 624 h 220'],
  ['#F0C23C','#D19A10','M 512 320 l 62 128 140 20 -101 99 24 140 -125 -66 -125 66 24 -140 -101 -99 140 -20 Z'],
];
function neighbourTile(i){
  const [a,b,d] = NEIGHBOURS[i % NEIGHBOURS.length];
  const gid = 'ng' + (++n);
  return '<svg viewBox="0 0 1024 1024" class="tile" aria-hidden="true">' +
    '<defs><linearGradient id="' + gid + '" x1="0" y1="0" x2="0" y2="1">' +
    '<stop offset="0" stop-color="' + a + '"/><stop offset="1" stop-color="' + b + '"/></linearGradient></defs>' +
    '<rect width="1024" height="1024" rx="230" fill="url(#' + gid + ')"/>' +
    '<path d="' + d + '" fill="none" stroke="#FFFFFF" stroke-width="64" stroke-linecap="round" stroke-linejoin="round" opacity="0.92"/></svg>';
}
const NAMES = ['Камера','Заметки','Фото','Календарь','Почта','Здоровье','Файлы','Погода'];
function renderPreview(){
  const phone = document.getElementById('phone');
  const cells = [];
  for (let i = 0; i < 12; i++){
    if (i === 5){
      cells.push('<div class="app me">' + svgFor(sel, palId, 'tile') + '<span>Choose Reply</span></div>');
    } else {
      const j = i > 5 ? i - 1 : i;
      cells.push('<div class="app">' + neighbourTile(j) + '<span>' + NAMES[j % NAMES.length] + '</span></div>');
    }
  }
  phone.innerHTML = '<div class="hs">' + cells.join('') + '</div>' +
    '<div class="dock">' + [0,2,4,6].map(neighbourTile).join('') + '</div>';
  const m = META.find(x => x.id === sel);
  const p = PALS.find(x => x.id === palId);
  document.getElementById('pvMeta').innerHTML =
    '<b>' + m.id + ' · ' + m.ru + '</b> — палитра «' + p.ru + '»<br>' + m.verdict[0] + '. Минус: ' + m.verdict[1].toLowerCase() + '.';
}
document.getElementById('wallToggle').addEventListener('click', e => {
  const b = e.target.closest('button[data-wall]'); if (!b) return;
  wall = b.dataset.wall;
  [...e.currentTarget.querySelectorAll('button')].forEach(x => x.setAttribute('aria-pressed', String(x.dataset.wall === wall)));
  document.getElementById('phone').style.background = wall === 'light'
    ? 'linear-gradient(160deg,#DCE7F8 0%,#C3D3EC 60%,#B0C4E4 100%)'
    : 'linear-gradient(160deg,#17253F 0%,#0C1220 55%,#05080E 100%)';
  document.getElementById('phone').style.setProperty('--wall-ink', wall === 'light' ? '#152238' : '#E7EBF2');
  document.querySelectorAll('.hs .app span, .dock + * span').forEach(s => {
    s.style.color = wall === 'light' ? '#152238' : '#E7EBF2';
    s.style.textShadow = wall === 'light' ? '0 1px 2px rgba(255,255,255,.6)' : '0 1px 3px rgba(0,0,0,.45)';
  });
});

/* ---------- colourways ---------- */
const CW_NOTE = {
  night:'Палитра сайта: циан на тёмно-синем. Самая яркая из тёмных.',
  ink:'Фирменный индиго приложения — акцент тёмной темы. Тише циана, зато знак и интерфейс говорят одним цветом.',
  indigo:'Индиго из акцента приложения (#3E61C6). Ярче на светлых обоях, но теряет связь с цианом сайта.',
  cyan:'Максимальная заметность: на домашнем экране почти нет ярко-бирюзовых иконок.',
  light:'Белый фон — редкость среди мессенджеров, поэтому выделяется. Самая «айфонная» из всех.',
  graphite:'Нейтральный графит: не спорит ни с какими обоями, но и не запоминается.',
  green:'Зелёный говорит «WhatsApp» ещё до запуска. Сильный маркетинг — и риск на ревью Apple.',
};
function renderCw(){
  const m = META.find(x => x.id === sel);
  document.getElementById('cwTitle').textContent = 'Палитры — на знаке ' + m.id;
  document.getElementById('cwLede').textContent =
    'Форма и цвет — два независимых решения. Выше выбирается форма, здесь — во что её красить. Нажмите на любой знак выше, чтобы примерить палитры на него.';
  document.getElementById('cw').innerHTML = PALS.map(p =>
    '<div class="item">' + svgFor(m.id, p.id) +
    '<div class="nm"><b>' + p.ru + '</b><span>' + p.bg[0] + '</span></div>' +
    '<p>' + CW_NOTE[p.id] + '</p></div>').join('');
}

function renderAll(){ renderMain(); renderPreview(); renderCw(); }
renderAll();
</script>`;

fs.writeFileSync(path.join(__dirname, 'out', 'appicon-review.html'), html);
console.log('page written:', (html.length / 1024).toFixed(0) + 'KB');
