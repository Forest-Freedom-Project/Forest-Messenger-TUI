using System.Text.Json;
using Forest.Models;

namespace ForestMSG.Core.Models
{
    public class Chat
    {
        public string? Id { get; set; }     
        public string? SelfId { set; get; } 
        public string? PeerId { set; get; } 
        public List<Message>? Messages = new List<Message>();
        public string? PeerName { get; set; } 
        public Chat(string? Id, string? SelfId, string? PeerId, List<Message>? Messages, string? PeerName)
        {
            this.Id = Id;
            this.SelfId = SelfId;
            this.PeerId = PeerId;
            this.Messages = Messages;
            this.PeerName = PeerName;
        }
        public Chat() { }
        public string ToJson() => JsonSerializer.Serialize(this);
    }
}