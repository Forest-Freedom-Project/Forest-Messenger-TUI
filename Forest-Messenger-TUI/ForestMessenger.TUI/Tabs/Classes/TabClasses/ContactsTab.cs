using System.Text;
using ForestMessenger.TUI.Tabs.Interfaces;

namespace ForestMessenger.TUI.Tabs.Classes.TabClasses
{
    public class ContactsTab : ITab
    {
        public string Name => "Контакты";
        public string Description => "Управление контактами";
        public bool IsActive { get; set; }
        public bool IsDirty { get; set; }

        private readonly INavigationService _navigationService;
        private List<ContactItem> _contacts = new();
        private int _selectedIndex = 0;
        private string _searchQuery = string.Empty;
        private bool _isSearchMode = false;

        public ContactsTab(INavigationService navigationService)
        {
            _navigationService = navigationService;
            
            _contacts = new List<ContactItem>
            {
                new ContactItem { Name = "Alice", Status = "В сети", LastSeen = DateTime.Now, IsOnline = true },
                new ContactItem { Name = "Bob", Status = "Отошел", LastSeen = DateTime.Now.AddMinutes(-5), IsOnline = false },
                new ContactItem { Name = "Charlie", Status = "Не в сети", LastSeen = DateTime.Now.AddHours(-2), IsOnline = false },
                new ContactItem { Name = "David", Status = "В сети", LastSeen = DateTime.Now, IsOnline = true },
                new ContactItem { Name = "Eve", Status = "В сети", LastSeen = DateTime.Now, IsOnline = true },
                new ContactItem { Name = "Frank", Status = "Не в сети", LastSeen = DateTime.Now.AddDays(-1), IsOnline = false },
                new ContactItem { Name = "Grace", Status = "Отошел", LastSeen = DateTime.Now.AddMinutes(-15), IsOnline = false },
                new ContactItem { Name = "Henry", Status = "В сети", LastSeen = DateTime.Now, IsOnline = true },
            };
        }

        public async Task HandleInputAsync(ConsoleKeyInfo key)
        {
            if (_isSearchMode)
            {
                await HandleSearchInputAsync(key);
                return;
            }

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    _selectedIndex = (_selectedIndex - 1 + _contacts.Count) % _contacts.Count;
                    await RenderAsync();
                    break;

                case ConsoleKey.DownArrow:
                    _selectedIndex = (_selectedIndex + 1) % _contacts.Count;
                    await RenderAsync();
                    break;

                case ConsoleKey.Enter:
                    if (_contacts.Any())
                    {
                        var contact = _contacts[_selectedIndex];
                        await _navigationService.ShowStatusAsync(
                            $"Открыт чат с {contact.Name}",
                            ConsoleColor.Cyan
                        );
                        // Здесь переход в чат
                        // await _navigationService.SwitchToTabAsync<ChatsTab>();
                    }
                    break;

                case ConsoleKey.F1:
                    await AddContactAsync();
                    break;

                case ConsoleKey.Delete:
                    if (_contacts.Any())
                    {
                        var contact = _contacts[_selectedIndex];
                        await DeleteContactAsync(contact);
                    }
                    break;

                case ConsoleKey.S:
                    if (key.Modifiers == ConsoleModifiers.Control)
                    {
                        _isSearchMode = true;
                        _searchQuery = string.Empty;
                        await RenderAsync();
                    }
                    break;

                case ConsoleKey.Escape:
                    await _navigationService.SwitchToTabAsync<MainTab>();
                    break;
            }
        }

        private async Task HandleSearchInputAsync(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    _isSearchMode = false;
                    await ApplySearchAsync();
                    await RenderAsync();
                    break;

                case ConsoleKey.Escape:
                    _isSearchMode = false;
                    _searchQuery = string.Empty;
                    await RenderAsync();
                    break;

                case ConsoleKey.Backspace:
                    if (_searchQuery.Length > 0)
                    {
                        _searchQuery = _searchQuery[..^1];
                        await RenderAsync();
                    }
                    break;

                default:
                    if (char.IsLetterOrDigit(key.KeyChar) || char.IsWhiteSpace(key.KeyChar))
                    {
                        _searchQuery += key.KeyChar;
                        await RenderAsync();
                    }
                    break;
            }
        }

        private async Task ApplySearchAsync()
        {
            await _navigationService.ShowStatusAsync($"Поиск: {_searchQuery}", ConsoleColor.Cyan);
        }

        private async Task AddContactAsync()
        {
            await _navigationService.ShowStatusAsync("Добавление контакта... (в разработке)", ConsoleColor.Yellow);
        }

        private async Task DeleteContactAsync(ContactItem contact)
        {
            await _navigationService.ShowStatusAsync($"Удаление контакта {contact.Name}...", ConsoleColor.Red);
            _contacts.Remove(contact);
            _selectedIndex = Math.Min(_selectedIndex, _contacts.Count - 1);
            await RenderAsync();
        }

        public async Task OnEnterAsync()
        {
            await RenderAsync();
        }

        public async Task OnLeaveAsync()
        {
            _isSearchMode = false;
            _searchQuery = string.Empty;
            await Task.CompletedTask;
        }

        public async Task RenderAsync()
        {
            Console.Clear();
            
            int width = Console.WindowWidth;
            
            await RenderHeaderAsync();
            
            int online = _contacts.Count(c => c.IsOnline);
            await RenderStatsAsync(online);
            
            await RenderContactsAsync();
            
            await RenderFooterAsync();
        }

        private async Task RenderHeaderAsync()
        {
            int width = Console.WindowWidth;
            var sb = new StringBuilder();

            sb.AppendLine($"╔{new string('═', width - 2)}╗");
            
            string title = $"👤 {Name}";
            sb.AppendLine($"║ {title.PadRight(width - 4)} ║");
            
            sb.AppendLine($"╠{new string('═', width - 2)}╣");
            
            if (_isSearchMode)
            {
                string searchPrompt = $"🔍 Поиск: {_searchQuery}";
                sb.AppendLine($"║ {searchPrompt.PadRight(width - 4)} ║");
                sb.AppendLine($"╠{new string('═', width - 2)}╣");
            }

            await Task.CompletedTask;
            Console.Write(sb.ToString());
        }

        private async Task RenderStatsAsync(int online)
        {
            int width = Console.WindowWidth;
            
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"║ Всего: {_contacts.Count}  |  В сети: {online}  |  Офлайн: {_contacts.Count - online} ║");
            Console.WriteLine($"╠{new string('═', width - 2)}╣");
            Console.ResetColor();
        }

        private async Task RenderContactsAsync()
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            
            if (!_contacts.Any())
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("║ Нет контактов. Нажмите F1 для добавления.            ║");
                Console.ResetColor();
                return;
            }

            int maxVisible = Math.Max(1, height - 12);
            
            int startIndex = Math.Max(0, _selectedIndex - maxVisible / 2);
            int endIndex = Math.Min(_contacts.Count, startIndex + maxVisible);

            for (int i = startIndex; i < endIndex; i++)
            {
                var contact = _contacts[i];
                bool isSelected = (i == _selectedIndex);
                
                string statusSymbol = contact.IsOnline ? "●" : "○";
                string statusColor = contact.IsOnline ? "🟢" : "⚪";
                
                string line = $"  {statusSymbol} {contact.Name}".PadRight(width - 20);
                line += $" {statusColor} {contact.Status}".PadRight(20);
                
                if (isSelected)
                {
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"▶ {line}");
                    Console.ResetColor();
                }
                else
                {
                    Console.Write($"  {line}");
                }
                
                Console.WriteLine();
            }
            
            Console.WriteLine($"╠{new string('═', width - 2)}╣");
        }

        private async Task RenderFooterAsync()
        {
            int width = Console.WindowWidth;
            var sb = new StringBuilder();

            string[] hints = new[]
            {
                "↑↓: Выбор",
                "Enter: Чат",
                "Ctrl+S: Поиск",
                "F1: Добавить",
                "Del: Удалить",
                "Esc: Назад"
            };

            sb.Append($"└ {string.Join("  │  ", hints)} ");
            sb.Append(new string(' ', Math.Max(0, width - sb.Length - 2)));
            sb.Append("┘");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(sb.ToString());
            Console.ResetColor();
        }
    }

    public class ContactItem
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime LastSeen { get; set; }
        public bool IsOnline { get; set; }
    }
}