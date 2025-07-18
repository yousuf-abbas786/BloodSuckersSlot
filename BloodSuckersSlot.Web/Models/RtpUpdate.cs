namespace BloodSuckersSlot.Web.Models
{
    public class RtpUpdate
    {
        public int SpinNumber { get; set; }
        public double ActualRtp { get; set; }
        public double TargetRtp { get; set; }
        public double ActualHitRate { get; set; }
        public double TargetHitRate { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
