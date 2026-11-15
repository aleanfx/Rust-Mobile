// Phase 3 mechanics, alone: temperature, radiation, machete and sword, crossbow, large box, fishing.
// Each part checks the numbers that `stats` and `fish-state` log and prints PASS or FAIL.
// Usage: node phase3.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-phase3');
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
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1',
  });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} [pageerror] ${e.message}`));
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const shot = async name => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const cmd = async (c, ms = 1500) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} [cmd] ${c}`); await wait(ms); };
  const hold = async (key, ms) => { await page.keyboard.down(key); await wait(ms); await page.keyboard.up(key); log.push(`${stamp()} [key] ${key} ${ms}ms`); await wait(600); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };
  const has = needle => log.some(l => l.includes(needle));
  const results = [];
  const check = (name, ok, detail) => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' (' + detail + ')' : ''}`); log.push(`${stamp()} [test] ${results[results.length - 1]}`); };
  // The last value of `key=` in the latest Stats or Fish line.
  const lastOf = (prefix, key) => {
    for (let i = log.length - 1; i >= 0; i--) {
      if (!log[i].includes(prefix)) continue;
      const m = log[i].match(new RegExp('\\b' + key + '=([^ ]+)'));
      if (m) return m[1];
    }
    return null;
  };
  const stat = async key => { await cmd('stats', 700); return parseFloat(lastOf('[Oxfall] Stats', key)); };
  const fish = async key => { await cmd('fish-state', 500); return lastOf('[Oxfall] Fish', key); };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-12', 1000);
  await cmd('clear-animals', 500);
  // The blades kit first: give-all alone nearly fills the bag.
  await cmd('give-blades', 1200);
  await cmd('give-all', 1200);
  await cmd('nearest-hemp', 2000);

  // Temperature: warm at noon, cold on a rainy night, warmer in hide clothes, warm by a campfire.
  const noon = await stat('warmth');
  await cmd('time-2', 800);
  await cmd('rain-1', 2500);
  const night = await stat('warmth');
  await shot('01-cold-night-rain');
  await cmd('wear-hide', 1200);
  const dressed = await stat('warmth');
  await cmd('campfire', 1200);
  await cmd('deploy', 1800);
  await cmd('face-fire', 1500);
  const byFire = await stat('warmth');
  await shot('02-by-the-fire');
  check('temperature: night+rain colder than noon', night < noon, `noon=${noon} night=${night}`);
  check('temperature: hide clothes warm', dressed > night, `dressed=${dressed}`);
  check('temperature: campfire warms', byFire > dressed, `fire=${byFire}`);
  await cmd('rain-0', 500);
  await cmd('time-12', 1000);
  await cmd('heal', 200);

  // Radiation: dose inside the military ruins, less with clothes on, fades outside.
  await cmd('undress', 800);
  await cmd('to-outpost', 2500);
  const doseBare = await stat('dose');
  await wait(4000);
  const radsIn = await stat('rads');
  await shot('03-radiation-chip');
  await cmd('wear-hide', 1000);
  const doseDressed = await stat('dose');
  check('radiation: dose inside the outpost', doseBare > 0, `dose=${doseBare}`);
  check('radiation: soaked up', radsIn > 0, `rads=${radsIn}`);
  check('radiation: clothes cut the dose', doseDressed < doseBare, `dressed=${doseDressed}`);
  await cmd('nearest-hemp', 2000);
  const radsOutA = await stat('rads');
  await wait(4000);
  const radsOutB = await stat('rads');
  check('radiation: fades outside', radsOutB < radsOutA && (await stat('dose')) === 0, `${radsOutA} -> ${radsOutB}`);
  await cmd('heal', 200);

  // Machete: kills a boar and cuts the carcass for meat. Sword: hits harder.
  await cmd('clear-animals', 600);
  await cmd('machete', 1500);
  check('machete: in hand', /machete/i.test(lastOf('[Oxfall] Stats', 'held') || '') || (await stat('held'), /machete/i.test(lastOf('[Oxfall] Stats', 'held') || '')), lastOf('[Oxfall] Stats', 'held'));
  await shot('04-machete-held');
  await cmd('spawn-boar', 1500);
  for (let i = 0; i < 12 && !has('Killed'); i++) {
    await cmd('approach-animal', 250);
    await cmd('use', 900);
    if (i === 0) await shot('05-machete-hit');
    await cmd('heal', 100);
  }
  check('machete: kills a boar', has('Killed'));
  const meatBefore = await stat('meat');
  for (let i = 0; i < 8; i++) { await cmd('approach-animal', 250); await cmd('use', 950); }
  const meatAfter = await stat('meat');
  await shot('06-machete-carcass');
  check('machete: cuts meat from the carcass', meatAfter > meatBefore, `${meatBefore} -> ${meatAfter}`);
  await cmd('clear-animals', 600);
  const killedSoFar = log.filter(l => l.includes('Killed')).length;
  await cmd('sword', 1500);
  await shot('07-sword-held');
  await cmd('spawn-boar', 1500);
  let swings = 0;
  for (; swings < 12 && log.filter(l => l.includes('Killed')).length === killedSoFar; swings++) {
    await cmd('approach-animal', 250);
    await cmd('use', 1100);
    await cmd('heal', 100);
  }
  check('sword: kills a boar', log.filter(l => l.includes('Killed')).length > killedSoFar, `${swings} swings`);
  await cmd('clear-animals', 600);

  // Crossbow: arrows leave the quiver, a deer goes down.
  await cmd('crossbow', 1500);
  const arrowsA = await stat('arrows');
  // A boar a few metres off (arrows at a few frames a second can pass through a far target between frames).
  await cmd('spawn-boar', 1500);
  await cmd('approach-animal', 300);
  await cmd('back-5', 800);
  await cmd('face-animal', 600);
  await shot('08-crossbow-aim');
  const killedBefore = log.filter(l => l.includes('Killed')).length;
  for (let i = 0; i < 4 && log.filter(l => l.includes('Killed')).length === killedBefore; i++) {
    await cmd('face-animal', 200);
    await cmd('use', 200);
    if (i === 0) await shot('09-crossbow-bolt');
    // Loads again in a few seconds.
    await wait(4000);
  }
  const arrowsB = await stat('arrows');
  check('crossbow: shoots arrows', arrowsB < arrowsA, `${arrowsA} -> ${arrowsB}`);
  check('crossbow: kills a boar', log.filter(l => l.includes('Killed')).length > killedBefore);
  await cmd('clear-animals', 600);

  // Large box: 24 slots in two pages.
  await cmd('large-box', 1500);
  await shot('10-large-box-ghost');
  const depA = await stat('deployables');
  await cmd('deploy', 1800);
  const depB = await stat('deployables');
  check('large box: placed', depB > depA, `${depA} -> ${depB}`);
  await cmd('open-large-box', 2000);
  await shot('11-large-box-page1');
  const looting = parseInt(await stat('looting'), 10);
  await cmd('box-page', 1200);
  await shot('12-large-box-page2');
  await cmd('close-box', 800);
  check('large box: opens', looting > 0, `looting=${looting}`);

  // Fishing: to the shore, cast, wait for a bite, reel in a fish.
  await cmd('go-shore', 2500);
  await cmd('fishing-rod', 1500);
  await shot('13-rod-at-shore');
  // This machine runs at a few frames a second: a screenshot takes long enough for a fish to bite
  // and get away, so watch the float with no screenshots between casting and reeling (up to 3 casts).
  let bit = false, casted = false;
  for (let attempt = 0; attempt < 3 && !bit; attempt++) {
    const linesBefore = log.length;
    await cmd('cast', 300);
    for (let i = 0; i < 90 && !bit; i++) {
      await page.evaluate(() => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', 'fish-state'));
      await wait(300);
      const fresh = log.slice(linesBefore);
      casted = casted || fresh.some(l => l.includes('casting=True'));
      bit = fresh.some(l => l.includes('biting=True'));
      if (!bit && i > 5 && fresh.length && fresh[fresh.length - 1].includes('casting=False') && fresh.some(l => l.includes('casting=True'))) break;
    }
  }
  check('fishing: cast', casted);
  check('fishing: a fish bites', bit);
  await cmd('reel', 2000);
  await shot('15-reeled');
  const caught = parseInt(await fish('raw-fish'), 10);
  await shot('16-caught');
  check('fishing: fish in the bag', caught > 0, `raw-fish=${caught}`);

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(test|pageerror|error)\]|xception/.test(l)).join('\n'));
  console.log('\n' + results.join('\n'));
  await browser.close();
})();
