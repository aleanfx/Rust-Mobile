// Screenshots of the hotbar, the inventory panel and each tool in the hand.
// Usage: node inventory.js [outDir]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-inventory');
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
  const cmd = async (c, ms = 2000) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} [cmd] ${c}`); await wait(ms); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 30000)}`);
  await wait(2000);
  await cmd('quality-high', 2500);
  await cmd('view-forest', 3000);
  await shot('1-rock');
  await cmd('give-all', 1500);
  await cmd('slot-3', 2000);
  await shot('2-hatchet');
  await cmd('slot-4', 2000);
  await shot('3-pickaxe');
  await cmd('slot-2', 2000);
  await shot('4-torch');
  await cmd('nearest-tree', 2500);
  await cmd('slot-5', 2500);
  await shot('5-building-plan');
  await cmd('slot-5', 1000);
  await cmd('inventory', 2500);
  await shot('6-inventory');
  await cmd('tap-6', 1000);
  await shot('7-picked');
  await cmd('tap-5', 1500);
  await shot('8-moved');
  // A real tap on the first hotbar slot, to check touch input reaches the hotbar.
  await cmd('inventory', 1500);
  await page.touchscreen.tap(844 / 2 - (758 / 2 - 59) * (390 / 1080), 390 - (16 + 59) * (390 / 1080));
  await wait(1500);
  await cmd('stats', 1000);
  await shot('9-after-tap');
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error|warning)\]|Oxfall\] (Stats|Rendered|Icon|Built|Crafted)|rror|xception/.test(l)).join('\n'));
  await browser.close();
})();
