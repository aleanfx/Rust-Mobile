// Online: one player's blows on a tree are seen and heard by another nearby (their tool swings, the
// chop carries). Needs a test server (OXFALL_DEV=1 dotnet run --project server/Oxfall.Server -- --port 7777)
// and the web build on :8080.
// Usage: node swing.js [outDir] [server]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-swing');
const server = process.argv[3] || 'ws://localhost:7777/ws';
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);
const wait = ms => new Promise(r => setTimeout(r, ms));
const results = [];
const check = (name, ok) => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}`); log.push(`${stamp()} [test] ${ok ? 'PASS' : 'FAIL'} ${name}`); };

async function open(browser, name) {
  const context = await browser.newContext({ viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} ${name} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} ${name} [pageerror] ${e.message}`));
  await page.goto(`http://localhost:8080/index.html?server=${encodeURIComponent(server)}&name=${name}`);
  return page;
}

(async () => {
  const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'] });
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };
  const cmd = async (page, who, c, ms = 1500) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} ${who} [cmd] ${c}`); await wait(ms); };
  const shot = async (page, name) => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };

  const ana = await open(browser, 'Ana');
  check('Ana online', await waitForLog('Ana [log] [Oxfall] Online', 240000));
  const beto = await open(browser, 'Beto');
  check('Beto online', await waitForLog('Beto [log] [Oxfall] Online', 240000));
  await wait(3000);
  await cmd(ana, 'Ana', 'time-12', 800);
  await cmd(ana, 'Ana', 'nearest-tree', 2500);
  await cmd(beto, 'Beto', 'nearest-tree', 2500);
  // Beto steps back to watch.
  await beto.keyboard.down('s'); await wait(1500); await beto.keyboard.up('s'); await wait(800);
  await cmd(beto, 'Beto', 'face-player', 2000);
  for (let i = 0; i < 4; i++) {
    await cmd(ana, 'Ana', 'use', i === 1 ? 250 : 1300);
    if (i === 1) await shot(beto, '01-beto-sees-ana-chop');
  }
  check('Beto sees Ana swing at wood', await waitForLog('Beto [log] [Oxfall] Saw Ana swing: Wood', 10000));
  check('Ana does not get her own swing back', !log.some(l => l.includes('Ana [log] [Oxfall] Saw Ana')));
  await shot(beto, '02-beto-after');
  // Ana fires and reloads: Beto hears and sees the reload.
  await cmd(ana, 'Ana', 'give-guns', 1500);
  await cmd(ana, 'Ana', 'revolver', 1500);
  await cmd(ana, 'Ana', 'fire', 1200);
  await cmd(ana, 'Ana', 'reload', 300);
  await shot(beto, '03-beto-sees-ana-reload');
  check('Beto sees Ana reload', await waitForLog('Beto [log] [Oxfall] Saw Ana swing: Reload', 10000));
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('pageerror') || l.includes('Exception')).join('\n'));
  console.log(results.join('\n'));
  await browser.close();
})();
