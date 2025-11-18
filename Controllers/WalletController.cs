using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using PicklePlay.Services;
using PicklePlay.Models;
using Microsoft.EntityFrameworkCore;
using PicklePlay.Data;
using static PicklePlay.Services.MockPaymentService;
using PicklePlay.ViewModels;

public class WalletController : Controller
{
    private readonly IPaymentService _paymentService;
    private readonly IPayPalService _payPalService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<WalletController> _logger;

    public WalletController(
        IPaymentService paymentService,
        IPayPalService payPalService,
        ApplicationDbContext context,
        ILogger<WalletController> logger)
    {
        _paymentService = paymentService;
        _payPalService = payPalService;
        _context = context;
        _logger = logger;
    }

    public async Task<IActionResult> WalletManagement()
{
    if (HttpContext.Session.GetString("UserEmail") == null)
        return RedirectToAction("Login", "Auth");

    int? userId = HttpContext.Session.GetInt32("UserId");
    if (userId == null)
        return RedirectToAction("Login", "Auth");

    // Fetch Wallet
    var wallet = await _context.Wallets
        .Include(w => w.Transactions)
        .FirstOrDefaultAsync(w => w.UserId == userId.Value);

    // Fetch User
    var user = await _context.Users
        .FirstOrDefaultAsync(u => u.UserId == userId.Value);

    // Fetch Escrow
    var escrows = await _context.Escrows
        .Include(e => e.Transactions)
        .Include(e => e.EscrowDisputes)
        .Where(e => e.UserId == userId.Value)
        .ToListAsync();

    // Fetch Disputes
    var disputes = await _context.EscrowDisputes
        .Where(d => d.RaisedByUserId == userId.Value)
        .ToListAsync();

    // Fetch Escrow Payment Requests  
    // (If you store them in DB - if not, keep empty)
    var escrowRequests = new List<EscrowPaymentRequest>(); 
    // Add your DB fetch here later when implemented 

    var vm = new WalletManagementViewModel
    {
        Wallet = wallet ?? new Wallet(),
        User = user ?? new User(),
        Transactions = wallet?.Transactions.OrderByDescending(t => t.CreatedAt).ToList() ?? new List<Transaction>(),
        Escrows = escrows,
        EscrowDisputes = disputes,
        EscrowPaymentRequests = escrowRequests
    };

    ViewData["Title"] = "Wallet Management";
    ViewData["UserName"] = HttpContext.Session.GetString("UserName") ?? "User";

    return View(vm);
}


    // GET: /Wallet/TopUp
    public async Task<IActionResult> TopUp()
    {
        if (HttpContext.Session.GetString("UserEmail") == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        var userId = GetCurrentUserId();
        decimal currentBalance = 0.00m;

        if (userId.HasValue)
        {
            var wallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == userId.Value);

            if (wallet != null)
            {
                currentBalance = wallet.WalletBalance;
            }
        }

        ViewData["Title"] = "Top Up Wallet";
        ViewData["UserName"] = HttpContext.Session.GetString("UserName") ?? "User";
        ViewData["CurrentBalance"] = currentBalance;

        return View();
    }

    // GET: /Wallet/Withdraw
    public async Task<IActionResult> Withdraw()
    {
        if (HttpContext.Session.GetString("UserEmail") == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        var userId = GetCurrentUserId();
        decimal currentBalance = 0.00m;

        if (userId.HasValue)
        {
            var wallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == userId.Value);

            if (wallet != null)
            {
                currentBalance = wallet.WalletBalance;
            }
        }

        ViewData["Title"] = "Withdraw Funds";
        ViewData["UserName"] = HttpContext.Session.GetString("UserName") ?? "User";
        ViewData["CurrentBalance"] = currentBalance;

        return View();
    }

    // API: Process Top Up
    [HttpPost]
    [Route("/api/wallet/topup")]
    public async Task<IActionResult> ProcessTopUp([FromBody] TopUpRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized(new { success = false, message = "User not logged in" });
            }

            _logger.LogInformation($"Processing top-up request for user {userId}");

            var result = await _paymentService.ProcessTopUpAsync(
                userId.Value,
                request.Amount,
                request.PaymentMethod,
                request.CardInfo);

            if (result.Success)
            {
                return Ok(new
                {
                    success = true,
                    message = result.Message,
                    transactionId = result.TransactionId,
                    newBalance = result.NewBalance
                });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing top-up");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // API: Process Withdraw
    [HttpPost]
    [Route("/api/wallet/withdraw")]
    public async Task<IActionResult> ProcessWithdraw([FromBody] WithdrawRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized(new { success = false, message = "User not logged in" });
            }

            _logger.LogInformation($"Processing withdraw request for user {userId}");

            var result = await _paymentService.ProcessWithdrawAsync(
                userId.Value,
                request.Amount,
                request.WithdrawMethod,
                request.PaymentDetails);

            if (result.Success)
            {
                return Ok(new
                {
                    success = true,
                    message = result.Message,
                    transactionId = result.TransactionId,
                    newBalance = result.NewBalance
                });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing withdraw");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // API: Create PayPal Payment
    [HttpPost]
    [Route("/api/wallet/paypal/create-payment")]
    public async Task<IActionResult> CreatePayPalPayment([FromBody] CreatePayPalPaymentRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized(new { success = false, message = "User not logged in" });
            }

            _logger.LogInformation($"Creating PayPal payment for user {userId}, amount: {request.Amount}");

            // Store the amount in session for later use
            HttpContext.Session.SetString("PayPalAmount", request.Amount.ToString());

            var approvalUrl = await _payPalService.CreatePaymentAsync(
                request.Amount,
                "MYR",
                $"Wallet Top Up - PicklePlay");

            return Ok(new
            {
                success = true,
                approvalUrl = approvalUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PayPal payment");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // GET: PayPal Success Callback
    [HttpGet]
    [Route("/api/PayPal/success")]
    public async Task<IActionResult> PayPalSuccess([FromQuery] string paymentId, [FromQuery] string PayerID)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            _logger.LogInformation($"PayPal payment success: {paymentId}, payer: {PayerID}");

            // Get the amount from session or database (you might need to store it temporarily)
            var amount = HttpContext.Session.GetString("PayPalAmount");
            if (string.IsNullOrEmpty(amount) || !decimal.TryParse(amount, out decimal topUpAmount))
            {
                topUpAmount = 50.00m; // Default amount if not stored
            }

            // Execute the payment
            var paymentSuccess = await _payPalService.ExecutePaymentAsync(paymentId, PayerID);

            if (paymentSuccess)
            {
                // Process the top-up in our system
                var topUpResult = await _paymentService.ProcessPayPalTopUpAsync(userId.Value, topUpAmount, paymentId);

                if (topUpResult.Success)
                {
                    // Clear the session
                    HttpContext.Session.Remove("PayPalAmount");

                    // Redirect to success page
                    return Redirect($"/Wallet/TopUpSuccess?amount={topUpAmount}&transactionId={topUpResult.TransactionId}&newBalance={topUpResult.NewBalance}");
                }
                else
                {
                    return Redirect($"/Wallet/TopUpFailed?message={topUpResult.Message}");
                }
            }
            else
            {
                return Redirect("/Wallet/TopUpFailed?message=Payment execution failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PayPal payment success");
            return Redirect($"/Wallet/TopUpFailed?message={ex.Message}");
        }
    }

    // GET: PayPal Cancel Callback
    [HttpGet]   
    [Route("/api/PayPal/cancel")]
    public IActionResult PayPalCancel()
    {
        _logger.LogInformation("PayPal payment cancelled by user");
        HttpContext.Session.Remove("PayPalAmount");
        return Redirect("/Wallet/TopUpFailed?message=Payment cancelled by user");
    }

    // GET: Top Up Success Page
    [HttpGet]
    [Route("/Wallet/TopUpSuccess")]
    public IActionResult TopUpSuccess()
    {
        if (HttpContext.Session.GetString("UserEmail") == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        ViewData["Title"] = "Top Up Success";
        ViewData["UserName"] = HttpContext.Session.GetString("UserName") ?? "User";
        ViewData["Amount"] = HttpContext.Request.Query["amount"];
        ViewData["NewBalance"] = HttpContext.Request.Query["newBalance"];
        ViewData["TransactionId"] = HttpContext.Request.Query["transactionId"];

        return View();
    }

    // GET: Withdraw Success Page
    [HttpGet]
    [Route("/Wallet/WithdrawSuccess")]
    public IActionResult WithdrawSuccess()
    {
        if (HttpContext.Session.GetString("UserEmail") == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        ViewData["Title"] = "Withdraw Success";
        ViewData["UserName"] = HttpContext.Session.GetString("UserName") ?? "User";
        ViewData["Amount"] = HttpContext.Request.Query["amount"];
        ViewData["NewBalance"] = HttpContext.Request.Query["newBalance"];
        ViewData["TransactionId"] = HttpContext.Request.Query["transactionId"];

        return View();
    }

    // GET: Top Up Failed Page
    [HttpGet]
    [Route("/Wallet/TopUpFailed")]
    public IActionResult TopUpFailed()
    {
        if (HttpContext.Session.GetString("UserEmail") == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        ViewData["Title"] = "Top Up Failed";
        ViewData["UserName"] = HttpContext.Session.GetString("UserName") ?? "User";
        ViewData["Message"] = HttpContext.Request.Query["message"];

        return View();
    }

    // GET: Withdraw Failed Page
    [HttpGet]
    [Route("/Wallet/WithdrawFailed")]
    public IActionResult WithdrawFailed()
    {
        if (HttpContext.Session.GetString("UserEmail") == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        ViewData["Title"] = "Withdraw Failed";
        ViewData["UserName"] = HttpContext.Session.GetString("UserName") ?? "User";
        ViewData["Message"] = HttpContext.Request.Query["message"];

        return View();
    }

    // API: Get Wallet Balance
    [HttpGet]
    [Route("/api/wallet/balance")]
    public async Task<IActionResult> GetWalletBalance()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized(new { success = false, message = "User not logged in" });
            }

            var wallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == userId.Value);

            if (wallet == null)
            {
                return Ok(new { success = true, balance = 0.00m });
            }

            return Ok(new
            {
                success = true,
                balance = wallet.WalletBalance,
                escrowBalance = wallet.EscrowBalance,
                totalSpent = wallet.TotalSpent
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting wallet balance");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // API: Get Transaction History
    [HttpGet]
    [Route("/api/wallet/transactions")]
    public async Task<IActionResult> GetTransactions()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized(new { success = false, message = "User not logged in" });
            }

            var wallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == userId.Value);

            if (wallet == null)
            {
                return Ok(new { success = true, transactions = new List<object>() });
            }

            var transactions = await _context.Transactions
                .Where(t => t.WalletId == wallet.WalletId)
                .OrderByDescending(t => t.CreatedAt)
                .Take(10)
                .Select(t => new
                {
                    id = t.TransactionId,
                    type = t.TransactionType,
                    amount = t.Amount,
                    method = t.PaymentMethod,
                    status = t.PaymentStatus,
                    date = t.CreatedAt,
                    description = $"{t.TransactionType} - {t.PaymentMethod}",
                    // Add these for the frontend display
                    isTopUp = t.TransactionType == "TopUp",
                    isWithdraw = t.TransactionType == "Withdraw",
                    formattedDate = t.CreatedAt.ToString("MMM dd, yyyy"),
                    formattedTime = t.CreatedAt.ToString("hh:mm tt")
                })
                .ToListAsync();

            return Ok(new { success = true, transactions });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transactions");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    private int? GetCurrentUserId()
    {
        // Use GetInt32 instead of GetString
        return HttpContext.Session.GetInt32("UserId");
    }



    public class TopUpRequest
    {
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public CardInfo? CardInfo { get; set; }
    }

    public class WithdrawRequest
    {
        public decimal Amount { get; set; }
        public string WithdrawMethod { get; set; } = string.Empty;
        public PaymentDetails? PaymentDetails { get; set; }
    }

    public class CreatePayPalPaymentRequest
    {
        public decimal Amount { get; set; }
    }
}