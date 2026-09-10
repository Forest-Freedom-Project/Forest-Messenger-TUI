namespace ForestMSG.Core.Models
{
    public class HandshakeConfirmation
    {
        public string? ChatId { set; get; }
        public string? RecipientId { set; get; }
        public DateTime? ConfirmedAt { set; get; }
        public byte[]? Signature { get; set; }
    }
}