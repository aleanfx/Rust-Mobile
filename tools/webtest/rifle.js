// The semi-automatic rifle, alone: in hand, aimed at a boar, fired (flash, crack), a run of shots,
// and its locked blueprint in the craft panel's weapons tab.
// Usage: node rifle.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-rifle');
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);

(async () => {
  const browser = await chromium.launch({
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'],
  });
  const context = await browser.newContext({
    viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true,
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1',
  });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} [pageerror] ${e.message}`));
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const shot = async name => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const cmd = async (c, ms = 1500) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} [cmd] ${c}`); await wait(ms); };
  const hold = async (key, ms) => { await page.keyboard.down(key); await wait(ms); await page.keyboard.up(key); log.push(`${stamp()} [key] ${key} ${ms}ms`); await wait(600); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };
  const has = needle => log.some(l => l.includes(needle));

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 30000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-12', 1000);
  await cmd('give-guns', 1000);
  await cmd('rifle', 2000);
  await shot('01-rifle-held');
  await cmd('spawn-boar', 2500);
  await cmd('face-animal', 1500);
  await shot('02-rifle-aim');
  await cmd('fire', 0);
  await shot('03-rifle-fire');
  for (let i = 0; i < 3; i++) { await cmd('face-animal', 300); await cmd('fire', 400); }
  await shot('04-rifle-after');
  await cmd('stats', 600);
  await cmd('inventory', 2000);
  await shot('05-inventory');
  await cmd('inventory', 800);
  await cmd('craft-panel', 1200);
  await cmd('craft-tab-2', 1500);
  await shot('06-craft-weapons');
  await cmd('craft-panel', 800);

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|key|pageerror|error|warning)\]|Oxfall\] (Stats|Shot|Animal|Gun)|rror|xception/.test(l)).join('\n'));
  await browser.close();
})();
