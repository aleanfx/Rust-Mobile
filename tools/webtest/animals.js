// The island's animals, alone: a deer that bolts, a boar rooting about, a wolf that charges and bites,
// killing it with a spear, cutting up the carcass with a hatchet for meat and leather, then raw meat
// on a campfire and a bite of it.
// Usage: node animals.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-animals');
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
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };
  const has = needle => log.some(l => l.includes(needle));
  const count = needle => log.filter(l => l.includes(needle)).length;

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 30000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('give-all', 1500);
  await cmd('time-12', 1500);
  await cmd('animals', 800);
  await cmd('stats', 800);

  // Inland, on open grass, with no animals but the ones the test puts down.
  await cmd('nearest-hemp', 2000);
  await cmd('clear-animals', 800);
  await cmd('slot-1', 600);

  // A deer: it grazes, then bolts when walked up to.
  await cmd('spawn-deer', 1800);
  await cmd('face-animal', 1500);
  await shot('01-deer');
  await cmd('approach-animal', 300);
  await wait(1200);
  await cmd('face-animal', 400);
  await shot('02-deer-bolts');
  await cmd('clear-animals', 800);

  // A boar: left alone it keeps to itself.
  await cmd('spawn-boar', 1800);
  await cmd('face-animal', 1500);
  await shot('03-boar');
  await cmd('clear-animals', 800);

  // A wolf: it comes and bites.
  await cmd('spawn-wolf', 600);
  await cmd('face-animal', 600);
  await shot('04-wolf-charges');
  await wait(2500);
  await cmd('face-animal', 400);
  await shot('05-wolf-bites');
  await cmd('stats', 600);

  // The spear: three or four blows.
  await cmd('spear', 1200);
  for (let i = 0; i < 6 && !has('Killed'); i++) {
    await cmd('face-animal', 250);
    await cmd('use', 900);
    if (i === 0) await shot('06-spear-hit');
  }
  await wait(1200);
  await cmd('face-animal', 800);
  await shot('07-wolf-dead');
  await cmd('stats', 600);

  // Cut up with the hatchet.
  await cmd('hatchet', 1200);
  for (let i = 0; i < 10 && !has('UsedUp'); i++) {
    await cmd('approach-animal', 250);
    await cmd('use', 950);
    if (i === 1) await shot('08-cutting');
  }
  await shot('09-carcass-done');
  await cmd('stats', 600);

  // Raw meat on a campfire.
  await cmd('campfire', 1200);
  await cmd('deploy', 2000);
  await cmd('face-fire', 1200);
  await shot('10-campfire');
  await cmd('interact', 1800);
  await cmd('put-meat', 1500);
  await shot('11-fire-panel');
  await wait(11000);
  await shot('12-roasting');
  await cmd('close-box', 800);

  // A bite of raw meat: food, but thirsty work.
  await cmd('raw-meat', 1200);
  await shot('13-meat-in-hand');
  await cmd('use', 1200);
  await shot('14-ate');
  await cmd('stats', 600);

  log.push(`${stamp()} [test] bitten: ${count('te muerde') + count('Bitten')} hurt-hits: ${count('Animal hit: Wolf') } killed: ${has(' Killed ')} cut: ${count(' Cut ')} usedup: ${has('UsedUp')}`);
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error)\]|Oxfall\] (Stats|Animal|Facing|Put meat|Gathered|The player)|xception/.test(l)).join('\n'));
  await browser.close();
})();
