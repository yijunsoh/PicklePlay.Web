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
        Task<string> PayoutAsync(string receiverEmail, decimal amount, string currency, string referenceId);
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

        public async Task<string> PayoutAsync(string receiverEmail, decimal amount, string currency, string referenceId)
        {
            try
            {
                _logger.LogInformation($"Initiating Payout to {receiverEmail} for {amount} {currency}");

                var payout = new Payout
                {
                    sender_batch_header = new PayoutSenderBatchHeader
                    {
                        sender_batch_id = "batch_" + Guid.NewGuid().ToString().Substring(0, 8),
                        email_subject = "You have a withdrawal from PicklePlay"
                    },
                    items = new List<PayoutItem>
                    {
                        new PayoutItem
                        {
                            recipient_type = PayoutRecipientType.EMAIL,
                            amount = new Currency
                            {
                                value = amount.ToString("F2"),
                                currency = currency
                            },
                            receiver = receiverEmail,
                            note = $"Withdrawal Ref: {referenceId}",
                            sender_item_id = referenceId
                        }
                    }
                };

                // Run the synchronous SDK method in a Task
                var createdPayout = await Task.Run(() => Payout.Create(_apiContext, payout));

                _logger.LogInformation($"Payout Batch Created: {createdPayout.batch_header.payout_batch_id}");
                
                // Return the Batch ID to track it later
                return createdPayout.batch_header.payout_batch_id;
            }
            catch (PayPalException ppEx)
            {
                // Log detailed PayPal errors (very important for debugging Payouts)
                _logger.LogError($"PayPal Payout Error: {ppEx.Message}");
                if (ppEx.InnerException != null)
                {
                    _logger.LogError($"Inner Error: {ppEx.InnerException.Message}");
                }
                return null!; // Indicate failure
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General Error executing Payout");
                throw;
            }
        }
    }
}