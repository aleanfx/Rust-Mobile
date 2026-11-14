// Two players on one server: a code lock one sets and the other must guess (a wrong code shocks, the
// right one opens), then a fight with a wooden spear: three blows, the victim falls, sees who killed
// them, and their bag lies where they fell for the other to empty. Before the fight they chat, and
// the fight opens with two arrows from 12 m. Needs a test server running
// (OXFALL_DEV=1 dotnet server/Oxfall.Server) and the web build on :8080.
// Usage: node fight.js [outDir] [server]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-fight');
const server = process.argv[3] || 'ws://localhost:7777/ws';
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);
const wait = ms => new Promise(r => setTimeout(r, ms));

async function open(browser, name) {
  const context = await browser.newContext({
    viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true,
  });
  const page = await context.newPage();
  page.on('console', m => log.push(`${stamp()} ${name} [${m.type()}] ${m.text()}`));
  page.on('pageerror', e => log.push(`${stamp()} ${name} [pageerror] ${e.message}`));
  await page.goto(`http://localhost:8080/index.html?server=${encodeURIComponent(server)}&name=${name}`);
  return page;
}

(async () => {
  const browser = await chromium.launch({
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'],
  });
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(500); }
    return false;
  };
  const has = needle => log.some(l => l.includes(needle));
  const cmd = async (page, who, c, ms = 1500) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} ${who} [cmd] ${c}`); await wait(ms); };
  const shot = async (page, name) => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const hold = async (page, who, key, ms) => { await page.keyboard.down(key); await wait(ms); await page.keyboard.up(key); log.push(`${stamp()} ${who} [key] ${key} ${ms}ms`); await wait(500); };
  const focus = page => page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });

  const ana = await open(browser, 'Ana');
  log.push(`${stamp()} [test] Ana online: ${await waitForLog('Ana [log] [Oxfall] Online', 240000)}`);
  const beto = await open(browser, 'Beto');
  log.push(`${stamp()} [test] Beto online: ${await waitForLog('Beto [log] [Oxfall] Online', 240000)}`);
  await wait(3000);
  await focus(ana);
  await focus(beto);
  for (const [p, who] of [[ana, 'Ana'], [beto, 'Beto']]) {
    await cmd(p, who, 'quality-high', 1200);
    await cmd(p, who, 'give-all', 1500);
    await cmd(p, who, 'time-12', 800);
  }

  // Beto steps back from the shared spawn; Ana sets a box down ahead and puts a code lock on it.
  await hold(beto, 'Beto', 's', 450);
  await cmd(ana, 'Ana', 'box', 1500);
  await cmd(ana, 'Ana', 'deploy', 2000);
  await cmd(ana, 'Ana', 'slot-1', 600);
  await cmd(ana, 'Ana', 'code-lock', 1500);
  await cmd(ana, 'Ana', 'face-box', 1500);
  await cmd(ana, 'Ana', 'place-lock', 2500);
  await shot(ana, '01-ana-keypad-set');
  await cmd(ana, 'Ana', 'code-2580', 2500);
  await cmd(ana, 'Ana', 'slot-1', 600);
  await cmd(ana, 'Ana', 'stats', 600);

  // Beto: the box asks him for the code. A wrong one shocks; the right one lets him in.
  await cmd(beto, 'Beto', 'slot-1', 600);
  await cmd(beto, 'Beto', 'face-box', 2000);
  await shot(beto, '02-beto-sees-lock');
  await cmd(beto, 'Beto', 'interact', 1800);
  await shot(beto, '03-beto-keypad-enter');
  await cmd(beto, 'Beto', 'code-1111', 300);
  await shot(beto, '04-beto-wrong-code');
  await wait(1500);
  await cmd(beto, 'Beto', 'stats', 600);
  await cmd(beto, 'Beto', 'code-2580', 2500);
  await cmd(beto, 'Beto', 'face-box', 1200);
  await cmd(beto, 'Beto', 'interact', 2000);
  await shot(beto, '05-beto-opens-box');
  await cmd(beto, 'Beto', 'close-box', 800);

  // They talk: Ana's line, Beto's answer (a tag in it shows as plain text), both screens.
  await cmd(ana, 'Ana', 'say:hola Beto, ¿vecinos?', 1500);
  await cmd(beto, 'Beto', 'say:<b>ya</b> abrí tu caja jaja', 1500);
  await shot(ana, '05b-ana-chat');
  await shot(beto, '05c-beto-chat');

  // The fight: Beto backs off and shoots Ana twice with his bow, then finishes her with the spear.
  await cmd(beto, 'Beto', 'bow', 1500);
  await cmd(beto, 'Beto', 'face-player', 600);
  await cmd(beto, 'Beto', 'back-12', 800);
  await cmd(beto, 'Beto', 'face-player', 1000);
  await shot(beto, '06a-beto-bow');
  for (let i = 0; i < 2; i++) {
    await cmd(beto, 'Beto', 'use', 450);
    if (i === 0) { await shot(beto, '06b-beto-arrow-flies'); await wait(700); await shot(ana, '06c-ana-shot'); }
    await cmd(beto, 'Beto', 'face-player', 900);
  }
  await cmd(beto, 'Beto', 'fwd-10.5', 800);
  await cmd(beto, 'Beto', 'spear', 1500);
  await cmd(beto, 'Beto', 'face-player', 1500);
  await shot(beto, '06-beto-spear');
  for (let i = 0; i < 3 && !has('killed'); i++) {
    await cmd(beto, 'Beto', 'use', 400);
    if (i === 0) { await shot(beto, '07-beto-hits'); await shot(ana, '08-ana-hurt'); }
    await cmd(beto, 'Beto', 'face-player', 800);
  }
  await wait(1500);
  await shot(ana, '09-ana-killed');
  await shot(beto, '10-beto-sees-ana-down');
  await cmd(beto, 'Beto', 'stats', 600);

  // Ana's bag: Beto empties it.
  await cmd(beto, 'Beto', 'face-bag', 1500);
  await shot(beto, '11-beto-sees-bag');
  await cmd(beto, 'Beto', 'open-bag', 2000);
  await shot(beto, '12-beto-bag-open');
  await cmd(beto, 'Beto', 'light', 2500);
  await shot(beto, '13-beto-took-all');
  await cmd(beto, 'Beto', 'stats', 600);

  // Ana wakes up on the beach.
  await cmd(ana, 'Ana', 'wake', 3000);
  await shot(ana, '14-ana-woke');
  await cmd(ana, 'Ana', 'stats', 600);

  // Ana leaves: she sleeps where she stands, Beto finds her and kills her in her sleep; when she
  // comes back she is dead, and told by whom.
  await cmd(ana, 'Ana', 'disconnect', 3000);
  await cmd(beto, 'Beto', 'stats', 600);
  await cmd(beto, 'Beto', 'approach-sleeper', 1500);
  await shot(beto, '15-beto-finds-sleeper');
  await cmd(beto, 'Beto', 'spear', 1200);
  const sleeperKilled = () => log.some(l => l.includes('Beto [log] [Oxfall] Sleeper hit') && l.includes('killed'));
  for (let i = 0; i < 4 && !sleeperKilled(); i++) {
    await cmd(beto, 'Beto', 'approach-sleeper', 300);
    await cmd(beto, 'Beto', 'use', 900);
    if (i === 0) await shot(beto, '16-beto-strikes-sleeper');
  }
  await wait(1500);
  await shot(beto, '17-sleeper-bag');
  await cmd(beto, 'Beto', 'stats', 600);
  await cmd(ana, 'Ana', 'reconnect', 6000);
  await shot(ana, '18-ana-killed-asleep');
  await cmd(ana, 'Ana', 'stats', 600);

  log.push(`${stamp()} [test] lock set: ${has('Ana [log] [Oxfall] Code set')} wrong: ${has('Beto [log] [Oxfall] Wrong code')} accepted: ${has('Beto [log] [Oxfall] Code accepted')}`);
  log.push(`${stamp()} [test] hits: ${log.filter(l => l.includes('Beto [log] [Oxfall] Hurt: Ana by')).length} killed: ${has('killed')} bag: ${has('Ana [log] [Oxfall] The player died')}`);
  log.push(`${stamp()} [test] sleeper: hits ${log.filter(l => l.includes('Beto [log] [Oxfall] Sleeper hit')).length}, killed ${sleeperKilled()}, Ana told ${has('Ana [log] [Oxfall] Sleeper hit')}`);
  log.push(`${stamp()} [test] chat: Ana heard ${log.filter(l => l.includes('Ana [log] [Oxfall] Chat:')).length}, Beto heard ${log.filter(l => l.includes('Beto [log] [Oxfall] Chat:')).length}`);
  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|key|pageerror|error)\]|Oxfall\] (Stats|Online|Hurt|Code|Wrong|Keypad|Lock|Opened|The server|The player|Chat|Sleeper|Facing sleeper|No sleepers)|xception/.test(l)).join('\n'));
  await browser.close();
})();
