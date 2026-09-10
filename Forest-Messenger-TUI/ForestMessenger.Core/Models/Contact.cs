using System.Text.Json.Serialization;

namespace ForestMSG.Core.Models
{
    public class Contact
    {
        public string? PublicId { get; set; }        
        public string? Name { get; set; }               
        public string? Avatar { get; set; }            
        public string? Bio { get; set; }              

        public string? PublicKey { get; set; }        
        public string? EncryptionKey { get; set; }    

        public bool IsPrivate { get; set; }            
        public DateTime? CreatedAt { get; set; }       
        public string? Version { get; set; } = "0.0.1";  

        [JsonIgnore] 
        public string? Salt { get; set; }              

        public string? Signature { get; set; }        

        public Contact() { }

        public Contact(string name, string publicId, string publicKey, string encryptionKey, bool isPrivate = true)
        {
            Name = name;
            PublicId = publicId;
            PublicKey = publicKey;
            EncryptionKey = encryptionKey;
            IsPrivate = isPrivate;
            CreatedAt = DateTime.UtcNow;
            Version = "1.0";
        }
    }
}