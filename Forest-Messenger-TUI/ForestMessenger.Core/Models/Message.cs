using ForestMSG.Core.Enums;

namespace Forest.Models
{
    public class Message
    {
        public ulong Id { get; set; }
        public string? ChatId { get; set; }
        public string? SenderId { get; set; }
        public MessageType Type { get; set; }
        public DateTime SentAt { get; set; }
        public bool IsDownloaded { get; set; }
        public string? MessageFolderPath { get; set; }
        public string TextFilePath { get; set; } = Path.Combine("Text", "content.txt");          
        public List<string>? AudioFiles { get; set; }     
        public List<string>? VoicesFiles { get; set; }
        public List<string>? VideoFiles { get; set; }
        public List<string>? PictureFiles { get; set; }
        public MessageType MessageType { get; internal set; }
    }
}