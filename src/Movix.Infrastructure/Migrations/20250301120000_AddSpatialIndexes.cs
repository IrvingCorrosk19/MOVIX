using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Movix.Infrastructure.Migrations
{
    public partial class AddSpatialIndexes : Migration
    {
        protected override void Up(MigrationBuilder b)
        {
            b.Sql("CREATE EXTENSION IF NOT EXISTS postgis;");
            b.Sql("CREATE INDEX IF NOT EXISTS \"IX_trips_PickupLocation_gist\" ON trips USING GIST (\"PickupLocation\");");
            b.Sql("CREATE INDEX IF NOT EXISTS \"IX_trips_DropoffLocation_gist\" ON trips USING GIST (\"DropoffLocation\");");
            b.Sql("CREATE INDEX IF NOT EXISTS \"IX_driver_location_live_Location_gist\" ON driver_location_live USING GIST (\"Location\");");
        }

        protected override void Down(MigrationBuilder b)
        {
            b.Sql("DROP INDEX IF EXISTS \"IX_trips_PickupLocation_gist\";");
            b.Sql("DROP INDEX IF EXISTS \"IX_trips_DropoffLocation_gist\";");
            b.Sql("DROP INDEX IF EXISTS \"IX_driver_location_live_Location_gist\";");
        }
    }
}
