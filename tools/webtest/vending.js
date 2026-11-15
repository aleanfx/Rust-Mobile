// Vending machine, alone: set one down, stock it with wood, put up an offer, buy from it.
// Usage: node vending.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-vending');
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
  const machine = async () => { await cmd('vending', 800); return [...log].reverse().find(l => l.includes('[Oxfall] Vending ')) || ''; };
  const num = (line, key) => { const m = line.match(new RegExp('\\b' + key + '=([-\\d.]+)')); return m ? parseFloat(m[1]) : NaN; };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('time-10', 1000);
  await cmd('clear-animals', 500);
  await cmd('give-vending', 800);
  await hold('KeyW', 2000);
  await cmd('vending-machine', 1200);
  await cmd('look--15', 600);
  await shot('01-ghost');
  for (let i = 0; i < 6 && !has('Placed VendingMachine'); i++) { await cmd('deploy', 1500); if (!has('Placed VendingMachine')) await hold('KeyW', 800); }
  check('the machine goes down', has('Placed VendingMachine'), '');
  await cmd('look-0', 600);
  await shot('02-machine');

  await cmd('open-vending', 1500);
  check('its shop window opens', has('Opened vending machine'), '');
  await shot('03-shop-empty');
  await cmd('stock-vending', 2000);
  await shot('04-stocking');
  await cmd('open-vending', 1500);
  await cmd('vend-act-0', 1500);
  let m = await machine();
  check('stocked and an offer set', num(m, 'wood') >= 300 && m.includes('offer0=[100 wood for 10 scrap]'), m.replace(/.*Vending/, 'Vending'));
  await shot('05-owner');

  const wood0 = num(m, 'my-wood'), scrap0 = num(m, 'my-scrap'), stock0 = num(m, 'wood');
  await cmd('vend-buy-0', 1500);
  m = await machine();
  check('buying swaps goods for the price', has('Bought 100 wood') && num(m, 'my-wood') === wood0 + 100 && num(m, 'my-scrap') === scrap0 - 10
    && num(m, 'wood') === stock0 - 100 && num(m, 'scrap') === 10, m.replace(/.*Vending/, 'Vending'));
  await shot('06-bought');
  await cmd('close-vending', 800);
  await hold('KeyS', 900);
  await cmd('look--5', 600);
  await shot('07-machine-front');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(test|pageerror|error)\]|xception|Oxfall\] (Vending|Placed|Bought|Offer)/.test(l)).slice(-40).join('\n'));
  console.log('\n' + results.join('\n'));
  await browser.close();
})();
