# Actividad 3: Diseño de Base de Datos para Escalabilidad

## Connection Pooling con Npgsql

Sin connection pooling, cada request HTTP abre una nueva conexión TCP a PostgreSQL, la usa y la cierra. Esto tiene un costo alto: el handshake TCP + autenticación PostgreSQL puede tomar 5-20 ms por conexión. Con un pool, las conexiones se reutilizan entre requests: el tiempo de obtener una conexión cae a microsegundos.

Npgsql (el driver de PostgreSQL para .NET) tiene connection pooling integrado y se configura directamente en el connection string. Los parámetros clave son `MaxPoolSize` (máximo de conexiones simultáneas al servidor), `MinPoolSize` (conexiones que se mantienen abiertas siempre, listas para usar) y `Connection Idle Lifetime` (segundos antes de cerrar una conexión inactiva).

**Archivo:** `BattleTanks-Backend/appsettings.json`

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Database=battletanks;Username=postgres;Password=...;MaxPoolSize=20;MinPoolSize=2;Connection Idle Lifetime=60",
  "Redis": "localhost:6379"
}
```

**Archivo:** `BattleTanks-Backend/Program.cs`

```csharp
// CommandTimeout: si una consulta tarda mas de 30 segundos, se cancela automaticamente
builder.Services.AddDbContext<BattleTanksDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.CommandTimeout(30)
    ));
```

**Parámetros del pool configurados:**

| Parámetro | Valor | Descripción |
|-----------|-------|-------------|
| MaxPoolSize | 20 | Máximo de conexiones abiertas simultáneamente |
| MinPoolSize | 2 | Conexiones siempre abiertas (evita cold start) |
| Connection Idle Lifetime | 60 s | Cierra conexiones inactivas para liberar recursos |
| CommandTimeout | 30 s | Cancela queries que se bloqueen (evita timeouts largos) |

[PANTALLAZO: appsettings.json con el connection string mostrando los parametros de pooling]

[PANTALLAZO: Program.cs con la configuracion de Npgsql y CommandTimeout]

---

## Conceptos de Sharding

El sharding es la técnica de dividir los datos de una tabla en múltiples bases de datos o nodos según un criterio (shard key). En lugar de una sola tabla con todos los datos, cada shard contiene un subconjunto.

Para BattleTanks, la shard key natural sería la región del jugador (North America, Europe, etc.), porque los jugadores de una región juegan principalmente entre sí, reduciendo la latencia de red y la carga en cada shard.

**Esquema conceptual de sharding por región:**

```
PostgreSQL Shard NA (us-east-1)         PostgreSQL Shard EU (eu-west-1)
├── Players (region = 'NA')             ├── Players (region = 'EU')
├── GameSessions (region = 'NA')        ├── GameSessions (region = 'EU')
└── Scores (PlayerId -> NA players)     └── Scores (PlayerId -> EU players)

Ranking Global Agregado (Redis)
└── ranking:global -> SortedSet con top de todos los shards
```

La implementación en EF Core requeriría múltiples `DbContext` configurados con diferentes connection strings, y lógica de routing que elija el contexto correcto según la región del jugador autenticado.

[PANTALLAZO: diagrama o esquema dibujado del sharding por region]

---

## Réplicas de Lectura (Read Replicas)

PostgreSQL Streaming Replication permite tener un servidor primario (escritura) y uno o más réplicas (solo lectura, sincronizadas en tiempo real). Las consultas de alto tráfico como el ranking global se envían a la réplica, liberando al primario para escrituras (INSERT de jugadores, UPDATE de scores).

La configuración en .NET se haría con dos connection strings: uno para escritura (primario) y otro para lectura (réplica).

**Configuración conceptual en appsettings.json:**

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=pg-primary;Database=battletanks;Username=postgres;...",
  "ReadConnection": "Host=pg-replica;Database=battletanks;Username=postgres;..."
}
```

**Uso en código:** las consultas de ranking y estadísticas (solo lectura) usarían el `ReadConnection`, mientras que las de autenticación y creación de partidas usarían el `DefaultConnection`.

En BattleTanks, el candidato principal para la réplica de lectura es `RankingCacheService`: si Redis no está disponible, en lugar de ir al primario, iría a la réplica, manteniendo el primario exclusivo para escrituras.

[PANTALLAZO: diagrama de arquitectura con primario y replica de PostgreSQL]

---

## Benchmarking de Carga Alta (Investigación)

Para simular jugadores conectados simultáneamente se puede usar **k6**, una herramienta de load testing open source.

**Instalación:**
```bash
# Ubuntu/Debian
sudo apt install k6
# O con snap
sudo snap install k6
```

**Script básico para simular 100 jugadores haciendo login:**

```javascript
// k6-load-test.js
import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
  vus: 100,          // 100 usuarios virtuales simultáneos
  duration: '30s',   // durante 30 segundos
};

export default function () {
  const payload = JSON.stringify({
    username: `player_${__VU}`,
    password: 'test123'
  });

  const params = { headers: { 'Content-Type': 'application/json' } };
  const res = http.post('http://localhost:5000/api/auth/login', payload, params);

  // Verificar que el servidor responde correctamente
  check(res, { 'status 200 or 401': (r) => r.status === 200 || r.status === 401 });

  sleep(1); // Esperar 1 segundo entre iteraciones
}
```

**Ejecutar:**
```bash
k6 run k6-load-test.js
```

**Métricas que muestra k6:**
- `http_req_duration`: tiempo de respuesta promedio, p95 y p99
- `http_reqs`: total de requests por segundo
- `http_req_failed`: porcentaje de errores

**Resultados esperados con connection pooling activo (MaxPoolSize=20) vs sin pooling:**

| Métrica | Sin Pooling | Con Pooling (Max=20) |
|---------|-------------|----------------------|
| p95 latencia | ~180 ms | ~45 ms |
| Requests/seg | ~55 | ~210 |
| Errores (%) | ~8% | ~0.5% |

[PANTALLAZO: k6 corriendo el load test mostrando las metricas en tiempo real]

[PANTALLAZO: resumen final de k6 con http_req_duration y requests/sec]

---

## Pasos para Ejecutar la Actividad 3

### 1. Verificar la configuración del connection string con pooling
```bash
cat BattleTanks-Backend/appsettings.json | grep -A2 "DefaultConnection"
```

### 2. Verificar la conexión activa de Npgsql desde psql
```sql
-- Contar conexiones activas desde la aplicacion
SELECT count(*), state, application_name
FROM pg_stat_activity
WHERE datname = 'battletanks'
GROUP BY state, application_name;
```

### 3. Instalar k6 y correr el load test (opcional)
```bash
sudo snap install k6
k6 run lab07/k6-load-test.js
```

### 4. Monitorear el pool en caliente
```bash
# Con el backend corriendo y k6 activo, en psql:
SELECT count(*) as active_connections
FROM pg_stat_activity
WHERE datname = 'battletanks' AND state = 'active';
```

[PANTALLAZO: resultado de pg_stat_activity mostrando conexiones activas del pool]

[PANTALLAZO: backend corriendo con dotnet run y respondiendo requests del load test]
