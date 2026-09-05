using System.Text;
using ForestMessenger.TUI.Tabs.Interfaces;

namespace ForestMessenger.TUI.Tabs.Classes.TabClasses
{
    public class MainTab : ITab
    {
        public string Name => "Главное меню";

        public string Description => "Это главное меню!";

        public bool IsActive { get; set; }
        public bool IsDirty { get; set; }

        private readonly List<MenuItem> _menuItems = new()
        {
            new MenuItem { Key = '1', Name = "Контакты", Description = "Управление контактами", Icon = "👤" },
            new MenuItem { Key = '2', Name = "Чаты", Description = "Ваши активные чаты", Icon = "💬" },
            new MenuItem { Key = '3', Name = "Каналы", Description = "Подписки на каналы", Icon = "📢" },
            new MenuItem { Key = '4', Name = "Настройки", Description = "Параметры приложения", Icon = "⚙️" },
            new MenuItem { Key = '5', Name = "О программе", Description = "Информация о Forest Messenger", Icon = "🌲" }
        };

        private readonly INavigationService _navigationService;

        private int _selectedIndex = 0;
        private bool _animationCompleted = false;

        public MainTab(INavigationService navigationService)
        {
            _navigationService = navigationService;
        }

        public async Task HandleInputAsync(ConsoleKeyInfo key)
        {           
            if (char.IsDigit(key.KeyChar))
            {
                int index = int.Parse(key.KeyChar.ToString()) - 1;
                if (index >= 0 && index < _menuItems.Count)
                {
                    await ExecuteMenuItemAsync(index);
                    return;
                }
            }

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    _selectedIndex = (_selectedIndex - 1 + _menuItems.Count) % _menuItems.Count;
                    await RenderAsync();
                    break;

                case ConsoleKey.DownArrow:
                    _selectedIndex = (_selectedIndex + 1) % _menuItems.Count;
                    await RenderAsync();
                    break;

                case ConsoleKey.Enter:
                    await ExecuteMenuItemAsync(_selectedIndex);
                    break;

                case ConsoleKey.Escape:
                    await _navigationService.ShowStatusAsync("Выход...", ConsoleColor.Yellow);
                    await Task.Delay(300);
                    Console.Clear();
                    Environment.Exit(0);
                    break;
            }
        }

        private async Task ExecuteMenuItemAsync(int index)
        {
            var menuItem = _menuItems[index];
            
            await _navigationService.ShowStatusAsync(
                $"Переход в раздел: {menuItem.Icon} {menuItem.Name}...", 
                ConsoleColor.Cyan
            );

            await Task.Delay(200);

            switch (index)
            {
                case 0: 
                    await _navigationService.SwitchToTabAsync<ContactsTab>();
                    break;
                case 1: 
                    await _navigationService.SwitchToTabAsync<ChatsTab>();
                    break;
                case 2:
                    await _navigationService.SwitchToTabAsync<ChannelsTab>();
                    break;
                case 3:
                    await _navigationService.SwitchToTabAsync<SettingsTab>();
                    break;
                case 4: 
                    await ShowAboutAsync();
                    break;
            }
        }

        public async Task OnEnterAsync()
        {
            _animationCompleted = false;
            await RenderAsync();
            _animationCompleted = true;
        }

        public async Task OnLeaveAsync()
        {
            await Task.CompletedTask;
        }

        public async Task RenderAsync()
        {
            Console.Clear();
            
            await RenderHeaderAsync();
            
            await RenderMenuAsync();
            
            await RenderFooterAsync();
        }
        
        private async Task RenderHeaderAsync()
        {
            int width = Console.WindowWidth;
            var sb = new StringBuilder();

            sb.AppendLine($"╔{new string('═', width - 2)}╗");
            
            string title = "🌲 Forest Messenger";
            sb.AppendLine($"║ {title.PadRight(width - 4)} ║");
            
            sb.AppendLine($"╠{new string('═', width - 2)}╣");
            
            string subtitle = "Главное меню";
            sb.AppendLine($"║ {subtitle.PadRight(width - 4)} ║");
            
            sb.AppendLine($"╠{new string('═', width - 2)}╣");
            
            string header = sb.ToString();
            Console.WriteLine(header);
        }

        private async Task RenderMenuAsync()
        {
            int width = Console.WindowWidth;
            
            Console.WriteLine();

            for (int i = 0; i < _menuItems.Count; i++)
            {
                var item = _menuItems[i];
                bool isSelected = (i == _selectedIndex);

                string prefix = isSelected ? "▶" : " ";
                string number = $"{item.Key}.";
                string icon = item.Icon;
                string name = item.Name;
                string description = item.Description;

                string line = $"  {prefix} {number} {icon} {name}".PadRight(width - 20);
                
                if (isSelected)
                {
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(line);
                    Console.ResetColor();
                    
                    Console.SetCursorPosition(width - 20, Console.CursorTop);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"\n  └ {description}");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write(line);
                    Console.ResetColor();
                }
                
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine(new string('─', Console.WindowWidth));
        }

        private async Task RenderFooterAsync()
        {
            int width = Console.WindowWidth;
            var sb = new StringBuilder();

            string[] hints = new[]
            {
                "1-5: Выбор раздела",
                "↑↓: Навигация",
                "Enter: Выбрать",
                "Esc: Выход"
            };

            sb.Append($"└ {string.Join("  │  ", hints)} ");
            sb.Append(new string(' ', Math.Max(0, width - sb.Length - 2)));
            sb.Append("┘");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(sb.ToString());
            Console.ResetColor();
        }

        private async Task ShowAboutAsync()
        {            
            Console.Clear();
            
            string logo = @"
    ███████╗ ██████╗ ██████╗ ███████╗███████╗████████╗
    ██╔════╝██╔═══██╗██╔══██╗██╔════╝██╔════╝╚══██╔══╝
    █████╗  ██║   ██║██████╔╝█████╗  ███████╗   ██║   
    ██╔══╝  ██║   ██║██╔══██╗██╔══╝  ╚════██║   ██║   
    ██║     ╚██████╔╝██║  ██║███████╗███████║   ██║   
    ╚═╝      ╚═════╝ ╚═╝  ╚═╝╚══════╝╚══════╝   ╚═╝   ";
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(logo);
            Console.ResetColor();
            
            Console.WriteLine();
            Console.WriteLine("  🌲 Forest Messenger TUI");
            Console.WriteLine("  Децентрализованный, анонимный мессенджер");
            Console.WriteLine();
            Console.WriteLine("  Версия: Alpha 0.0.1");
            Console.WriteLine("  Лицензия: GPLv3");
            Console.WriteLine("  GitHub: https://github.com/Forest-Freedom-Project/Forest-Messager-TUI");
            Console.WriteLine();
            Console.WriteLine("  Нажмите любую клавишу для возврата...");
            
            await Task.Delay(100);
            Console.ReadKey(intercept: true);
            await RenderAsync();
        }
    }

    public class MenuItem
    {
        public char Key { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }
}