// Online: Ana calls the patrol helicopter (test command); the server's gunship comes, hunts and fires at
// her; her rifle hurts it; shot down, everyone is told, and three burning crates cool and open for her.
// Run tools/netbot alongside to see a second player hear it all. PASS/FAIL lines.
// Needs a test server (OXFALL_DEV=1 dotnet run --project server/Oxfall.Server -- --port 7777) and the web build on :8080.
// Usage: node heli-online.js [outDir] [server]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-heli-online');
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
  const waitForLog = async (needle, ms, from = 0) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.slice(from).some(l => l.includes(needle))) return true; await wait(400); }
    return false;
  };
  const context = await browser.newContext({ viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const ana = await context.newPage();
  ana.on('console', m => log.push(`${stamp()} Ana [${m.type()}] ${m.text()}`));
  ana.on('pageerror', e => log.push(`${stamp()} Ana [pageerror] ${e.message}`));
  const cmd = async (c, ms = 1500) => { await ana.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} Ana [cmd] ${c}`); await wait(ms); };
  const heli = async () => {
    const i = log.length;
    await cmd('heli-state', 900);
    const lines = log.slice(i);
    const h = lines.find(l => l.includes('[Oxfall] Heli ') && l.includes('health='));
    return {
      state: h ? (h.match(/Heli \d+ (\w+)/) || [])[1] : null,
      health: h ? +(h.match(/health=(\d+)/) || [])[1] : null,
      crates: lines.filter(l => l.includes('[Oxfall] Heli crate')),
      mine: (() => { const m = lines.find(l => l.includes('Heli count=')); return m ? +(m.match(/ health=(\d+)/) || [])[1] : null; })(),
    };
  };
  const until = async (pred, ms) => { const end = Date.now() + ms; let s; while (Date.now() < end) { s = await heli(); if (pred(s)) return s; await wait(1500); } return s; };

  await ana.goto(`http://localhost:8080/index.html?server=${encodeURIComponent(server)}&name=Ana`);
  check('Ana online', await waitForLog('Ana [log] [Oxfall] Online', 300000));
  await cmd('time-12');
  await cmd('give-guns', 1200);
  await cmd('give-armour', 1200);
  await cmd('wear-armour', 1200);
  await cmd('rifle', 1500);
  let mark = log.length;
  await cmd('heli-here', 2000);
  check('the server sent the helicopter and she was told', await waitForLog('Patrol helicopter 1 came in', 15000, mark));
  let s = await until(s => s.state === 'Engage', 90000);
  check('it hunts her', s.state === 'Engage');
  // She fires back at once, before it wears her down.
  const h0 = s.health;
  for (let i = 0; i < 6; i++) await cmd('shoot-heli', 700);
  await ana.screenshot({ path: path.join(out, '01-online-heli.png') });
  await wait(1500);
  const after = await heli();
  const h1 = after.health;
  check('its rounds reach her', after.mine !== null && after.mine < 100);
  check('the server took her rifle rounds', h1 < h0);
  mark = log.length;
  await cmd('heli-down', 1500);
  check('everyone is told it went down', await waitForLog('derribó el helicóptero', 10000, mark) || await waitForLog('helicóptero de patrulla cayó', 1000, mark));
  s = await until(s => s.state === 'Wreck', 60000);
  check('it lies burning, three hot crates round it', s.state === 'Wreck' && s.crates.length === 3 && s.crates.every(l => l.includes('open=False')));
  await cmd('go-wreck', 3500);
  await ana.screenshot({ path: path.join(out, '02-online-wreck.png') });
  s = await until(s => s.crates.length === 3 && s.crates.every(l => l.includes('open=True')), 150000);
  check('the server cools them and they open', s.crates.length === 3 && s.crates.every(l => l.includes('open=True')));
  mark = log.length;
  await cmd('open-heli-crate', 3000);
  check('she loots one', await waitForLog('[Oxfall] Opened box', 6000, mark));
  await ana.screenshot({ path: path.join(out, '03-online-loot.png') });
  check('nothing wrong on Ana\'s side', !log.some(l => l.includes('Ana [pageerror]') || /Ana .*Exception/.test(l)));
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]')).join('\n'));
  console.log(results.join('\n'));
  await browser.close();
})();
