using Microsoft.AspNetCore.SignalR;

namespace DelTechApi.Hubs
{
    public class DeviceHub : Hub
    {
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
    }
}