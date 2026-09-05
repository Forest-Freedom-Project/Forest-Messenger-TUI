namespace ForestMessenger.TUI.Tabs.Interfaces
{
    public interface ITab
    {
        public string Name { get; }
        public string Description { get; }

        Task RenderAsync();
        Task HandleInputAsync(ConsoleKeyInfo key);
        Task OnEnterAsync();
        Task OnLeaveAsync();
        bool IsActive { get; set; }
        bool IsDirty { get; set; }
    }
}