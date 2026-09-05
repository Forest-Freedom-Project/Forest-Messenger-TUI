using System.Text;
using ForestMessenger.TUI.Tabs.Interfaces;

namespace ForestMessenger.TUI.Tabs.Classes.TabClasses
{
    public class ChatsTab : ITab
    {
        public string Name => "Чаты";
        public string Description => "Ваши активные чаты";
        public bool IsActive { get; set; }
        public bool IsDirty { get; set; }

        private readonly INavigationService _navigationService;
        private List<ChatItem> _chats = new();
        private int _selectedIndex = 0;
        private bool _isMessageInputMode = false;
        private string _messageInput = string.Empty;

        public ChatsTab(INavigationService navigationService)
        {
            _navigationService = navigationService;

            _chats = new List<ChatItem>
            {
                new ChatItem { Name = "Alice", LastMessage = "Привет! Как дела?", Time = DateTime.Now.AddMinutes(-2), UnreadCount = 3 },
                new ChatItem { Name = "Bob", LastMessage = "Завтра встреча в 10", Time = DateTime.Now.AddHours(-1), UnreadCount = 1 },
                new ChatItem { Name = "Charlie", LastMessage = "Спасибо!", Time = DateTime.Now.AddHours(-3), UnreadCount = 0 },
                new ChatItem { Name = "Группа: Работа", LastMessage = "Документы готовы", Time = DateTime.Now.AddHours(-5), UnreadCount = 5 },
                new ChatItem { Name = "Группа: Семья", LastMessage = "Все на выходные?", Time = DateTime.Now.AddDays(-1), UnreadCount = 0 },
            };
        }

        public async Task HandleInputAsync(ConsoleKeyInfo key)
        {
            try
            {
                if (_isMessageInputMode)
                {
                    await HandleMessageInputAsync(key);
                    return;
                }

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        _selectedIndex = (_selectedIndex - 1 + _chats.Count) % _chats.Count;
                        await RenderAsync();
                        break;

                    case ConsoleKey.DownArrow:
                        _selectedIndex = (_selectedIndex + 1) % _chats.Count;
                        await RenderAsync();
                        break;

                    case ConsoleKey.Enter:
                        if (_chats.Any())
                        {
                            var chat = _chats[_selectedIndex];

                            var dmChatTab = new DmChatTab(_navigationService);
                            dmChatTab.OpenChat(chat.Name);

                            _navigationService.RegisterTab(dmChatTab);

                            await _navigationService.SwitchToTabAsync(dmChatTab);
                        }
                        break;

                    case ConsoleKey.R:
                        if (key.Modifiers == ConsoleModifiers.Control)
                        {
                            await RefreshChatsAsync();
                        }
                        break;

                    case ConsoleKey.Escape:
                        if (_isMessageInputMode)
                        {
                            _isMessageInputMode = false;
                            await RenderAsync();
                        }
                        else
                        {
                            await _navigationService.SwitchToTabAsync<MainTab>();
                        }
                        break;
                }
            }
            catch
            {

            }
        }

        private async Task HandleMessageInputAsync(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    if (!string.IsNullOrWhiteSpace(_messageInput))
                    {
                        await SendMessageAsync(_messageInput);
                        _messageInput = string.Empty;
                    }
                    _isMessageInputMode = false;
                    await RenderAsync();
                    break;

                case ConsoleKey.Escape:
                    _isMessageInputMode = false;
                    _messageInput = string.Empty;
                    await RenderAsync();
                    break;

                case ConsoleKey.Backspace:
                    if (_messageInput.Length > 0)
                    {
                        _messageInput = _messageInput[..^1];
                        await RenderAsync();
                    }
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        _messageInput += key.KeyChar;
                        await RenderAsync();
                    }
                    break;
            }
        }

        private async Task SendMessageAsync(string message)
        {
            var chat = _chats[_selectedIndex];
            chat.LastMessage = message;
            chat.Time = DateTime.Now;
            chat.UnreadCount = 0;

            await _navigationService.ShowStatusAsync($"Сообщение отправлено в {chat.Name}", ConsoleColor.Green);
        }

        private async Task RefreshChatsAsync()
        {
            await _navigationService.ShowStatusAsync("Обновление чатов...", ConsoleColor.Yellow);
            await Task.Delay(200);
            await RenderAsync();
        }

        public async Task OnEnterAsync()
        {
            await RenderAsync();
        }

        public async Task OnLeaveAsync()
        {
            _isMessageInputMode = false;
            _messageInput = string.Empty;
            await Task.CompletedTask;
        }

        public async Task RenderAsync()
        {
            Console.Clear();

            await RenderHeaderAsync();
            await RenderStatsAsync();

            if (_isMessageInputMode)
            {
                await RenderMessageInputAsync();
            }
            else
            {
                await RenderChatsAsync();
            }

            await RenderFooterAsync();
        }

        private async Task RenderHeaderAsync()
        {
            int width = Console.WindowWidth;
            var sb = new StringBuilder();

            sb.AppendLine($"╔{new string('═', width - 2)}╗");

            string title = $"💬 {Name}";
            sb.AppendLine($"║ {title.PadRight(width - 4)} ║");

            sb.AppendLine($"╠{new string('═', width - 2)}╣");

            Console.Write(sb.ToString());
        }

        private async Task RenderStatsAsync()
        {
            int width = Console.WindowWidth;
            int totalUnread = _chats.Sum(c => c.UnreadCount);

            var sb = new StringBuilder();

            string[] hints =
            {
                $"Всего чатов: {_chats.Count}",
                $"Непрочитанных: {totalUnread}"
            };

            sb.Append($"║ {string.Join("  │  ", hints)} ");
            sb.Append(new string(' ', Math.Max(0, width - sb.Length - 1)));
            sb.Append("║");

            Console.WriteLine(sb.ToString());
            Console.WriteLine($"╠{new string('═', width - 2)}╣");
            Console.ResetColor();
        }

        private async Task RenderChatsAsync()
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;

            if (!_chats.Any())
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;

                var sb = new StringBuilder();

                string[] hints =
                {
                    $"Нет активных чатов.",
                };

                sb.Append($"║ {string.Join("  │  ", hints)} ");
                sb.Append(new string(' ', Math.Max(0, width - sb.Length - 1)));
                sb.Append("║");

                Console.WriteLine(sb.ToString());
                Console.WriteLine($"╠{new string('═', width - 2)}╣");
                Console.ResetColor();

                return;
            }

            int maxVisible = Math.Max(1, height - 12);
            int startIndex = Math.Max(0, _selectedIndex - maxVisible / 2);
            int endIndex = Math.Min(_chats.Count, startIndex + maxVisible);

            for (int i = startIndex; i < endIndex; i++)
            {
                var chat = _chats[i];
                bool isSelected = (i == _selectedIndex);

                string unreadMark = chat.UnreadCount > 0 ? $"● {chat.UnreadCount}" : "";
                string timeStr = chat.Time.ToString("HH:mm");

                string line = $"  {chat.Name}".PadRight(25) + $"{chat.LastMessage}".PadRight(width - 45) + $"{timeStr}".PadRight(10);

                if (isSelected)
                {
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"▶ {line} {unreadMark}");
                    Console.ResetColor();
                }
                else
                {
                    Console.Write($"  {line} {unreadMark}");
                }

                Console.WriteLine();
            }

            Console.WriteLine($"╠{new string('═', width - 2)}╣");
        }

        private async Task RenderMessageInputAsync()
        {
            int width = Console.WindowWidth;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"║ Сообщение: {_messageInput.PadRight(width - 15)} ║");
            Console.WriteLine($"╠{new string('═', width - 2)}╣");
            Console.ResetColor();
        }

        private async Task RenderFooterAsync()
        {
            int width = Console.WindowWidth;
            var sb = new StringBuilder();

            string[] hints = new[]
            {
                "↑↓: Выбор",
                "Enter: Написать",
                "Ctrl+R: Обновить",
                "Esc: Назад"
            };

            if (_isMessageInputMode)
            {
                hints = new[] { "Введите сообщение", "Enter: Отправить", "Esc: Отмена" };
            }

            sb.Append($"└ {string.Join("  │  ", hints)} ");
            sb.Append(new string(' ', Math.Max(0, width - sb.Length - 1)));
            sb.Append("┘");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(sb.ToString());
            Console.ResetColor();
        }
    }

    public class ChatItem
    {
        public string Name { get; set; } = string.Empty;
        public string LastMessage { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public int UnreadCount { get; set; }
    }
}