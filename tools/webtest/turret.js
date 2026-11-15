// The automatic turret, alone: set it down, load it, a wolf walks up in front of it and is shot.
// Usage: node turret.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-turret');
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



  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-10', 1000);
  await cmd('clear-animals', 500);
  await cmd('give-turret', 800);
  await hold('KeyW', 2000);
  await cmd('turret', 1200);
  await cmd('look--15', 600);
  await shot('01-ghost');
  for (let i = 0; i < 6 && !has('Placed AutoTurret'); i++) { await cmd('deploy', 1500); if (!has('Placed AutoTurret')) await hold('KeyW', 800); }
  check('the turret goes down', has('Placed AutoTurret'), '');
  await cmd('open-turret', 1500);
  await cmd('load-turret', 1500);
  await shot('02-loaded');
  await cmd('close-box', 800);
  await cmd('turrets', 700);
  const loaded = [...log].reverse().find(l => l.includes('[Oxfall] Turret ') && l.includes('rifle=')) || '';
  check('loaded with bullets', /rifle=[1-9]/.test(loaded), loaded.replace(/.*Turret/, 'Turret'));
  // Stand behind it, looking the way it faces, and send a wolf in.
  await hold('KeyS', 1500);
  await cmd('look-0', 600);
  await cmd('spawn-wolf', 600);
  await shot('03-wolf');
  const fired = await waitForLog('[Oxfall] Turret', 20000) && await waitForLog('fired at Animal', 20000);
  await shot('04-firing');
  await wait(3000);
  check('it fires at the wolf', fired, (log.filter(l => l.includes('fired at')).slice(0, 4).map(l => l.replace(/.*Turret/, 'Turret')).join(' | ')));
  check('the wolf dies', has(', killed'), '');
  await cmd('turrets', 700);
  const after = [...log].reverse().find(l => l.includes('[Oxfall] Turret ') && l.includes('rifle=')) || '';
  check('it spent bullets', after !== loaded, after.replace(/.*Turret/, 'Turret'));
  await shot('05-after');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(test|pageerror|error)\]|xception|Oxfall\] (Turret|Placed)/.test(l)).slice(-40).join('\n'));
  console.log('\n' + results.join('\n'));
  await browser.close();
})();
