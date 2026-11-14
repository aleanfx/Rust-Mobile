// The weather: the same views clear, in rain, in fog and in a storm at night.
// Usage: node weather.js [outDir]   (solo, against the build on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-weather');
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

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 60000)}`);
  await wait(2000);
  await cmd('quality-high', 2500);
  await cmd('time-11', 1500);
  await cmd('hide-hud', 800);

  // A hill and a ruin: clear, raining, foggy; then a stormy night.
  const views = [['view-hill', 'hill'], ['view-ruin:0:20:20', 'ruin'], ['view-forest', 'forest']];
  for (const [weather, tag] of [[['rain-0', 'fog-0'], 'clear'], [['rain-1'], 'rain'], [['rain-0', 'fog-1'], 'fog']]) {
    for (const c of weather) await cmd(c, 2500);
    for (const [view, name] of views) {
      await cmd(view, 3500);
      await shot(`${tag}-${name}`);
    }
  }
  await cmd('weather-auto', 500);
  await cmd('rain-1', 500);
  await cmd('fog-0.6', 500);
  await cmd('time-22', 4000);
  await cmd('view-ruin:0:20:20', 3500);
  await shot('storm-night-ruin');
  await wait(9000);
  await shot('storm-night-ruin-2');
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('Viewing') || l.includes('weather') || l.includes('[pageerror]') || l.includes('Exception') || l.includes('Unknown debug')).join('\n'));
  await browser.close();
})();
