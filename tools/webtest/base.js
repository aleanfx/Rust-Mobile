// A base worth defending: place a box, furnace, tool cupboard and sleeping bag; fill the box,
// smelt ore, authorize, die and wake up in the bag; try to pick up a full box with the hammer.
// Usage: node base.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-base');
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
  // Walks sideways or back by holding a key (the web build reads the keyboard when no finger is down).
  const hold = async (key, ms) => { await page.keyboard.down(key); await wait(ms); await page.keyboard.up(key); log.push(`${stamp()} [key] ${key} ${ms}ms`); await wait(600); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };
  const count = needle => log.filter(l => l.includes(needle)).length;

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 30000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('give-all', 1500);
  await cmd('time-16', 1500);

  // A row of things set down ahead, stepping sideways between them.
  await cmd('box', 1800);
  await shot('01-box-ghost');
  await cmd('deploy', 1500);
  await hold('d', 420);
  await cmd('slot-1', 600);
  await cmd('furnace', 1500);
  await shot('02-furnace-ghost');
  await cmd('deploy', 1500);
  await hold('d', 420);
  await cmd('slot-1', 600);
  await cmd('cupboard', 1500);
  await cmd('deploy', 1500);
  await hold('a', 1300);
  await cmd('slot-1', 600);
  await cmd('sleeping-bag', 1500);
  await cmd('deploy', 1500);
  log.push(`${stamp()} [test] placed: ${count('[Oxfall] Placed')}`);
  await cmd('slot-1', 800);
  await hold('s', 650);
  await hold('d', 450);
  await shot('03-base');
  await cmd('stats', 800);

  // The box: stone in it.
  await cmd('open-box', 1800);
  await shot('04-box-open');
  await cmd('tap-14', 600);
  await cmd('across', 1500);
  await shot('05-box-filled');
  await cmd('close-box', 1000);

  // The furnace: wood and ore in, lit.
  await cmd('open-furnace', 1800);
  await cmd('tap-13', 600);
  await cmd('across', 900);
  await cmd('tap-15', 600);
  await cmd('across', 900);
  await cmd('light', 1500);
  await shot('06-furnace-lit');
  await cmd('close-box', 800);
  await cmd('face-furnace', 2500);
  await shot('07-furnace-fire');
  await wait(9000);
  await cmd('open-furnace', 1800);
  await shot('08-furnace-smelted');
  await cmd('close-box', 800);

  // The cupboard.
  await cmd('face-cupboard', 1500);
  await shot('09-cupboard-look');
  await cmd('open-cupboard', 1500);
  await shot('10-cupboard');

  // Death, then waking up in the bag.
  await cmd('slot-1', 600);
  await cmd('starve', 3500);
  await shot('11-death-bags');
  await cmd('bag-1', 3000);
  await shot('12-woke-in-bag');
  await cmd('stats', 800);

  // The hammer on a full box: it must stay.
  await cmd('hammer', 1200);
  await cmd('face-box', 1800);
  await shot('13-hammer-box');
  await cmd('demolish', 1500);
  await shot('14-box-full');
  await cmd('stats', 800);

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|key|pageerror|error|warning)\]|Oxfall\] (Stats|Placed|Picked|Opened|Woke|Bag|Door)|rror|xception/.test(l)).join('\n'));
  await browser.close();
})();
