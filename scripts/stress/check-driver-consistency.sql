-- =============================================================================
-- check-driver-consistency.sql
-- =============================================================================
-- Purpose: Detect if any driver was assigned to more than one trip in Accepted
--          state simultaneously (data inconsistency / double assignment bug).
--
-- Usage:   Run against the Movix PostgreSQL database, e.g.:
--          psql -h localhost -U movix -d movix_core -f check-driver-consistency.sql
--
-- Result:  If no rows returned → consistent (no driver has 2+ Accepted trips).
--          If rows returned   → BUG: driver_id appears with COUNT(*) > 1.
-- =============================================================================

SELECT
    "DriverId" AS driver_id,
    COUNT(*)   AS accepted_trip_count
FROM trips
WHERE "Status" = 'Accepted'
  AND "DriverId" IS NOT NULL
GROUP BY "DriverId"
HAVING COUNT(*) > 1;
