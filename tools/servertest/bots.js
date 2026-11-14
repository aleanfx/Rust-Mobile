// Bots for the Oxfall server: each joins, walks in a circle and checks what the server sends back.
// Usage: node bots.js [url] [bots] [seconds]     e.g. node bots.js ws://localhost:7777/ws 20 10
// Exits 1 if any check fails. Uses Node's built-in WebSocket (Node 22+).
const url = process.argv[2] || 'ws://localhost:7777/ws';
const count = +(process.argv[3] || 5);
const seconds = +(process.argv[4] || 6);
const VERSION = 1;
const T = { Hello: 1, Move: 2, Welcome: 10, Rejected: 11, Joined: 12, Left: 13, Snapshot: 14, Correction: 15 };

function hello(name) {
  const n = Buffer.from(name, 'utf8');
  const b = Buffer.alloc(1 + 2 + 2 + n.length);
  b.writeUInt8(T.Hello, 0); b.writeUInt16LE(VERSION, 1); b.writeUInt16LE(n.length, 3); n.copy(b, 5);
  return b;
}
function move(x, y, z, yaw, teleport) {
  const b = Buffer.alloc(1 + 16 + 1);
  b.writeUInt8(T.Move, 0);
  b.writeFloatLE(x, 1); b.writeFloatLE(y, 5); b.writeFloatLE(z, 9); b.writeFloatLE(yaw, 13);
  b.writeUInt8(teleport ? 1 : 0, 17);
  return b;
}

const fails = [];
function check(ok, what) { if (!ok) fails.push(what); }

function bot(i) {
  return new Promise(resolve => {
    const s = { id: 0, joined: new Set(), left: 0, snapshots: 0, maxSeen: 0, corrections: 0, bytes: 0 };
    const ws = new WebSocket(url);
    ws.binaryType = 'arraybuffer';
    let timer, t0 = Date.now();
    const cx = (i % 10) * 6, cz = Math.floor(i / 10) * 6;
    ws.onopen = () => {
      ws.send(hello('Bot' + i));
      timer = setInterval(() => {
        const t = (Date.now() - t0) / 1000, a = t * 1.2 + i;
        ws.send(move(cx + Math.cos(a) * 3, 5, cz + Math.sin(a) * 3, (a * 57.3) % 360, false));
      }, 1000 / 15);
      // Bot 0 tries to cheat once: a 500 m jump must be refused with a correction.
      if (i === 0) setTimeout(() => ws.send(move(500, 5, 500, 0, false)), 2000);
    };
    ws.onmessage = e => {
      const b = Buffer.from(e.data);
      s.bytes += b.length;
      switch (b[0]) {
        case T.Welcome: s.id = b.readUInt16LE(1); s.seed = b.readInt32LE(3); break;
        case T.Joined: s.joined.add(b.readUInt16LE(1)); break;
        case T.Left: s.left++; break;
        case T.Snapshot: s.snapshots++; s.maxSeen = Math.max(s.maxSeen, b.readUInt16LE(5)); break;
        case T.Correction: s.corrections++; break;
        case T.Rejected: s.rejected = b.toString('utf8', 3); break;
      }
    };
    ws.onerror = e => { s.error = String(e.message || e.type); };
    setTimeout(() => { clearInterval(timer); ws.close(); setTimeout(() => resolve(s), 300); }, seconds * 1000 + i * 20);
  });
}

(async () => {
  const results = await Promise.all([...Array(count).keys()].map(bot));
  results.forEach((s, i) => {
    check(s.id > 0, `bot ${i} got no Welcome (${s.rejected || s.error || 'nothing'})`);
    check(s.seed === 20261113, `bot ${i} seed ${s.seed}`);
    check(s.maxSeen === count, `bot ${i} saw at most ${s.maxSeen} of ${count} players`);
    check(s.joined.size === count - 1, `bot ${i} heard of ${s.joined.size} others, not ${count - 1}`);
    check(s.snapshots >= seconds * 15, `bot ${i} got only ${s.snapshots} snapshots in ${seconds} s`);
  });
  check(results[0].corrections >= 1, 'the 500 m jump was not corrected');
  const kbps = results.reduce((a, s) => a + s.bytes, 0) / count / seconds / 1024;
  console.log(`${count} bots, ${seconds} s: ids ${results.map(s => s.id).join(',')}, ` +
    `snapshots ~${Math.round(results[0].snapshots / seconds)}/s, ${kbps.toFixed(1)} KB/s per client, ` +
    `corrections ${results[0].corrections}`);
  if (fails.length) { console.log('FAIL\n' + fails.join('\n')); process.exit(1); }
  console.log('OK');
})();
