// Supply drops, solo or online: a plane called over, seen coming, the crate falling under its
// parachute, the red smoke where it lands, then walked up to, opened and emptied.
// Usage: node airdrops.js [outDir] [ws://server]   (against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-airdrops');
// Online: pass the server, e.g. ws://localhost:7777/ws (the game is on /ws).
const server = process.argv[3];
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

  const url = server ? `http://localhost:8080/index.html?server=${encodeURIComponent(server)}&name=Piloto` : 'http://localhost:8080/index.html?solo';
  await page.goto(url);
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  if (server) log.push(`${stamp()} [test] online: ${await waitForLog('[Oxfall] Online', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-12', 1500);

  // A plane called over, dropping a little ahead.
  await cmd('airdrop', 1500);
  log.push(`${stamp()} [test] plane: ${await waitForLog('Supply plane', 30000)}`);
  await wait(4000);
  await cmd('face-airdrop', 1200);
  await shot('01-plane-coming');
  await wait(5000);
  await cmd('face-airdrop', 1200);
  await shot('02-plane-over');
  await wait(9000);
  await cmd('face-airdrop', 1200);
  await shot('03-crate-falling');
  await wait(12000);
  await cmd('face-airdrop', 1200);
  await shot('04-crate-falling-2');
  log.push(`${stamp()} [test] landed: ${await waitForLog('Supply crate', 120000)}`);
  await wait(6000);
  await cmd('face-airdrop', 1500);
  await shot('05-smoke');

  // Walked up to, opened and emptied.
  await cmd('go-airdrop', 2500);
  await shot('06-crate');
  await cmd('open-airdrop', 2500);
  await shot('07-crate-open');
  await cmd('light', 2500);
  await shot('08-taken');
  await cmd('stats', 800);

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('Supply') || l.includes('Opened') || l.includes('Went up') || l.includes('[pageerror]') || l.includes('Exception') || l.includes('Unknown debug') || l.includes('stats')).join('\n'));
  await browser.close();
})();
