// The ruins, solo: the nearest monument seen from its middle, a supply crate opened with the use
// button (scrap, parts and a find into the inventory), the map with the ruins on it, and the
// workbench: set down, its MESA button opening the craft panel, a revolver made beside it; and the
// recycler turning the parts carried into scrap.
// Usage: node ruins.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-ruins');
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

  // The ruins, from their middle; then from a little way back to see them whole.
  await cmd('to-ruins', 3500);
  await shot('01-ruins-inside');
  await cmd('turn-180', 2500);
  await shot('02-ruins-other-way');
  await cmd('to-ruins', 1500);
  await cmd('back-16', 1500);
  await cmd('look-8', 2500);
  await shot('03-ruins-from-outside');

  // A crate: walk up to it and open it.
  await cmd('to-ruins', 1500);
  await cmd('nearest-crate', 2500);
  await shot('04-crate-in-sight');
  const before = count('[Oxfall] Gathered');
  await cmd('interact', 2500);
  log.push(`${stamp()} [test] crate gave ${count('[Oxfall] Gathered') - before} kinds of things`);
  await shot('05-crate-opened');
  await cmd('stats', 800);

  // The map, with the ruins marked.
  await cmd('map', 2500);
  await shot('06-map-ruins');
  await cmd('map', 800);

  // The workbench: set down, then its button opens the panel on the weapons tab; a revolver needs it.
  await cmd('give-metal', 1500);
  await cmd('craft-panel', 1500);
  await cmd('craft-tab-2', 2000);
  await shot('07-craft-no-bench');
  await cmd('craft-panel', 800);
  await cmd('workbench', 1500);
  await cmd('look--20', 1200);
  await shot('08-placing-bench');
  await cmd('deploy', 2000);
  await cmd('face-workbench', 1500);
  await cmd('interact', 2500);
  await shot('09-bench-craft-panel');
  await cmd('craft-revolver', 1500);
  await cmd('stats', 800);
  await cmd('craft-panel', 800);
  await cmd('look-0', 1500);
  await shot('10-bench');

  // The recycler: walk up to it, put the parts in, switch it on, and watch scrap come out.
  await cmd('to-ruins', 1500);
  await cmd('to-recycler', 3000);
  await shot('11-recycler');
  await cmd('give-metal', 1500);
  await cmd('interact', 2500);
  await shot('12-recycler-panel');
  await cmd('put-parts', 2000);
  await cmd('light', 2500);
  await shot('13-recycler-running');
  await wait(14000);
  await shot('14-recycler-scrap');
  await cmd('stats', 800);
  await cmd('close-box', 800);
  await cmd('inventory', 800);
  await shot('15-recycler-machine');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('Gathered') || l.includes('At the ruins') || l.includes('Craft') || l.includes('Put ') || l.includes('recycler') || l.includes('[pageerror]') || l.includes('Exception') || l.includes('Unknown debug')).join('\n'));
  await browser.close();
})();
