// Locked crate, solo: one turns up in an outpost; it will not open; hacking it starts the clock and
// wakes the guards; when the time is up it opens and holds the rifle. PASS/FAIL lines.
// Usage: node crate.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-crate');
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
  const waitForLog = async (needle, ms, from = 0) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.slice(from).some(l => l.includes(needle))) return true; await wait(250); }
    return false;
  };
  const state = async () => { const i = log.length; await cmd('crate-state', 600); return log.slice(i).filter(l => l.includes('[Oxfall] Crate ')); };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  await waitForLog('item icons', 60000);
  await wait(2000);
  await cmd('quality-high', 2500);
  await cmd('time-12', 1000);
  await cmd('locked-crate', 1500);
  let s = await state();
  check('a locked crate turned up, shut', s.length === 1 && s[0].includes('hacked=False') && s[0].includes('open=False'));
  check('everyone is told where', log.some(l => l.includes('[Oxfall] Locked crate') && l.includes(': locked')));
  await cmd('view-crate', 3500);
  await cmd('freeze-animals', 300);
  await shot('01-crate-in-outpost');
  await cmd('go-crate', 3000);
  await shot('02-crate-locked');

  // It will not open before it is hacked.
  let mark = log.length;
  await cmd('open-crate', 1200);
  check('it does not open before the hack', !log.slice(mark).some(l => l.includes('[Oxfall] Opened box')));

  // The use button starts the hack; the guards hear it.
  await cmd('thaw-animals', 200);
  mark = log.length;
  await cmd('interact', 2000);
  check('the use button starts the hack', await waitForLog('Hack started on crate', 5000, mark));
  s = await state();
  check('the clock runs', s.length === 1 && s[0].includes('hacked=True') && s[0].includes('open=False'));
  mark = log.length;
  await cmd('bandits', 600);
  const bandits = log.slice(mark).filter(l => l.includes('[Oxfall] Bandit '));
  const near = bandits.filter(l => { const m = l.match(/\(([\d.]+) m\)/); return m && +m[1] < 60; });
  check('the guards near come for the hacker', near.length > 0 && near.every(l => l.includes('Chase') || l.includes('Dead')));
  await cmd('freeze-animals', 300);
  await cmd('heal', 200);
  await cmd('go-crate', 3500);
  await shot('03-crate-hacking');
  await cmd('view-crate', 3000);
  await shot('04-hack-countdown');

  // At the end of the clock it opens on its own.
  mark = log.length;
  await cmd('crate-done', 500);
  check('it opens when the time is up', await waitForLog(': open', 15000, mark));
  await cmd('go-crate', 3000);
  await shot('05-crate-open');
  mark = log.length;
  await cmd('interact', 2500);
  check('then it opens for whoever is there', await waitForLog('[Oxfall] Opened box', 5000, mark));
  await shot('06-military-loot');
  await cmd('close-box', 800);
  await cmd('map', 2500);
  await shot('07-map-pin');
  await cmd('map', 500);

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('[pageerror]') || l.includes('Exception')).join('\n'));
  console.log(results.join('\n'));
  await browser.close();
})();
