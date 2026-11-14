// The map marker: tapped on the island, shown on the map and the compass, tapped again to remove;
// then the crafting panel's three rows and the metal armour.
// Usage: node markers.js [outDir]   (solo, against the build on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-markers');
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

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await cmd('time-11', 1500);
  await cmd('view-hill', 3000);

  // Canvas units (1080 high) to screen pixels; the island's middle sits 6 units below the screen's.
  const W = 844, H = 390, s = H / 1080;
  const tapMap = async (label, x, y) => {
    await page.touchscreen.tap(W / 2 + x * s, H / 2 + 6 * s - y * s);
    log.push(`${stamp()} [tap] ${label}`);
    await wait(1800);
  };

  await cmd('map', 3000);
  await shot('01-map-open');
  // 100 units right and 80 up of the middle: about (47, 37) on the island.
  await tapMap('mark', 100, 80);
  log.push(`${stamp()} [test] marked: ${await waitForLog('[Oxfall] Marker: 4', 5000)}`);
  await shot('02-map-marked');
  // On the pin: it goes.
  await tapMap('unmark', 100, 104);
  log.push(`${stamp()} [test] removed: ${await waitForLog('[Oxfall] Marker: none', 5000)}`);
  await shot('03-map-unmarked');
  // Marked again, farther off, then a tap beside the island closes the map.
  await tapMap('mark again', -240, -160);
  log.push(`${stamp()} [test] marked again: ${await waitForLog('[Oxfall] Marker: -1', 5000)}`);
  await shot('04-map-marked-again');
  await tapMap('close', 1000, 0);
  log.push(`${stamp()} [test] closed: map stays shut after ${'close'}`);
  await shot('05-compass');
  await cmd('face-marker', 2500);
  await shot('06-compass-facing');
  await cmd('turn-150', 2500);
  await shot('07-compass-behind');

  // The crafting panel: weapons (three rows now) and clothing with the metal armour to learn.
  await cmd('craft-panel', 1500);
  await cmd('craft-tab-2', 3000);
  await shot('08-craft-weapons');
  await cmd('craft-tab-3', 2500);
  await shot('09-craft-clothing');
  await cmd('craft-panel', 1000);
  // The armour worn, in the inventory.
  await cmd('give-armour', 1000);
  await cmd('wear-armour', 1500);
  await cmd('inventory', 2500);
  await shot('10-armour-worn');
  await cmd('inventory', 800);
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('Marker') || l.includes('[pageerror]') || l.includes('Exception') || l.includes('Unknown debug')).join('\n'));
  await browser.close();
})();
