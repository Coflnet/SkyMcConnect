using System;

namespace Coflnet.Sky.McConnect.Models;
#nullable enable
public class Challenge
{
    public int Id { get; set; }
    public string MinecraftUuid { get; set; }
    public string AuctionUuid { get; set; }
    public string? BoughtBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime BoughtAt { get; set; }
    public string UserId { get; set; }
    public DateTime CompletedAt { get; internal set; }
}
