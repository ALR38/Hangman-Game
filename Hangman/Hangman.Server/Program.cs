using System.Net;
using System.Net.Sockets;
using System.Text;
using Core.Configuration;

namespace Hangman.Server
{
    enum GamePhase
    {
        WaitingForWord,
        Guessing
    }

    public class Program
    {
        private static readonly List<Socket> clients = new List<Socket>();
        private static readonly Dictionary<Socket, string> userNames = new Dictionary<Socket, string>();
        private static readonly Dictionary<string, string> userColors = new Dictionary<string, string>();
        private static readonly HashSet<string> activeUsers = new HashSet<string>();
        private static GamePhase currentPhase = GamePhase.WaitingForWord;
        private static string currentWord = "";
        private static HashSet<char> guessedLetters = new HashSet<char>();
        private static int attemptsLeft = 6;
        private static string wordSetter = "";
        private static string currentGuesser = "";
        private static readonly Random random = new Random();
        // Очки игроков
        private static readonly Dictionary<string, int> scores = new Dictionary<string, int>();

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            
            var config = AppConfig.Instance;
            var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                serverSocket.Bind(new IPEndPoint(IPAddress.Parse(config.ServerIpAddress), config.ServerPort));
                serverSocket.Listen(100);
                Console.WriteLine($"Сервер запущен на {config.ServerIpAddress}:{config.ServerPort}");

                while (true)
                {
                    var clientSocket = await serverSocket.AcceptAsync();
                    clients.Add(clientSocket);
                    _ = HandleClientAsync(clientSocket);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сервера: {ex.Message}");
            }
        }

        private static async Task HandleClientAsync(Socket clientSocket)
        {
            try
            {
                byte[] buffer = new byte[1024];

                while (true)
                {
                    int bytesRead = await clientSocket.ReceiveAsync(buffer, SocketFlags.None);
                    if (bytesRead == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"Получено сообщение: {message}");

                    if (message.StartsWith("CONNECT:"))
                    {
                        string userName = message.Substring("CONNECT:".Length);
                        userNames[clientSocket] = userName;
                        activeUsers.Add(userName);
                        if (!userColors.ContainsKey(userName))
                        {
                            userColors[userName] = GenerateRandomColor();
                        }
                        if (!scores.ContainsKey(userName))
                        {
                            scores[userName] = 0;
                        }

                        // Если это первый игрок, он становится ведущим
                        if (string.IsNullOrEmpty(wordSetter))
                        {
                            wordSetter = userName;
                            currentPhase = GamePhase.WaitingForWord;
                            await SendMessageAsync(clientSocket, $"WAITWORD:{wordSetter}");
                        }
                        else
                        {
                            if (currentPhase == GamePhase.Guessing)
                                await SendGameStateToClient(clientSocket);
                            else
                                await SendMessageAsync(clientSocket, $"WAITWORD:{wordSetter}");
                        }
            
                        await BroadcastUserListAsync();
                    }
                    else if (message.StartsWith("SETWORD:"))
                    {
                        // Формат: SETWORD:ИмяВедущего:Слово
                        string[] parts = message.Split(':');
                        if (parts.Length == 3)
                        {
                            string setter = parts[1];
                            string word = parts[2].ToUpper();
                            if (setter == wordSetter && currentPhase == GamePhase.WaitingForWord)
                            {
                                currentWord = word;
                                guessedLetters.Clear();
                                attemptsLeft = 6;
                                // Выбираем первого угадывающего – первого из activeUsers, кто не ведущий
                                currentGuesser = activeUsers.FirstOrDefault(u => u != wordSetter)!;
                                if (string.IsNullOrEmpty(currentGuesser))
                                    currentGuesser = wordSetter;
                                currentPhase = GamePhase.Guessing;
                                await BroadcastGameState();
                            }
                        }
                    }
                    else if (message.StartsWith("GUESS:"))
                    {
                        // Формат: GUESS:Имя:Буква
                        string[] parts = message.Split(':');
                        if (parts.Length == 3)
                        {
                            string guesser = parts[1];
                            char letter = parts[2].ToUpper()[0];
                            if (currentPhase == GamePhase.Guessing && guesser == currentGuesser)
                            {
                                await ProcessGuess(guesser, letter);
                            }
                        }
                    }
                    else if (message.StartsWith("DISCONNECT:"))
                    {
                        await DisconnectClient(clientSocket);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки клиента: {ex.Message}");
            }
            finally
            {
                await DisconnectClient(clientSocket);
            }
        }

        private static async Task DisconnectClient(Socket clientSocket)
        {
            if (userNames.TryGetValue(clientSocket, out string? userName))
            {
                activeUsers.Remove(userName);
                userNames.Remove(clientSocket);
                clients.Remove(clientSocket);
        
                // Если отключился ведущий или текущий угадывающий
                if (userName == wordSetter || userName == currentGuesser)
                {
                    if (activeUsers.Count > 0)
                    {
                        currentGuesser = activeUsers.FirstOrDefault(u => u != wordSetter) ?? wordSetter;
                        await BroadcastGameState();
                    }
                    else
                    {
                        currentWord = "";
                        guessedLetters.Clear();
                        attemptsLeft = 6;
                        wordSetter = "";
                        currentGuesser = "";
                        currentPhase = GamePhase.WaitingForWord;
                    }
                }
        
                await BroadcastUserListAsync();
            }

            try
            {
                clientSocket.Shutdown(SocketShutdown.Both);
                clientSocket.Close();
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        private static async Task ProcessGuess(string guesser, char letter)
        {
            letter = char.ToUpper(letter);
            if (guessedLetters.Contains(letter))
                return;

            guessedLetters.Add(letter);
            bool isCorrectGuess = currentWord.Contains(letter);
            
            if (!isCorrectGuess)
            {
                attemptsLeft--;
                var guessers = activeUsers.Where(u => u != wordSetter).ToList();
                if (guessers.Count > 0)
                {
                    int idx = guessers.IndexOf(currentGuesser);
                    currentGuesser = guessers[(idx + 1) % guessers.Count];
                }
            }

            if (IsGameWon() || attemptsLeft == 0)
            {
                if (IsGameWon())
                {
                    // Угадавший получает балл и становится новым ведущим
                    scores[currentGuesser] = scores.GetValueOrDefault(currentGuesser, 0) + 1;
                    wordSetter = currentGuesser;
                    currentGuesser = "";  // Сбрасываем угадывающего
                }
                else
                {
                    // Если не угадано, балл получает ведущий
                    scores[wordSetter] = scores.GetValueOrDefault(wordSetter, 0) + 1;

                    // Выбираем нового угадывающего (он не может быть ведущим)
                    var guessers = activeUsers.Where(u => u != wordSetter).ToList();
                    if (guessers.Count > 0)
                    {
                        currentGuesser = guessers[0]; // Берем первого доступного угадывающего
                    }
                }

                // Проверка победителя (3 очка)
                var winner = scores.FirstOrDefault(x => x.Value >= 3);
                if (winner.Key != null)
                {
                    await BroadcastMessageAsync($"GAMEFINAL:{winner.Key} победил со счётом {winner.Value} очков!");
                    scores.Clear();
                    wordSetter = winner.Key;
                    currentGuesser = "";
                }

                // Начинаем новый раунд
                currentPhase = GamePhase.WaitingForWord;
                currentWord = "";
                guessedLetters.Clear();
                attemptsLeft = 6;

                await BroadcastMessageAsync($"UPDATEWORD:"); 
                await BroadcastMessageAsync($"WAITWORD:{wordSetter}");
                await BroadcastUserListAsync();
            }
            else
            {
                await BroadcastGameState();
            }
        }
        
        private static async Task BroadcastUserListAsync()
        {
            // Формируем список всех пользователей, включая неактивных
            var userStatuses = userNames.Values.Distinct().Select(name =>
            {
                string status = activeUsers.Contains(name) ? "active" : "inactive";
                return $"{name}:{userColors[name]}:{scores.GetValueOrDefault(name, 0)}:{status}";
            });

            string userList = "USERS:" + string.Join(",", userStatuses);
            Console.WriteLine($"Отправка списка пользователей: {userList}");
            await BroadcastMessageAsync(userList);
        }

        private static bool IsGameWon()
        {
            return currentWord.All(c => guessedLetters.Contains(c));
        }

        private static string GetMaskedWord()
        {
            return string.Join(" ", currentWord.Select(c => guessedLetters.Contains(c) ? c : '_'));
        }

        private static async Task SendMessageAsync(Socket client, string message)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                await client.SendAsync(buffer, SocketFlags.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки сообщения клиенту: {ex.Message}");
                if (userNames.TryGetValue(client, out string? userName))
                {
                    activeUsers.Remove(userName);
                    userNames.Remove(client);
                }
                clients.Remove(client);
                await BroadcastUserListAsync();
            }
        }

        private static async Task BroadcastMessageAsync(string message)
        {
            var deadClients = new List<Socket>();

            foreach (var client in clients.ToList())
            {
                try
                {
                    await SendMessageAsync(client, message);
                }
                catch
                {
                    deadClients.Add(client);
                }
            }

            foreach (var deadClient in deadClients)
            {
                await DisconnectClient(deadClient);
            }
        }

        private static async Task SendGameStateToClient(Socket client)
        {
            string gameState = $"GAME:{GetMaskedWord()}:{attemptsLeft}:{currentGuesser}:{string.Join(",", guessedLetters)}";
            await SendMessageAsync(client, gameState);
        }

        private static async Task BroadcastGameState()
        {
            string gameState = $"GAME:{GetMaskedWord()}:{attemptsLeft}:{currentGuesser}:{string.Join(",", guessedLetters)}";
            await BroadcastMessageAsync(gameState);
        }

        private static string GenerateRandomColor()
        {
            return $"#{random.Next(256):X2}{random.Next(256):X2}{random.Next(256):X2}";
        }
    }
}
