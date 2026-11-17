// Online: one player's blows on a tree and their reload reach another player nearby. Ana plays in the
// browser; Beto is the bare test player (tools/netbot), standing on the same beach, printing what he hears.
// Needs a test server (OXFALL_DEV=1 dotnet run --project server/Oxfall.Server -- --port 7777), the web
// build on :8080 and tools/netbot built (dotnet build tools/netbot).
// Usage: node swing.js [outDir] [server]
const { chromium } = require('playwright');
const { spawn } = require('child_process');
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

(async () => {
  const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'] });
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };
  const context = await browser.newContext({ viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const ana = await context.newPage();
  ana.on('console', m => log.push(`${stamp()} Ana [${m.type()}] ${m.text()}`));
  ana.on('pageerror', e => log.push(`${stamp()} Ana [pageerror] ${e.message}`));
  const cmd = async c => { await ana.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} Ana [cmd] ${c}`); await wait(1500); };

  await ana.goto(`http://localhost:8080/index.html?server=${encodeURIComponent(server)}&name=Ana`);
  check('Ana online', await waitForLog('Ana [log] [Oxfall] Online', 300000));
  const id = (log.find(l => l.includes('Online as player')) || '').match(/player (\d+)/)?.[1];
  const bot = spawn('dotnet', [path.join(__dirname, '../netbot/bin/Debug/net8.0/netbot.dll'), server, 'Beto', '400']);
  bot.stdout.on('data', d => String(d).split('\n').filter(Boolean).forEach(l => log.push(`${stamp()} Beto ${l}`)));
  check('Beto in', await waitForLog('Beto [bot] Welcome', 30000));

  await cmd('time-12');
  await cmd('nearest-tree');
  for (let i = 0; i < 3; i++) await cmd('use');
  check('Beto hears Ana chop wood', await waitForLog(`Beto [bot] Swung ${id} Wood`, 20000));
  await cmd('nearest-stone');
  await cmd('use');
  await cmd('use');
  check('Beto hears Ana hit stone', await waitForLog(`Beto [bot] Swung ${id} Stone`, 20000));
  await cmd('give-guns');
  await cmd('revolver');
  await cmd('fire');
  check('Beto hears the shot', await waitForLog(`Beto [bot] Gunshot ${id}`, 20000));
  await cmd('reload');
  check('Beto hears Ana reload', await waitForLog(`Beto [bot] Swung ${id} Reload`, 20000));
  check('nothing wrong on Ana\'s side', !log.some(l => l.includes('Ana [pageerror]') || /Ana .*Exception/.test(l)));
  bot.kill();
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('Beto [bot]')).join('\n'));
  console.log(results.join('\n'));
  await browser.close();
})();
