// Horses, alone: walk up to one, mount, trot, gallop until winded, get off.
// Usage: node horse.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-horse');
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



  const horseLine = () => [...log].reverse().find(l => /\[Oxfall\] Horse \d+ at/.test(l) && /driver=[1-9]/.test(l)) || '';
  const num = (line, key) => { const m = line.match(new RegExp('\\b' + key + '=([-\\d.]+)')); return m ? parseFloat(m[1]) : NaN; };
  const pos3 = line => { const m = line.match(/at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)/); return m ? m.slice(1, 4).map(parseFloat) : [NaN, NaN, NaN]; };
  const horse = async () => { await cmd('horses', 500); return horseLine(); };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-10', 1000);
  await cmd('clear-animals', 500);
  await cmd('horses', 800);
  check('horses graze in the meadows', has('[Oxfall] Horse 1 at'), (log.find(l => l.includes('Horse 1 at')) || '').replace(/.*Horse/, 'Horse'));
  await cmd('go-horse', 2500);
  await cmd('look--5', 600);
  await shot('01-horse');
  await cmd('board', 1500);
  check('gets on', has('Mounted horse'), (log.find(l => l.includes('Mounted horse')) || '').trim());
  await cmd('look-0', 600);
  await shot('02-saddle');
  let h = await horse();
  const p0 = pos3(h);
  await page.keyboard.down('KeyW');
  await wait(4000);
  h = await horse();
  await shot('03-trot');
  await page.keyboard.up('KeyW');
  const p1 = pos3(h);
  const trotted = Math.hypot(p1[0] - p0[0], p1[2] - p0[2]);
  check('trots where the rider looks', trotted > 6, `${trotted.toFixed(1)} m, speed=${num(h, 'speed')}`);
  await wait(1500);
  // Gallop: sprint held, turning gently so it stays on the meadow.
  await page.keyboard.down('ShiftLeft');
  await page.keyboard.down('KeyW');
  let top = 0, lowest = 100;
  for (let i = 0; i < 16; i++) {
    await wait(1000);
    h = await horse();
    top = Math.max(top, num(h, 'speed'));
    lowest = Math.min(lowest, num(h, 'stamina'));
    if (i === 3) await shot('04-gallop');
  }
  await page.keyboard.up('KeyW');
  await page.keyboard.up('ShiftLeft');
  check('gallops and tires', top > 9 && lowest < 60, `top=${top} m/s stamina low=${lowest}`);
  await wait(2000);
  await cmd('leave-boat', 1500);
  await cmd('look--10', 600);
  check('gets off', has('Got off the horse'), '');
  await shot('05-off');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(test|pageerror|error)\]|xception|Oxfall\] (Horse|Mounted|Got off|At horse)/.test(l)).slice(-60).join('\n'));
  console.log('\n' + results.join('\n'));
  await browser.close();
})();
