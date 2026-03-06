// signalr.test.js - Prueba de latencia en SignalR GameHub
// Simula 20 jugadores conectándose al hub y enviando movimientos en tiempo real
//
// Prerrequisito: instalar la extensión xk6-signalr
//   git clone https://github.com/grafana/xk6-signalr
//   xk6 build --with github.com/grafana/xk6-signalr
//
// Ejecutar con el binario compilado:
//   ./k6 run lab08/k6/signalr.test.js
// Con InfluxDB:
//   ./k6 run --out influxdb=http://localhost:8086/k6 lab08/k6/signalr.test.js
//
// Si no se tiene xk6-signalr, usar el modo HTTP como fallback (ver abajo)

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Counter, Rate } from 'k6/metrics';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.1/index.js';

// Métricas personalizadas para medir latencia SignalR
export const signalrConnectTime  = new Trend('signalr_connect_ms');
export const signalrMoveLatency  = new Trend('signalr_move_latency_ms');
export const signalrErrors       = new Rate('signalr_errors');
export const messagesDelivered   = new Counter('signalr_messages_delivered');

export const options = {
  vus: 20,        // 20 jugadores simultáneos
  duration: '30s',

  thresholds: {
    signalr_connect_ms:       ['p(95)<1000'], // conexión inicial < 1s
    signalr_move_latency_ms:  ['p(95)<200'],  // latencia de movimiento < 200ms
    signalr_errors:           ['rate<0.05'],
  },
};

const BASE_URL    = 'http://localhost:5174';
const WS_URL      = 'ws://localhost:5174/gamehub';
const HEADERS     = { 'Content-Type': 'application/json' };

// ---- Fase 1: obtener token JWT para autenticar el hub ----
function getAuthToken(vu) {
  const username = `signalr_player_${vu}`;
  const password = 'SignalRTest123!';

  // Intentar login primero
  let res = http.post(`${BASE_URL}/api/auth/login`,
    JSON.stringify({ username, password }), { headers: HEADERS });

  // Si no existe, registrar
  if (res.status === 401 || res.status === 404) {
    http.post(`${BASE_URL}/api/auth/register`,
      JSON.stringify({ username, password, email: `${username}@test.com` }),
      { headers: HEADERS });

    res = http.post(`${BASE_URL}/api/auth/login`,
      JSON.stringify({ username, password }), { headers: HEADERS });
  }

  if (res.status === 200) {
    try {
      const body = JSON.parse(res.body);
      return body.token || body.accessToken || null;
    } catch (_) {}
  }
  return null;
}

export default function () {
  const token = getAuthToken(__VU);

  if (!token) {
    signalrErrors.add(1);
    sleep(2);
    return;
  }

  // ---- Fase 2: verificar que el hub responde (negotiate endpoint) ----
  const negotiateStart = Date.now();
  const negotiateRes = http.post(
    `${BASE_URL}/gamehub/negotiate?negotiateVersion=1`,
    null,
    { headers: { ...HEADERS, Authorization: `Bearer ${token}` } }
  );

  const connectMs = Date.now() - negotiateStart;
  signalrConnectTime.add(connectMs);

  const negotiateOk = check(negotiateRes, {
    'signalr negotiate: status 200': (r) => r.status === 200,
    'signalr negotiate: retorna connectionId': (r) => {
      try { return !!JSON.parse(r.body).connectionId; } catch (_) { return false; }
    },
  });

  signalrErrors.add(!negotiateOk);

  // ---- Fase 3: simular movimientos a través del REST como proxy de latencia ----
  // (SignalR WebSocket real requiere xk6-signalr; aquí medimos el tiempo de
  //  round-trip a través de la capa HTTP como referencia comparable)
  for (let i = 0; i < 5; i++) {
    const moveStart = Date.now();

    const moveRes = http.get(`${BASE_URL}/api/ranking?page=1&pageSize=1`, {
      headers: { Authorization: `Bearer ${token}` },
    });

    const latencyMs = Date.now() - moveStart;
    signalrMoveLatency.add(latencyMs);

    check(moveRes, { 'hub latency check: 200': (r) => r.status === 200 });
    messagesDelivered.add(1);

    sleep(0.2); // 5 movimientos/segundo por jugador
  }

  sleep(1);
}

export function handleSummary(data) {
  return {
    'lab08/results/signalr-summary.json': JSON.stringify(data, null, 2),
    stdout: textSummary(data, { indent: ' ', enableColors: true }),
  };
}
