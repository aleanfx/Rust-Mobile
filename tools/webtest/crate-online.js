// Online: Ana sets a locked crate in front of her (test command), hacks it; the server's clock runs on
// her screen and, at the end, the server opens it and she loots it. PASS/FAIL lines.
// Needs a test server (OXFALL_DEV=1 dotnet run --project server/Oxfall.Server -- --port 7777) and the web build on :8080.
// Usage: node crate-online.js [outDir] [server]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-crate-online');
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
  const left = async () => {
    const i = log.length;
    await cmd('crate-state', 800);
    const l = log.slice(i).find(l => l.includes('[Oxfall] Crate '));
    return l ? { hacked: l.includes('hacked=True'), open: l.includes('open=True'), left: +(l.match(/left=([\d.]+)/) || [])[1] } : null;
  };

  await ana.goto(`http://localhost:8080/index.html?server=${encodeURIComponent(server)}&name=Ana`);
  check('Ana online', await waitForLog('Ana [log] [Oxfall] Online', 300000));
  await cmd('time-12');
  await cmd('crate-here', 2500);
  check('the server set the crate down and told her', await waitForLog(': locked', 10000));
  await cmd('go-crate', 2500);
  let mark = log.length;
  await cmd('open-crate', 2000);
  check('it will not open before the hack', !log.slice(mark).some(l => l.includes('[Oxfall] Opened box')));
  mark = log.length;
  await cmd('interact', 2500);
  check('the server started the hack', await waitForLog(': hacking', 10000, mark));
  const a = await left();
  await wait(4000);
  const b = await left();
  check('her screen counts down between the server\'s word', a && b && a.hacked && !b.open && b.left < a.left - 2 && b.left > 280);
  await cmd('go-crate', 2500);
  await ana.screenshot({ path: path.join(out, '01-online-hacking.png') });
  mark = log.length;
  await cmd('crate-done', 500);
  check('the server opened it', await waitForLog(': open', 15000, mark));
  mark = log.length;
  await cmd('interact', 2500);
  check('she loots it', await waitForLog('[Oxfall] Opened box', 6000, mark));
  await ana.screenshot({ path: path.join(out, '02-online-loot.png') });
  check('nothing wrong on Ana\'s side', !log.some(l => l.includes('Ana [pageerror]') || /Ana .*Exception/.test(l)));
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]')).join('\n'));
  console.log(results.join('\n'));
  await browser.close();
})();
