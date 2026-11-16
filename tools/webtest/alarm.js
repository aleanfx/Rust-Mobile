// Alarms and electric doors, alone: a sensor sets off a siren; a switch opens a door through a door controller.
// Usage: node alarm.js [outDir]   (against the build served on localhost:8080)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-alarm');
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

  // A hut ahead with a door, and a door controller outside it.
  await cmd('electric-door', 2500);
  check('the door controller goes by the door', has('controller Ok'), last('Electric door').replace(/.*Electric door/, 'Electric door'));
  // Behind the player: a generator, a switch, a sensor and a siren.
  await cmd('turn-180', 800);
  await cmd('look--20', 600);
  await place('generator', 'Generator', 40);
  await place('switch', 'ElectricSwitch', 40);
  await place('motion-sensor', 'MotionSensor', 40);
  await place('siren', 'SirenLight', 0);
  await cmd('fuel-generator', 1500);
  await cmd('close-box', 800);
  await cmd('wire:Generator-MotionSensor', 1200);
  await cmd('wire:MotionSensor-SirenLight', 1200);
  await cmd('wire:Generator-ElectricSwitch', 1200);
  await cmd('wire:ElectricSwitch-DoorController', 1200);
  const wires = log.filter(l => l.includes('[Oxfall] Wire'));
  check('cables run', wires.filter(l => l.includes(': Ok')).length === 4, wires.map(l => l.replace(/.*Wire/, 'Wire')).join(' | '));

  await power();
  check('the sensor watches', powerOf('MotionSensor').includes('running=True'), powerOf('MotionSensor').replace(/.*Power/, 'Power'));
  check('only strangers: the owner does not set off the siren', powerOf('SirenLight').includes('running=False'), powerOf('SirenLight').replace(/.*Power/, 'Power'));
  await cmd('sensor-mode', 1500);
  await power();
  check('set to everyone, it sees the owner: the siren goes', powerOf('SirenLight').includes('running=True'), powerOf('SirenLight').replace(/.*Power/, 'Power'));
  await cmd('time-22', 3000);
  await cmd('back-2', 800);
  await shot('01-siren-night');
  await wait(1500);
  await shot('02-siren-turning');
  await cmd('sensor-mode', 1500);
  await power();
  check('back to strangers only, quiet', powerOf('SirenLight').includes('running=False'), powerOf('SirenLight').replace(/.*Power/, 'Power'));

  // The door: shut while the switch is off, open while it is on.
  await cmd('time-12', 1500);
  await cmd('turn-180', 800);
  log.push('---');
  await cmd('door-state', 800);
  check('switch off: the door is shut', has('open=False') && !log.slice(log.lastIndexOf('---')).some(l => l.includes('open=True')), '');
  await cmd('toggle-switch', 2000);
  log.push('---');
  await cmd('door-state', 800);
  check('switch on: the controller opens it', log.slice(log.lastIndexOf('---')).some(l => l.includes('[Oxfall] Door') && l.includes('open=True')), '');
  await shot('03-door-open');
  await cmd('toggle-switch', 2000);
  log.push('---');
  await cmd('door-state', 800);
  check('switch off again: shut', !log.slice(log.lastIndexOf('---')).some(l => l.includes('open=True')), '');
  await shot('04-door-shut');

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(test|pageerror|error)\]|xception/.test(l)).slice(-50).join('\n'));
  console.log('\n' + results.join('\n'));
  await browser.close();
})();
