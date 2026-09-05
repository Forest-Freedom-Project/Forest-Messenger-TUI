using ForestMessenger.TUI.Navigation;
using ForestMessenger.TUI.Tabs.Classes.TabClasses;

namespace ForestMessenger.Main
{
    public class MainProgram
    {
        public static async Task Main(string[] args)
        {
            await Test();
        }
        public static async Task Test()
        {
            var navigationService = new NavigationService();

            var mainTab = new MainTab(navigationService);
            var contactsTab = new ContactsTab(navigationService);
            var chatsTab = new ChatsTab(navigationService);
            var channelsTab = new ChannelsTab(navigationService);
            var settingsTab = new SettingsTab(navigationService);

            navigationService.RegisterTab(mainTab);
            navigationService.RegisterTab(contactsTab);
            navigationService.RegisterTab(chatsTab);
            navigationService.RegisterTab(channelsTab);
            navigationService.RegisterTab(settingsTab);

            await navigationService.InitializeAsync();

            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                await navigationService.HandleInputAsync(key);
            }
        }
    }
}