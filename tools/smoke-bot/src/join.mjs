/**
 * Minimal join smoke (ADR §58).
 * Requires a running Zenith with offline (or self-signed) auth accept.
 *
 * Exit 0 = reached spawn / in-game-ish success.
 * Exit 1 = timeout or error.
 */
import bedrock from 'bedrock-protocol';

const host = process.env.ZENITH_HOST ?? '127.0.0.1';
const port = Number(process.env.ZENITH_PORT ?? '19132');
const username = process.env.ZENITH_BOT_USERNAME ?? 'ZenithSmoke';
const version = process.env.ZENITH_BOT_VERSION ?? '1.26.33';
const timeoutMs = Number(process.env.ZENITH_SMOKE_TIMEOUT_MS ?? '30000');

let settled = false;

function finish(code, message) {
  if (settled) return;
  settled = true;
  if (message) {
    const line = code === 0 ? message : `FAIL: ${message}`;
    console[code === 0 ? 'log' : 'error'](line);
  }
  try {
    client?.close?.();
  } catch {
    /* ignore */
  }
  process.exit(code);
}

console.log(
  `smoke:join → ${host}:${port} user=${username} version=${version} timeout=${timeoutMs}ms`,
);

const client = bedrock.createClient({
  host,
  port,
  username,
  offline: true,
  version,
});

const timer = setTimeout(() => {
  finish(1, `timeout after ${timeoutMs}ms (is Zenith up? auth.accept include offline?)`);
}, timeoutMs);

function clearAndOk(reason) {
  clearTimeout(timer);
  finish(0, `OK: ${reason}`);
}

client.on('error', (err) => {
  clearTimeout(timer);
  finish(1, err?.message ?? String(err));
});

client.on('kick', (reason) => {
  clearTimeout(timer);
  finish(1, `kicked: ${typeof reason === 'string' ? reason : JSON.stringify(reason)}`);
});

// Prismarine emits different milestones across versions — accept first strong signal.
client.on('join', () => {
  console.log('event: join');
});

client.on('spawn', () => {
  console.log('event: spawn');
  clearAndOk('spawn');
});

client.on('start_game', () => {
  console.log('event: start_game');
});

client.on('close', () => {
  if (!settled) {
    clearTimeout(timer);
    finish(1, 'connection closed before spawn');
  }
});
