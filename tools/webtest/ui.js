// The interface: HUD by day with pickups, crafting panel and queue chip, inventory, settings,
// low vitals and the death screen. Usage: node ui.js [outDir] [query]   (query e.g. "?server=...")
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-ui');
const query = process.argv[3] || '';
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

  await page.goto('http://localhost:8080/index.html' + query);
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 30000)}`);
  await wait(2000);
  await cmd('quality-high', 2500);
  await shot('1-hud-start');
  await cmd('nearest-tree', 1500);
  await cmd('use', 700);
  await cmd('use', 700);
  await cmd('use', 1200);
  await shot('2-pickups');
  await cmd('give-all', 1500);
  await cmd('craft-panel', 2500);
  await shot('3-craft-panel');
  await cmd('craft-hatchet', 1500);
  await cmd('craft-pickaxe', 1500);
  await shot('4-craft-queue');
  await cmd('craft-panel', 2500);
  await shot('5-crafting-chip');
  await cmd('inventory', 2500);
  await cmd('tap-1', 1500);
  await shot('6-inventory');
  await cmd('inventory', 1000);
  await cmd('settings', 2000);
  await shot('7-settings');
  await cmd('settings', 1000);
  await cmd('hungry', 1500);
  await cmd('time-22', 2500);
  await shot('8-night-hungry');
  await cmd('starve', 3000);
  await shot('9-dead');
  await cmd('stats', 800);
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error)\]|Oxfall\] (Stats|Gathered|Crafted|Online|Offline)|xception/.test(l)).join('\n'));
  await browser.close();
})();
