// Teams, with a server: Ana walks up to Beto and asks him in (the use button reads INVITAR); Beto
// sees the invitation card and accepts; both see the other's name in green, the team panel, each
// other on the map, and a line said to the team alone. Then Beto leaves.
// Usage: node teams.js [outDir] server   (the build on :8080; a test server)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-teams');
const server = process.argv[3] || 'ws://localhost:7777/ws';
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);
const wait = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const browser = await chromium.launch({
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'],
  });
  const open = async (name, query) => {
    const context = await browser.newContext({ viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
    const page = await context.newPage();
    page.on('console', m => log.push(`${stamp()} ${name} [${m.type()}] ${m.text()}`));
    page.on('pageerror', e => log.push(`${stamp()} ${name} [pageerror] ${e.message}`));
    await page.goto(`http://localhost:8080/index.html?${query}`);
    return page;
  };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(250); }
    return false;
  };
  const cmd = async (page, who, c, ms = 1500) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} ${who} [cmd] ${c}`); await wait(ms); };
  const shot = async (page, name) => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const focus = page => page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });

  const q = name => `server=${encodeURIComponent(server)}&name=${name}`;
  const ana = await open('Ana', q('Ana'));
  log.push(`${stamp()} [test] Ana online: ${await waitForLog('Ana [log] [Oxfall] Online', 240000)}`);
  const beto = await open('Beto', q('Beto'));
  let betoOnline = await waitForLog('Beto [log] [Oxfall] Online', 240000);
  if (!betoOnline) {
    await cmd(beto, 'Beto', 'reconnect', 1000);
    betoOnline = await waitForLog('Beto [log] [Oxfall] Online', 120000);
  }
  log.push(`${stamp()} [test] Beto online: ${betoOnline}`);
  await wait(3000);
  await focus(ana);
  await focus(beto);
  await cmd(ana, 'Ana', 'quality-high', 1500);
  await cmd(beto, 'Beto', 'quality-high', 1500);
  await cmd(ana, 'Ana', 'time-12', 800);

  // Ana goes up to Beto: the use button asks him in.
  await cmd(ana, 'Ana', 'near-player', 3000);
  await cmd(beto, 'Beto', 'face-player', 2000);
  await shot(ana, '01-ana-invitar');
  await cmd(ana, 'Ana', 'team-invite', 2500);
  log.push(`${stamp()} [test] Beto invited: ${await waitForLog('Beto [log] [Oxfall] Team invitation from Ana', 20000)}`);
  await shot(beto, '02-beto-invitation');
  await cmd(beto, 'Beto', 'team-accept', 3000);
  log.push(`${stamp()} [test] Beto in a team: ${await waitForLog('Beto [log] [Oxfall] Team ', 20000)}`);
  log.push(`${stamp()} [test] Ana told: ${await waitForLog('Ana [log] [Oxfall] Team: Beto joined', 20000)}`);
  await shot(ana, '03-ana-joined');

  // Apart: the name in green from afar.
  await cmd(beto, 'Beto', 'back-30', 3000);
  await cmd(ana, 'Ana', 'face-player', 2500);
  await shot(ana, '04-ana-mate-far');
  await cmd(ana, 'Ana', 'team-panel', 2500);
  await shot(ana, '05-ana-team-panel');
  await cmd(ana, 'Ana', 'team-panel', 800);
  await cmd(beto, 'Beto', 'team-panel', 2500);
  await shot(beto, '06-beto-team-panel');
  await cmd(beto, 'Beto', 'team-panel', 800);
  await cmd(ana, 'Ana', 'map', 3500);
  await shot(ana, '07-ana-map');
  await cmd(ana, 'Ana', 'map', 800);

  // A line to the team, and one to everyone.
  await cmd(beto, 'Beto', 'team-say:vamos a la ruina del norte', 2000);
  await cmd(ana, 'Ana', 'say:hola a todos', 2500);
  await shot(ana, '08-ana-chat');

  // Beto leaves.
  await cmd(beto, 'Beto', 'team-leave', 3000);
  log.push(`${stamp()} [test] Ana alone again: ${await waitForLog('Ana [log] [Oxfall] Team: none now', 20000)}`);
  await shot(ana, '09-ana-alone');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error)\]|Oxfall\] (Team|Chat|Online)|xception/.test(l)).join('\n'));
  await browser.close();
})();
