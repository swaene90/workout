using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Npgsql;

var root = Directory.GetCurrentDirectory();
var raw = File.ReadAllText(Path.Combine(root, "secrets.txt"));
var match = Regex.Match(raw, @"(?im)(?:Host|Server)\s*=\s*[^\r\n]+");
if (!match.Success) { Console.WriteLine("No connection string found."); return; }
var source = new NpgsqlConnectionStringBuilder(match.Value.Trim().Trim('"')) { Timeout = 5 };
try
{
    await using var connection = new NpgsqlConnection(source.ConnectionString);
    await connection.OpenAsync();
    Console.WriteLine("PostgreSQL connection succeeded.");
    await using var check = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname='workout'), EXISTS(SELECT 1 FROM pg_roles WHERE rolname='workout_app')", connection);
    bool databaseExists, roleExists;
    await using (var reader = await check.ExecuteReaderAsync()) { await reader.ReadAsync(); databaseExists = reader.GetBoolean(0); roleExists = reader.GetBoolean(1); }
    if (databaseExists || roleExists) { Console.WriteLine("Dedicated database or role already exists; no existing objects changed."); return; }
    var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    await using var createRole = new NpgsqlCommand($"CREATE ROLE workout_app LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE", connection);
    await createRole.ExecuteNonQueryAsync();
    var app = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "workout", Username = "workout_app", Password = password, Timeout = 15 };
    var envPath = Path.Combine(root, ".env");
    var lines = File.Exists(envPath) ? File.ReadAllLines(envPath).Where(line => !line.StartsWith("ConnectionStrings__Workout=")).ToList() :
        new List<string> { "Authentication__Google__ClientId=", "Authentication__Google__ClientSecret=", "PUBLIC_HOSTNAME=localhost", "TUNNEL_TOKEN=" };
    lines.Insert(0, "ConnectionStrings__Workout='" + app.ConnectionString.Replace("'", "\\'") + "'");
    File.WriteAllLines(envPath, lines);
    await using var createDb = new NpgsqlCommand("CREATE DATABASE workout OWNER workout_app", connection);
    await createDb.ExecuteNonQueryAsync();
    await using var appConnection = new NpgsqlConnection(app.ConnectionString);
    await appConnection.OpenAsync();
    await using var revoke = new NpgsqlCommand("REVOKE CREATE ON SCHEMA public FROM PUBLIC", appConnection);
    await revoke.ExecuteNonQueryAsync();
    Console.WriteLine("Created workout database and dedicated app role; generated credentials saved only to ignored .env.");
}
catch (Exception exception)
{
    Console.WriteLine($"Database setup failed: {exception.GetType().Name}; SQLSTATE {(exception as PostgresException)?.SqlState ?? "not available"}.");
    Environment.ExitCode = 1;
}
