// Building tiers, the hammer, doors: screenshots of a twig hut, upgrading, a door opening, demolishing.
// Usage: node hammer.js [outDir]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-hammer');
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
  await cmd('build-hut', 2500);
  await shot('1-twig-hut');
  await cmd('hammer', 1500);
  await cmd('aim-wall', 2000);
  await shot('2-hammer-aim');
  await cmd('upgrade', 2000);
  await shot('3-wall-wood');
  await cmd('upgrade', 2000);
  await shot('4-wall-stone');
  await cmd('stats', 800);
  await cmd('upgrade-all-1', 2000);
  await cmd('aim-door', 1500);
  await shot('5-hut-wood');
  await cmd('door', 1500);
  await cmd('aim-door', 1500);
  await shot('6-door-ghost');
  await cmd('place-door', 2000);
  await cmd('slot-1', 1500);
  await cmd('aim-door', 1500);
  await shot('7-door-closed');
  await cmd('use-door', 2000);
  await shot('8-door-open');
  await cmd('stats', 800);
  await cmd('upgrade-all-2', 2000);
  await cmd('time-21.5', 2500);
  await cmd('slot-2', 2000);
  await shot('9-stone-hut-night');
  await cmd('time-12', 1500);
  await cmd('hammer', 1200);
  await cmd('aim-wall', 1500);
  await cmd('demolish', 800);
  await cmd('demolish', 2000);
  await shot('10-demolished');
  await cmd('stats', 800);
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error|warning)\]|Oxfall\] (Stats|Rendered|Icon|Built|Upgraded|Door|Demolished|Test hut)|rror|xception/.test(l)).join('\n'));
  await browser.close();
})();
