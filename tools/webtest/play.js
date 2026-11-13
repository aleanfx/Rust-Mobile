// Plays the web build in headless Chromium as a phone in landscape: waits for the world, takes
// screenshots, walks with the joystick, looks around, holds attack, and saves the console log.
// Usage: node play.js [outDir] [width] [height]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const out = process.argv[2] || path.join(__dirname, 'shots');
const W = parseInt(process.argv[3] || '844', 10), H = parseInt(process.argv[4] || '390', 10);
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);

(async () => {
  const browser = await chromium.launch({
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'],
  });
  const context = await browser.newContext({
    viewport: { width: W, height: H }, deviceScaleFactor: 1, isMobile: true, hasTouch: true,
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1',
  });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} [pageerror] ${e.message}`));
  const cdp = await context.newCDPSession(page);

  const touch = (type, points) => cdp.send('Input.dispatchTouchEvent', {
    type, touchPoints: points.map(p => ({ x: p.x, y: p.y, id: p.id, radiusX: 4, radiusY: 4, force: 1 })),
  });
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const shot = async name => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };

  await page.goto('http://localhost:8080/index.html');
  const started = await waitForLog('[Oxfall] World built', 240000);
  log.push(`${stamp()} [test] world built: ${started}`);
  await wait(4000);
  await shot('01-start');

  // Walk forward: thumb down on the left, pushed up, held.
  const stick = { x: Math.round(W * 0.16), y: Math.round(H * 0.72), id: 1 };
  await touch('touchStart', [stick]);
  for (let i = 1; i <= 6; i++) { await touch('touchMove', [{ ...stick, y: stick.y - i * 10 }]); await wait(30); }
  await wait(2500);
  await shot('02-walking');
  await wait(2500);
  await touch('touchEnd', []);
  await shot('03-walked');

  // Look right by dragging on the right side, then a little down.
  const look = { x: Math.round(W * 0.62), y: Math.round(H * 0.45), id: 2 };
  await touch('touchStart', [look]);
  for (let i = 1; i <= 20; i++) { await touch('touchMove', [{ ...look, x: look.x + i * 8, y: look.y + i * 1.5 }]); await wait(40); }
  await touch('touchEnd', []);
  await wait(1000);
  await shot('04-looked');

  // Hold attack (bottom-right button), using the HUD layout: 1080-high reference, matched on height.
  const k = H / 1080;
  const attack = { x: Math.round(W - 210 * k), y: Math.round(H - 230 * k), id: 3 };
  await touch('touchStart', [attack]);
  await wait(800);
  await shot('05-attacking');
  await wait(2200);
  await touch('touchEnd', []);

  // Tap the quality button twice (top right) and screenshot each tier.
  const quality = { x: Math.round(W - (24 + 150) * k), y: Math.round((14 + 35) * k), id: 4 };
  for (let i = 0; i < 2; i++) {
    await touch('touchStart', [quality]); await wait(120); await touch('touchEnd', []);
    await wait(2500);
    await shot(`06-quality-${i + 1}`);
  }

  await wait(6000); // let an FPS line or two reach the log
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n') + '\n');
  await browser.close();
  console.log(log.filter(l => /Oxfall|error|pageerror|test|Unity/i.test(l)).slice(-60).join('\n'));
})().catch(e => { console.error(e); fs.writeFileSync(path.join(out, 'console.log'), log.join('\n')); process.exit(1); });
