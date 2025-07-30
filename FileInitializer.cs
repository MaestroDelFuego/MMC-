using System.IO;

public static class FileInitializer
{
    public static void Initialize()
    {
        string[] folders = { "world", "logs", "config", "playerdata", "datapacks" };

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Logger.Log($"Created folder: {folder}");
            }
        }

        if (!File.Exists("server.properties"))
        {
            File.WriteAllText("server.properties", "# Server config\nmotd=CSharp Server\nmax-players=20\n");
            Logger.Log("Created default server.properties");
        }

        if (!File.Exists("ops.json"))
        {
            File.WriteAllText("ops.json", "[]");
            Logger.Log("Created default ops.json");
        }
    }
}
