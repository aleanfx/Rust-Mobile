// Patrol helicopter, solo: it comes for the player, circles and fires; rifle rounds hurt it; badly hurt it
// smokes; shot down it burns with three crates that open once cool; searchlight at night. PASS/FAIL lines.
// Usage: node heli.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-heli');
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

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  await waitForLog('item icons', 60000);
  await wait(2000);
  await cmd('quality-high', 2500);
  await cmd('time-12', 1000);
  await cmd('freeze-animals', 300);
  await cmd('give-guns', 800);
  await cmd('give-armour', 800);
  await cmd('wear-armour', 800);
  await cmd('rifle', 1500);
  await cmd('view-hill', 2500);

  // It comes for the player and opens fire.
  let mark = log.length;
  await cmd('heli-here', 1500);
  check('the helicopter comes in, and everyone is told', await waitForLog('Patrol helicopter 1 came in', 8000, mark));
  let s = await until(s => s.state === 'Engage', 90000);
  check('it spots the player in the open and engages', s.state === 'Engage');
  await cmd('heal', 200);
  await cmd('view-heli', 1200);
  await shot('01-heli-engaging');
  const before = (await heli()).mine;
  await wait(9000);
  await cmd('view-heli', 600);
  await shot('02-heli-firing');
  s = await heli();
  check('its gun hurts the player', s.mine !== null && before !== null && s.mine < before || log.slice(mark).some(l => l.includes('te dispara') || l.includes('cohete')));
  await cmd('heal', 200);

  // Rifle rounds at it.
  const h0 = (await heli()).health;
  mark = log.length;
  for (let i = 0; i < 6; i++) { await cmd('shoot-heli', 700); await cmd('heal', 100); }
  const h1 = (await heli()).health;
  check('rifle rounds hurt it', log.slice(mark).some(l => l.includes('Hit the helicopter')) && h1 < h0);

  // Badly hurt: it smokes.
  await cmd('heli-hurt', 2500);
  await cmd('heal', 200);
  await cmd('view-heli', 1500);
  await shot('03-heli-smoking');
  check('badly hurt', (await heli()).health <= 500);

  // Shot down: it falls and burns, with three hot crates round it.
  mark = log.length;
  await cmd('heli-down', 1000);
  check('everyone hears it go down', await waitForLog('going down', 8000, mark));
  await cmd('view-heli', 800);
  await shot('04-heli-falling');
  s = await until(s => s.state === 'Wreck', 60000);
  check('it ends on the ground as a burning wreck', s.state === 'Wreck');
  check('three crates lie round it, burning', s.crates.length === 3 && s.crates.every(l => l.includes('open=False')));
  await cmd('go-wreck', 3500);
  await shot('05-wreck-burning');
  mark = log.length;
  await cmd('open-heli-crate', 1200);
  check('a crate will not open while it burns', log.slice(mark).some(l => l.includes('still burning')) && !log.slice(mark).some(l => l.includes('[Oxfall] Opened box')));
  await cmd('heli-cool', 4000);
  s = await heli();
  check('cooled, they can be opened', s.crates.length === 3 && s.crates.every(l => l.includes('open=True')));
  mark = log.length;
  await cmd('open-heli-crate', 2500);
  check('its loot', await waitForLog('[Oxfall] Opened box', 5000, mark));
  await shot('06-heli-loot');
  await cmd('close-box', 800);
  await cmd('map', 2500);
  await shot('07-map-wreck');
  await cmd('map', 500);

  // At night it hunts with a searchlight.
  await cmd('heli-clear', 800);
  await cmd('time-23', 1500);
  await cmd('view-hill', 2000);
  await cmd('heli-here', 1500);
  s = await until(s => s.state === 'Engage', 90000);
  await cmd('heal', 200);
  await cmd('view-heli', 1500);
  await shot('08-heli-night-searchlight');
  check('it hunts at night too', s.state === 'Engage');
  check('nothing broke', !log.some(l => l.includes('[pageerror]') || /Exception/.test(l)));

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('[pageerror]') || l.includes('Exception')).join('\n'));
  console.log(results.join('\n'));
  await browser.close();
})();
