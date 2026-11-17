// The bandits' new ways, solo: one sees this player and radios the others; he fires six rounds, then
// reloads (gun tipped, hand at it); badly hurt he runs for cover and bandages himself on one knee;
// someone lost is searched for where last seen. PASS/FAIL lines for each.
// Usage: node bandits2.js [outDir]   (solo, against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-bandits2');
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);
const results = [];
const check = (name, ok) => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}`); log.push(`${stamp()} [test] ${ok ? 'PASS' : 'FAIL'} ${name}`); };

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
  const cmd = async (c, ms = 1500) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} [cmd] ${c}`); await wait(ms); };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(250); }
    return false;
  };
  const since = (i, needle) => log.slice(i).some(l => l.includes(needle));
  // Polls the bandits list until a line matches.
  const pollBandits = async (re, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) {
      const i = log.length;
      await cmd('bandits', 400);
      const hit = log.slice(i).find(l => re.test(l));
      if (hit) return hit;
    }
    return null;
  };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  await waitForLog('item icons', 60000);
  await wait(2000);
  await cmd('quality-high', 2500);
  await cmd('time-12', 1500);
  await cmd('give-guns', 1500);
  await cmd('revolver', 1500);

  // In sight: he calls it in on the radio.
  await cmd('to-bandit', 2500);
  await shot('01-in-sight');
  check('a bandit noticed and fired', await waitForLog('Bitten by Bandit', 60000) || await waitForLog('Bandit busy', 1));
  check('his radio call heard', await waitForLog('Bandit radio heard', 20000));
  await cmd('heal', 300);
  const calls = await pollBandits(/Bandit \d+ Chase .*calls=[1-9]/, 15000);
  check('he is after this player and has called', !!calls);

  // Six rounds, then a reload: caught in the act.
  let reloading = null;
  for (let i = 0; i < 40 && !reloading; i++) {
    await cmd('heal', 200);
    reloading = await pollBandits(/act=Reloading/, 1500);
  }
  if (reloading) {
    await cmd('freeze-animals', 200);
    await cmd('close-bandit', 3000);
    await shot('02-reloading');
    await cmd('to-bandit', 1500);
    await cmd('thaw-animals', 300);
  }
  check('he runs dry and reloads', !!reloading);
  check('the reload is heard', await waitForLog('Bandit busy: Reloading', 15000));
  const strafed = log.some(l => /strafing=True/.test(l)) || !!(await pollBandits(/strafing=True/, 15000));
  check('he steps aside between shots', strafed);

  // Badly hurt: cover, then the bandage.
  await cmd('heal', 300);
  const i0 = log.length;
  await cmd('hurt-bandit', 800);
  check('badly hurt, he runs for cover', since(i0, 'act=Cover') || !!(await pollBandits(/act=(Cover|Healing)/, 3000)));
  const healing = await pollBandits(/act=Healing/, 20000);
  if (healing) {
    await cmd('freeze-animals', 200);
    await cmd('close-bandit', 3000);
    await shot('03-bandaging');
    await cmd('to-bandit', 1500);
    await cmd('thaw-animals', 300);
  }
  check('he bandages himself', !!healing);
  check('the bandage is heard', await waitForLog('Bandit busy: Healing', 15000));
  const healed = await pollBandits(/healed=True/, 20000);
  check('and is healed after', !!healed && /hp=(\d+)/.test(healed) && +healed.match(/hp=(\d+)/)[1] > 35);

  // Lost: gone far off, he searches where this player was last seen, then goes back.
  await cmd('heal', 300);
  await cmd('view-sea', 2000);
  const searching = await pollBandits(/act=Searching/, 40000);
  check('he searches where this player was last seen', !!searching);

  await cmd('stats', 800);
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => l.includes('[test]') || l.includes('[pageerror]') || l.includes('Exception')).join('\n'));
  console.log(results.join('\n'));
  await browser.close();
})();
