// auth.test.js - Prueba de carga en endpoints de autenticación
// Simula 100 usuarios registrándose y haciendo login simultáneamente
// Ejecutar: k6 run lab08/k6/auth.test.js
// Con InfluxDB: k6 run --out influxdb=http://localhost:8086/k6 lab08/k6/auth.test.js

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.1/index.js';

export const registerErrors = new Rate('register_errors');
export const loginErrors    = new Rate('login_errors');
export const registerTime   = new Trend('register_duration_ms');
export const loginTime      = new Trend('login_duration_ms');
export const httpErrors4xx  = new Counter('http_4xx_errors');
export const httpErrors5xx  = new Counter('http_5xx_errors');

export const options = {
  stages: [
    { duration: '10s', target: 20  }, // Ramp-up: 0 → 20 usuarios en 10s
    { duration: '30s', target: 100 }, // Carga: subir a 100 usuarios simultáneos
    { duration: '20s', target: 100 }, // Sostener 100 usuarios durante 20s
    { duration: '10s', target: 0   }, // Ramp-down: bajar a 0
  ],

  thresholds: {
    http_req_duration:    ['p(95)<800', 'p(99)<1500'],
    register_errors:      ['rate<0.05'], // menos de 5% de errores en registro
    login_errors:         ['rate<0.05'], // menos de 5% de errores en login
    http_req_failed:      ['rate<0.1'],
  },
};

const BASE_URL = 'http://localhost:5174';
const HEADERS  = { 'Content-Type': 'application/json' };

export default function () {
  // Cada VU usa un username único basado en su ID y timestamp
  const username = `loadtest_user_${__VU}_${Date.now()}`;
  const password = 'LoadTest123!';

  group('Registro de usuario', () => {
    const payload = JSON.stringify({ username, password, email: `${username}@test.com` });
    const res = http.post(`${BASE_URL}/api/auth/register`, payload, { headers: HEADERS });

    registerTime.add(res.timings.duration);

    const ok = check(res, {
      'registro: status 200 o 201': (r) => r.status === 200 || r.status === 201,
      'registro: body tiene token o mensaje': (r) => r.body && r.body.length > 0,
    });

    registerErrors.add(!ok);

    if (res.status >= 400 && res.status < 500) httpErrors4xx.add(1);
    if (res.status >= 500) httpErrors5xx.add(1);
  });

  sleep(0.5);

  group('Login de usuario', () => {
    const payload = JSON.stringify({ username, password });
    const res = http.post(`${BASE_URL}/api/auth/login`, payload, { headers: HEADERS });

    loginTime.add(res.timings.duration);

    let token = null;
    if (res.status === 200) {
      try {
        const body = JSON.parse(res.body);
        token = body.token || body.accessToken || null;
      } catch (_) {}
    }

    const ok = check(res, {
      'login: status 200': (r) => r.status === 200,
      'login: retorna token JWT': (_) => token !== null,
      'login: responde en <500ms': (r) => r.timings.duration < 500,
    });

    loginErrors.add(!ok);

    if (res.status >= 400 && res.status < 500) httpErrors4xx.add(1);
    if (res.status >= 500) httpErrors5xx.add(1);

    // Si se obtuvo token, hacer logout para limpiar la sesión en Redis
    if (token) {
      sleep(0.2);
      http.post(`${BASE_URL}/api/auth/logout`, null, {
        headers: { ...HEADERS, Authorization: `Bearer ${token}` },
      });
    }
  });

  sleep(1);
}

export function handleSummary(data) {
  return {
    'lab08/results/auth-summary.json': JSON.stringify(data, null, 2),
    stdout: textSummary(data, { indent: ' ', enableColors: true }),
  };
}
