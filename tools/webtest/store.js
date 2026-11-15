// The skin store, alone: the store with the welcome coins, test coins, buying the gold rifle (it
// goes on at once), the rifle in hand and on the hotbar in gold, a shirt bought and worn, then a
// skin taken off; and the fog, thin and thick.
// Usage: node store.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-store');
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
  log.push(`${stamp()} [test] skin icons: ${await (async () => { const end = Date.now() + 90000; while (Date.now() < end) { if (log.filter(l => l.includes('item icons')).length >= 2) return true; await wait(500); } return false; })()}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-12', 1000);
  await cmd('skins', 600);
  await cmd('store', 2500);
  await shot('01-store-welcome');
  await cmd('store-select:rifle_gold', 1200);
  await shot('02-store-too-dear');
  await cmd('give-coins', 1200);
  await cmd('store-act', 1500);
  await shot('03-store-bought-gold');
  await cmd('store-select:shirt_midnight', 800);
  await cmd('store-act', 1200);
  await cmd('skins', 600);
  await cmd('store', 1000);
  await cmd('give-guns', 1000);
  await cmd('rifle', 2500);
  await shot('04-gold-rifle-held');
  await cmd('inventory', 2000);
  await shot('05-inventory-gold');
  await cmd('inventory', 800);
  // Taking it off again.
  await cmd('store', 1500);
  await cmd('store-select:rifle_gold', 800);
  await cmd('store-act', 1500);
  await shot('06-store-taken-off');
  await cmd('store', 1500);
  await shot('07-plain-rifle-held');
  await cmd('skins', 600);
  // The fog: clear, then thick.
  await cmd('view-hill', 2500);
  await cmd('fog-0', 4000);
  await shot('08-fog-none');
  await cmd('fog-1', 9000);
  await shot('09-fog-full');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error)\]|Oxfall\] (Skins|Store)|rror|xception/.test(l)).join('\n'));
  await browser.close();
})();
