// The bandits, solo: in sight of the nearest one (he notices, raises his revolver and fires), then
// shot back with a revolver until he falls, and the bag he drops opened.
// Usage: node bandits.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-bandits');
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
  });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} [pageerror] ${e.message}`));
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const shot = async name => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const cmd = async (c, ms = 2000) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} [cmd] ${c}`); await wait(ms); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(250); }
    return false;
  };
  const count = needle => log.filter(l => l.includes(needle)).length;

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-12', 1500);
  await cmd('give-guns', 1500);
  await cmd('revolver', 1500);

  // In sight of one: he turns, raises his revolver and fires.
  await cmd('to-bandit', 2500);
  await cmd('animals', 800);
  await shot('01-bandit-in-sight');
  log.push(`${stamp()} [test] shot at: ${await waitForLog('Bitten by Bandit', 60000)}`);
  await shot('02-bandit-shooting');
  await wait(1200);
  await shot('03-bandit-shooting-2');
  await cmd('heal', 500);

  // Shot back until he falls.
  let down = false;
  for (let i = 0; i < 18 && !down; i++) {
    await cmd('face-animal', 600);
    await cmd('fire', 1400);
    if (i % 3 === 2) await cmd('heal', 300);
    if (i % 6 === 5) await cmd('reload', 3000);
    down = count('Animal hit: Bandit') > 0 && log.some(l => l.includes('Animal hit: Bandit') && l.includes('Killed'));
  }
  log.push(`${stamp()} [test] hits on him: ${count('Animal hit: Bandit')}, down: ${down}`);
  await wait(1500);
  await shot('04-bandit-down');
  await cmd('go-bag', 2500);
  await shot('05-his-bag');
  await cmd('open-bag', 2500);
  await shot('06-bag-open');
  await cmd('stats', 800);

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('Bandit') || l.includes('bandit') || l.includes('Animal ') || l.includes('[pageerror]') || l.includes('Exception') || l.includes('Unknown debug')).join('\n'));
  await browser.close();
})();
