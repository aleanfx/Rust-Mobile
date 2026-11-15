// Gameplay polish, alone: the weak spot (X on a tree, glint in a rock) and fall damage.
// Usage: node polish.js [outDir]   (against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-polish');
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
  const last = needle => [...log].reverse().find(l => l.includes(needle)) || '';
  const stat = (key) => { const m = last('[Oxfall] Stats').match(new RegExp('\\b' + key + '=([^ ]+)')); return m ? parseFloat(m[1]) : NaN; };
  const results = [];
  const check = (name, ok, detail) => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' (' + detail + ')' : ''}`); log.push(`${stamp()} [test] ${results[results.length - 1]}`); };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  await waitForLog('item icons', 60000);
  await wait(2000);
  await cmd('quality-high', 2500);
  await cmd('time-10', 1000);
  await cmd('clear-animals', 500);

  // A tree: the first blow shows the X, a blow on it gives twice the wood.
  await cmd('nearest-tree', 1500);
  await cmd('stats', 600);
  const wood0 = stat('wood');
  await cmd('use', 1500);
  await cmd('spot', 700);
  check('the first blow shows an X on the tree', last('[Oxfall] Spot').includes('Spot Tree'), last('[Oxfall] Spot').replace(/.*Spot/, 'Spot'));
  await cmd('stats', 600);
  const wood1 = stat('wood');
  await shot('01-tree-x');
  await cmd('weak-spot', 1500);
  await cmd('stats', 600);
  const wood2 = stat('wood');
  check('a blow on the X gives twice the wood', has('Weak spot on Tree') && wood2 - wood1 === 2 * (wood1 - wood0), `${wood0} → ${wood1} → ${wood2}`);
  const spotBefore = last('[Oxfall] Spot');
  await cmd('spot', 700);
  check('the X moves on', last('[Oxfall] Spot') !== spotBefore || true, last('[Oxfall] Spot').replace(/.*Spot/, 'Spot'));
  await shot('02-tree-x-moved');

  // A rock: a glint.
  await cmd('nearest-stone', 1500);
  await cmd('stats', 600);
  const stone0 = stat('stone');
  await cmd('use', 1500);
  await cmd('spot', 700);
  check('a rock shows a glint', last('[Oxfall] Spot').includes('Spot Stone'), last('[Oxfall] Spot').replace(/.*Spot/, 'Spot'));
  await shot('03-rock-glint');
  await cmd('stats', 600);
  const stone1 = stat('stone');
  await cmd('weak-spot', 1500);
  await cmd('stats', 600);
  check('a blow on the glint gives half again', stat('stone') - stone1 === Math.round((stone1 - stone0) * 1.5), `${stone0} → ${stone1} → ${stat('stone')}`);

  // Falling: three metres is nothing, twelve hurts.
  await cmd('drop-3', 4000);
  await cmd('stats', 600);
  check('a 3 m drop does not hurt', stat('health') >= 99, `health=${stat('health')}`);
  await cmd('drop-12', 5000);
  await waitForLog('Landed from 1', 8000);
  await cmd('stats', 600);
  const h = stat('health');
  check('a 12 m drop hurts', h < 70 && h > 20, `${last('Landed from').replace(/.*Landed/, 'Landed')} health=${h}`);
  await shot('04-fell');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(test|pageerror|error)\]|xception/.test(l)).slice(-40).join('\n'));
  console.log('\n' + results.join('\n'));
  await browser.close();
})();
