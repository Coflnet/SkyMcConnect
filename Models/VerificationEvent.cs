using System.Runtime.Serialization;

namespace Coflnet.Sky.McConnect.Models
{
    /// <summary>
    /// Event signaling that an accout was verified 
    /// </summary>
    [DataContract]
    public class VerificationEvent
    {
        /// <summary>
        /// UserId of the user
        /// </summary>
        /// <value></value>
        [DataMember(Name = "userId")]
        public string UserId { get; set; }
        /// <summary>
        /// Minecraft uuid of the verified account
        /// </summary>
        /// <value></value>
        [DataMember(Name = "uuid")]
        public string MinecraftUuid { get; set; }
    }
}