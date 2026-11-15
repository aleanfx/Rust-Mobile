// The minicopter, alone: carry fuel to it, get in, lift off, fly level where you look, hover, look down and land softly, get out.
// Each part checks the numbers that `stats` and `fish-state` log and prints PASS or FAIL.
// Usage: node copter.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-copter');
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

  const copterLine = () => [...log].reverse().find(l => /\[Oxfall\] Copter \d+ at/.test(l) && /driver=[1-9]/.test(l)) || '';
  const num = (line, key) => { const m = line.match(new RegExp('\\b' + key + '=([-\\d.]+)')); return m ? parseFloat(m[1]) : NaN; };
  const pos3 = line => { const m = line.match(/at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)/); return m ? m.slice(1, 4).map(parseFloat) : [NaN, NaN, NaN]; };
  const copter = async () => { await cmd('copters', 500); return copterLine(); };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-10', 1000);
  await cmd('clear-animals', 500);
  await cmd('give-fuel', 800);
  await cmd('copters', 800);
  check('minicopters wait by the ruins', has('[Oxfall] Copter 1 at'), '');
  await cmd('go-copter', 2500);
  await shot('01-copter');
  await cmd('board', 1500);
  let s = await stats();
  check('gets in', has('Mounted copter'), (log.find(l => l.includes('Mounted copter')) || '').trim());
  let c = await copter();
  check('fuel poured into the tank', num(c, 'fuel') > 30, `fuel=${num(c, 'fuel')}`);
  await shot('02-cockpit');
  await page.keyboard.down('Space');
  await wait(6000);
  await page.keyboard.up('Space');
  c = await copter();
  const lifted = num(c, 'above');
  check('lifts off', lifted > 6, `above=${lifted}`);
  await cmd('look-0', 600);
  await shot('03-airborne');
  const p0 = pos3(c);
  await page.keyboard.down('KeyW');
  await wait(5000);
  c = await copter();
  await shot('04-flying');
  await page.keyboard.up('KeyW');
  const p1 = pos3(c);
  const flew = Math.hypot(p1[0] - p0[0], p1[2] - p0[2]);
  check('flies where it looks', flew > 15, `${flew.toFixed(1)} m, above=${num(c, 'above')}`);
  await wait(3000);
  const h1 = num(await copter(), 'above');
  await wait(3000);
  c = await copter();
  check('holds its height with the stick let go', Math.abs(num(c, 'above') - h1) < 3 && num(c, 'above') > 5, `above ${h1} -> ${num(c, 'above')}`);
  await cmd('look--70', 600);
  await shot('05-looking-down');
  await page.keyboard.down('KeyW');
  for (let i = 0; i < 20; i++) { await wait(1500); c = await copter(); if (num(c, 'above') < 0.2) break; }
  await page.keyboard.up('KeyW');
  await wait(1500);
  c = await copter();
  check('lands softly', num(c, 'above') < 0.3 && num(c, 'health') >= 99 && !has('Crashed'), `above=${num(c, 'above')} health=${num(c, 'health')}`);
  await cmd('look--5', 600);
  await shot('06-landed');
  await cmd('leave-boat', 1500);
  s = await stats();
  check('gets out', has('Left the copter'), `pos=${s.pos}`);
  await cmd('look-0', 600);
  await shot('07-out');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(test|pageerror|error)\]|xception|Oxfall\] (Copter|Mounted|Left|At copter|Crashed)/.test(l)).join('\n'));
  console.log('\n' + results.join('\n'));
  await browser.close();
})();
