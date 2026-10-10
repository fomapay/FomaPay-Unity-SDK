using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FomaPay
{
    /// <summary>Requête HTTP à exécuter par le transport.</summary>
    public sealed class FomaPayHttpRequest
    {
        public string Method;
        public string Url;
        public Dictionary<string, string> Headers = new Dictionary<string, string>();

        /// <summary>Corps JSON, ou null.</summary>
        public string Body;

        public int TimeoutSeconds;
    }

    /// <summary>Réponse HTTP reçue par le transport.</summary>
    public sealed class FomaPayHttpResponse
    {
        public int Status;
        public string Body = "";

        /// <summary>Valeur de l'en-tête Retry-After, en secondes (0 si absent).</summary>
        public int RetryAfterSeconds;
    }

    /// <summary>
    /// Transport HTTP. Dans Unity, c'est UnityWebRequest ; dans les tests, un faux.
    /// Doit lever une <see cref="FomaPayException"/> de type Network quand aucune réponse n'arrive.
    /// </summary>
    public interface IFomaPayTransport
    {
        Task<FomaPayHttpResponse> SendAsync(FomaPayHttpRequest request);
    }

    /// <summary>
    /// Client FomaPay pour un jeu. N'utilise que la clé **publique** (pk_…) : elle sait créer et suivre
    /// un paiement, rien d'autre. La clé secrète reste sur votre serveur de jeu.
    /// </summary>
    public sealed class FomaPayApi
    {
        public const string Version = "0.1.0";
        public const string DefaultApiUrl = "https://api.fomapay.cd";
        private const int MaxBackoffMs = 8000;

        private readonly string _publicKey;
        private readonly string _baseUrl;
        private readonly IFomaPayTransport _transport;
        private readonly Random _random = new Random();
        private Task<StoreInfo> _store;

        /// <param name="publicKey">Clé publique de la boutique (pk_test_… ou pk_live_…).</param>
        /// <param name="transport">Transport HTTP.</param>
        /// <param name="apiUrl">À surcharger pour pointer vers un backend local en développement.</param>
        public FomaPayApi(string publicKey, IFomaPayTransport transport, string apiUrl = null)
        {
            if (publicKey == null || !(publicKey.StartsWith("pk_test_", StringComparison.Ordinal) || publicKey.StartsWith("pk_live_", StringComparison.Ordinal)))
            {
                throw new FomaPayException(
                    FomaPayErrorKind.Configuration,
                    "ERROR_INVALID_API_KEY",
                    "Clé publique absente ou mal formée : elle commence par pk_test_ ou pk_live_ (FomaPay Business › Développeurs).");
            }
            _publicKey = publicKey;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _baseUrl = (string.IsNullOrEmpty(apiUrl) ? DefaultApiUrl : apiUrl).TrimEnd('/');
        }

        /// <summary>Délai maximal d'une requête, en secondes.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>Nouvelles tentatives après un échec rejouable (0 pour désactiver).</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>Attente entre deux tentatives, en millisecondes ; remplaçable dans les tests.</summary>
        public Func<int, Task> Delay { get; set; } = milliseconds => Task.Delay(milliseconds);

        /// <summary>Faux avec une clé pk_test_… : aucun argent ne circule.</summary>
        public bool Livemode => _publicKey.StartsWith("pk_live_", StringComparison.Ordinal);

        /// <summary>Boutique liée à la clé, avec les modes de paiement autorisés (mise en cache).</summary>
        public Task<StoreInfo> GetStoreAsync()
        {
            if (_store == null || _store.IsFaulted || _store.IsCanceled)
            {
                _store = LoadStoreAsync();
            }
            return _store;
        }

        private async Task<StoreInfo> LoadStoreAsync() =>
            StoreInfo.FromJson(await RequestAsync("GET", "/v1/store", null, null).ConfigureAwait(true));

        /// <summary>Crée une demande de paiement. Porte toujours une clé d'idempotence : rejouable sans doublon.</summary>
        public async Task<Payment> CreatePaymentAsync(PaymentRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            var key = string.IsNullOrEmpty(request.IdempotencyKey) ? "unity-" + Guid.NewGuid().ToString("N") : request.IdempotencyKey;
            return Payment.FromJson(await RequestAsync("POST", "/v1/payments", request.ToJson(), key).ConfigureAwait(true));
        }

        /// <summary>Relit un paiement par son identifiant ou sa référence (FP-…).</summary>
        public async Task<Payment> GetPaymentAsync(string id) =>
            Payment.FromJson(await RequestAsync("GET", "/v1/payments/" + Uri.EscapeDataString(id), null, null).ConfigureAwait(true));

        /// <summary>Envoie la demande de débit (push USSD) sur le téléphone du joueur.</summary>
        public async Task<Payment> PayWithMobileMoneyAsync(string id, string mobileOperator, string phone)
        {
            var body = new Dictionary<string, object> { ["operator"] = mobileOperator, ["phone"] = phone };
            return Payment.FromJson(
                await RequestAsync("POST", "/v1/payments/" + Uri.EscapeDataString(id) + "/mobile-money", body, null).ConfigureAwait(true));
        }

        /// <summary>Force l'issue d'un paiement. Sandbox uniquement.</summary>
        public async Task<Payment> SimulatePaymentAsync(string id, bool success = true)
        {
            var body = new Dictionary<string, object> { ["outcome"] = success ? "success" : "failed" };
            return Payment.FromJson(
                await RequestAsync("POST", "/v1/payments/" + Uri.EscapeDataString(id) + "/simulate", body, null).ConfigureAwait(true));
        }

        /// <summary>Attend l'issue d'un paiement (succès, échec ou expiration) en l'interrogeant régulièrement.</summary>
        /// <param name="id">Identifiant ou référence du paiement.</param>
        /// <param name="pollMilliseconds">Intervalle entre deux interrogations.</param>
        /// <param name="timeoutSeconds">Durée maximale d'attente.</param>
        /// <param name="cancellation">Annulation (fermeture de la fenêtre, changement de scène…).</param>
        public async Task<Payment> WaitForPaymentAsync(
            string id, int pollMilliseconds = 2500, int timeoutSeconds = 600, CancellationToken cancellation = default)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                Payment payment = null;
                try
                {
                    payment = await GetPaymentAsync(id).ConfigureAwait(true);
                }
                catch (FomaPayException e) when (e.Kind == FomaPayErrorKind.Network)
                {
                    // Coupure passagère (tunnel, changement de réseau) : la prochaine interrogation réessaiera.
                }
                if (payment != null && !payment.IsPending)
                {
                    return payment;
                }
                if (DateTime.UtcNow >= deadline)
                {
                    throw new FomaPayException(FomaPayErrorKind.Network, "ERROR_PAYMENT_TIMEOUT", "Le paiement n'a pas été confirmé à temps.");
                }
                await Delay(pollMilliseconds).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Exécute une requête, avec nouvelles tentatives quand elle est rejouable sans risque
        /// (lecture, ou POST accompagné d'une clé d'idempotence).
        /// </summary>
        private async Task<Dictionary<string, object>> RequestAsync(string method, string path, Dictionary<string, object> body, string idempotencyKey)
        {
            var request = new FomaPayHttpRequest
            {
                Method = method,
                Url = _baseUrl + path,
                Body = body == null ? null : Json.Stringify(body),
                TimeoutSeconds = TimeoutSeconds,
            };
            request.Headers["Authorization"] = "Bearer " + _publicKey;
            request.Headers["Accept"] = "application/json";
            if (body != null)
            {
                request.Headers["Content-Type"] = "application/json";
            }
            if (idempotencyKey != null)
            {
                request.Headers["Idempotency-Key"] = idempotencyKey;
            }
            var replayable = method == "GET" || idempotencyKey != null;

            for (var attempt = 0; ; attempt++)
            {
                FomaPayException error;
                bool retry;
                var waitMs = -1;
                try
                {
                    var response = await _transport.SendAsync(request).ConfigureAwait(true);
                    if (response.Status >= 200 && response.Status < 300)
                    {
                        return ParseObject(response.Body) ?? throw new FomaPayException(
                            FomaPayErrorKind.Api, "ERROR_INVALID_RESPONSE", "Réponse illisible de l'API FomaPay.", response.Status);
                    }
                    error = ApiError(response);
                    if (response.Status == 429)
                    {
                        // La gateway a refusé la requête avant tout traitement : toujours rejouable.
                        retry = true;
                        if (response.RetryAfterSeconds > 0)
                        {
                            waitMs = Math.Min(response.RetryAfterSeconds * 1000, MaxBackoffMs);
                        }
                    }
                    else
                    {
                        retry = response.Status >= 500 && replayable;
                    }
                }
                catch (FomaPayException e) when (e.Kind == FomaPayErrorKind.Network)
                {
                    error = e;
                    retry = replayable;
                }

                if (!retry || attempt >= Math.Max(0, MaxRetries))
                {
                    throw error;
                }
                await Delay(waitMs >= 0 ? waitMs : Backoff(attempt)).ConfigureAwait(true);
            }
        }

        /// <summary>0,5 s, 1 s, 2 s… plafonné à 8 s, avec un aléa pour désynchroniser les clients.</summary>
        private int Backoff(int attempt)
        {
            var delay = Math.Min(500 << Math.Min(attempt, 10), MaxBackoffMs);
            lock (_random)
            {
                return delay / 2 + _random.Next(delay / 2 + 1);
            }
        }

        private static Dictionary<string, object> ParseObject(string text)
        {
            try
            {
                return Json.Parse(text) as Dictionary<string, object>;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static FomaPayException ApiError(FomaPayHttpResponse response)
        {
            var code = "ERROR_UNKNOWN";
            var message = "Erreur HTTP " + response.Status;
            var root = ParseObject(response.Body);
            if (root != null && root.TryGetValue("error", out var value) && value is Dictionary<string, object> error)
            {
                if (Json.Str(error, "code").Length > 0)
                {
                    code = Json.Str(error, "code");
                }
                if (Json.Str(error, "message").Length > 0)
                {
                    message = Json.Str(error, "message");
                }
            }
            return new FomaPayException(FomaPayErrorKind.Api, code, message, response.Status);
        }
    }
}
