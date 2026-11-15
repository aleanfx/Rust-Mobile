// Crops, alone: sow hemp and berries, watch them grow to ripe, harvest, let one wither.
// Usage: node crops.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-crops');
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
  const stats = async () => { await cmd('stats', 700); const line = [...log].reverse().find(l => l.includes('[Oxfall] Stats')) || ''; const pm = line.match(/pos=(\([^)]*\))/); const pos = pm ? pm[1] : null; return { boat: parseInt(lastOf('[Oxfall] Stats', 'boat'), 10), pos }; };
  const xz = pos => { const m = pos && pos.match(/\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)/); return m ? [parseFloat(m[1]), parseFloat(m[3])] : [NaN, NaN]; };


  const crops = async () => { const n = log.length; await cmd('crops', 700); return log.slice(n).filter(l => l.includes('[Oxfall] Crop ')); };
  const num = (line, key) => { const m = line.match(new RegExp('\\b' + key + '=([-\\d.]+)')); return m ? parseFloat(m[1]) : NaN; };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-10', 1000);
  await cmd('clear-animals', 500);
  await cmd('give-seeds', 800);
  await hold('KeyW', 2500);
  // Sow: a hemp seed, a step to the side, a berry seed.
  let sown = [];
  for (const [seed, kind] of [['hemp-seed', 'HempPlant'], ['berry-seed', 'BerryPlant']]) {
    await cmd(seed, 1000);
    for (let tries = 0; tries < 8; tries++) {
      await cmd('look--20', 500);
      await cmd('deploy', 1200);
      sown = await crops();
      if (sown.some(l => l.includes(kind))) break;
      await hold('KeyW', 900);
    }
    if (seed === 'hemp-seed') { await shot('01-sown'); await hold('KeyD', 900); }
  }
  check('seeds sow a hemp crop and a berry crop', sown.length >= 2 && sown.every(l => l.includes('stage=Sprout')), sown.map(l => l.replace(/.*Crop /, '')).join(' | '));
  await cmd('look--10', 600);
  await hold('KeyS', 700);
  await shot('02-sprouts');
  await cmd('grow-crops', 800);
  sown = await crops();
  check('they grow (young)', sown.length >= 2 && sown.every(l => l.includes('stage=Young')), sown.map(l => l.replace(/.*stage=/, '')).join(' | '));
  await shot('03-young');
  await cmd('grow-crops', 800);
  await cmd('grow-crops', 800);
  sown = await crops();
  check('they ripen', sown.every(l => l.includes('stage=Ripe')), sown.map(l => l.replace(/.*stage=/, '')).join(' | '));
  await shot('04-ripe');
  const before = sown[0];
  const cloth0 = num(before, 'cloth'), berries0 = num(before, 'berries');
  await cmd('aim-crop', 700);
  await shot('05-cosechar');
  await cmd('interact', 1200);
  await cmd('aim-crop', 700);
  await cmd('interact', 1200);
  await cmd('crops', 700);
  await cmd('stats', 700);
  check('harvests', has('Harvested'), (log.filter(l => l.includes('Harvested')).join(' | ')));
  await shot('06-harvested');
  // A third crop left to wither.
  await cmd('hemp-seed', 800);
  for (let tries = 0; tries < 6 && (await crops()).length === 0; tries++) { await cmd('look--20', 500); await cmd('deploy', 1200); await hold('KeyW', 700); }
  for (let i = 0; i < 18; i++) await cmd('grow-crops', 200);
  sown = await crops();
  check('left too long it withers', sown.some(l => l.includes('stage=Withered')), sown.map(l => l.replace(/.*stage=/, '')).join(' | '));
  await cmd('aim-crop', 700);
  await shot('07-withered');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(test|pageerror|error)\]|xception|Oxfall\] (Crop|Harvested|Placed|Stats)/.test(l)).join('\n'));
  console.log('\n' + results.join('\n'));
  await browser.close();
})();
