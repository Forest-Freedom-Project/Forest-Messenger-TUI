namespace ForestMSG.Core.Models
{
    
    public class HandshakePacket
    {
        public string? ChatId { get; set; }                 
        public string? InitiatorId { get; set; }            
        public string? RecipientId { get; set; }            
        public byte[]? EncryptedRootKey { get; set; }       
        public byte[]? EncryptedChatSalt { get; set; }      
        public byte[]? Signature { get; set; }              
        public DateTime CreatedAt { get; set; }            
        public uint TTL { get; set; } = 300;               

        public bool IsExpired() => DateTime.UtcNow - CreatedAt > TimeSpan.FromSeconds(TTL);
    }
}