// Building up, solo: a two-storey house (a room with a doorway and windows, a stairwell with spiral
// stairs, an upper room with windows all round), as twig, wood and stone; the stairs hold a player
// dropped onto them; the view from upstairs; and the building plan's ghost aiming at the storeys.
// Usage: node storeys.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-storeys');
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
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(250); }
    return false;
  };
  const lastPos = () => {
    const line = [...log].reverse().find(l => l.includes('[Oxfall] Stats:'));
    const m = line && line.match(/pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)/);
    return m ? [+m[1], +m[2], +m[3]] : null;
  };

  await page.goto('http://localhost:8080/index.html?solo');
  const built = await waitForLog('[Oxfall] World built', 240000);
  log.push(`${stamp()} [test] world built: ${built}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });
  await cmd('quality-high', 2500);
  await cmd('give-all', 1200);
  await cmd('time-12', 1500);

  // The house, 5 m ahead; then step back to see it whole.
  await cmd('build-tower', 3000);
  await cmd('back-7', 800);
  await cmd('look-12', 2500);
  await shot('01-house-twig');
  await cmd('upgrade-all-1', 2500);
  await shot('02-house-wood');
  await cmd('upgrade-all-2', 2500);
  await shot('03-house-stone');
  await cmd('turn-35', 2000);
  await shot('03b-house-stone-side');

  // Dropped onto the middle of the stairs: they hold.
  await cmd('on-stairs', 3500);
  await cmd('stats', 800);
  const dropLine = [...log].reverse().find(l => l.includes('Dropped onto the stairs'));
  const stepTop = dropLine && +dropLine.match(/step 9 top is ([-\d.]+)/)[1];
  const onStairs = lastPos();
  log.push(`${stamp()} [test] on the stairs at y=${onStairs && onStairs[1]} (step 9 top ${stepTop}): ${onStairs && Math.abs(onStairs[1] - stepTop) < 0.35}`);
  await cmd('look--35', 2000);
  await shot('04-on-the-stairs');

  // Upstairs, looking out of a window.
  await cmd('tower-top', 3000);
  await cmd('stats', 800);
  const up = lastPos();
  log.push(`${stamp()} [test] upstairs at y=${up && up[1]}`);
  await cmd('look-0', 2000);
  await shot('05-upstairs-window');
  await cmd('turn-180', 2000);
  await shot('06-upstairs-stairwell');

  // The building plan up there: a window ghost on this storey, then on the next one up.
  await cmd('build', 1500);
  await cmd('turn-180', 800);
  for (let i = 0; i < 3; i++) await cmd('piece', 600);
  await cmd('look-0', 2000);
  await shot('07-ghost-window-here');
  await cmd('look-40', 2000);
  await shot('08-ghost-next-storey');
  await cmd('place', 1500);
  await cmd('stats', 800);

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('[Oxfall] Test tower') || l.includes('Tower:') || l.includes('Built ') || l.includes('[pageerror]') || l.includes('[error]') || l.includes('Exception')).join('\n'));
  await browser.close();
})();
