// Screenshots through the day and night: dusk, night with and without a torch, a campfire.
// Usage: node night.js [outDir]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-night');
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
  const cmd = async (c, ms = 2000) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} [cmd] ${c}`); await wait(ms); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };

  await page.goto('http://localhost:8080/index.html');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  log.push(`${stamp()} [test] icons: ${await waitForLog('item icons', 30000)}`);
  await wait(2000);
  await cmd('quality-high', 2500);
  await cmd('give-all', 1500);
  await cmd('time-17.5', 2500);
  await shot('1-afternoon');
  await cmd('time-19.6', 2500);
  await shot('2-dusk');
  await cmd('time-23', 2500);
  await shot('3-night-rock');
  await cmd('slot-2', 2500);
  await shot('4-night-torch');
  await cmd('slot-6', 2500);
  await shot('5-campfire-ghost');
  await cmd('place-fire', 2500);
  await cmd('slot-2', 1500);
  await shot('6-campfire-night');
  await cmd('view-forest', 3000);
  await shot('7-forest-night-torch');
  await cmd('time-6.4', 2500);
  await shot('8-dawn');
  // Survival: thirsty and hungry, drink at a pond, eat berries, die and wake up.
  await cmd('time-12', 1500);
  await cmd('hungry', 1000);
  await cmd('view-pond', 3000);
  await shot('9-pond');
  await cmd('slot-1', 800);
  await cmd('use', 1200);
  await cmd('stats', 1000);
  await cmd('nearest-berries', 2500);
  await shot('9b-berries');
  await cmd('use', 1200);
  await cmd('stats', 1000);
  await cmd('starve', 2000);
  await shot('10-dead');
  await cmd('wake', 2500);
  await shot('11-awake');
  await cmd('stats', 1000);
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error|warning)\]|Oxfall\] (Stats|Rendered|Icon|Built|Crafted)|rror|xception/.test(l)).join('\n'));
  await browser.close();
})();
