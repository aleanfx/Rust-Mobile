// The metal age and code locks, alone: a sheet-metal hut and door, a code lock put on and its code
// chosen, managing it, then the same lock as a stranger's (a wrong code shocks, the right one opens),
// a lock on a box, and the metal tools in hand.
// Usage: node locks.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-locks');
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

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 30000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('give-all', 1500);
  await cmd('time-12', 1500);

  // A sheet-metal hut with a sheet-metal door.
  await cmd('build-hut', 1500);
  await cmd('metal-door', 1500);
  await cmd('aim-door', 1000);
  await cmd('place-door', 1500);
  await cmd('upgrade-all-3', 2500);
  await cmd('slot-1', 800);
  await shot('01-metal-hut');
  await hold('w', 380);

  // A code lock on the door: the ghost, then its keypad to choose the code.
  await cmd('code-lock', 1500);
  await cmd('aim-door', 1200);
  await shot('02-lock-ghost');
  await cmd('place-lock', 1800);
  await shot('03-keypad-set');
  for (const k of [1, 9, 8, 4]) await cmd('key-' + k, 250);
  await wait(500);
  await shot('04-keypad-typed');
  await cmd('keypad-ok', 150);
  await shot('05-code-saved');
  await wait(1500);
  await cmd('slot-1', 800);
  await cmd('aim-door', 1200);
  await shot('06-locked-door');
  await cmd('stats', 800);

  // Managing it: open, then locked again.
  await cmd('lock-button', 1500);
  await shot('07-keypad-manage');
  await cmd('keypad-toggle', 1200);
  await shot('08-keypad-unlocked');
  await cmd('keypad-toggle', 800);
  await cmd('close-keypad', 800);

  // The same lock as someone else's: CÓDIGO, a wrong code shocks, the right one opens.
  await cmd('lock-stranger', 800);
  await cmd('aim-door', 1200);
  await shot('09-codigo-button');
  await cmd('interact', 1500);
  await shot('10-keypad-enter');
  for (const k of [1, 1, 1]) await cmd('key-' + k, 250);
  await wait(400);
  await shot('11-enter-typed');
  await cmd('key-1', 300);
  await cmd('keypad-ok', 120);
  await shot('12-wrong-code');
  await wait(1200);
  await cmd('stats', 800);
  await cmd('code-4321', 150);
  await shot('13-right-code');
  await wait(1500);
  await cmd('interact', 2200);
  await shot('14-door-open');
  await cmd('stats', 800);

  // A lock on a box.
  await hold('s', 500);
  await cmd('turn-100', 800);
  await cmd('box', 1500);
  await cmd('deploy', 1500);
  await cmd('slot-1', 600);
  await cmd('code-lock', 1500);
  await cmd('face-box', 1500);
  await cmd('place-lock', 1800);
  await shot('15-box-keypad');
  await cmd('code-0000', 2000);
  await cmd('slot-1', 600);
  await cmd('face-box', 1500);
  await shot('16-box-lock');
  await cmd('stats', 800);

  // The metal tools in hand, and the craft panel.
  await cmd('turn-180', 800);
  await cmd('metal-hatchet', 1800);
  await shot('17-metal-hatchet');
  await cmd('metal-pickaxe', 1800);
  await shot('18-metal-pickaxe');
  await cmd('craft-panel', 1500);
  await shot('19-craft-panel');
  await cmd('craft-panel', 600);

  log.push(`${stamp()} [test] code set: ${has('Code set on door')} wrong: ${has('Wrong code on door')} accepted: ${has('Code accepted on door')} box lock: ${has('Lock put on deployable')}`);
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|key|pageerror|error|warning)\]|Oxfall\] (Stats|Placed|Lock|Code|Wrong|Keypad|Door|Sheet|Metal|Wood)|rror|xception/.test(l)).join('\n'));
  await browser.close();
})();
