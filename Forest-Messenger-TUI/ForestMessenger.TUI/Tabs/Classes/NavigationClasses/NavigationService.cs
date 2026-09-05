using ForestMessenger.TUI.Tabs.Interfaces;
using System.Text;

namespace ForestMessenger.TUI.Navigation
{
    public class NavigationService : INavigationService
    {
        private readonly List<ITab> _tabs = new();
        private int _currentTabIndex = 0;
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public void RegisterTab(ITab tab)
        {
            if (tab == null)
                throw new ArgumentNullException(nameof(tab));
            
            _tabs.Add(tab);
        }

        public async Task InitializeAsync()
        {
            Console.CursorVisible = false;
            Console.Title = "Forest Messenger TUI";
            
            _cts = new CancellationTokenSource();
            _isRunning = true;

            if (_tabs.Any())
            {
                await SwitchToTabAsync(_tabs[0]);
            }
            
            await RenderCurrentViewAsync();
        }

        public async Task HandleInputAsync(ConsoleKeyInfo key)
        {
            if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.Q)
            {
                await ExitAsync();
                return;
            }

            if (key.Key == ConsoleKey.Tab)
            {
                if (key.Modifiers == ConsoleModifiers.Shift)
                {
                    await SwitchToPreviousTabAsync();
                }
                else
                {
                    await SwitchToNextTabAsync();
                }
                return;
            }

            if (_currentTabIndex < _tabs.Count)
            {
                await _tabs[_currentTabIndex].HandleInputAsync(key);
            }
        }

        public async Task RenderCurrentViewAsync()
        {
            Console.Clear();
            
            await RenderGlobalUIAsync();
            
            if (_currentTabIndex < _tabs.Count)
            {
                await _tabs[_currentTabIndex].RenderAsync();
            }
            
            await RenderFooterAsync();
        }

        private async Task RenderGlobalUIAsync()
        {
            int width = Console.WindowWidth;
            var sb = new StringBuilder();

            // Верхняя рамка
            sb.AppendLine($"╔{new string('═', width - 2)}╗");
            
            // Заголовок
            string title = "🌲 Forest Messenger TUI";
            sb.AppendLine($"║ {title.PadRight(width - 4)} ║");
            
            // Вкладки
            string tabs = await BuildTabBarAsync();
            sb.AppendLine($"║ {tabs.PadRight(width - 4)} ║");
            
            // Разделитель
            sb.AppendLine($"╠{new string('═', width - 2)}╣");
            
            Console.Write(sb.ToString());
        }

        private async Task<string> BuildTabBarAsync()
        {
            var sb = new StringBuilder();
            int tabWidth = 12;
            
            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                string tabName = tab.Name.Length > tabWidth - 2 
                    ? tab.Name[..(tabWidth - 2)] 
                    : tab.Name;
                    
                if (i == _currentTabIndex)
                {
                    sb.Append($"[{tabName}]");
                }
                else
                {
                    sb.Append($" {tabName} ");
                }
                
                int padding = tabWidth - tabName.Length - 2;
                sb.Append(new string(' ', padding > 0 ? padding : 0));
            }
            
            return sb.ToString();
        }

        private async Task RenderFooterAsync()
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            
            var sb = new StringBuilder();
            
            sb.AppendLine($"╚{new string('═', width - 2)}╝");
            
            string[] hints = new[]
            {
                "Tab: Переключить",
                "↑↓: Навигация",
                "Enter: Выбрать",
                "Ctrl+Q: Выход"
            };
            
            int keyWidth = width / hints.Length;
            foreach (string hint in hints)
            {
                sb.Append($"{hint.PadRight(keyWidth)}");
            }
            
            Console.SetCursorPosition(0, height - 1);
            Console.Write(sb.ToString());
        }

        public async Task SwitchToTabAsync<T>() where T : ITab
        {
            var targetTab = _tabs.OfType<T>().FirstOrDefault();
            if (targetTab != null)
            {
                await SwitchToTabAsync(targetTab);
            }
        }

        private async Task SwitchToTabAsync(ITab tab)
        {
            if (_currentTabIndex < _tabs.Count)
            {
                await _tabs[_currentTabIndex].OnLeaveAsync();
                _tabs[_currentTabIndex].IsActive = false;
            }

            _currentTabIndex = _tabs.IndexOf(tab);
            if (_currentTabIndex == -1) return;

            _tabs[_currentTabIndex].IsActive = true;
            await _tabs[_currentTabIndex].OnEnterAsync();
            await RenderCurrentViewAsync();
        }

        private async Task SwitchToNextTabAsync()
        {
            int nextIndex = (_currentTabIndex + 1) % _tabs.Count;
            await SwitchToTabAsync(_tabs[nextIndex]);
        }

        private async Task SwitchToPreviousTabAsync()
        {
            int prevIndex = (_currentTabIndex - 1 + _tabs.Count) % _tabs.Count;
            await SwitchToTabAsync(_tabs[prevIndex]);
        }

        public async Task ShowStatusAsync(string message, ConsoleColor color = ConsoleColor.Green)
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            
            Console.SetCursorPosition(1, height - 2);
            Console.ForegroundColor = color;
            Console.Write($"  {message}  ".PadRight(width - 2));
            Console.ResetColor();
            
            await Task.CompletedTask;
        }

        public async Task ShowErrorAsync(string message)
        {
            await ShowStatusAsync($"⚠ {message}", ConsoleColor.Red);
        }

        private async Task ExitAsync()
        {
            await ShowStatusAsync("👋 До свидания!", ConsoleColor.Yellow);
            await Task.Delay(500);
            _isRunning = false;
            Environment.Exit(0);
        }
    }
}