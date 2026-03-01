-- PostGIS + GIST indexes (generated for verification)
-- Applied by EF migration 20250301120000_AddSpatialIndexes

CREATE EXTENSION IF NOT EXISTS postgis;

CREATE INDEX IF NOT EXISTS "IX_trips_PickupLocation_gist" ON trips USING GIST ("PickupLocation");
CREATE INDEX IF NOT EXISTS "IX_trips_DropoffLocation_gist" ON trips USING GIST ("DropoffLocation");
CREATE INDEX IF NOT EXISTS "IX_driver_location_live_Location_gist" ON driver_location_live USING GIST ("Location");
