// Rowboats, alone: walk up to one, get in, row out, turn, get out on the beach.
// Each part checks the numbers that `stats` and `fish-state` log and prints PASS or FAIL.
// Usage: node boats.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-boats');
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
  const stats = async () => { await cmd('stats', 700); const pos = lastOf('[Oxfall] Stats', 'pos'); return { boat: parseInt(lastOf('[Oxfall] Stats', 'boat'), 10), pos }; };
  const xz = pos => { const m = pos && pos.match(/\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)/); return m ? [parseFloat(m[1]), parseFloat(m[3])] : [NaN, NaN]; };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-10', 1000);
  await cmd('clear-animals', 500);
  await cmd('boats', 800);
  await cmd('go-boat', 2500);
  await shot('01-boat-at-shore');
  await cmd('board', 1500);
  let s = await stats();
  check('boards the boat', s.boat > 0, `boat=${s.boat}`);
  await shot('02-seated');
  const start = xz(s.pos);
  await hold('KeyW', 4000);
  s = await stats();
  const out1 = xz(s.pos);
  const rowed = Math.hypot(out1[0] - start[0], out1[1] - start[1]);
  check('rows out to sea', rowed > 3, `${rowed.toFixed(1)} m`);
  await shot('03-rowing');
  await page.keyboard.down('KeyW');
  await hold('KeyD', 2500);
  await page.keyboard.up('KeyW');
  await cmd('boats', 600);
  await shot('04-turning');
  await cmd('look-30', 800);
  await shot('05-look-at-sea');
  await cmd('leave-boat', 1500);
  s = await stats();
  check('gets out', s.boat === 0, `boat=${s.boat} pos=${s.pos}`);
  await shot('06-out');
  await cmd('boats', 600);

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(test|pageerror|error)\]|xception|Oxfall\] (Boat|Boarded|Left|At boat)/.test(l)).join('\n'));
  console.log('\n' + results.join('\n'));
  await browser.close();
})();
