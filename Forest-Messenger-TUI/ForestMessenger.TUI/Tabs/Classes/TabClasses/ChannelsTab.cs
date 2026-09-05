using System.Text;
using ForestMessenger.TUI.Tabs.Interfaces;

namespace ForestMessenger.TUI.Tabs.Classes.TabClasses
{
    public class ChannelsTab : ITab
    {
        public string Name => "Каналы";
        public string Description => "Подписки на каналы";
        public bool IsActive { get; set; }
        public bool IsDirty { get; set; }

        private readonly INavigationService _navigationService;
        private List<ChannelItem> _channels = new();
        private int _selectedIndex = 0;

        public ChannelsTab(INavigationService navigationService)
        {
            _navigationService = navigationService;
            
            _channels = new List<ChannelItem>
            {
                new ChannelItem { Name = "Новости мира", Subscribers = 1234, Description = "Глобальные новости", Category = "Новости" },
                new ChannelItem { Name = "Tech News", Subscribers = 890, Description = "Технологические новости", Category = "Технологии" },
                new ChannelItem { Name = "Крипто-обзор", Subscribers = 567, Description = "Обзор криптовалют", Category = "Финансы" },
                new ChannelItem { Name = "Музыкальный канал", Subscribers = 345, Description = "Новые релизы музыки", Category = "Музыка" },
                new ChannelItem { Name = "Игровые новости", Subscribers = 234, Description = "Игровая индустрия", Category = "Игры" },
                new ChannelItem { Name = "Наука и космос", Subscribers = 678, Description = "Научные открытия", Category = "Наука" },
            };
        }

        public async Task HandleInputAsync(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    _selectedIndex = (_selectedIndex - 1 + _channels.Count) % _channels.Count;
                    await RenderAsync();
                    break;

                case ConsoleKey.DownArrow:
                    _selectedIndex = (_selectedIndex + 1) % _channels.Count;
                    await RenderAsync();
                    break;

                case ConsoleKey.Enter:
                    if (_channels.Any())
                    {
                        var channel = _channels[_selectedIndex];
                        await _navigationService.ShowStatusAsync(
                            $"📢 Подписка на канал {channel.Name} (в разработке)",
                            ConsoleColor.Cyan
                        );
                    }
                    break;

                case ConsoleKey.F1:
                    await ShowAllChannelsAsync();
                    break;

                case ConsoleKey.F2:
                    await ShowSubscribedChannelsAsync();
                    break;

                case ConsoleKey.Escape:
                    await _navigationService.SwitchToTabAsync<MainTab>();
                    break;
            }
        }

        private async Task ShowAllChannelsAsync()
        {
            await _navigationService.ShowStatusAsync("Все каналы (в разработке)", ConsoleColor.Yellow);
        }

        private async Task ShowSubscribedChannelsAsync()
        {
            await _navigationService.ShowStatusAsync("Мои подписки (в разработке)", ConsoleColor.Yellow);
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
            await RenderStatsAsync();
            await RenderChannelsAsync();
            await RenderFooterAsync();
        }

        private async Task RenderHeaderAsync()
        {
            int width = Console.WindowWidth;
            var sb = new StringBuilder();

            sb.AppendLine($"╔{new string('═', width - 2)}╗");
            
            string title = $"📢 {Name}";
            sb.AppendLine($"║ {title.PadRight(width - 4)} ║");
            
            sb.AppendLine($"╠{new string('═', width - 2)}╣");

            Console.Write(sb.ToString());
        }

        private async Task RenderStatsAsync()
        {
            int width = Console.WindowWidth;
            
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"║ Всего каналов: {_channels.Count}  |  Всего подписчиков: {_channels.Sum(c => c.Subscribers)} ║");
            Console.WriteLine($"╠{new string('═', width - 2)}╣");
            Console.ResetColor();
        }

        private async Task RenderChannelsAsync()
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            
            if (!_channels.Any())
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("║ Нет доступных каналов.                               ║");
                Console.ResetColor();
                return;
            }

            int maxVisible = Math.Max(1, height - 12);
            int startIndex = Math.Max(0, _selectedIndex - maxVisible / 2);
            int endIndex = Math.Min(_channels.Count, startIndex + maxVisible);

            for (int i = startIndex; i < endIndex; i++)
            {
                var channel = _channels[i];
                bool isSelected = (i == _selectedIndex);
                
                string line = $"  📣 {channel.Name}".PadRight(25);
                line += $"{channel.Description}".PadRight(width - 45);
                line += $"{channel.Subscribers}".PadRight(10);
                
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
                "Enter: Подписаться",
                "F1: Все каналы",
                "F2: Мои подписки",
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

    public class ChannelItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Subscribers { get; set; }
        public bool IsSubscribed { get; set; }
    }
}