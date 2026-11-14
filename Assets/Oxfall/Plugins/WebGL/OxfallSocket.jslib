// The browser's WebSocket for the web build (the .NET socket classes do not run in WebGL).
// C# polls: messages that arrive wait in a queue until OxfallSocketReceive copies them out.
mergeInto(LibraryManager.library, {
  OxfallSocketOpen: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    if (!Module.oxfallSockets) { Module.oxfallSockets = {}; Module.oxfallNextSocket = 1; }
    var id = Module.oxfallNextSocket++;
    var entry = { queue: [], state: 0, error: '' };
    try {
      var ws = new WebSocket(url);
      ws.binaryType = 'arraybuffer';
      ws.onopen = function () { entry.state = 1; };
      ws.onmessage = function (e) { if (e.data instanceof ArrayBuffer) entry.queue.push(new Uint8Array(e.data)); };
      ws.onerror = function () { entry.error = 'error'; };
      ws.onclose = function (e) { entry.state = 3; if (!entry.error) entry.error = 'closed ' + e.code; };
      entry.ws = ws;
    } catch (e) {
      entry.state = 3;
      entry.error = String(e);
    }
    Module.oxfallSockets[id] = entry;
    return id;
  },

  // 0 connecting, 1 open, 3 closed.
  OxfallSocketState: function (id) {
    var entry = Module.oxfallSockets && Module.oxfallSockets[id];
    return entry ? entry.state : 3;
  },

  OxfallSocketSend: function (id, dataPtr, length) {
    var entry = Module.oxfallSockets && Module.oxfallSockets[id];
    if (!entry || entry.state !== 1) return 0;
    entry.ws.send(HEAPU8.slice(dataPtr, dataPtr + length));
    return 1;
  },

  // The size of the next waiting message, or -1 when none.
  OxfallSocketNextLength: function (id) {
    var entry = Module.oxfallSockets && Module.oxfallSockets[id];
    return entry && entry.queue.length > 0 ? entry.queue[0].length : -1;
  },

  // Copies the next message out (at most maxLength bytes); returns its length.
  OxfallSocketReceive: function (id, bufferPtr, maxLength) {
    var entry = Module.oxfallSockets && Module.oxfallSockets[id];
    if (!entry || entry.queue.length === 0) return -1;
    var message = entry.queue.shift();
    var n = Math.min(message.length, maxLength);
    HEAPU8.set(message.subarray(0, n), bufferPtr);
    return n;
  },

  OxfallSocketClose: function (id) {
    var entry = Module.oxfallSockets && Module.oxfallSockets[id];
    if (!entry) return;
    try { if (entry.ws) entry.ws.close(); } catch (e) {}
    delete Module.oxfallSockets[id];
  }
});
