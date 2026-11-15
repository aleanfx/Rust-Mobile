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
  // Two games on one software GPU run at a frame or two a second: ask again if the first was too early.
  let invited = false;
  for (let i = 0; i < 3 && !invited; i++) {
    if (i > 0) { await cmd(ana, 'Ana', 'near-player', 6000); await cmd(beto, 'Beto', 'face-player', 4000); }
    await cmd(ana, 'Ana', 'team-invite', 2500);
    invited = await waitForLog('Beto [log] [Oxfall] Team invitation from Ana', 30000);
  }
  log.push(`${stamp()} [test] Beto invited: ${invited}`);
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
  // Beto marks the map: Ana sees his marker on hers.
  await cmd(beto, 'Beto', 'map-mark:60,40', 2500);
  await cmd(ana, 'Ana', 'map', 3500);
  await shot(ana, '07-ana-map');
  await cmd(ana, 'Ana', 'map', 800);

  // A line to the team, and one to everyone.
  await cmd(beto, 'Beto', 'team-say:vamos a la ruina del norte', 2000);
  await cmd(ana, 'Ana', 'say:hola a todos', 2500);
  await shot(ana, '08-ana-chat');

  // Beto in metal armour with the rifle, seen up close by Ana.
  await cmd(beto, 'Beto', 'give-armour', 1500);
  await cmd(beto, 'Beto', 'wear-armour', 2000);
  await cmd(beto, 'Beto', 'give-guns', 1500);
  // His gold rifle skin, bought online: Ana sees it in his hands.
  await cmd(beto, 'Beto', 'give-coins', 2500);
  await cmd(beto, 'Beto', 'store', 2500);
  await cmd(beto, 'Beto', 'store-select:rifle_gold', 1500);
  await cmd(beto, 'Beto', 'store-act', 4000);
  await shot(beto, '08a-beto-store');
  await cmd(beto, 'Beto', 'store', 1500);
  await cmd(beto, 'Beto', 'skins', 800);
  await cmd(beto, 'Beto', 'rifle', 2000);
  await cmd(ana, 'Ana', 'near-player', 3000);
  await cmd(beto, 'Beto', 'face-player', 3000);
  await shot(ana, '08b-ana-sees-armour');
  await cmd(beto, 'Beto', 'inventory', 2500);
  await shot(beto, '08c-beto-armour-worn');
  await cmd(beto, 'Beto', 'inventory', 800);

  // Beto leaves.
  await cmd(beto, 'Beto', 'team-leave', 3000);
  log.push(`${stamp()} [test] Ana alone again: ${await waitForLog('Ana [log] [Oxfall] Team: none now', 20000)}`);
  await shot(ana, '09-ana-alone');

  // No longer mates: Ana sets a loaded shotgun trap facing Beto, and it fires at him.
  // Inland first: the beach they woke on runs into the sea, and nothing is set up in water.
  await cmd(beto, 'Beto', 'nearest-tree', 3000);
  await cmd(beto, 'Beto', 'back-3', 3000);
  await cmd(ana, 'Ana', 'give-trap', 1500);
  await cmd(ana, 'Ana', 'near-player', 6000);
  // Ana steps back to set it with room, facing Beto: it points where she looks.
  await cmd(ana, 'Ana', 'back-5', 4000);
  await cmd(beto, 'Beto', 'stats', 800);
  await cmd(ana, 'Ana', 'trap', 4000);
  await shot(ana, '10-ana-trap-ghost');
  await cmd(ana, 'Ana', 'deploy', 4000);
  await cmd(ana, 'Ana', 'open-trap', 1500);
  await cmd(ana, 'Ana', 'load-trap', 1500);
  await cmd(ana, 'Ana', 'stats', 800);
  await cmd(ana, 'Ana', 'close-box', 800);
  await cmd(beto, 'Beto', 'face-player', 12000);
  await shot(beto, '11-beto-shot-by-trap');
  await shot(ana, '12-ana-trap-fires');
  await cmd(beto, 'Beto', 'stats', 800);

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error)\]|Oxfall\] (Team|Chat|Online)|xception/.test(l)).join('\n'));
  await browser.close();
})();
