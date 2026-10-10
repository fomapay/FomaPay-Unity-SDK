using System;
using System.Collections.Generic;
using System.Globalization;

namespace FomaPay
{
    /// <summary>Devises acceptées.</summary>
    public static class Currencies
    {
        public const string USD = "USD";
        public const string CDF = "CDF";
    }

    /// <summary>Modes de paiement proposés au joueur.</summary>
    public static class PaymentModes
    {
        /// <summary>Portefeuille FomaPay. Disponible dès le Niveau 0.</summary>
        public const string FomaPayDirect = "fomapay_direct";

        /// <summary>M-Pesa, Orange Money, Airtel Money. Réservé aux boutiques vérifiées (Niveau 1+).</summary>
        public const string MobileMoney = "mobile_money";
    }

    /// <summary>Opérateurs Mobile Money.</summary>
    public static class Operators
    {
        public const string MPesa = "mpesa";
        public const string OrangeMoney = "orange_money";
        public const string AirtelMoney = "airtel_money";
    }

    /// <summary>Statuts d'un paiement.</summary>
    public static class PaymentStatuses
    {
        public const string Pending = "pending";
        public const string Processing = "processing";
        public const string Success = "success";
        public const string Failed = "failed";
        public const string Expired = "expired";
    }

    /// <summary>Achat à encaisser. Les champs laissés vides ne sont pas envoyés.</summary>
    public sealed class PaymentRequest
    {
        /// <summary>Montant en unités de la devise (12.5 = 12,50 USD).</summary>
        public double Amount;

        public string Currency = Currencies.USD;

        /// <summary>Votre identifiant de commande (ex. « gems_500:joueur42 »), renvoyé tel quel dans les webhooks.</summary>
        public string OrderId;

        /// <summary>Libellé affiché au joueur (ex. « 500 gemmes »).</summary>
        public string Description;

        public string CustomerName;
        public string CustomerPhone;
        public string CustomerEmail;

        /// <summary>Par défaut, tous les modes que le niveau KYC de la boutique autorise.</summary>
        public List<string> PaymentModes;

        public string CallbackUrl;

        /// <summary>Données libres (identifiant du joueur, du produit…), renvoyées dans les webhooks.</summary>
        public Dictionary<string, string> Metadata;

        /// <summary>Un appel répété avec la même clé renvoie le même paiement.</summary>
        public string IdempotencyKey;

        internal Dictionary<string, object> ToJson()
        {
            var body = new Dictionary<string, object> { ["amount"] = Amount, ["currency"] = Currency };
            Put(body, "orderId", OrderId);
            Put(body, "description", Description);

            var customer = new Dictionary<string, object>();
            Put(customer, "name", CustomerName);
            Put(customer, "phone", CustomerPhone);
            Put(customer, "email", CustomerEmail);
            if (customer.Count > 0)
            {
                body["customer"] = customer;
            }
            if (PaymentModes != null && PaymentModes.Count > 0)
            {
                body["paymentModes"] = new List<object>(PaymentModes);
            }
            Put(body, "callbackUrl", CallbackUrl);
            if (Metadata != null && Metadata.Count > 0)
            {
                var metadata = new Dictionary<string, object>();
                foreach (var entry in Metadata)
                {
                    metadata[entry.Key] = entry.Value;
                }
                body["metadata"] = metadata;
            }
            return body;
        }

        private static void Put(Dictionary<string, object> target, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                target[key] = value;
            }
        }
    }

    /// <summary>Un paiement. Les champs absents de la réponse restent vides.</summary>
    public sealed class Payment
    {
        public string Id = "";

        /// <summary>Référence lisible (FP-…).</summary>
        public string Reference = "";

        /// <summary>Voir <see cref="PaymentStatuses"/>.</summary>
        public string Status = "";

        public double Amount;
        public double Fee;
        public double Net;
        public string Currency = "";

        /// <summary>Canal réellement utilisé (fomapay_direct, mpesa…).</summary>
        public string Channel = "";

        public string OrderId = "";
        public string Description = "";
        public List<string> PaymentModes = new List<string>();
        public Dictionary<string, string> Metadata = new Dictionary<string, string>();
        public string PayerPhone = "";

        /// <summary>Lien de paiement FomaPay Direct : à ouvrir sur le téléphone du joueur, ou à afficher en QR code.</summary>
        public string QrCode = "";

        public bool Livemode;
        public string FailureCode = "";
        public string FailureReason = "";

        /// <summary>Réponse JSON complète de l'API.</summary>
        public Dictionary<string, object> Raw = new Dictionary<string, object>();

        public bool IsSuccessful => Status == PaymentStatuses.Success;

        /// <summary>Le paiement attend encore le joueur ou l'opérateur.</summary>
        public bool IsPending => Status == PaymentStatuses.Pending || Status == PaymentStatuses.Processing;

        internal static Payment FromJson(Dictionary<string, object> json)
        {
            var payment = new Payment
            {
                Id = Json.Str(json, "id"),
                Reference = Json.Str(json, "reference"),
                Status = Json.Str(json, "status"),
                Amount = Json.Num(json, "amount"),
                Fee = Json.Num(json, "fee"),
                Net = Json.Num(json, "net"),
                Currency = Json.Str(json, "currency"),
                Channel = Json.Str(json, "channel"),
                OrderId = Json.Str(json, "orderId"),
                Description = Json.Str(json, "description"),
                PaymentModes = Json.Strings(json, "paymentModes"),
                PayerPhone = Json.Str(json, "payerPhone"),
                QrCode = Json.Str(json, "qrCode"),
                Livemode = Json.Flag(json, "livemode"),
                FailureCode = Json.Str(json, "failureCode"),
                FailureReason = Json.Str(json, "failureReason"),
                Raw = json,
            };
            if (json.TryGetValue("metadata", out var value) && value is Dictionary<string, object> metadata)
            {
                foreach (var entry in metadata)
                {
                    if (entry.Value is string text)
                    {
                        payment.Metadata[entry.Key] = text;
                    }
                }
            }
            return payment;
        }
    }

    /// <summary>Boutique liée à la clé, avec les modes que son niveau KYC autorise.</summary>
    public sealed class StoreInfo
    {
        public string Id = "";
        public string Name = "";
        public string ShortCode = "";
        public bool Livemode;
        public int KycLevel;
        public List<string> PaymentModes = new List<string>();

        internal static StoreInfo FromJson(Dictionary<string, object> json)
        {
            return new StoreInfo
            {
                Id = Json.Str(json, "id"),
                Name = Json.Str(json, "name"),
                ShortCode = Json.Str(json, "shortCode"),
                Livemode = Json.Flag(json, "livemode"),
                KycLevel = (int)Json.Num(json, "kycLevel"),
                PaymentModes = Json.Strings(json, "paymentModes"),
            };
        }
    }

    /// <summary>Mise en forme pour l'affichage.</summary>
    public static class FomaPayFormat
    {
        /// <summary>« 1 000 CDF », « 12,50 USD ».</summary>
        public static string Amount(double amount, string currency)
        {
            var whole = currency == Currencies.CDF && Math.Abs(amount - Math.Round(amount)) < 0.000001;
            var text = Math.Abs(amount).ToString(whole ? "#,0" : "#,0.00", CultureInfo.InvariantCulture).Replace(',', ' ').Replace('.', ',');
            return (amount < 0 ? "-" : "") + text + " " + currency;
        }

        /// <summary>
        /// Met un numéro congolais au format international : « 081 234 5678 », « 812345678 » ou « +243 81… »
        /// deviennent « +243812345678 ». Renvoie null si le numéro est incomplet.
        /// </summary>
        public static string NormalizePhone(string input)
        {
            var digits = new System.Text.StringBuilder();
            foreach (var c in input ?? "")
            {
                if (c >= '0' && c <= '9')
                {
                    digits.Append(c);
                }
            }
            var number = digits.ToString();
            if (number.StartsWith("243", StringComparison.Ordinal))
            {
                number = number.Substring(3);
            }
            if (number.StartsWith("0", StringComparison.Ordinal))
            {
                number = number.Substring(1);
            }
            return number.Length == 9 ? "+243" + number : null;
        }
    }
}
