// The title screen: the island turning behind it, the name chip, "play online" with no server yet,
// then "play solo" into the game. Real taps, as on a phone. Usage: node title.js [outDir]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-title');
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);

(async () => {
  const browser = await chromium.launch({
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'],
  });
  const W = 844, H = 390;
  const context = await browser.newContext({
    viewport: { width: W, height: H }, deviceScaleFactor: 2, isMobile: true, hasTouch: true,
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1',
  });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} [pageerror] ${e.message}`));
  page.on('dialog', async d => { log.push(`${stamp()} [dialog] ${d.type()} ${d.message()}`); await d.accept('Alejo'); });
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const shot = async name => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };
  // The title is laid out on 1920×1080 matched to the height; buttons hang off the left middle.
  const s = H / 1080;
  const tap = async (label, x, y) => { await page.touchscreen.tap(x * s, H / 2 - y * s); log.push(`${stamp()} [tap] ${label}`); await wait(1500); };

  await page.goto('http://localhost:8080/index.html');
  log.push(`${stamp()} [test] title: ${await waitForLog('[Oxfall] Title screen', 240000)}`);
  await page.evaluate(() => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', 'quality-high'));
  await wait(4000);
  await shot('1-title');
  await wait(6000);
  await shot('2-title-later');
  await tap('name', 430, -300);
  await wait(1000);
  await shot('3-name');
  await tap('online', 430, 10);
  await shot('4-no-server');
  await tap('solo', 430, -180);
  log.push(`${stamp()} [test] in game: ${await waitForLog('[Oxfall] In the game', 10000)}`);
  await wait(3000);
  await shot('5-in-game');
  fs.writeFileSync(path.join(out, 'log.txt'), log.join('\n'));
  await browser.close();
})();
