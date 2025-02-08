using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Core.Configuration;

namespace Hangman
{
    public partial class MainWindow : Window
    {
        private Socket? clientSocket;
        private string? userName;
        private TextBlock? wordDisplay;
        private TextBlock? attemptsDisplay;
        private TextBlock? currentPlayerDisplay;
        private TextBox? letterInput;
        private Button? guessButton;
        private TextBlock? guessedLettersDisplay;
        private ListBox? usersList;
        private TextBox? nicknameInput;
        private Button? addUserButton;
        private Random random = new Random();
        private TextBlock? gameStatus;
        private Image? hangmanImage;

        // Флаг: true – режим ведущего (задаёт слово), false – режим угадывания
        private bool isWordSetter = false;
        // Храним имя текущего ведущего (того, кто задал слово)
        private string? currentWordSetter = null;

        public MainWindow()
        {
            InitializeComponent();

            // Находим все контролы
            gameStatus = this.FindControl<TextBlock>("GameStatus");
            hangmanImage = this.FindControl<Image>("HangmanImage");
            wordDisplay = this.FindControl<TextBlock>("WordDisplay");
            attemptsDisplay = this.FindControl<TextBlock>("AttemptsDisplay");
            currentPlayerDisplay = this.FindControl<TextBlock>("CurrentPlayerDisplay");
            letterInput = this.FindControl<TextBox>("LetterInput");
            guessButton = this.FindControl<Button>("GuessButton");
            guessedLettersDisplay = this.FindControl<TextBlock>("GuessedLettersDisplay");
            usersList = this.FindControl<ListBox>("UsersList");
            nicknameInput = this.FindControl<TextBox>("NicknameInput");
            addUserButton = this.FindControl<Button>("AddUserButton");

            // Настройка событий
            if (letterInput != null)
            {
                letterInput.IsEnabled = false;
                letterInput.KeyDown += OnLetterInputKeyDown;
            }

            if (guessButton != null)
            {
                guessButton.IsEnabled = false;
                guessButton.Click += OnGuessClick;
            }

            if (addUserButton != null)
            {
                addUserButton.Click += OnAddUserClick;
            }

            if (nicknameInput != null)
            {
                nicknameInput.KeyDown += OnNicknameInputKeyDown;
            }

            UpdateHangmanImage(6);
        }

        // Проверка: символ – заглавная русская буква (включая Ё)
        private bool IsRussianLetter(char c)
        {
            return (c >= 'А' && c <= 'Я') || c == 'Ё';
        }

        // Проверка, что слово состоит только из русских букв
        private bool IsValidWord(string word)
        {
            return word.All(c => IsRussianLetter(c));
        }

        private async void OnNicknameInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await AddUser();
            }
        }

        /// <summary>
        /// Отрисовка виселицы с использованием стандартного ASCII-арта.
        /// </summary>
        private void UpdateHangmanImage(int attemptsLeft)
        {
            if (hangmanImage == null) return;

            try
            {
                // Стандартный ASCII-арт виселицы (7 этапов)
                int stage = 6 - attemptsLeft;
                string[] hangmanArt = {
                    "  +---+\n  |   |\n      |\n      |\n      |\n      |\n=========",
                    "  +---+\n  |   |\n  O   |\n      |\n      |\n      |\n=========",
                    "  +---+\n  |   |\n  O   |\n  |   |\n      |\n      |\n=========",
                    "  +---+\n  |   |\n  O   |\n /|   |\n      |\n      |\n=========",
                    "  +---+\n  |   |\n  O   |\n /|\\  |\n      |\n      |\n=========",
                    "  +---+\n  |   |\n  O   |\n /|\\  |\n /    |\n      |\n=========",
                    "  +---+\n  |   |\n  O   |\n /|\\  |\n / \\  |\n      |\n========="
                };

                if (stage < 0 || stage >= hangmanArt.Length) return;

                var textBlock = new TextBlock
                {
                    Text = hangmanArt[stage],
                    FontFamily = "Courier New",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                };

                var grid = new Grid
                {
                    Background = new SolidColorBrush(Color.Parse("#1e1e2e")),
                    Width = 250,
                    Height = 250
                };
                grid.Children.Add(textBlock);

                var renderer = new RenderTargetBitmap(new PixelSize(250, 250));
                grid.Measure(new Size(250, 250));
                grid.Arrange(new Rect(0, 0, 250, 250));
                renderer.Render(grid);

                hangmanImage.Source = renderer;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обновлении виселицы: {ex.Message}");
            }
        }

        private async void OnAddUserClick(object? sender, RoutedEventArgs e)
        {
            await AddUser();
        }

        private async Task AddUser()
        {
            if (nicknameInput == null || string.IsNullOrWhiteSpace(nicknameInput.Text))
                return;

            userName = nicknameInput.Text.Trim();
            await InitializeConnectionAsync();

            if (clientSocket?.Connected == true)
            {
                nicknameInput.IsEnabled = false;
                if (addUserButton != null)
                    addUserButton.IsEnabled = false;
                if (letterInput != null)
                    letterInput.IsEnabled = true;
            }
        }

        /// <summary>
        /// Подключение к серверу. Если в конфигурации указан адрес 0.0.0.0, для подключения используется 127.0.0.1.
        /// </summary>
        private async Task InitializeConnectionAsync()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            var config = AppConfig.Instance;
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                // Если сервер указан как 0.0.0.0, используем 127.0.0.1 для подключения
                string serverAddress = config.ServerIpAddress;
                if (serverAddress == "0.0.0.0")
                    serverAddress = "127.0.0.1";

                Console.WriteLine($"Попытка подключения к {serverAddress}:{config.ServerPort}");
                await clientSocket.ConnectAsync(serverAddress, config.ServerPort);

                string connectMessage = $"CONNECT:{userName}";
                byte[] buffer = Encoding.UTF8.GetBytes(connectMessage);
                await clientSocket.SendAsync(buffer, SocketFlags.None);

                _ = ReceiveMessagesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка подключения: {ex.Message}");
                await ShowErrorAsync($"Ошибка подключения: {ex.Message}\nУбедитесь, что сервер запущен на {config.ServerIpAddress}:{config.ServerPort}");

                if (nicknameInput != null)
                    nicknameInput.IsEnabled = true;
                if (addUserButton != null)
                    addUserButton.IsEnabled = true;
            }
        }

        private async void OnLetterInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (isWordSetter)
                    await SetWord();
                else
                    await MakeGuess();
            }
        }

        private async void OnGuessClick(object? sender, RoutedEventArgs e)
        {
            if (isWordSetter)
                await SetWord();
            else
                await MakeGuess();
        }

        /// <summary>
        /// Отправка буквы (режим угадывания).
        /// </summary>
        private async Task MakeGuess()
        {
            if (letterInput == null || string.IsNullOrWhiteSpace(letterInput.Text))
                return;
            if (clientSocket?.Connected != true || string.IsNullOrEmpty(userName))
                return;

            string input = letterInput.Text.Trim().ToUpper();
            if (input.Length != 1 || !IsRussianLetter(input[0]))
            {
                letterInput.Text = "";
                return;
            }

            string letter = input;
            var message = $"GUESS:{userName}:{letter}";
            var buffer = Encoding.UTF8.GetBytes(message);
            await clientSocket.SendAsync(buffer, SocketFlags.None);
            letterInput.Text = "";
        }

        /// <summary>
        /// Отправка слова (режим ведущего).
        /// </summary>
        private async Task SetWord()
        {
            if (letterInput == null || string.IsNullOrWhiteSpace(letterInput.Text))
                return;
            string word = letterInput.Text.Trim().ToUpper();
            if (!IsValidWord(word))
            {
                letterInput.Text = "";
                return;
            }

            string message = $"SETWORD:{userName}:{word}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await clientSocket.SendAsync(buffer, SocketFlags.None);
            letterInput.Text = "";
        }

        private async Task ShowErrorAsync(string message)
        {
            var messageBox = new Window
            {
                Title = "Ошибка",
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                CanResize = false
            };

            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10),
                MinWidth = 200
            };

            var button = new Button
            {
                Content = "OK",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10)
            };

            var stack = new StackPanel
            {
                Margin = new Thickness(10),
                Spacing = 10
            };

            stack.Children.Add(textBlock);
            stack.Children.Add(button);
            messageBox.Content = stack;

            button.Click += (s, e) => messageBox.Close();

            await messageBox.ShowDialog(this);
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var messageBox = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                CanResize = false
            };

            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10),
                MinWidth = 200
            };

            var button = new Button
            {
                Content = "OK",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10)
            };

            var stack = new StackPanel
            {
                Margin = new Thickness(10),
                Spacing = 10
            };

            stack.Children.Add(textBlock);
            stack.Children.Add(button);
            messageBox.Content = stack;

            button.Click += (s, e) => messageBox.Close();

            await messageBox.ShowDialog(this);
        }

        private async Task ReceiveMessagesAsync()
        {
            byte[] buffer = new byte[1024];
            while (clientSocket?.Connected == true)
            {
                try
                {
                    int bytesRead = await clientSocket.ReceiveAsync(buffer, SocketFlags.None);
                    if (bytesRead == 0)
                    {
                        await HandleDisconnect();
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (message.StartsWith("WAITWORD:"))
                        {
                            // Формат: WAITWORD:ИмяВедущего
                            string setter = message.Substring("WAITWORD:".Length);
                            currentWordSetter = setter;
                            if (setter == userName)
                            {
                                isWordSetter = true;
                                letterInput!.MaxLength = 50;
                                letterInput.IsEnabled = true;
                                guessButton!.Content = "Задать слово";
                                guessButton.IsEnabled = true;
                            }
                            else
                            {
                                isWordSetter = false;
                                letterInput!.MaxLength = 1;
                                guessButton!.Content = "Отгадать букву";
                                // Для остальных кнопка пока не активна, пока не наступит их ход
                                guessButton.IsEnabled = false;
                            }
                        }
                        else if (message.StartsWith("GAME:"))
                        {
                            // Переход в режим угадывания
                            isWordSetter = false;
                            letterInput!.MaxLength = 1;
                            guessButton!.Content = "Отгадать букву";
                            UpdateGameState(message);
                        }
                        else if (message.StartsWith("GAMEOVER:"))
                        {
                            HandleGameOver(message);
                        }
                        else if (message.StartsWith("USERS:"))
                        {
                            // Формат: USERS:Имя:Цвет:Очки:active/inactive,Имя:...
                            string[] users = message.Substring(6).Split(',', StringSplitOptions.RemoveEmptyEntries);
                            UpdateUsersList(users);
                        }
                        else if (message.StartsWith("GAMEFINAL:"))
                        {
                            _ = ShowMessageAsync("Игра окончена", message.Substring("GAMEFINAL:".Length));
                        }
                    });
                }
                catch (Exception)
                {
                    await HandleDisconnect();
                    break;
                }
            }
        }

        private async Task HandleDisconnect()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (nicknameInput != null)
                    nicknameInput.IsEnabled = true;
                if (addUserButton != null)
                    addUserButton.IsEnabled = true;
                if (letterInput != null)
                    letterInput.IsEnabled = false;
                if (guessButton != null)
                    guessButton.IsEnabled = false;
                if (usersList != null)
                    usersList.Items.Clear();
                if (wordDisplay != null)
                    wordDisplay.Text = "";
                if (attemptsDisplay != null)
                    attemptsDisplay.Text = "";
                if (currentPlayerDisplay != null)
                    currentPlayerDisplay.Text = "";
                if (guessedLettersDisplay != null)
                    guessedLettersDisplay.Text = "";
            });

            await ShowErrorAsync("Соединение с сервером потеряно");
        }

        /// <summary>
        /// Обновление игрового состояния.
        /// Формат: GAME:maskedWord:attemptsLeft:currentGuesser:guessedLetters
        /// </summary>
        private void UpdateGameState(string message)
        {
            var parts = message.Split(':');
            if (parts.Length != 5)
                return;

            if (wordDisplay != null)
                wordDisplay.Text = parts[1];

            if (int.TryParse(parts[2], out int attempts))
            {
                if (attemptsDisplay != null)
                    attemptsDisplay.Text = $"Попыток: {new string('♥', attempts)}{new string('♡', 6 - attempts)}";
                UpdateHangmanImage(attempts);
            }

            if (currentPlayerDisplay != null)
                currentPlayerDisplay.Text = $"Ход угадывания: {parts[3]}";

            if (guessedLettersDisplay != null)
            {
                var letters = parts[4].Split(',', StringSplitOptions.RemoveEmptyEntries);
                guessedLettersDisplay.Text = letters.Length > 0
                    ? $"Использованные буквы:\n{string.Join(" ", letters.OrderBy(l => l))}"
                    : "Использованные буквы: нет";
            }

            // В режиме угадывания поле ввода и кнопка становятся активными только для текущего угадывающего,
            // при этом, если вы являетесь ведущим (т.е. currentWordSetter совпадает с вашим именем),
            // то вы не должны участвовать в угадывании.
            if (letterInput != null && guessButton != null)
            {
                bool isGuesser = (parts[3] == userName) && (currentWordSetter != userName);
                letterInput.IsEnabled = isGuesser;
                guessButton.IsEnabled = isGuesser;
            }
        }

        private void UpdateUsersList(string[] users)
        {
            if (usersList == null)
                return;

            usersList.Items.Clear();
            foreach (var userInfo in users)
            {
                // Формат: Имя:Цвет:Очки:active/inactive
                var parts = userInfo.Split(':');
                if (parts.Length != 4)
                    continue;

                var name = parts[0];
                var colorHex = parts[1];
                var score = parts[2];
                var isActive = parts[3] == "active";

                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10
                };

                var nameBlock = new TextBlock
                {
                    Text = name,
                    Foreground = isActive ? new SolidColorBrush(Color.Parse(colorHex)) : new SolidColorBrush(Colors.Gray)
                };

                var scoreBlock = new TextBlock
                {
                    Text = $"[{score} очков]",
                    Foreground = isActive ? new SolidColorBrush(Colors.LightGray) : new SolidColorBrush(Colors.Gray)
                };

                stackPanel.Children.Add(nameBlock);
                stackPanel.Children.Add(scoreBlock);
                usersList.Items.Add(stackPanel);
            }
        }

        private void HandleGameOver(string message)
        {
            var parts = message.Split(':');
            if (parts.Length != 4) // Теперь ожидаем 4 части
                return;

            string word = parts[1];
            string result = parts[2];
            string winnerName = parts[3];

            if (gameStatus != null)
            {
                if (result == "win")
                {
                    gameStatus.Text = userName == winnerName ? 
                        "Вы победили!" : 
                        $"Игрок {winnerName} победил!";
                }
                else
                {
                    gameStatus.Text = userName == winnerName ?
                        "Вы проиграли" :
                        "Раунд проигран";
                }
            }

            string resultMessage = result == "win"
                ? $"Поздравляем {winnerName}!\nЗагаданное слово: {word}\nПобеда в раунде!"
                : $"Раунд проигран\nЗагаданное слово: {word}\nВедущий {winnerName} получает балл.";

            _ = ShowMessageAsync(
                result == "win" ? "Победа раунда!" : "Проигрыш раунда",
                resultMessage
            );
        }
        
        private void HandleGameState(string message)
        {
            if (message.StartsWith("WAITWORD:"))
            {
                string setter = message.Substring("WAITWORD:".Length);
                currentWordSetter = setter;
        
                // Обновляем UI для ведущего
                if (setter == userName)
                {
                    isWordSetter = true;
                    if (letterInput != null)
                    {
                        letterInput.MaxLength = 50;
                        letterInput.IsEnabled = true;
                        letterInput.Text = "";
                    }
                    if (guessButton != null)
                    {
                        guessButton.Content = "Задать слово";
                        guessButton.IsEnabled = true;
                    }
                    if (gameStatus != null)
                    {
                        gameStatus.Text = "Вы ведущий! Введите слово";
                    }
                }
                else
                {
                    isWordSetter = false;
                    if (letterInput != null)
                    {
                        letterInput.MaxLength = 1;
                        letterInput.IsEnabled = false;
                        letterInput.Text = "";
                    }
                    if (guessButton != null)
                    {
                        guessButton.Content = "Отгадать букву";
                        guessButton.IsEnabled = false;
                    }
                    if (gameStatus != null)
                    {
                        gameStatus.Text = $"Ожидаем слово от игрока {setter}";
                    }
                }
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (clientSocket?.Connected == true)
            {
                try
                {
                    string disconnectMessage = $"DISCONNECT:{userName}";
                    byte[] buffer = Encoding.UTF8.GetBytes(disconnectMessage);
                    clientSocket.Send(buffer);

                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                }
                catch
                {
                    // Игнорируем ошибки при закрытии
                }
            }
            base.OnClosing(e);
        }
    }
}
