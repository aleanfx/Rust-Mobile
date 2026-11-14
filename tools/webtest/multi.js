// Two players on one server: each should see the other walk, with a name over the head, and what
// one builds; a player who leaves and comes back finds their things. Needs a test server running
// (OXFALL_DEV=1 dotnet server/Oxfall.Server) and the web build on :8080.
// Usage: node multi.js [outDir] [server]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-multi');
const server = process.argv[3] || 'ws://localhost:7777/ws';
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);
const wait = ms => new Promise(r => setTimeout(r, ms));

async function open(browser, name, context) {
  context = context || await browser.newContext({
    viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true,
  });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} ${name} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} ${name} [pageerror] ${e.message}`));
  await page.goto(`http://localhost:8080/index.html?server=${encodeURIComponent(server)}&name=${name}`);
  return page;
}

(async () => {
  const browser = await chromium.launch({
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'],
  });
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };
  const cmd = async (page, who, c, ms = 1500) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} ${who} [cmd] ${c}`); await wait(ms); };
  const shot = async (page, name) => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };

  const ana = await open(browser, 'Ana');
  log.push(`${stamp()} [test] Ana online: ${await waitForLog('Ana [log] [Oxfall] Online', 240000)}`);
  const beto = await open(browser, 'Beto');
  log.push(`${stamp()} [test] Beto online: ${await waitForLog('Beto [log] [Oxfall] Online', 240000)}`);
  log.push(`${stamp()} [test] Ana sees Beto join: ${await waitForLog('Ana [log] [Oxfall] Beto joined', 10000)}`);
  await wait(3000);

  // Beto walks forward for two seconds, away from the shared spawn point.
  await beto.keyboard.down('w'); await wait(2000); await beto.keyboard.up('w'); await wait(1000);
  await cmd(ana, 'Ana', 'quality-high', 2000);
  await cmd(ana, 'Ana', 'face-player', 2500);
  await shot(ana, '1-ana-sees-beto');
  await cmd(beto, 'Beto', 'face-player', 2500);
  await shot(beto, '2-beto-sees-ana');
  // Ana walks towards Beto: Beto should see her move.
  await ana.keyboard.down('w'); await wait(800);
  await shot(beto, '3-ana-walking');
  await ana.keyboard.up('w'); await wait(500);
  await cmd(ana, 'Ana', 'stats', 600);
  await cmd(beto, 'Beto', 'stats', 600);

  // Ana gets a kit from the test server, builds a floor and a wall and starts a hatchet: the server
  // does it, and Beto's copy of the island shows the same pieces.
  const lastStats = who => [...log].reverse().find(l => l.includes(`${who} [log] [Oxfall] Stats`)) || '';
  const field = (line, key) => (line.match(new RegExp(key + '=([^ ]+)')) || [])[1];
  await cmd(ana, 'Ana', 'give-all', 1500);
  await cmd(ana, 'Ana', 'build', 1500);
  await cmd(ana, 'Ana', 'place', 1500);
  await cmd(ana, 'Ana', 'piece', 500);
  await cmd(ana, 'Ana', 'place', 1500);
  await cmd(ana, 'Ana', 'craft-hatchet', 500);
  await cmd(ana, 'Ana', 'stats', 800);
  await cmd(beto, 'Beto', 'face-player', 2000);
  await shot(beto, '4-beto-sees-ana-build');
  await cmd(beto, 'Beto', 'stats', 800);
  const anaPieces = field(lastStats('Ana'), 'pieces'), betoPieces = field(lastStats('Beto'), 'pieces');
  log.push(`${stamp()} [test] pieces Ana=${anaPieces} Beto=${betoPieces}: ${anaPieces > 0 && anaPieces === betoPieces}`);
  log.push(`${stamp()} [test] hatchet crafted on the server: ${await waitForLog('Ana [log] [Oxfall] Crafted', 15000)}`);
  await cmd(ana, 'Ana', 'stats', 800);
  const woodBefore = field(lastStats('Ana'), 'wood');
  const anaContext = ana.context();
  await ana.close();
  log.push(`${stamp()} [test] Beto sees Ana leave: ${await waitForLog('Beto [log] [Oxfall] Ana left', 10000)}`);

  // Ana comes back on the same phone: her survivor kept her wood.
  log.push(`${stamp()} [test] Ana reopens`);
  const again = await open(browser, 'Ana', anaContext);
  const back = log.length;
  const end = Date.now() + 240000;
  while (Date.now() < end && !log.slice(back).some(l => l.includes('Ana [log] [Oxfall] Online'))) await wait(500);
  await wait(3000);
  await cmd(again, 'Ana', 'stats', 800);
  const woodAfter = field(lastStats('Ana'), 'wood');
  log.push(`${stamp()} [test] Ana's wood ${woodBefore} -> ${woodAfter}: ${woodBefore === woodAfter && +woodAfter > 0}`);
  await shot(again, '5-ana-back');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error)\]|Oxfall\] (Stats|Online|Offline|Facing|.* joined|.* left|The server)|xception/.test(l)).join('\n'));
  await browser.close();
})();
