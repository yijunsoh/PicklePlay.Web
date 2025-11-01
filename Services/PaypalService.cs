using Microsoft.Extensions.Options;
using PayPal;
using PayPal.Api;
using PicklePlay.Models;
using Transaction = PayPal.Api.Transaction;

namespace PicklePlay.Services
{
    public interface IPayPalService
    {
        Task<string> CreatePaymentAsync(decimal amount, string currency, string description);
        Task<bool> ExecutePaymentAsync(string paymentId, string payerId);
    }

    public class PayPalService : IPayPalService
    {
        private readonly PayPalConfig _config;
        private readonly APIContext _apiContext;
        private readonly ILogger<PayPalService> _logger;

        public PayPalService(IOptions<PayPalConfig> config, ILogger<PayPalService> logger)
        {
            _config = config.Value;
            _logger = logger;

            // Create PayPal API context
            var configDict = new Dictionary<string, string>
            {
                { "mode", _config.Environment },
                { "clientId", _config.ClientId },
                { "clientSecret", _config.ClientSecret }
            };

            var accessToken = new OAuthTokenCredential(configDict).GetAccessToken();
            _apiContext = new APIContext(accessToken) { Config = configDict };
        }

        public async Task<string> CreatePaymentAsync(decimal amount, string currency, string description)
        {
            try
            {
                _logger.LogInformation($"Creating PayPal payment: {amount} {currency}");

                var payment = new Payment
                {
                    intent = "sale",
                    payer = new Payer { payment_method = "paypal" },
                    transactions = new List<Transaction>
                    {
                        new Transaction
                        {
                            description = description,
                            amount = new Amount
                            {
                                currency = currency,
                                total = amount.ToString("F2") // Format to 2 decimal places
                            }
                        }
                    },
                    redirect_urls = new RedirectUrls
                    {
                        return_url = _config.ReturnUrl,
                        cancel_url = _config.CancelUrl
                    }
                };

                var createdPayment = await Task.Run(() => payment.Create(_apiContext));

                // Find approval URL
                var approvalUrl = createdPayment.links
                    .FirstOrDefault(link => link.rel.Equals("approval_url", StringComparison.OrdinalIgnoreCase))?
                    .href;

                if (string.IsNullOrEmpty(approvalUrl))
                {
                    throw new Exception("Could not get PayPal approval URL");
                }

                _logger.LogInformation($"PayPal payment created: {createdPayment.id}");
                return approvalUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating PayPal payment");
                throw;
            }
        }

        public async Task<bool> ExecutePaymentAsync(string paymentId, string payerId)
        {
            try
            {
                _logger.LogInformation($"Executing PayPal payment: {paymentId} for payer: {payerId}");

                var paymentExecution = new PaymentExecution { payer_id = payerId };
                var payment = new Payment { id = paymentId };

                var executedPayment = await Task.Run(() => payment.Execute(_apiContext, paymentExecution));

                var isSuccess = executedPayment.state.ToLower() == "approved";
                
                _logger.LogInformation($"PayPal payment execution result: {isSuccess}");

                return isSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing PayPal payment: {paymentId}");
                throw;
            }
        }
    }
}