// Guns. Alone: the revolver in hand, a few shots at a boar (flash, smoke, the bullet striking), a
// reload, the blunderbuss and its spray, and the ARMAS tab. With a server: Beto shoots Ana with the
// revolver from 12 m until she falls, and both screens show it.
// Usage: node guns.js [outDir] [server]   (the build on :8080; a test server for the second half)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || path.join(__dirname, 'shots-guns');
const server = process.argv[3];
fs.mkdirSync(out, { recursive: true });
const log = [];
const t0 = Date.now();
const stamp = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);
const wait = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const browser = await chromium.launch({
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--enable-webgl'],
  });
  const open = async (name, query) => {
    const context = await browser.newContext({ viewport: { width: 844, height: 390 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
    const page = await context.newPage();
    page.on('console', m => log.push(`${stamp()} ${name} [${m.type()}] ${m.text()}`));
    page.on('pageerror', e => log.push(`${stamp()} ${name} [pageerror] ${e.message}`));
    await page.goto(`http://localhost:8080/index.html?${query}`);
    return page;
  };
  const waitForLog = async (needle, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) { if (log.some(l => l.includes(needle))) return true; await wait(250); }
    return false;
  };
  const count = needle => log.filter(l => l.includes(needle)).length;
  const cmd = async (page, who, c, ms = 1500) => { await page.evaluate(c => window.unityInstance.SendMessage('Oxfall', 'DebugCommand', c), c); log.push(`${stamp()} ${who} [cmd] ${c}`); await wait(ms); };
  const shot = async (page, name) => { await page.screenshot({ path: path.join(out, name + '.png') }); log.push(`${stamp()} [shot] ${name}`); };
  const focus = page => page.evaluate(() => { const c = document.querySelector('canvas'); if (c) { c.setAttribute('tabindex', '0'); c.focus(); } });

  // ---- Alone
  const solo = await open('Solo', 'solo');
  log.push(`${stamp()} [test] world built: ${await waitForLog('Solo [log] [Oxfall] World built', 240000)}`);
  await wait(3000);
  await focus(solo);
  await cmd(solo, 'Solo', 'quality-high', 2000);
  await cmd(solo, 'Solo', 'time-12', 1000);
  await cmd(solo, 'Solo', 'give-guns', 1000);
  await cmd(solo, 'Solo', 'revolver', 2000);
  await cmd(solo, 'Solo', 'spawn-boar', 2500);
  await cmd(solo, 'Solo', 'face-animal', 1500);
  await shot(solo, '01-revolver-aim');
  await cmd(solo, 'Solo', 'fire', 0);
  await shot(solo, '02-revolver-fire');
  await wait(400);
  await shot(solo, '03-revolver-smoke');
  for (let i = 0; i < 4; i++) { await cmd(solo, 'Solo', 'face-animal', 300); await cmd(solo, 'Solo', 'fire', 500); }
  await shot(solo, '04-revolver-after');
  await cmd(solo, 'Solo', 'fire', 300);
  await wait(500);
  await shot(solo, '05-revolver-reloading');
  await cmd(solo, 'Solo', 'stats', 600);
  await wait(3000);
  await cmd(solo, 'Solo', 'blunderbuss', 2000);
  await cmd(solo, 'Solo', 'turn-60', 800);
  await cmd(solo, 'Solo', 'nearest-stone', 1500);
  await cmd(solo, 'Solo', 'back-4', 1200);
  await shot(solo, '06-blunderbuss-aim');
  await cmd(solo, 'Solo', 'fire', 0);
  await shot(solo, '07-blunderbuss-fire');
  await wait(500);
  await shot(solo, '08-blunderbuss-smoke');
  await cmd(solo, 'Solo', 'craft-panel', 1200);
  await cmd(solo, 'Solo', 'craft-tab-2', 1500);
  await shot(solo, '09-craft-guns');
  await cmd(solo, 'Solo', 'craft-panel', 800);
  await cmd(solo, 'Solo', 'stats', 600);
  log.push(`${stamp()} [test] solo fired ${count('Solo [log] [Oxfall] Fired')} times`);
  await solo.context().close();

  // ---- Two players
  if (server) {
    const q = name => `server=${encodeURIComponent(server)}&name=${name}`;
    const ana = await open('Ana', q('Ana'));
    log.push(`${stamp()} [test] Ana online: ${await waitForLog('Ana [log] [Oxfall] Online', 240000)}`);
    const beto = await open('Beto', q('Beto'));
    log.push(`${stamp()} [test] Beto online: ${await waitForLog('Beto [log] [Oxfall] Online', 240000)}`);
    await wait(3000);
    await focus(ana);
    await focus(beto);
    for (const [p, who] of [[ana, 'Ana'], [beto, 'Beto']]) {
      await cmd(p, who, 'quality-high', 1200);
      await cmd(p, who, 'time-12', 800);
    }
    await cmd(beto, 'Beto', 'give-guns', 1500);
    await cmd(beto, 'Beto', 'revolver', 1500);
    await cmd(beto, 'Beto', 'face-player', 600);
    await cmd(beto, 'Beto', 'back-12', 800);
    await cmd(beto, 'Beto', 'face-player', 1000);
    await cmd(ana, 'Ana', 'face-player', 1000);
    await shot(beto, '10-beto-aims');
    const killed = () => log.some(l => l.includes('Beto [log] [Oxfall] Hurt: Ana') && l.includes('killed'));
    for (let i = 0; i < 8 && !killed(); i++) {
      await cmd(beto, 'Beto', 'face-player', 250);
      await cmd(beto, 'Beto', 'fire', 0);
      if (i === 0) { await shot(beto, '11-beto-fires'); await wait(300); await shot(ana, '12-ana-sees-shot'); }
      await wait(700);
    }
    await wait(1500);
    await shot(ana, '13-ana-down');
    await shot(beto, '14-beto-sees-her-down');
    await cmd(beto, 'Beto', 'stats', 600);
    log.push(`${stamp()} [test] hits on Ana: ${count('Beto [log] [Oxfall] Hurt: Ana by')} killed: ${killed()} Ana heard shots: ${count('Ana [log] [Oxfall] Gunshot')}`);
  }

  fs.writeFileSync(path.join(out, 'console.log'), log.join('\n'));
  console.log(log.filter(l => /\[(cmd|test|shot|pageerror|error)\]|Oxfall\] (Stats|Fired|Hurt|Online|Animal|Killed)|xception/.test(l)).join('\n'));
  await browser.close();
})();
