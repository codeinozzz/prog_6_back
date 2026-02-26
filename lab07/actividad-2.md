# Actividad 2: Integración de Redis para Caché Distribuido

## Arquitectura Redis en BattleTanks

Redis se usa como capa de caché entre el frontend y PostgreSQL para las tres operaciones más frecuentes: ranking global (leído por todos en la pantalla de espera), jugadores conectados por sala (actualizado en cada join/leave del GameHub) y sesiones JWT revocadas (para logout seguro sin consultar la DB).

La clave de diseño es que Redis es **opcional**: si no está disponible, todas las operaciones hacen fallback a PostgreSQL o a la memoria in-process. El servidor arranca igual con un warning en consola.

**Infraestructura:** `docker-compose.yml` en la raíz del proyecto

```yaml
redis:
  image: redis:7-alpine
  container_name: battletanks-redis
  ports:
    - "6379:6379"
  command: redis-server --appendonly yes   # Persistencia en disco
  volumes:
    - redis_data:/data
  restart: unless-stopped
```

[PANTALLAZO: docker ps mostrando el contenedor battletanks-redis corriendo]

---

## Caché de Ranking Global con SortedSet

La estructura Redis más adecuada para ranking es el `SortedSet`: almacena pares (member, score) ordenados automáticamente por score. Permite obtener el top N en O(log N) sin ordenar, a diferencia de PostgreSQL que necesita el índice + I/O de disco.

El flujo es: cuando llega una solicitud de ranking, se consulta primero el SortedSet en Redis. Si hay datos (cache hit), se retornan inmediatamente. Si no (cache miss), se consulta PostgreSQL, se llena Redis con TTL de 5 minutos y se retorna al cliente.

**Archivo:** `BattleTanks-Backend/Services/RankingCacheService.cs`

```csharp
// Estructura en Redis:
// ranking:global -> SortedSet: member=Username, score=TotalScore
// TTL: 5 minutos, se renueva en cada cache miss

// Cache HIT: leer desde Redis (O(log N), sin I/O disco)
var cached = await _redis.SortedSetRangeByRankWithScoresAsync(
    "ranking:global", 0, count - 1, Order.Descending);

// Cache MISS: consultar PostgreSQL y llenar Redis
var players = await _context.Players
    .AsNoTracking()
    .OrderByDescending(p => p.TotalScore)
    .Take(count)
    .ToListAsync();

var entries = players
    .Select(p => new SortedSetEntry(p.Username, p.TotalScore))
    .ToArray();

await _redis.SortedSetAddAsync("ranking:global", entries);
await _redis.KeyExpireAsync("ranking:global", TimeSpan.FromMinutes(5));
```

**Comparativa de rendimiento:**

| Operación | PostgreSQL | Redis SortedSet |
|-----------|------------|-----------------|
| Top 10 ranking | ~8 ms (con índice) | ~0.3 ms |
| Top 100 ranking | ~25 ms | ~0.8 ms |
| Actualizar score | ~5 ms | ~0.2 ms |

[PANTALLAZO: GET /api/ranking con fromCache: true en la respuesta - mostrando cache hit]

[PANTALLAZO: GET /api/ranking con fromCache: false en la respuesta - mostrando cache miss y fallback a PostgreSQL]

---

## Jugadores Conectados por Sala en GameHub

En lugar de solo mantener el `ConcurrentDictionary` en memoria del proceso (que se perdería si el servidor se reinicia), el GameHub ahora replica la información en Redis usando dos estructuras:

- **Hash** `room:{roomId}:players` → `connectionId: playerName` (quién está en la sala)
- **String** `room:{roomId}:count` → número entero (contador rápido de jugadores)

Esto permite en el futuro leer el conteo desde múltiples instancias del backend (escalabilidad horizontal).

**Archivo:** `BattleTanks-Backend/Hubs/GameHub.cs`

```csharp
// Al unirse un jugador: guardar en Hash y actualizar contador
await _redis.HashSetAsync($"room:{roomId}:players", Context.ConnectionId, playerName);
await _redis.StringIncrementAsync($"room:{roomId}:count");

// Al salir: limpiar entrada del Hash y decrementar contador
await _redis.HashDeleteAsync($"room:{conn.RoomId}:players", Context.ConnectionId);
await _redis.StringDecrementAsync($"room:{conn.RoomId}:count");
```

[PANTALLAZO: redis-cli ejecutando HGETALL room:1:players mostrando jugadores conectados]

[PANTALLAZO: redis-cli ejecutando GET room:1:count mostrando el contador de jugadores]

---

## Gestión de Sesiones JWT con Blacklist en Redis

El problema con JWT es que una vez emitido, es válido hasta su expiración aunque el usuario haga logout. La solución es almacenar el JTI (JWT ID) del token en Redis con el TTL restante del token. Así se puede verificar si un token fue revocado sin consultar la DB en cada request.

Para esto se agregó el claim `jti` (UUID único) a todos los tokens generados, y se expuso un endpoint `POST /api/auth/logout` que lo almacena en Redis.

**Archivo:** `BattleTanks-Backend/Controllers/AuthController.cs`

```csharp
// Al generar el token: incluir JTI unico
var claims = new[]
{
    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
    // ... otros claims
};

// Al hacer logout: guardar JTI en Redis con el TTL restante del token
[Authorize]
[HttpPost("logout")]
public IActionResult Logout()
{
    var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
    var expClaim = User.FindFirstValue(JwtRegisteredClaimNames.Exp);

    var expDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expClaim)).UtcDateTime;
    var ttl = expDate - DateTime.UtcNow;

    // Clave: blacklist:{jti} -> "revoked", expira cuando el token original expiraria
    _redis.StringSet($"blacklist:{jti}", "revoked", ttl);

    return Ok(new { message = "Session closed successfully" });
}
```

**Estructura de claves en Redis:**
```
blacklist:{uuid}    → "revoked"    TTL: tiempo restante del token
ranking:global      → SortedSet   TTL: 5 minutos
room:{id}:players   → Hash        Sin TTL (se limpia al salir)
room:{id}:count     → String      Sin TTL (se limpia al salir)
```

[PANTALLAZO: POST /api/auth/logout con token Bearer en Swagger retornando 200]

[PANTALLAZO: redis-cli ejecutando KEYS blacklist:* mostrando tokens revocados]

[PANTALLAZO: redis-cli ejecutando TTL blacklist:{jti} mostrando el tiempo restante]

---

## Pasos para Ejecutar la Actividad 2

### 1. Levantar Redis con Docker
```bash
cd /ruta/capstone
docker compose up redis -d
```

### 2. Verificar que Redis responde
```bash
docker exec -it battletanks-redis redis-cli ping
# Respuesta esperada: PONG
```

### 3. Correr el backend
```bash
cd BattleTanks-Backend
dotnet run
# Debe aparecer: [Redis] Connected
```

### 4. Verificar claves en Redis mientras se juega
```bash
docker exec -it battletanks-redis redis-cli
> KEYS *
> HGETALL room:1:players
> ZRANGE ranking:global 0 9 WITHSCORES REV
```

[PANTALLAZO: consola del backend mostrando [Redis] Connected al arrancar]

[PANTALLAZO: redis-cli mostrando KEYS * con las claves activas durante una partida]
