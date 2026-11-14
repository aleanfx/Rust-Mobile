// A raid, solo: a stone hut with a wooden door; the satchel charge in hand shows how many it takes,
// one blows the door in (fuse, fireball, smoke, rubble), two scorch the doorway, the hammer repairs
// it once the 30 s pass, and four more bring it down. Also the sulfur rocks and the ARMAS tab.
// Usage: node raid.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-raid');
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
  const hold = async (key, ms) => { await page.keyboard.down(key); await wait(ms); await page.keyboard.up(key); log.push(`${stamp()} [key] ${key} ${ms}ms`); await wait(500); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(250); }
    return false;
  };
  const count = needle => log.filter(l => l.includes(needle)).length;
  // Where the doorway is: the horizontal distance from the eyes, from the last "Aimed at" line.
  const doorwayDistance = () => {
    const line = [...log].reverse().find(l => l.includes('[Oxfall] Aimed at Doorway'));
    const m = line && line.match(/Doorway \(([-\d.]+), [-\d.]+, ([-\d.]+)\) from \(([-\d.]+), [-\d.]+, ([-\d.]+)\)/);
    return m ? Math.hypot(+m[1] - +m[3], +m[2] - +m[4]) : null;
  };
  // Stands 2.5 m from the doorway, facing it (and turned onto its side post if asked).
  const approach = async post => {
    await cmd('aim-door', 600);
    const d = doorwayDistance();
    if (d !== null && Math.abs(d - 2.5) > 0.2) { await cmd(`fwd-${(d - 2.5).toFixed(2)}`, 500); await cmd('aim-door', 600); }
    if (post) await cmd(`turn-${(Math.atan(1.05 / 2.5) * 180 / Math.PI).toFixed(1)}`, 600);
  };
  // Throws, then steps back and watches it go off; shoots the blast if asked.
  const throwAndWatch = async (label, shots, post) => {
    const before = count('[Oxfall] Blast ');
    await approach(post);
    await cmd('throw', 1000);
    if (shots) await shot(label + '-stuck');
    await cmd('back-6', 200);
    await cmd('aim-door', 200);
    const end = Date.now() + 8000;
    while (Date.now() < end && count('[Oxfall] Blast ') === before) await wait(50);
    if (shots) {
      await wait(100);
      await shot(label + '-blast');
      await wait(600);
      await shot(label + '-smoke');
      await wait(1800);
      await shot(label + '-after');
    } else await wait(1500);
  };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 30000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('give-all', 1200);
  // The kit fills the backpack: wear the hide and set a box down to make room for the charges.
  await cmd('wear-hide', 1000);
  await cmd('give-raid', 1200);
  await cmd('stats', 600);
  await cmd('time-12', 1500);

  // A stone hut with a wooden door, right ahead.
  await cmd('build-hut', 2500);
  await cmd('upgrade-all-2', 2000);
  await cmd('door', 1200);
  await cmd('aim-door', 1500);
  await cmd('place-door', 2000);
  await cmd('stats', 600);

  // The charge in hand, aimed at the door.
  await cmd('satchel', 1500);
  await approach(false);
  await wait(800);
  await shot('03-satchel-aim-door');
  await throwAndWatch('04-door', true, false);
  await cmd('stats', 600);

  // The bare doorway now: two charges scorch it.
  await approach(true);
  await wait(800);
  await shot('05-doorway-aim');
  await throwAndWatch('06-frame1', false, true);
  await throwAndWatch('07-frame2', false, true);
  await cmd('heal', 300);
  await cmd('hammer', 1200);
  await approach(true);
  await wait(800);
  await shot('08-hammer-scorched');
  await cmd('upgrade', 1500);
  await shot('09-repair-too-soon');
  await wait(30000);
  await approach(true);
  await cmd('upgrade', 1800);
  await shot('10-repaired');
  await cmd('stats', 600);

  // Four charges bring the stone doorway down.
  await cmd('satchel', 1200);
  for (let i = 1; i <= 3; i++) { await throwAndWatch('11-fell' + i, false, true); await cmd('heal', 300); }
  await throwAndWatch('12-fall', true, true);
  await cmd('stats', 600);
  // Sulfur on the map, and the charge in the crafting menu (last: the rocks stand on rough ground
  // where a hut would stand on stilts).
  await cmd('nearest-sulfur', 3000);
  await shot('01-sulfur-rock');
  await cmd('craft-panel', 1200);
  await cmd('craft-tab-2', 1500);
  await shot('02-craft-weapons');
  await cmd('craft-panel', 1000);
  log.push(`${stamp()} [test] blasts=${count('[Oxfall] Blast ')} repaired=${count('[Oxfall] Repaired')}`);

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error|warning)\]|Oxfall\] (Stats|Threw|Blast|Repaired|Test hut|Aimed)|rror|xception/.test(l)).join('\n'));
  await browser.close();
})();
