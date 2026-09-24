# choosereply.com — лендинг v2

Премиальный статический лендинг. Исходники в `src/`, сборка — в `dist/` (публикуется только `dist/`,
`src/` на сервер не попадает). Спека — `docs/superpowers/specs/2026-09-19-premium-landing-design.md`;
план — `docs/superpowers/plans/2026-09-20-premium-landing.md`: там же бюджеты и текстовые ограничения
(«Global Constraints»), файловая структура и контракт модуля секции.

## Установка

```bash
cd Tools/landing
npm i
```

`node_modules`/`package-lock.json` — общее санкционированное состояние, не удалять.

## Сборка

```bash
npm run build          # src/ → dist/ (стирает и пересобирает dist/ целиком)
```

Сборка **минифицирует** то, что публикуется, а исходники оставляет читаемыми и с комментариями
(`esbuild`, devDependency; режим transform, **без бандла**):

* каждый `src/js/**/*.js` остаётся отдельным ES-модулем по своему пути в `assets/<hash>/js/` —
  статические и динамические `import` целы, граф модулей и `modulepreload` работают как в `src/`;
* `src/css/*.css` склеиваются и минифицируются в `assets/<hash>/site.css`;
* инлайновый скрипт тира в `<head>` минифицируется целиком (target `es2019` — минификатору нельзя
  свернуть `a && a.b` в `a?.b`: копия в `<head>` должна разбираться старыми движками);
* `charset: 'utf8'` — иначе esbuild экранирует кириллицу в `\uXXXX` и русские строки раздуваются;
* **хеш считается по уже сжатым байтам**: смена минификатора или его опций обязана поменять
  `/assets/<hash>/`, иначе новое содержимое легло бы под старый immutable-путь.

Без `esbuild` сборка **падает** (а не выкладывает молча несжатый код) — тогда `npm i` в `Tools/landing`.
Эффект на бюджеты (замер 2026-09-23, тир medium): `kbBeforeScroll` 431 → 358 (моб.) и 447 → 375 (десктоп)
при бюджете 450; JS 113 → 78 и 118 → 83 КБ gz при бюджете 120.

## Тесты

```bash
npm test               # node:test — чистые модули (pricing-format, quality, form, shapes, …)
```

## Проверка (headless-стенд)

```bash
npm run serve                                   # раздаёт dist/ на 127.0.0.1:8809 (фон)
node tools/check.mjs --baseline=http://127.0.0.1:8808/baseline/index.html
```

`check.mjs` — единственный способ проверить форму и производительность: он блокирует любой запрос на
`choosereply.com`/`n8n.choosereply.com` и локально подменяет `POST …/webhook/LandingLead` на
`{success:true}` (правило NO LIVE TRAFFIC). Проверяет ширины 360/390/768/1440 (нет горизонтального
скролла), reduced-motion, форму, якоря (в т. ч. «Ранний доступ» → карточка формы на 360×800, 390×844,
390×664; дип-линк `/#lead` на 390×664 и 1440×900; экраны ниже ~620 px → блок полей на 104), переписку
«Сигнала» на коротких телефонах (390×664, 375×548, 360×640: текст внутри пузырей, живое состояние и
тихий откат), CLS и — вместе с `--baseline` — FPS относительно baseline v1.

Телефонная проба — то, чего не видно на 390×844 (повторное ревью 2, N-5/N-6):

```bash
node tools/probe-phone.mjs                      # ~12 мин: 19 окон «Сигнала» + посадка «Ранний доступ»
node tools/probe-phone.mjs --only=signal --sizes=390x664,360x640 --sweep=0   # быстро, одно-два окна
```

- `signal` — 360×540 … 430×932, планшет, три десктопа: в покое каждого шага (живой GL и тихий откат
  после `webglcontextlost`) текст не выходит из пузыря (`spill`), не ложится на подписи/время/другой пузырь
  и под шапку, переписка видна в покое шага на телефоне, в откате не ложится на текст шага; плюс проход
  прокруткой через оба шага — показанный оверлей ни разу не на видимом тексте.
- `cta` — три входа: тап «Ранний доступ» в шапке, в hero и заход по `/#lead` (повторное ревью 3, O-1 —
  все три обязаны приходить в одно место). В покое поле телефона целиком на экране под шапкой, и вместе
  с ним заголовок формы; на экране, которому этой пары мало (360×540, 375×548 и до ~630 — O-2), посадка
  уходит на блок полей, и заголовок с него не требуется.
- Часы кадров страницы зажаты до 19 мс (`tools/signal-fit.mjs`): проба меряет раскладку, а не скорость,
  и регулятор кадров не гасит программное поле посреди замера; `--real-frames` — без зажима.
  `--shots=<dir>` — скриншоты. Exit 1 при любом нарушении. Один браузер через `tools/chrome.mjs`.
Каждый headless Chrome в проекте (включая `check.mjs`, `og.mjs`, `raster-icon.mjs`) идёт через
`tools/chrome.mjs` (`launchChrome()`) — аппаратная видеокарта выключена; не запускать `chromium.launch`
напрямую и не держать больше одного браузера одновременно на этой машине.

Ключи `check.mjs`:
- `--url=<...>` — что проверять (по умолчанию `http://127.0.0.1:8809/`); можно с `?tier=`.
- `--baseline=<url>` — прогнать тот же перф-замер на v1-прототипе и сравнить (без него бюджет FPS не проверяется).
- `--tier=low|medium|high` — форсирует профиль качества движения на всех проходах, кроме
  reduced-motion (та всегда проверяет `low`) и `--baseline` (v1 меряется как есть). Headless Chrome
  здесь всегда отдаёт `hardwareConcurrency 4`, поэтому без `--tier` `decideTier()` сам решает по
  этой цифре.
- `--out=<dir>` — куда писать скриншоты/отчёт (по умолчанию `.check/`).
- `--skip-perf` — пропустить FPS-проход (быстрая проверка вёрстки/консоли/якорей).

Baseline v1 (для парных перф-замеров) — статика `.planning/sketches/008-premium-landing/`:

```bash
cd /Users/ayan/Projects/Automation/.planning/sketches/008-premium-landing
python3 -m http.server 8808 --bind 127.0.0.1
```

Машина может быть нагружена параллельной работой — FPS всегда сравнивать с baseline **в одном и том
же прогоне**, при провале — повторить один раз.

## Как обновить цены

Единственный источник — `src/js/pricing-format.js` (`PLANS`), зеркалит
`Assets/Scripts/Billing/PlanCatalog.cs`. Поменять значения там же, затем свериться с
`tools/test/pricing.test.mjs` (пинит месячные/годовые суммы, лимиты ботов/каналов/диалогов и флаг
«Популярный» — обновить тест вместе с ценами) и пересобрать/прогнать `npm test`.

## Как перегенерировать ассеты

```bash
node tools/fetch-fonts.mjs     # Onest (RU+EN+символьный сабсет) + Noto Serif Display Italic → src/assets/fonts/, src/css/00-fonts.css
bash tools/fetch-vendor.sh     # GSAP + ScrollTrigger + Lenis (закреплённые версии) → src/assets/vendor/
bash tools/make-images.sh      # скриншоты приложения → webp 720/1080, favicon/apple-touch/mark → src/assets/img/, src/static/
node tools/og.mjs              # tools/og.html → src/static/og.png (1200×630) через headless Chrome
```

`fetch-fonts.mjs` и `fetch-vendor.sh` ходят в сеть (Google Fonts / jsdelivr); `make-images.sh` работает
локально — читает `Tools/store/screenshots` и иконку из `Tools/icon-lab`, нужны `ffmpeg` и Chrome.
Запускать только когда исходники правда изменились, не при обычной сборке.

## Выкладка

Публикуется только `dist/`, никогда `src/`. Процесс — `tools/deploy.sh`, по умолчанию **dry-run**
(печатает `DRY: <команда>`, ничего не меняет на VPS):

```bash
node tools/build.mjs
bash tools/deploy.sh            # dry-run — смотрим, что будет сделано
bash tools/deploy.sh --go       # настоящая выкладка — только по согласию владельца
```

Что делает `--go` (VPS-файл Caddy обслуживает и `n8n.choosereply.com` — боевой бэкенд приложения,
поэтому на любом пути отказа файл на диске остаётся равен конфигу, который исполняет Caddy):

0. **предпроверка — на VPS только чтение, остановка до любых изменений.** Живой Caddyfile читается
   (`ssh cat`) и преобразуется **локально** (`tools/caddy-block.py`, чистое преобразование с тестами на
   образцах Caddyfile): меняется только блок `choosereply.com { … }`, блоки `www` (редирект) и `n8n`
   остаются байт в байт — печатается diff всего файла. Если в живом блоке есть директива, которой нет
   в `tools/Caddyfile.site` и которая не входит в короткий список заменяемых строк v1 (`root * /srv/site`,
   `encode gzip`, `file_server`), — **СТОП** с diff: иначе замена молча выбросила бы `file_server browse`/
   `handle` для `/review/` (там видео для App Review), заголовки, редиректы, а `caddy validate` об этом не
   знает. Такую директиву переносят в `Caddyfile.site` (тогда она переедет) и запускают снова; флага
   «выбросить» нет. Дальше скрипт проверяет свои допущения: контейнер `caddy` читает именно
   `~/choosereply/Caddyfile` как `/etc/caddy/Caddyfile` и отдаёт `~/choosereply/site` как `/srv/site`
   (sha256 с обеих сторон); живой конфиг валиден (он — цель отката); **новый конфиг проходит
   `caddy validate` внутри контейнера до того, как что-либо записано**. Запоминает коды каждого
   `/review/<каталог>/`, каждого файла в нём (`review/*/*` — запросом HEAD, видео не скачивается) и
   `n8n /healthz`;
1. бэкапит `~/choosereply/site` → `site.bak-<stamp>` и `Caddyfile` → `Caddyfile.bak-<stamp>` и сразу
   печатает команду отката. **С этого момента любой отказ — ошибка, промах дымовой проверки, Ctrl-C,
   оборванный ssh — запускает откат автоматически** (ловушка `EXIT`);
2. `rsync` **без `--delete`** в два прохода: сначала `dist/assets/`, потом страницы — `index.html` не
   приезжает раньше каталога `assets/<hash>/`, на который ссылается (`review/` и старые ассеты не трогаются);
3. `tools/caddy-install.sh` на VPS: ещё раз `validate` кандидата, запись **на месте** (тот же inode),
   проверка, что контейнер видит ровно эти байты, `validate` живого пути, `reload`; любой сбой здесь
   сразу возвращает `Caddyfile.bak-<stamp>` на место. Пути отказа проверены локально:
   `tools/test/deploy.test.mjs` (поддельный `docker`) и `tools/test/deploy-go.test.mjs` — настоящий
   `deploy.sh --go` под `/bin/bash` 3.2 в локали `en_US.UTF-8` (как в Terminal.app) против поддельного VPS
   на диске: ssh/scp/rsync/curl/docker подменены (`tools/test/fake-vps/`), в сеть не выходит ничего;
4. дымовая проверка: `/`, `/privacy.html`, `/terms.html` отвечают 200, каждый `/review/<каталог>/`,
   каждый файл `review/*/*` (видео для App Review) и `n8n.choosereply.com/healthz` — тем же кодом, что до
   выкладки. Промах — автоматический откат.

### Перед первым `--go`

`deploy.sh` писался по описанию VPS, не по факту; главное он проверяет сам в шаге 0 и останавливается,
если что-то не так. Если остановился — поправить на месте (только чтение на сервере, `ssh choosereply`):

- имя сервиса Caddy в `docker compose ps` (скрипты зовут `docker compose exec -T caddy …`);
- пути `/etc/caddy/Caddyfile` и `/srv/site` в контейнере (`docker compose config`, раздел `volumes`
  сервиса Caddy) — или `root` в `tools/Caddyfile.site`; тип монтирования неважен: и выкладка, и откат
  пишут **на месте** (`rsync --delete` в `site/`, `cp`/`cat` поверх Caddyfile), никогда `mv`;
- всё, что шаг 0 назвал «would DROP», перенести в `tools/Caddyfile.site`.

## Откат

Без пересборки — восстановить бэкап, который сделал `--go` (при сбое скрипт делает это сам; команда
напечатана сразу после бэкапа и в конце вывода). Восстановление на месте — inode каталога и файла не
меняются, конфиг проверяется перед перезагрузкой:

```bash
ssh choosereply 'cd ~/choosereply && rsync -a --delete site.bak-<stamp>/ site/ && cp Caddyfile.bak-<stamp> Caddyfile && docker compose exec -T caddy caddy validate --config /etc/caddy/Caddyfile && docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile'
```
