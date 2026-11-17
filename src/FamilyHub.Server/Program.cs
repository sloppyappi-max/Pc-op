using FamilyHub.Server;

// Standalone mode: runs just the server (any OS). The desktop app hosts the same server in its window.
var dataDir = Environment.GetEnvironmentVariable("FAMILYHUB_DATA")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FamilyHub");

var app = HubServer.Build(dataDir);
Console.WriteLine($"Family Hub running at http://localhost:{HubServer.DefaultPort}");
foreach (var ip in HubServer.LanAddresses())
    Console.WriteLine($"  Phones on your Wi-Fi: http://{ip}:{HubServer.DefaultPort}");
Console.WriteLine($"Data file: {Path.Combine(dataDir, "familyhub.json")}");
app.Run();
