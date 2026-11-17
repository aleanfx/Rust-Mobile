// Patrol helicopter rockets on a base, solo: shot at from inside a wooden hut it cannot see the player, but it
// rockets the hut where the shots came from; the walls lose health and in the end give way. PASS/FAIL lines.
// Usage: node rockets.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-rockets');
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);
const results = [];
const check = (name, ok) => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}`); log.push(`${stamp()} [test] ${ok ? 'PASS' : 'FAIL'} ${name}`); };

(async () => {
  const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'] });
  const context = await browser.newContext({ viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} [pageerror] ${e.message}`));
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const shot = async name => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const cmd = async (c, ms = 1500) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} [cmd] ${c}`); await wait(ms); };
  const waitForLog = async (needle, ms, from = 0) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.slice(from).some(l => l.includes(needle))) return true; await wait(250); }
    return false;
  };
  const heli = async () => {
    const i = log.length;
    await cmd('heli-state', 700);
    const lines = log.slice(i);
    const h = lines.find(l => l.includes('[Oxfall] Heli ') && l.includes('health='));
    const crates = lines.filter(l => l.includes('[Oxfall] Heli crate'));
    const me = lines.find(l => l.includes('Heli count='));
    return {
      state: h ? (h.match(/Heli \d+ (\w+)/) || [])[1] : null,
      health: h ? +(h.match(/health=(\d+)/) || [])[1] : null,
      crates, mine: me ? +(me.match(/ health=(\d+)/) || [])[1] : null,
    };
  };
  const until = async (pred, ms) => { const end = Date.now() + ms; let s; while (Date.now() < end) { s = await heli(); if (pred(s)) return s; await wait(1500); } return s; };

  const hut = async () => {
    const i = log.length;
    await cmd('hut-state', 600);
    const l = log.slice(i).find(l => l.includes('[Oxfall] Hut pieces='));
    return l ? { pieces: +l.match(/pieces=(\d+)/)[1], health: +l.match(/health=(\d+)/)[1], max: +l.match(/max=(\d+)/)[1] } : null;
  };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  await waitForLog('item icons', 60000);
  await wait(2000);
  await cmd('quality-high', 2500);
  await cmd('time-12', 1000);
  await cmd('freeze-animals', 300);
  await cmd('give-armour', 800);
  await cmd('wear-armour', 800);

  let mark = log.length;
  await cmd('rocket-hut', 2500);
  check('a wooden hut stands round the player', await waitForLog('Rocket hut at', 5000, mark));
  const h0 = await hut();
  check('all of it whole', h0 && h0.pieces >= 10 && h0.health === h0.max);
  await cmd('view-hut', 2500);
  await shot('01-hut-whole');
  await cmd('into-hut', 1200);

  // It comes; from inside the player "shoots" it now and then: it cannot see them, so it rockets the hut.
  mark = log.length;
  await cmd('heli-here', 1500);
  let s = await heli();
  let h1 = h0, firstHit = null;
  const end = Date.now() + 150000;
  while (Date.now() < end) {
    await cmd('heli-aggro', 300);
    await cmd('heal', 200);
    await wait(2000);
    h1 = await hut();
    if (!firstHit && h1 && h1.health < h0.health) firstHit = h1;
    if (h1 && h1.pieces < h0.pieces) break;
  }
  s = await heli();
  check('it turned on the player hidden inside', s.state === 'Engage' || s.state === 'Search');
  check('its rockets burst on the hut and hurt it', !!firstHit && log.slice(mark).some(l => l.includes('A helicopter rocket struck')));
  check('wood gives way: a piece of the hut fell', h1 && h1.pieces < h0.pieces);
  check('the player is told the base is being rocketed', log.slice(mark).some(l => l.includes('cohetes revientan la base')));
  await cmd('heal', 200);
  await cmd('view-hut', 1500);
  await shot('02-hut-rocketed');
  await cmd('view-heli', 900);
  await shot('03-heli-over-hut');
  await cmd('heal', 200);
  await cmd('view-hut', 4000);
  await shot('04-hut-after');
  check('nothing broke', !log.some(l => l.includes('[pageerror]') || /Exception/.test(l)));

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('[pageerror]') || l.includes('Exception') || l.includes('rocket struck')).join('\n'));
  console.log(results.join('\n'));
  await browser.close();
})();
