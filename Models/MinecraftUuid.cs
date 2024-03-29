using System;
using System.Runtime.Serialization;

namespace Coflnet.Sky.McConnect.Models
{
    [DataContract]
    public class MinecraftUuid
    {
        [IgnoreDataMember]
        public int Id { get; set; }
        [DataMember]
        public string AccountUuid { get; set; }
        [DataMember]
        public bool Verified { get; set; }
        [IgnoreDataMember]
        public User User { get; set; }
        [DataMember]
        public DateTime UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        [DataMember]
        public DateTime LastRequestedAt { get; set; }

        public MinecraftUuid()
        {
            CreatedAt = DateTime.UtcNow;
            LastRequestedAt = DateTime.UtcNow;
        }
    }
}