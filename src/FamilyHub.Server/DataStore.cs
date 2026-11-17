using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyHub.Server;

/// <summary>Keeps all data in one JSON file. Plenty for a household, and easy to back up.</summary>
public class DataStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private HubData _data;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public DataStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "familyhub.json");
        _data = LoadFromDisk();
        Save(); // makes sure a PIN is written on first run
    }

    public string FilePath => _filePath;

    private HubData LoadFromDisk()
    {
        if (!File.Exists(_filePath)) return new HubData();
        try
        {
            return JsonSerializer.Deserialize<HubData>(File.ReadAllText(_filePath), JsonOptions) ?? new HubData();
        }
        catch (JsonException)
        {
            // Keep the unreadable file instead of overwriting it.
            File.Copy(_filePath, _filePath + $".broken-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            return new HubData();
        }
    }

    /// <summary>Run a read against the data under the lock.</summary>
    public T Read<T>(Func<HubData, T> reader)
    {
        lock (_lock) return reader(_data);
    }

    /// <summary>Change the data under the lock and save it.</summary>
    public T Write<T>(Func<HubData, T> writer)
    {
        lock (_lock)
        {
            var result = writer(_data);
            Save();
            return result;
        }
    }

    private void Save()
    {
        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, JsonOptions));
        File.Move(tmp, _filePath, overwrite: true);
    }
}
