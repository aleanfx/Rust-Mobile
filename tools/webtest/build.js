// Tests building mode in the web build: give materials, enter building mode, place pieces.
// Usage: node build.js [outDir]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-build');
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);

(async () => {
  const browser = await chromium.launch({
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'],
  });
  const context = await browser.newContext({
    viewport: { width: 844, height: 390 }, deviceScaleFactor: 1, isMobile: true, hasTouch: true,
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1',
  });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} [pageerror] ${e.message}`));
  const cdp = await context.newCDPSession(page);
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const shot = async name => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const cmd = async c => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} [cmd] ${c}`); await wait(1500); };
  const tap = async (x, y) => {
    const p = [{ x, y, id: 7, radiusX: 4, radiusY: 4, force: 1 }];
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: p });
    await wait(120);
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
    log.push(`${stamp()} [tap] ${x},${y}`); await wait(1200);
  };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  await wait(3000);
  await shot('01-start');
  await tap(844 - 310 * 390 / 1080, 390 - 500 * 390 / 1080);   // CONSTRUIR without a plan: expect a toast
  await shot('02-no-plan');
  await cmd('give');
  await tap(844 - 310 * 390 / 1080, 390 - 500 * 390 / 1080);   // CONSTRUIR with a plan
  await shot('03-ghost-foundation');
  await tap(422 + 180 * 390 / 1080, 390 - 88 * 390 / 1080);    // COLOCAR
  await shot('04-placed-foundation');
  await tap(422 - 180 * 390 / 1080, 390 - 88 * 390 / 1080);    // Pieza -> Pared
  await shot('05-ghost-wall');
  await cmd('place');
  await cmd('piece'); await cmd('piece');                      // -> Techo
  await shot('06-ghost-roof');
  await cmd('place');
  await shot('07-built');
  await cmd('stats');
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|tap|test|shot|pageerror|error)\]|Oxfall\] (Built|Stats)|rror|xception/.test(l)).join('\n'));
  await browser.close();
})();
