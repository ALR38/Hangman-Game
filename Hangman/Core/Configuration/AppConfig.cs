using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Configuration
{
    public sealed class AppConfig
    {
        private static readonly object Lock = new();
        private static AppConfig? _instance;

        public static AppConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (Lock)
                    {
                        if (_instance == null)
                        {
                            _instance = LoadConfiguration();
                        }
                    }
                }
                return _instance;
            }
        }

        public string ServerIpAddress { get; }
        public int ServerPort { get; }
        public string ClientIpAddress { get; } 
        public int ClientPort { get; }

        private AppConfig()
        {
            ServerIpAddress = "0.0.0.0";
            ServerPort = 8888;
            ClientIpAddress = "127.0.0.1";
            ClientPort = 8888;
        }

        [JsonConstructor]
        public AppConfig(string? serverIpAddress, int serverPort, string? clientIpAddress, int clientPort)
        {
            ServerIpAddress = serverIpAddress ?? "0.0.0.0";
            ServerPort = serverPort > 0 ? serverPort : 8888;
            ClientIpAddress = clientIpAddress ?? "127.0.0.1";
            ClientPort = clientPort > 0 ? clientPort : 8888;
        }

        private static AppConfig LoadConfiguration()
        {
            // Получаем путь к папке, где находится исполняемый файл
            string baseDirectory = AppContext.BaseDirectory;
            // Указываем путь к config.json в той же папке
            string configPath = Path.Combine(baseDirectory, "config.json");

            if (File.Exists(configPath))
            {
                try
                {
                    var fileConfig = File.ReadAllText(configPath);
                    return JsonSerializer.Deserialize<AppConfig>(fileConfig) ?? new AppConfig();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка загрузки конфигурации из файла: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"Файл конфигурации '{configPath}' не найден. Используются значения по умолчанию.");
            }

            return new AppConfig();
        }
    }
}