using System.Text;

namespace FomaPay.Tests;

/// <summary>Faux transport : mémorise les requêtes et délègue la réponse au test.</summary>
internal sealed class FakeTransport(Func<int, FomaPayHttpRequest, FomaPayHttpResponse> respond) : IFomaPayTransport
{
    public List<FomaPayHttpRequest> Calls { get; } = [];

    public Task<FomaPayHttpResponse> SendAsync(FomaPayHttpRequest request)
    {
        Calls.Add(request);
        try
        {
            return Task.FromResult(respond(Calls.Count, request));
        }
        catch (Exception e)
        {
            return Task.FromException<FomaPayHttpResponse>(e);
        }
    }
}

/// <summary>Transport réel (HttpClient), pour les tests d'intégration hors de Unity.</summary>
internal sealed class HttpClientTransport : IFomaPayTransport
{
    private static readonly HttpClient Http = new();

    public async Task<FomaPayHttpResponse> SendAsync(FomaPayHttpRequest request)
    {
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        if (request.Body != null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }
        foreach (var header in request.Headers.Where(h => h.Key != "Content-Type"))
        {
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        try
        {
            using var response = await Http.SendAsync(message);
            return new FomaPayHttpResponse { Status = (int)response.StatusCode, Body = await response.Content.ReadAsStringAsync() };
        }
        catch (HttpRequestException e)
        {
            throw new FomaPayException(FomaPayErrorKind.Network, "ERROR_NETWORK", e.Message, 0, e);
        }
    }
}

[TestClass]
public sealed class ApiTests
{
    private const string PaymentJson =
        """{"id":"pay_1","reference":"FP-1","status":"pending","amount":25,"currency":"USD","orderId":"CMD-1","qrCode":"https://pay.fomapay.cd/p/FP-1","metadata":{"player_id":"42"},"paymentModes":["fomapay_direct"]}""";

    private readonly List<int> _waits = [];

    private (FomaPayApi Api, FakeTransport Transport) Make(Func<int, FomaPayHttpRequest, FomaPayHttpResponse> respond, int maxRetries = 2)
    {
        var transport = new FakeTransport(respond);
        var api = new FomaPayApi("pk_test_unit", transport, "https://api.test/")
        {
            MaxRetries = maxRetries,
            Delay = ms =>
            {
                _waits.Add(ms);
                return Task.CompletedTask;
            },
        };
        return (api, transport);
    }

    private static FomaPayHttpResponse Ok(string body, int status = 200) => new() { Status = status, Body = body };

    private static FomaPayHttpResponse Failure(int status, string code, int retryAfter = 0) =>
        new() { Status = status, Body = """{"error":{"code":"CODE","message":"oups"}}""".Replace("CODE", code), RetryAfterSeconds = retryAfter };

    private static FomaPayException Network(string code) => new(FomaPayErrorKind.Network, code, "coupure");

    [TestMethod]
    public void RejectsSecretOrMalformedKey()
    {
        var error = Assert.ThrowsExactly<FomaPayException>(() => new FomaPayApi("sk_test_abc", new FakeTransport((_, _) => Ok("{}"))));
        Assert.AreEqual("ERROR_INVALID_API_KEY", error.Code);
        Assert.AreEqual(FomaPayErrorKind.Configuration, error.Kind);
    }

    [TestMethod]
    public async Task CreateSendsStandardFormatAndIdempotencyKey()
    {
        var (api, transport) = Make((_, _) => Ok(PaymentJson, 201));
        var payment = await api.CreatePaymentAsync(new PaymentRequest
        {
            Amount = 12.5,
            Currency = Currencies.USD,
            OrderId = "CMD-1",
            CustomerName = "Jean Marc",
            CustomerPhone = "+243810000000",
            PaymentModes = [PaymentModes.FomaPayDirect, PaymentModes.MobileMoney],
            CallbackUrl = "https://marchand.cd/api/callback",
            Metadata = new Dictionary<string, string> { ["player_id"] = "42" },
        });

        var call = transport.Calls[0];
        Assert.AreEqual("POST https://api.test/v1/payments", call.Method + " " + call.Url);
        Assert.AreEqual("Bearer pk_test_unit", call.Headers["Authorization"]);
        Assert.IsTrue(call.Headers["Idempotency-Key"].StartsWith("unity-", StringComparison.Ordinal));
        Assert.AreEqual(
            """{"amount":12.5,"currency":"USD","orderId":"CMD-1","customer":{"name":"Jean Marc","phone":"+243810000000"},"paymentModes":["fomapay_direct","mobile_money"],"callbackUrl":"https://marchand.cd/api/callback","metadata":{"player_id":"42"}}""",
            call.Body);
        Assert.AreEqual(30, call.TimeoutSeconds);
        Assert.AreEqual("CMD-1", payment.OrderId);
        Assert.AreEqual(25d, payment.Amount);
        Assert.AreEqual("42", payment.Metadata["player_id"]);
        Assert.AreEqual("https://pay.fomapay.cd/p/FP-1", payment.QrCode);
        Assert.IsTrue(payment.IsPending);
        Assert.IsFalse(api.Livemode);
    }

    [TestMethod]
    public async Task RetriesServerAndNetworkErrorsWithTheSameIdempotencyKey()
    {
        var (api, transport) = Make((n, _) => n switch
        {
            1 => Failure(503, "ERROR_UNAVAILABLE"),
            2 => throw Network("ERROR_TIMEOUT"),
            _ => Ok(PaymentJson, 201),
        });

        Assert.AreEqual("pay_1", (await api.CreatePaymentAsync(new PaymentRequest { Amount = 25 })).Id);
        Assert.AreEqual(3, transport.Calls.Count);
        Assert.AreEqual(1, transport.Calls.Select(c => c.Headers["Idempotency-Key"]).Distinct().Count());
        Assert.IsTrue(_waits[0] is >= 250 and <= 500 && _waits[1] is >= 500 and <= 1000, string.Join(",", _waits));
    }

    [TestMethod]
    public async Task GivesUpAfterMaxRetriesAndDoesNotRetryClientErrors()
    {
        var (down, downTransport) = Make((_, _) => Failure(500, "ERROR_INTERNAL"), maxRetries: 1);
        var internalError = await Assert.ThrowsExactlyAsync<FomaPayException>(() => down.GetPaymentAsync("FP/1"));
        Assert.AreEqual(500, internalError.Status);
        Assert.AreEqual(2, downTransport.Calls.Count);
        Assert.AreEqual("https://api.test/v1/payments/FP%2F1", downTransport.Calls[0].Url);

        var (refused, refusedTransport) = Make((_, _) => Failure(403, "ERROR_LEVEL_0_LIMIT_REACHED"));
        var limit = await Assert.ThrowsExactlyAsync<FomaPayException>(() => refused.CreatePaymentAsync(new PaymentRequest { Amount = 900 }));
        Assert.AreEqual("ERROR_LEVEL_0_LIMIT_REACHED", limit.Code);
        Assert.AreEqual("oups", limit.Message);
        Assert.AreEqual(FomaPayErrorKind.Api, limit.Kind);
        Assert.AreEqual(1, refusedTransport.Calls.Count);

        var (html, _) = Make((_, _) => Ok("<html>", 502), maxRetries: 0);
        Assert.AreEqual("ERROR_UNKNOWN", (await Assert.ThrowsExactlyAsync<FomaPayException>(() => html.GetStoreAsync())).Code);
    }

    [TestMethod]
    public async Task PostWithoutIdempotencyKeyIsOnlyReplayedOnRateLimit()
    {
        var (broken, brokenTransport) = Make((_, _) => Failure(502, "ERROR_BAD_GATEWAY"));
        await Assert.ThrowsExactlyAsync<FomaPayException>(() => broken.PayWithMobileMoneyAsync("pay_1", Operators.MPesa, "+243810000000"));
        Assert.AreEqual(1, brokenTransport.Calls.Count);
        Assert.AreEqual("""{"operator":"mpesa","phone":"+243810000000"}""", brokenTransport.Calls[0].Body);

        _waits.Clear();
        var (limited, limitedTransport) = Make((n, _) => n == 1 ? Failure(429, "ERROR_RATE_LIMITED", 2) : Ok(PaymentJson));
        await limited.SimulatePaymentAsync("pay_1", success: false);
        Assert.AreEqual(2, limitedTransport.Calls.Count);
        Assert.AreEqual("""{"outcome":"failed"}""", limitedTransport.Calls[1].Body);
        CollectionAssert.AreEqual(new[] { 2000 }, _waits);
    }

    [TestMethod]
    public async Task StoreIsCachedExceptAfterAFailure()
    {
        var (api, transport) = Make(
            (n, _) => n == 1
                ? Failure(500, "ERROR_INTERNAL")
                : Ok("""{"id":"s1","name":"Studio Kin","shortCode":"KIN","kycLevel":2,"paymentModes":["fomapay_direct","mobile_money"]}"""),
            maxRetries: 0);

        await Assert.ThrowsExactlyAsync<FomaPayException>(() => api.GetStoreAsync());
        var store = await api.GetStoreAsync();
        await api.GetStoreAsync();
        Assert.AreEqual(2, transport.Calls.Count);
        Assert.AreEqual(2, store.KycLevel);
        CollectionAssert.AreEqual(new[] { "fomapay_direct", "mobile_money" }, store.PaymentModes);
    }

    [TestMethod]
    public async Task WaitForPaymentSurvivesNetworkBlipsAndHonoursCancellation()
    {
        var (api, transport) = Make(
            (n, _) => n switch
            {
                1 => Ok(PaymentJson),
                2 => throw Network("ERROR_NETWORK"),
                _ => Ok(PaymentJson.Replace("pending", "success")),
            },
            maxRetries: 0);

        var payment = await api.WaitForPaymentAsync("pay_1", pollMilliseconds: 100);
        Assert.IsTrue(payment.IsSuccessful);
        Assert.AreEqual(3, transport.Calls.Count);
        CollectionAssert.AreEqual(new[] { 100, 100 }, _waits);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.WaitForPaymentAsync("pay_1", cancellation: cancelled.Token));

        var (stuck, _) = Make((_, _) => Ok(PaymentJson));
        var timeout = await Assert.ThrowsExactlyAsync<FomaPayException>(() => stuck.WaitForPaymentAsync("pay_1", 10, timeoutSeconds: 0));
        Assert.AreEqual("ERROR_PAYMENT_TIMEOUT", timeout.Code);
    }

    [TestMethod]
    public void JsonAndFormatting()
    {
        var parsed = (Dictionary<string, object>)Json.Parse(""" {"a":[1,-2.5e1,true,null,"é\n\"x\"é"],"b":{}} """);
        var list = (List<object>)parsed["a"];
        Assert.AreEqual(-25d, list[1]);
        Assert.AreEqual("é\n\"x\"é", list[4]);
        Assert.AreEqual("""{"n":0.1,"i":3,"s":"a\"b","l":[true,null]}""", Json.Stringify(new Dictionary<string, object>
        {
            ["n"] = 0.1, ["i"] = 3, ["s"] = "a\"b", ["l"] = new List<object> { true, null },
        }));
        foreach (var invalid in new[] { "{\"a\":", "<html>", "{\"a\":1}x", "[1,]", "" })
        {
            Assert.ThrowsExactly<FormatException>(() => Json.Parse(invalid), invalid);
        }

        Assert.AreEqual("1 000 CDF", FomaPayFormat.Amount(1000, Currencies.CDF));
        Assert.AreEqual("1 234 567,50 USD", FomaPayFormat.Amount(1234567.5, Currencies.USD));
        foreach (var input in new[] { "0812345678", "812345678", "+243 81 234 5678", "243812345678" })
        {
            Assert.AreEqual("+243812345678", FomaPayFormat.NormalizePhone(input), input);
        }
        Assert.IsNull(FomaPayFormat.NormalizePhone("81234"));
    }

    /// <summary>
    /// Contre le sandbox, avec un transport HttpClient. Ne s'exécute que si FOMAPAY_TEST_PUBLIC_KEY est définie :
    /// <code>FOMAPAY_API_URL=http://localhost:8080 FOMAPAY_TEST_PUBLIC_KEY=pk_test_… dotnet test</code>
    /// </summary>
    [TestMethod]
    public async Task SandboxIntegration()
    {
        var key = Environment.GetEnvironmentVariable("FOMAPAY_TEST_PUBLIC_KEY");
        if (string.IsNullOrEmpty(key))
        {
            Assert.Inconclusive("FOMAPAY_TEST_PUBLIC_KEY non définie");
        }
        var api = new FomaPayApi(key, new HttpClientTransport(), Environment.GetEnvironmentVariable("FOMAPAY_API_URL"));

        var store = await api.GetStoreAsync();
        Assert.IsFalse(store.Livemode);
        Assert.AreEqual(2, store.PaymentModes.Count);

        var payment = await api.CreatePaymentAsync(new PaymentRequest
        {
            Amount = 1000,
            Currency = Currencies.CDF,
            OrderId = "gems_500:joueur42",
            Description = "500 gemmes",
            Metadata = new Dictionary<string, string> { ["player_id"] = "joueur42" },
        });
        Assert.AreEqual(PaymentStatuses.Pending, payment.Status);
        Assert.AreEqual("joueur42", payment.Metadata["player_id"]);
        Assert.IsTrue(payment.QrCode.EndsWith(payment.Reference, StringComparison.Ordinal));

        // Mobile Money : l'opérateur simulé confirme après ~3 s.
        var processing = await api.PayWithMobileMoneyAsync(payment.Id, Operators.AirtelMoney, "+243970000000");
        Assert.AreEqual(PaymentStatuses.Processing, processing.Status);
        var paid = await api.WaitForPaymentAsync(payment.Reference, pollMilliseconds: 1000, timeoutSeconds: 12);
        Assert.IsTrue(paid.IsSuccessful);
        Assert.AreEqual(Operators.AirtelMoney, paid.Channel);

        // FomaPay Direct : issue forcée en sandbox.
        var direct = await api.CreatePaymentAsync(new PaymentRequest { Amount = 2.5, PaymentModes = [PaymentModes.FomaPayDirect] });
        Assert.AreEqual(PaymentStatuses.Failed, (await api.SimulatePaymentAsync(direct.Id, success: false)).Status);

        var invalid = await Assert.ThrowsExactlyAsync<FomaPayException>(() => api.CreatePaymentAsync(new PaymentRequest { Amount = -1 }));
        Assert.AreEqual("ERROR_VALIDATION", invalid.Code);
    }
}
