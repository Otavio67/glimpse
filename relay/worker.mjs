import { DurableObject } from 'cloudflare:workers';

const route = /^\/v1\/rooms\/([a-f0-9]{64})\/(host|guest)$/;
const inviteRoute = /^\/v2\/invites\/([a-f0-9]{64})$/;
const hostKey = /^Bearer ([a-f0-9]{64})$/;
const maxPairs = 8;
const maxPacket = 32768;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname;
    if (request.method === 'GET' && path === '/health') return new Response('glimpse-relay-v1');
    if (request.method === 'GET' && path === '/v2/health') return new Response('glimpse-relay-v2');
    const invite = inviteRoute.exec(path);
    if (request.method === 'GET' && invite) {
      const headers = new Headers();
      if (request.headers.get('Upgrade')?.toLowerCase() === 'websocket') {
        const sealed = request.headers.get('X-Glimpse-Invite');
        if (!sealed || !/^[A-Za-z0-9_-]{80,512}$/.test(sealed)) return new Response(null, { status: 400 });
        headers.set('Upgrade', 'websocket');
        headers.set('X-Glimpse-Invite', sealed);
      }
      return env.ROOMS.get(env.ROOMS.idFromName('invite:' + invite[1])).fetch(request.url, { headers });
    }
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
    const url = new URL(request.url);
    if (inviteRoute.test(url.pathname)) return this.invite(request);
    const isHost = url.pathname.endsWith('/host');
    const ice = url.searchParams.get('ice') === '1';
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
      server.serializeAttachment({ pair, ice });
      waiting.serializeAttachment({ pair, ice });
      try { waiting.send(ice ? 'ice' : 'ready'); }
      catch { server.close(1011, 'Host disconnected'); }
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  invite(request) {
    const active = this.ctx.getWebSockets().find(socket => socket.readyState === 1);
    const sealed = request.headers.get('X-Glimpse-Invite');
    if (!sealed) {
      const value = active?.deserializeAttachment()?.invite;
      return new Response(value ?? null, { status: value ? 200 : 404, headers: { 'Cache-Control': 'no-store' } });
    }
    if (active) return new Response(null, { status: 409 });
    const [client, server] = Object.values(new WebSocketPair());
    this.ctx.acceptWebSocket(server);
    server.serializeAttachment({ invite: sealed });
    server.send('waiting');
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
    const ice = socket.deserializeAttachment()?.ice;
    const valid = ice ? typeof data === 'string' && data.length <= 16384 : data instanceof ArrayBuffer && data.byteLength <= maxPacket;
    if (!peer || !valid) {
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
