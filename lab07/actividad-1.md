# Actividad 1: Optimización de Consultas con PostgreSQL y EF Core

## Creación de Índices en Tablas Clave

Los índices en PostgreSQL permiten que el motor de base de datos localice filas sin recorrer toda la tabla (full table scan). En BattleTanks los índices más críticos son los del ranking global y el estado de sesiones, porque son las columnas que más se filtran y ordenan durante el juego.

Antes de esta actividad, `BattleTanksDbContext` solo tenía índices únicos en `Username` y `Email` para autenticación. Se agregaron índices en `TotalScore` (para ordenar el ranking sin escanear toda la tabla), `GameSession.Status` (para filtrar solo salas en espera o en juego), `Score.PlayerId` y un índice compuesto `(PlayerId, AchievedAt)` para consultas de historial de partidas de un jugador.

**Archivo:** `BattleTanks-Backend/Data/BattleTanksDbContext.cs`

```csharp
// Indice para ranking global: ORDER BY TotalScore DESC usa este indice en lugar de full scan
modelBuilder.Entity<Player>()
    .HasIndex(p => p.TotalScore)
    .HasDatabaseName("IX_Players_TotalScore");

// Indice para filtrar sesiones activas: WHERE Status = 'InProgress'
modelBuilder.Entity<GameSession>()
    .HasIndex(gs => gs.Status)
    .HasDatabaseName("IX_GameSessions_Status");

// Indice compuesto para historial de un jugador ordenado por fecha
modelBuilder.Entity<Score>()
    .HasIndex(s => new { s.PlayerId, s.AchievedAt })
    .HasDatabaseName("IX_Scores_PlayerId_AchievedAt");
```

[PANTALLAZO: BattleTanksDbContext.cs mostrando los nuevos indices definidos]

[PANTALLAZO: migration generada Lab7_PerformanceIndexes.cs con los CREATE INDEX en SQL]

---

## Optimización de Consultas de Solo Lectura con AsNoTracking

EF Core por defecto rastrea todos los objetos que retorna en el `ChangeTracker`. Esto permite detectar cambios y hacer `SaveChanges()` eficientemente, pero tiene un costo en memoria y CPU cuando solo queremos leer datos. Para consultas de ranking y estadísticas, que nunca se van a modificar en el mismo request, `AsNoTracking()` elimina ese overhead.

El efecto es más notable cuanto mayor sea el resultado: en una consulta de 10 jugadores la diferencia es pequeña (~1-2 ms), pero en consultas de historial con cientos de filas puede reducir el tiempo hasta un 30-40%.

**Archivo:** `BattleTanks-Backend/Controllers/RankingController.cs`

```csharp
// Sin AsNoTracking: EF rastrea cada objeto en el ChangeTracker (mas memoria, mas CPU)
var players = await _context.Players
    .OrderByDescending(p => p.TotalScore)
    .Take(10)
    .ToListAsync();

// Con AsNoTracking: EF solo materializa los objetos, no los rastrea (optimo para lectura)
var players = await _context.Players
    .AsNoTracking()
    .OrderByDescending(p => p.TotalScore)
    .Take(10)
    .ToListAsync();
```

[PANTALLAZO: RankingController.cs mostrando el endpoint GET /api/ranking con AsNoTracking y paginacion]

---

## Paginación de Consultas

Para evitar cargar todos los registros en memoria, el endpoint de ranking implementa paginación con `Skip` y `Take`. Esto es crítico en tablas grandes: sin paginación, obtener el ranking de 10.000 jugadores traería todos a memoria aunque el cliente solo vea 10.

**Endpoint:** `GET /api/ranking?page=1&pageSize=10`

```csharp
var skip = (page - 1) * pageSize;

var players = await _context.Players
    .AsNoTracking()
    .OrderByDescending(p => p.TotalScore)
    .Skip(skip)          // Saltar los primeros N registros
    .Take(pageSize)      // Tomar solo los del tamano de pagina
    .Select(p => new { p.Id, p.Username, p.TotalScore, p.Victories, p.GamesPlayed })
    .ToListAsync();
```

El `Select` proyecta solo las columnas necesarias, evitando traer `PasswordHash` u otros campos pesados innecesarios para el ranking.

[PANTALLAZO: Swagger o curl mostrando GET /api/ranking?page=1&pageSize=5 con respuesta paginada]

---

## Benchmarking: WithTracking vs AsNoTracking

Para demostrar el impacto real, se implementó un endpoint que ejecuta la misma consulta dos veces y mide el tiempo con `Stopwatch`.

**Endpoint:** `GET /api/ranking/benchmark`

```csharp
// Con Tracking
var swTracked = Stopwatch.StartNew();
var tracked = await _context.Players
    .OrderByDescending(p => p.TotalScore)
    .Take(10)
    .ToListAsync();
swTracked.Stop();

// Limpiar ChangeTracker para medicion limpia
_context.ChangeTracker.Clear();

// Sin Tracking
var swNoTracking = Stopwatch.StartNew();
var noTracking = await _context.Players
    .AsNoTracking()
    .OrderByDescending(p => p.TotalScore)
    .Take(10)
    .ToListAsync();
swNoTracking.Stop();
```

**Resultados de ejemplo:**

| Modo | Registros | Tiempo (ms) |
|------|-----------|-------------|
| WithTracking | 10 | ~8.5 ms |
| AsNoTracking | 10 | ~5.2 ms |

La diferencia aumenta proporcionalmente con el número de registros retornados.

[PANTALLAZO: respuesta de GET /api/ranking/benchmark mostrando tiempos comparativos]

[PANTALLAZO: tabla o grafica comparando tiempos WithTracking vs AsNoTracking]

---

## Pasos para Ejecutar la Actividad 1

### 1. Aplicar la migración de índices
```bash
cd BattleTanks-Backend
dotnet ef database update
```
Debe aparecer en consola la migración `Lab7_PerformanceIndexes` aplicada.

### 2. Verificar índices en PostgreSQL
```sql
SELECT indexname, tablename, indexdef
FROM pg_indexes
WHERE schemaname = 'public'
  AND indexname LIKE 'IX_%'
ORDER BY tablename;
```

### 3. Probar el endpoint de ranking
```bash
curl http://localhost:5000/api/ranking?page=1&pageSize=5
```

### 4. Probar el benchmarking
```bash
curl http://localhost:5000/api/ranking/benchmark
```

[PANTALLAZO: consola de dotnet ef database update aplicando la migracion Lab7_PerformanceIndexes]

[PANTALLAZO: resultado de la query SQL mostrando los indices creados en PostgreSQL]
