using System.Text;
using ForestMessenger.TUI.Tabs.Interfaces;

namespace ForestMessenger.TUI.Tabs.Classes.TabClasses
{
    public class SettingsTab : ITab
    {
        public string Name => "Настройки";
        public string Description => "Параметры приложения";
        public bool IsActive { get; set; }
        public bool IsDirty { get; set; }

        private readonly INavigationService _navigationService;
        private List<SettingItem> _settings = new();
        private int _selectedIndex = 0;

        public SettingsTab(INavigationService navigationService)
        {
            _navigationService = navigationService;

            _settings = new List<SettingItem>
            {
                new SettingItem { Name = "Профиль", Description = "Настройки профиля", Icon = "👤" },
                new SettingItem { Name = "Безопасность", Description = "Шифрование и приватность", Icon = "🔒" },
                new SettingItem { Name = "Сеть", Description = "I2P, Tor, P2P", Icon = "🌐" },
                new SettingItem { Name = "Внешний вид", Description = "Тема, цвета, шрифты", Icon = "🎨" },
                new SettingItem { Name = "Уведомления", Description = "Звуки и уведомления", Icon = "🔔" },
                new SettingItem { Name = "О программе", Description = "Версия, лицензия", Icon = "ℹ️" },
            };
        }

        public async Task HandleInputAsync(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    _selectedIndex = (_selectedIndex - 1 + _settings.Count) % _settings.Count;
                    await RenderAsync();
                    break;

                case ConsoleKey.DownArrow:
                    _selectedIndex = (_selectedIndex + 1) % _settings.Count;
                    await RenderAsync();
                    break;

                case ConsoleKey.Enter:
                    if (_settings.Any())
                    {
                        var setting = _settings[_selectedIndex];
                        await OpenSettingAsync(setting);
                    }
                    break;

                case ConsoleKey.Escape:
                    await _navigationService.SwitchToTabAsync<MainTab>();
                    break;
            }
        }

        private async Task OpenSettingAsync(SettingItem setting)
        {
            await _navigationService.ShowStatusAsync(
                $"⚙️ Открыт раздел: {setting.Name} (в разработке)",
                ConsoleColor.Cyan
            );
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

            await RenderHeaderAsync();
            await RenderSettingsAsync();
            await RenderFooterAsync();
        }

        private async Task RenderHeaderAsync()
        {
            int width = Console.WindowWidth;
            var sb = new StringBuilder();

            sb.AppendLine($"╔{new string('═', width - 2)}╗");

            string title = $"⚙️ {Name}";
            sb.AppendLine($"║ {title.PadRight(width - 4)} ║");

            sb.AppendLine($"╠{new string('═', width - 2)}╣");

            Console.Write(sb.ToString());
        }

        private async Task RenderSettingsAsync()
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;

            if (!_settings.Any())
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("║ Нет настроек.                                       ║");
                Console.ResetColor();
                return;
            }

            int maxVisible = Math.Max(1, height - 10);
            int startIndex = Math.Max(0, _selectedIndex - maxVisible / 2);
            int endIndex = Math.Min(_settings.Count, startIndex + maxVisible);

            for (int i = startIndex; i < endIndex; i++)
            {
                var setting = _settings[i];
                bool isSelected = (i == _selectedIndex);

                string line = $"  {setting.Icon} {setting.Name}".PadRight(30);
                line += $"{setting.Description}".PadRight(width - 35);

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
                "Enter: Открыть",
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

    public class SettingItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public object? Value { get; set; }
    }
}