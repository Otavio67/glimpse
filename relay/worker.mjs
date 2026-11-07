import { DurableObject } from 'cloudflare:workers';

const route = /^\/v1\/rooms\/([a-f0-9]{64})\/(host|guest)$/;
const hostKey = /^Bearer ([a-f0-9]{64})$/;
const maxPairs = 8;
const maxPacket = 32768;

export default {
  async fetch(request, env) {
    const path = new URL(request.url).pathname;
    if (request.method === 'GET' && path === '/health') return new Response('glimpse-relay-v1');
    const match = route.exec(path);
    if (request.method !== 'GET' || !match) return new Response(null, { status: 404 });
    if (request.headers.get('Upgrade')?.toLowerCase() !== 'websocket') return new Response(null, { status: 426 });
    const [, id, role] = match;
    if (role === 'host') {
      const key = hostKey.exec(request.headers.get('Authorization') ?? '');
      if (!key) return new Response(null, { status: 403 });
      const bytes = Uint8Array.from(key[1].match(/../g), pair => parseInt(pair, 16));
      const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
      const expected = Uint8Array.from(id.match(/../g), pair => parseInt(pair, 16));
      if (!crypto.subtle.timingSafeEqual(digest, expected)) return new Response(null, { status: 403 });
    }
    return env.ROOMS.get(env.ROOMS.idFromName(id)).fetch(request.url, {
      headers: { Upgrade: 'websocket' },
    });
  },
};

export class RelayRoom extends DurableObject {
  fetch(request) {
    const isHost = new URL(request.url).pathname.endsWith('/host');
    const sockets = this.ctx.getWebSockets().filter(socket => socket.readyState === 1);
    const waiting = sockets.find(socket => socket.deserializeAttachment()?.waiting);
    if (isHost && waiting) return new Response(null, { status: 409 });
    if (!isHost && !waiting) return new Response(null, { status: 404 });
    if (!isHost && sockets.length >= 2 * maxPairs + 1) return new Response(null, { status: 429 });

    const [client, server] = Object.values(new WebSocketPair());
    this.ctx.acceptWebSocket(server);
    if (isHost) {
      server.serializeAttachment({ waiting: true });
      server.send('waiting');
    } else {
      const pair = crypto.randomUUID();
      server.serializeAttachment({ pair });
      waiting.serializeAttachment({ pair });
      try { waiting.send('ready'); }
      catch { server.close(1011, 'Host disconnected'); }
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  peer(socket) {
    const pair = socket.deserializeAttachment()?.pair;
    if (!pair) return undefined;
    return this.ctx.getWebSockets().find(other => other !== socket && other.readyState === 1 &&
      other.deserializeAttachment()?.pair === pair);
  }

  webSocketMessage(socket, data) {
    const peer = this.peer(socket);
    if (!peer || !(data instanceof ArrayBuffer) || data.byteLength > maxPacket) {
      socket.close(1008, 'Invalid relay packet');
      return;
    }
    try { peer.send(data); }
    catch { socket.close(1011, 'Peer disconnected'); }
  }

  webSocketClose(socket) {
    this.peer(socket)?.close(1000, 'Peer disconnected');
  }

  webSocketError(socket) {
    this.peer(socket)?.close(1011, 'Peer disconnected');
    if (socket.readyState === 1) socket.close(1011, 'Relay connection lost');
  }
}
