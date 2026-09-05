using System.Text;
using ForestMessenger.TUI.Tabs.Interfaces;

namespace ForestMessenger.TUI.Tabs.Classes.TabClasses
{
   public class DmChatTab : ITab
    {
        public string Name => "Личный чат";
        public string Description => "Диалог с контактом";
        public bool IsActive { get; set; }
        public bool IsDirty { get; set; }

        private readonly INavigationService _navigationService;
        private string _contactName = string.Empty;
        private List<MessageItem> _messages = new();
        private string _messageInput = string.Empty;
        private int _scrollOffset = 0;
        private bool _isTyping = false;

        public DmChatTab(INavigationService navigationService)
        {
            _navigationService = navigationService;
        }

        public void OpenChat(string contactName)
        {
            _contactName = contactName;            
            _scrollOffset = Math.Max(0, _messages.Count - 10);
            _messageInput = string.Empty;
            _scrollOffset = 0;
        }

        public async Task HandleInputAsync(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    if (!string.IsNullOrWhiteSpace(_messageInput))
                    {
                        await SendMessageAsync();
                    }
                    break;

                case ConsoleKey.Backspace:
                    if (_messageInput.Length > 0)
                    {
                        _messageInput = _messageInput[..^1];
                        await RenderAsync();
                    }
                    break;

                case ConsoleKey.UpArrow:
                    if (_scrollOffset < _messages.Count - 1)
                    {
                        _scrollOffset++;
                        await RenderAsync();
                    }
                    break;

                case ConsoleKey.DownArrow:
                    if (_scrollOffset > 0)
                    {
                        _scrollOffset--;
                        await RenderAsync();
                    }
                    break;

                case ConsoleKey.PageUp:
                    _scrollOffset = Math.Max(0, _scrollOffset - 10);
                    await RenderAsync();
                    break;

                case ConsoleKey.PageDown:
                    _scrollOffset = Math.Min(_messages.Count - 1, _scrollOffset + 10);
                    await RenderAsync();
                    break;

                case ConsoleKey.Escape:
                    await _navigationService.SwitchToTabAsync<ChatsTab>();
                    break;

                case ConsoleKey.Home:
                    _scrollOffset = _messages.Count - 1;
                    await RenderAsync();
                    break;

                case ConsoleKey.End:
                    _scrollOffset = 0;
                    await RenderAsync();
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

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(_messageInput)) return;

            var message = new MessageItem
            {
                Sender = "Я",
                Text = _messageInput,
                Time = DateTime.Now,
                IsOwn = true
            };

            _messages.Add(message);
            _messageInput = string.Empty;
            _scrollOffset = 0;

            await RenderAsync();

            await SimulateReplyAsync();
        }

        private async Task SimulateReplyAsync()
        {
            await Task.Delay(1000);
            
            var replies = new[]
            {
                "Понял!",
                "Интересно...",
                "Да, согласен",
                "Хорошо, договорились",
                "Спасибо за информацию!",
                "Отлично!"
            };

            var reply = new MessageItem
            {
                Sender = _contactName,
                Text = replies[new Random().Next(replies.Length)],
                Time = DateTime.Now,
                IsOwn = false
            };

            _messages.Add(reply);
            _scrollOffset = 0;
            await RenderAsync();
        }

        public async Task OnEnterAsync()
        {
            await RenderAsync();
        }

        public async Task OnLeaveAsync()
        {
            await Task.CompletedTask;
        }

        public async Task RenderAsync()
        {
            Console.Clear();

            int width = Console.WindowWidth;
            int height = Console.WindowHeight;

            await RenderHeaderAsync();
            await RenderMessagesAsync(width, height);
            await RenderInputAreaAsync(width);
            await RenderFooterAsync(width);
        }

        private async Task RenderHeaderAsync()
        {
            int width = Console.WindowWidth;
            var sb = new StringBuilder();

            sb.AppendLine($"╔{new string('═', width - 2)}╗");
            
            string title = $"💬 {_contactName}";
            sb.AppendLine($"║ {title.PadRight(width - 4)} ║");
            
            sb.AppendLine($"╠{new string('═', width - 2)}╣");
            
            string status = _isTyping ? "✏️ Печатает..." : "🟢 В сети";
            sb.AppendLine($"║ {status.PadRight(width - 4)} ║");
            
            sb.AppendLine($"╠{new string('═', width - 2)}╣");

            Console.Write(sb.ToString());
        }

        private async Task RenderMessagesAsync(int width, int height)
        {
            int maxMessages = height - 8;
            int totalMessages = _messages.Count;

            int startIndex = Math.Max(0, totalMessages - maxMessages - _scrollOffset);
            int endIndex = Math.Min(totalMessages, startIndex + maxMessages);

            if (totalMessages < maxMessages)
            {
                int emptyLines = maxMessages - totalMessages - 3;
                for (int i = 0; i < emptyLines; i++)
                {
                    Console.WriteLine($"║{"".PadRight(width - 2)}║");
                }
                startIndex = 0;
                endIndex = totalMessages;
            }

            for (int i = startIndex; i < endIndex; i++)
            {
                var msg = _messages[i];
                string timeStr = msg.Time.ToString("HH:mm");

                string messageText = $"[{timeStr}] {msg.Text}";

                if (messageText.Length > width - 8)
                {
                    messageText = messageText[..(width - 11)] + "...";
                }

                Console.Write($"║ ");
                Console.ForegroundColor = msg.IsOwn ? ConsoleColor.Cyan : ConsoleColor.White;
                Console.Write(messageText.PadRight(width - 3));
                Console.ResetColor();
                Console.Write($"║");
                Console.WriteLine();
            }

            Console.WriteLine($"╠{new string('═', width - 2)}╣");
        }

        private async Task RenderInputAreaAsync(int width)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"║ ");
            Console.ResetColor();
            
            string displayText = _messageInput;
            if (displayText.Length > width - 8)
            {
                displayText = displayText[..(width - 11)] + "...";
            }
            
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(displayText.PadRight(width - 4));
            Console.ResetColor();
            
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" ║");
            Console.WriteLine($"╚{new string('═', width - 2)}╝");
            Console.ResetColor();
        }

        private async Task RenderFooterAsync(int width)
        {
            var sb = new StringBuilder();

            string[] hints = new[]
            {
                "Введите сообщение",
                "Enter: Отправить",
                "Esc: Выйти из чата",
                "↑↓: Прокрутка"
            };

            sb.Append($"└ {string.Join("  │  ", hints)} ");
            sb.Append(new string(' ', Math.Max(0, width - sb.Length - 1)));
            sb.Append("┘");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(sb.ToString());
            Console.ResetColor();
        }
    }

    public class MessageItem
    {
        public string Sender { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public bool IsOwn { get; set; }
    }
}