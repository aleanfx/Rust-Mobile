// Wolf packs, solo: a pack of three ahead; walking towards them, the first notices and howls, all
// three come, from different sides, and bite. PASS/FAIL lines.
// Usage: node wolves.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-wolves');
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);
const results = [];
const check = (name, ok) => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}`); log.push(`${stamp()} [test] ${ok ? 'PASS' : 'FAIL'} ${name}`); };

(async () => {
  const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'] });
  const context = await browser.newContext({ viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} [pageerror] ${e.message}`));
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const shot = async name => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const cmd = async (c, ms = 1500) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} [cmd] ${c}`); await wait(ms); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(250); }
    return false;
  };
  const wolves = async () => { const i = log.length; await cmd('wolves', 500); return log.slice(i).filter(l => l.includes('[Oxfall] Wolf ') && l.includes('pack=')); };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  await waitForLog('item icons', 60000);
  await wait(2000);
  await cmd('quality-high', 2500);
  await cmd('time-12', 1000);
  await cmd('clear-animals', 500);
  await cmd('view-hill', 2000);
  await cmd('wolf-pack', 2500);
  let list = await wolves();
  check('three wolves of one pack, not yet after anyone', list.length === 3 && list.every(l => !l.includes('Chase')));
  await cmd('face-animal', 2000);
  await shot('01-pack-ahead');

  // Walk at them until they notice.
  let chasing = [];
  for (let i = 0; i < 12 && chasing.length < 3; i++) {
    await page.keyboard.down('w'); await wait(700); await page.keyboard.up('w');
    await cmd('face-animal', 300);
    chasing = (await wolves()).filter(l => l.includes('Chase'));
  }
  check('all three come for this player', chasing.length === 3);
  check('the first howled to the pack', await waitForLog('Wolf howled', 15000));
  check('the pack was called', (await wolves()).some(l => /calls=[1-9]/.test(l)));
  await cmd('heal', 200);
  // As they close in: frozen for a picture.
  for (let i = 0; i < 20; i++) {
    const near = (await wolves()).filter(l => { const m = l.match(/\(([\d.]+) m\)/); return m && +m[1] < 8; });
    if (near.length >= 2) break;
  }
  await cmd('freeze-animals', 300);
  await cmd('face-animal', 2500);
  await shot('02-pack-closing-in');
  await cmd('thaw-animals', 300);
  check('they bite', await waitForLog('Bitten by Wolf', 30000));
  await cmd('heal', 300);
  await shot('03-pack-on-you');
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('[pageerror]') || l.includes('Exception')).join('\n'));
  console.log(results.join('\n'));
  await browser.close();
})();
