namespace ForestMessenger.TUI.Tabs.Interfaces
{
    public interface INavigationService
    {
        Task SwitchToTabAsync<T>() where T : ITab;
        Task ShowStatusAsync(string message, ConsoleColor color = ConsoleColor.Green);
        Task ShowErrorAsync(string message);
        Task RenderCurrentViewAsync();
        Task SwitchToTabAsync(ITab tab);
        void RegisterTab(ITab tab);
    }
}