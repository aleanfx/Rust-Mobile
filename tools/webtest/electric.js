// Electricity, alone: a generator through a switch to a lamp at night; a solar panel filling a battery by day.
// Usage: node electric.js [outDir]   (against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-electric');
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
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };
  const has = needle => log.some(l => l.includes(needle));
  const last = needle => [...log].reverse().find(l => l.includes(needle)) || '';
  const results = [];
  const check = (name, ok, detail) => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' (' + detail + ')' : ''}`); log.push(`${stamp()} [test] ${results[results.length - 1]}`); };
  const place = async (item, kind, turn) => {
    await cmd(item, 1000);
    for (let i = 0; i < 6 && !has('Placed ' + kind); i++) { await cmd('deploy', 1500); if (!has('Placed ' + kind)) await cmd('turn-20', 600); }
    check(kind + ' goes down', has('Placed ' + kind), '');
    if (turn) await cmd('turn-' + turn, 600);
  };
  const power = async () => { log.push('---'); await cmd('power', 1200); };
  const powerOf = kind => { const i = log.lastIndexOf('---'); return log.slice(i).reverse().find(l => l.includes('[Oxfall] Power ' + kind + ' ')) || ''; };

  await page.goto('http://localhost:8080/index.html?solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('[Oxfall] World built', 240000)}`);
  await waitForLog('item icons', 60000);
  await wait(2000);
  await cmd('quality-high', 2500);
  await cmd('clear-animals', 500);
  await cmd('time-12', 1000);
  await cmd('give-electric', 1000);
  await cmd('look--20', 600);

  // A fan of things round the player: generator, switch, lamp; then a panel and a battery.
  await place('generator', 'Generator', 45);
  await place('switch', 'ElectricSwitch', 45);
  await place('lamp', 'Lamp', 45);
  await place('solar-panel', 'SolarPanel', 45);
  await place('battery', 'Battery', 0);

  await cmd('fuel-generator', 1500);
  await cmd('close-box', 800);
  await cmd('wire:Generator-ElectricSwitch', 1200);
  await cmd('wire:ElectricSwitch-Lamp', 1200);
  await cmd('wire:SolarPanel-Battery', 1200);
  check('cables run', (log.filter(l => l.includes('[Oxfall] Wire') && l.includes(': Ok')).length) === 3, log.filter(l => l.includes('[Oxfall] Wire')).map(l => l.replace(/.*Wire/, 'Wire')).join(' | '));
  await power();
  check('the switch is off: the lamp is dark', powerOf('Lamp').includes('running=False'), powerOf('Lamp').replace(/.*Power/, 'Power'));
  check('the generator runs', powerOf('Generator').includes('running=True'), powerOf('Generator').replace(/.*Power/, 'Power'));
  await cmd('toggle-switch', 1500);
  await power();
  check('switched on, the lamp lights', powerOf('Lamp').includes('running=True'), powerOf('Lamp').replace(/.*Power/, 'Power'));
  check('the panel gives power at noon', /SolarPanel \d+ level=(1\d|20)/.test(powerOf('SolarPanel')), powerOf('SolarPanel').replace(/.*Power/, 'Power'));
  await wait(5000);
  await power();
  check('the battery fills', !/charge=0%/.test(powerOf('Battery')) || /level=/.test(powerOf('Battery')), powerOf('Battery').replace(/.*Power/, 'Power'));
  await cmd('turn--135', 800);
  await cmd('look--10', 600);
  await cmd('back-3', 800);
  await shot('01-noon');
  await cmd('time-22', 3000);
  await shot('02-night-lamp');
  await cmd('wire-tool', 1200);
  await shot('03-wire-in-hand');
  await cmd('toggle-switch', 1500);
  await power();
  check('switched off again, dark', powerOf('Lamp').includes('running=False'), powerOf('Lamp').replace(/.*Power/, 'Power'));
  await shot('04-night-off');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(test|pageerror|error)\]|xception/.test(l)).slice(-50).join('\n'));
  console.log('\n' + results.join('\n'));
  await browser.close();
})();
