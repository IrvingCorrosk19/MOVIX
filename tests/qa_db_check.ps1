$connStr = "Host=localhost;Port=5432;Database=movix;Username=postgres;Password=Panama2020$;Include Error Detail=true"

# Find Npgsql DLL
$npgsqlDll = Get-ChildItem "C:\Proyectos\RiderFlow" -Recurse -Filter "Npgsql.dll" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notlike "*\obj\*" } |
    Select-Object -First 1
Write-Host "Npgsql: $($npgsqlDll.FullName)"

if ($npgsqlDll) {
    Add-Type -Path $npgsqlDll.FullName
    $cn = New-Object Npgsql.NpgsqlConnection($connStr)
    $cn.Open()
    Write-Host "DB connected. PostgreSQL version: $($cn.ServerVersion)"

    # Query DriverAvailability
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = @"
SELECT da."DriverId", da."IsOnline", da."CurrentTripId", t."Status" as trip_status
FROM driver_availability da
LEFT JOIN trips t ON t."Id" = da."CurrentTripId"
"@
    $reader = $cmd.ExecuteReader()
    Write-Host "`n=== DRIVER AVAILABILITY ==="
    while ($reader.Read()) {
        Write-Host "  DriverId=$($reader['DriverId']) IsOnline=$($reader['IsOnline']) CurrentTripId=$($reader['CurrentTripId']) TripStatus=$($reader['trip_status'])"
    }
    $reader.Close()

    # Fix: Reset CurrentTripId to NULL for all drivers
    $cmd2 = $cn.CreateCommand()
    $cmd2.CommandText = 'UPDATE driver_availability SET "CurrentTripId" = NULL'
    $affected = $cmd2.ExecuteNonQuery()
    Write-Host "`n=== RESET CurrentTripId to NULL for $affected rows ==="

    # Verify
    $cmd3 = $cn.CreateCommand()
    $cmd3.CommandText = 'SELECT "DriverId", "IsOnline", "CurrentTripId" FROM driver_availability'
    $reader3 = $cmd3.ExecuteReader()
    Write-Host "`n=== AFTER RESET ==="
    while ($reader3.Read()) {
        Write-Host "  DriverId=$($reader3['DriverId']) IsOnline=$($reader3['IsOnline']) CurrentTripId=$($reader3['CurrentTripId'])"
    }
    $reader3.Close()
    $cn.Close()
    Write-Host "`nDone."
} else {
    Write-Host "Npgsql.dll not found. Looking in NuGet cache..."
    $nugetDll = Get-ChildItem "$env:USERPROFILE\.nuget\packages\npgsql" -Recurse -Filter "Npgsql.dll" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "net8" -or $_.FullName -match "net6" } |
        Select-Object -First 1
    Write-Host "NuGet Npgsql: $($nugetDll.FullName)"
    if ($nugetDll) {
        Add-Type -Path $nugetDll.FullName
        $cn = New-Object Npgsql.NpgsqlConnection($connStr)
        $cn.Open()
        $cmd = $cn.CreateCommand()
        $cmd.CommandText = 'SELECT "DriverId", "IsOnline", "CurrentTripId" FROM driver_availability'
        $reader = $cmd.ExecuteReader()
        while ($reader.Read()) {
            Write-Host "DriverId=$($reader['DriverId']) IsOnline=$($reader['IsOnline']) CurrentTripId=$($reader['CurrentTripId'])"
        }
        $reader.Close()
        $cmd2 = $cn.CreateCommand()
        $cmd2.CommandText = 'UPDATE driver_availability SET "CurrentTripId" = NULL'
        $n = $cmd2.ExecuteNonQuery()
        Write-Host "Reset $n rows CurrentTripId=NULL"
        $cn.Close()
    }
}
