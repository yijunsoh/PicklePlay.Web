namespace PicklePlay.Models
{
    public class PayPalConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string Environment { get; set; } = "sandbox"; // or "live"
        public string ReturnUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
    }
}