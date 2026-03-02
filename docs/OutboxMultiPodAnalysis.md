# Outbox — Análisis de riesgo multi-pod (FASE A)

## Estado actual

### Selección de mensajes
- **Dónde**: `OutboxProcessor.ProcessOnceAsync`
- **Query**: `Where(ProcessedAtUtc == null && !IsDeadLetter && AttemptCount < MaxAttempts).OrderBy(CreatedAtUtc).Take(batchSize).ToListAsync()`
- **Transacción**: No. Query y actualización sin transacción explícita.
- **Lock**: Ninguno (no hay `FOR UPDATE` ni `SKIP LOCKED`).

### Flujo actual
1. Cargar lote en memoria (sin lock).
2. Por cada mensaje: si no ha pasado el backoff, skip; si no, `PublishAsync`, luego en memoria `ProcessedAtUtc = now` o `RecordFailedAttempt()` / `MarkAsDeadLetter()`.
3. Un solo `SaveChangesAsync()` al final para todo el lote.

### Marca de procesado
- `ProcessedAtUtc` se asigna **después** de publicar y **antes** de `SaveChangesAsync`.
- Hasta el commit, otro pod puede seguir leyendo el mismo mensaje como pendiente.

---

## Riesgo con 3 pods simultáneos

1. **Doble (o triple) lectura**  
   Pod A, B y C ejecutan la misma `SELECT` casi a la vez. Los tres ven los mismos N mensajes (p. ej. ids 1..10) porque ninguna fila está bloqueada.

2. **Doble (o triple) publicación**  
   Los tres llaman a `PublishAsync` para los mismos eventos. Los consumidores reciben el mismo evento varias veces.

3. **Múltiples commits**  
   Los tres hacen `SaveChangesAsync` y marcan `ProcessedAtUtc`. No hay condición de carrera en la escritura de esa columna, pero el daño (eventos duplicados) ya está hecho.

4. **Crash entre Publish y SaveChanges**  
   Si un pod hace `PublishAsync` y falla antes de `SaveChangesAsync`, el mensaje sigue con `ProcessedAtUtc == null`. Otro pod puede tomarlo y volver a publicarlo (at-least-once; sin idempotencia en consumidor = duplicado).

**Conclusión**: El diseño actual **no es seguro para múltiples workers**: no hay reserva exclusiva de filas ni transacción que una “reserva + procesamiento + marca” en una sola unidad atómica.
