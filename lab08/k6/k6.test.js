// k6.test.js - Validación básica de instalación y conectividad
// Ejecutar: k6 run lab08/k6/k6.test.js
// Con salida a InfluxDB: k6 run --out influxdb=http://localhost:8086/k6 lab08/k6/k6.test.js

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

export const errorRate = new Rate('errors');

export const options = {
  vus: 5,          // 5 usuarios virtuales simultáneos
  duration: '15s', // durante 15 segundos

  thresholds: {
    http_req_duration: ['p(95)<500'], // 95% de requests deben responder en <500ms
    errors: ['rate<0.1'],             // menos del 10% de errores
  },
};

const BASE_URL = 'http://localhost:5174';

export default function () {
  // GET /api/ranking - endpoint público sin autenticación
  const rankingRes = http.get(`${BASE_URL}/api/ranking?page=1&pageSize=5`);

  const ok = check(rankingRes, {
    'ranking status 200': (r) => r.status === 200,
    'ranking tiene datos': (r) => r.body && r.body.length > 0,
    'ranking responde en <300ms': (r) => r.timings.duration < 300,
  });

  errorRate.add(!ok);
  sleep(1);
}
