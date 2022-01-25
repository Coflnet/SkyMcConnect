namespace Coflnet.Sky.McConnect.Models
{
    /// <summary>
    /// Event signaling that an accout was verified 
    /// </summary>
    public class VerificationEvent
    {
        /// <summary>
        /// UserId of the user
        /// </summary>
        /// <value></value>
        public string UserId { get; set; }
        /// <summary>
        /// Minecraft uuid of the verified account
        /// </summary>
        /// <value></value>
        public string MinecraftUuid { get; set; }
    }
}