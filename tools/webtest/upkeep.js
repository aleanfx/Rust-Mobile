// Upkeep, alone: a stone hut with an empty tool cupboard decays; the cupboard's panel says so; stocked
// with wood and stone it keeps the hut up (PROTEGIDA, what it costs a day), and its materials open
// beside the inventory like a box, with PERMISOS back to who may build.
// Usage: node upkeep.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-upkeep');
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
  await cmd('give-all', 1500);
  await cmd('time-12', 1500);

  // A stone hut ahead, and a cupboard set down between it and the player.
  await cmd('build-hut', 1500);
  await cmd('upgrade-all-2', 2500);
  await cmd('cupboard', 1500);
  await cmd('deploy', 1800);
  await cmd('slot-1', 800);
  await cmd('upkeep', 600);

  // Nothing in the cupboard: a round of upkeep, and the hut decays.
  await cmd('upkeep-now', 2500);
  await cmd('upkeep', 600);
  await cmd('face-cupboard', 1500);
  await cmd('open-cupboard', 2000);
  await shot('01-cupboard-decaying');

  // Its materials: wood and stone in.
  await cmd('cupboard-materials', 2000);
  await shot('02-materials-empty');
  await cmd('put-materials', 2000);
  await shot('03-materials-stocked');
  await cmd('upkeep', 600);

  // Back to the cupboard's panel: protected now.
  await cmd('light', 2000);
  await shot('04-cupboard-protected');
  await cmd('upkeep-now', 2500);
  await cmd('upkeep', 600);
  await cmd('close-cupboard', 1000);
  await cmd('stats', 800);

  // Something that is not a material is refused.
  await cmd('cupboard-materials', 2000);
  await cmd('tap-0', 600);
  await cmd('across', 1500);
  await shot('05-refused');
  await cmd('close-box', 800);

  const lines = log.filter(l => l.includes('[Oxfall] Upkeep:'));
  log.push(`${stamp()} [test] decaying first: ${lines.some(l => l.includes('SE DETERIORA'))} protected later: ${lines.some(l => l.includes('PROTEGIDA'))}`);
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|key|pageerror|error|warning)\]|Oxfall\] (Stats|Placed|Upkeep|Put|Opened)|rror|xception/.test(l)).join('\n'));
  await browser.close();
})();
