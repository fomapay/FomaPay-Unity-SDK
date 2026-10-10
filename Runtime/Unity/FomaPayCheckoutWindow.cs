using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FomaPay.Unity
{
    /// <summary>
    /// Fenêtre de paiement dessinée en IMGUI : aucun prefab, aucun paquet d'interface requis,
    /// elle s'affiche par-dessus n'importe quelle scène. Pour une interface aux couleurs de votre jeu,
    /// pilotez <see cref="FomaPayApi"/> depuis vos propres écrans.
    /// </summary>
    public sealed class FomaPayCheckoutWindow : MonoBehaviour
    {
        private enum Phase { Loading, Ready, Processing, Success, Failed, Error }

        private static readonly string[] OperatorIds = { Operators.MPesa, Operators.OrangeMoney, Operators.AirtelMoney };
        private static readonly string[] OperatorLabels = { "M-Pesa", "Orange Money", "Airtel Money" };

        private static readonly Color Navy = new Color32(0x03, 0x2B, 0x56, 0xFF);
        private static readonly Color Teal = new Color32(0x00, 0xC4, 0xB4, 0xFF);
        private static readonly Color Ink = new Color32(0x0B, 0x1B, 0x33, 0xFF);
        private static readonly Color Muted = new Color32(0x64, 0x74, 0x8B, 0xFF);
        private static readonly Color Danger = new Color32(0xD9, 0x2D, 0x20, 0xFF);
        private static readonly Color Success = new Color32(0x12, 0xA1, 0x50, 0xFF);

        private FomaPayApi _api;
        private PaymentRequest _request;
        private Action<Payment, FomaPayException> _onClose;
        private CancellationTokenSource _watch;

        private Phase _phase = Phase.Loading;
        private StoreInfo _store;
        private Payment _payment;
        private List<string> _modes = new List<string>();
        private int _mode;
        private int _operator;
        private string _phone = "";
        private string _message = "";
        private bool _busy;
        private bool _firstAttempt = true;

        private Texture2D _white;
        private GUIStyle _title, _text, _small, _amount, _button, _ghost, _field, _tab, _tabOn;

        internal void Open(FomaPayApi api, PaymentRequest request, Action<Payment, FomaPayException> onClose)
        {
            _api = api;
            _request = request;
            _onClose = onClose;
            Begin();
        }

        private void OnDestroy()
        {
            _watch?.Cancel();
            if (_white != null)
            {
                Destroy(_white);
            }
        }

        // ───────────────────────────── Logique ─────────────────────────────

        /// <summary>Crée le paiement. Rappelée par « Réessayer » : un paiement échoué ne se rejoue pas, on en crée un autre.</summary>
        private async void Begin()
        {
            _watch?.Cancel();
            _phase = Phase.Loading;
            _message = "";
            try
            {
                // La clé d'idempotence du jeu ne vaut que pour la première tentative.
                var key = _request.IdempotencyKey;
                if (!_firstAttempt)
                {
                    _request.IdempotencyKey = null;
                }
                _firstAttempt = false;
                var storeTask = _api.GetStoreAsync();
                var paymentTask = _api.CreatePaymentAsync(_request);
                _request.IdempotencyKey = key;
                _store = await storeTask;
                _payment = await paymentTask;
                if (this == null)
                {
                    return; // fenêtre fermée entre-temps
                }

                _modes = new List<string>();
                foreach (var mode in new[] { PaymentModes.FomaPayDirect, PaymentModes.MobileMoney })
                {
                    if (_store.PaymentModes.Contains(mode) && (_payment.PaymentModes.Count == 0 || _payment.PaymentModes.Contains(mode)))
                    {
                        _modes.Add(mode);
                    }
                }
                _mode = Mathf.Clamp(_mode, 0, Mathf.Max(0, _modes.Count - 1));
                Apply(_payment);
                Watch();
            }
            catch (FomaPayException e)
            {
                _message = e.Message;
                _phase = Phase.Error;
            }
        }

        /// <summary>Suit le paiement jusqu'à son issue.</summary>
        private async void Watch()
        {
            _watch?.Cancel();
            _watch = new CancellationTokenSource();
            var token = _watch.Token;
            try
            {
                var final = await _api.WaitForPaymentAsync(_payment.Id, 2500, 900, token);
                if (!token.IsCancellationRequested && this != null)
                {
                    Apply(final);
                }
            }
            catch (OperationCanceledException)
            {
                // fenêtre fermée ou nouvelle tentative
            }
            catch (FomaPayException e)
            {
                if (!token.IsCancellationRequested && this != null)
                {
                    _message = e.Message;
                    _phase = Phase.Failed;
                }
            }
        }

        private async void Act(Func<System.Threading.Tasks.Task<Payment>> action)
        {
            if (_busy || _payment == null)
            {
                return;
            }
            _busy = true;
            _message = "";
            try
            {
                Apply(await action());
            }
            catch (FomaPayException e)
            {
                _message = e.Message;
            }
            _busy = false;
        }

        private void Apply(Payment payment)
        {
            _payment = payment;
            switch (payment.Status)
            {
                case PaymentStatuses.Pending: _phase = Phase.Ready; break;
                case PaymentStatuses.Processing: _phase = Phase.Processing; break;
                case PaymentStatuses.Success: _phase = Phase.Success; break;
                default:
                    _phase = Phase.Failed;
                    _message = string.IsNullOrEmpty(payment.FailureReason)
                        ? "Le paiement n'a pas abouti. Aucun montant n'a été débité."
                        : payment.FailureReason;
                    break;
            }
        }

        private void Close(Payment payment)
        {
            _watch?.Cancel();
            _onClose?.Invoke(payment, null);
        }

        // ───────────────────────────── Affichage ─────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();
            GUI.depth = -1000;

            // Mise à l'échelle : la fenêtre garde la même taille apparente sur un téléphone et sur un écran 4K.
            var scale = Mathf.Max(1f, Mathf.Min(Screen.width / 400f, Screen.height / 640f));
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale;
            var height = Screen.height / scale;

            Fill(new Rect(0, 0, width, height), new Color(0.02f, 0.06f, 0.12f, 0.6f));
            var panel = new Rect((width - 360f) / 2f, (height - 520f) / 2f, 360f, 520f);
            Fill(panel, Color.white);
            Fill(new Rect(panel.x, panel.y, panel.width, 4f), Teal);

            GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 22f, panel.width - 48f, panel.height - 44f));
            Header();
            GUILayout.Space(14f);
            Body();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Paiement sécurisé par FomaPay", _small);
            GUILayout.EndArea();

            GUI.matrix = previous;
        }

        private void Header()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label((_store != null ? _store.Name : "Paiement") + (_api.Livemode ? "" : "   [TEST]"), _title);
            if (!string.IsNullOrEmpty(_request.Description))
            {
                GUILayout.Label(_request.Description, _small);
            }
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", _ghost, GUILayout.Width(36f), GUILayout.Height(32f)))
            {
                Close(null);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            GUILayout.Label(FomaPayFormat.Amount(_request.Amount, _request.Currency), _amount);
        }

        private void Body()
        {
            switch (_phase)
            {
                case Phase.Loading:
                    GUILayout.Space(60f);
                    GUILayout.Label("Préparation du paiement…", _text);
                    break;

                case Phase.Error:
                    Outcome("Paiement indisponible", _message, Danger);
                    if (GUILayout.Button("Réessayer", _button, GUILayout.Height(46f)))
                    {
                        Begin();
                    }
                    break;

                case Phase.Processing:
                    Outcome(
                        "Validez sur votre téléphone",
                        "Une demande de paiement a été envoyée au " + _payment.PayerPhone + ".\nSaisissez votre code secret pour confirmer.",
                        Navy);
                    break;

                case Phase.Success:
                    Outcome("Paiement réussi", "Référence " + _payment.Reference, Success);
                    if (GUILayout.Button("Continuer", _button, GUILayout.Height(46f)))
                    {
                        Close(_payment);
                    }
                    break;

                case Phase.Failed:
                    Outcome(_payment != null && _payment.Status == PaymentStatuses.Expired ? "Paiement expiré" : "Paiement refusé", _message, Danger);
                    if (GUILayout.Button("Réessayer", _button, GUILayout.Height(46f)))
                    {
                        Begin();
                    }
                    break;

                case Phase.Ready:
                    Ready();
                    break;
            }
        }

        private void Ready()
        {
            if (_modes.Count == 0)
            {
                Outcome("Aucun mode de paiement", "Cette boutique ne peut pas encore accepter ce paiement.", Danger);
                return;
            }
            if (_modes.Count > 1)
            {
                GUILayout.BeginHorizontal();
                for (var i = 0; i < _modes.Count; i++)
                {
                    var label = _modes[i] == PaymentModes.FomaPayDirect ? "FomaPay" : "Mobile Money";
                    if (GUILayout.Button(label, i == _mode ? _tabOn : _tab, GUILayout.Height(38f)))
                    {
                        _mode = i;
                        _message = "";
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(14f);
            }

            if (_modes[_mode] == PaymentModes.FomaPayDirect)
            {
                GUILayout.Label("Payez depuis votre portefeuille FomaPay.", _text);
                GUILayout.Space(6f);
                GUILayout.Label("Référence : " + _payment.Reference, _small);
                GUILayout.Space(12f);
                if (GUILayout.Button("Ouvrir FomaPay", _button, GUILayout.Height(46f)))
                {
                    Application.OpenURL(_payment.QrCode);
                }
                GUILayout.Space(10f);
                GUILayout.Label("En attente du paiement…", _small);
                if (!_api.Livemode)
                {
                    GUILayout.Space(10f);
                    GUILayout.BeginHorizontal();
                    GUI.enabled = !_busy;
                    if (GUILayout.Button("Simuler un succès", _ghost, GUILayout.Height(38f)))
                    {
                        Act(() => _api.SimulatePaymentAsync(_payment.Id, true));
                    }
                    if (GUILayout.Button("Simuler un échec", _ghost, GUILayout.Height(38f)))
                    {
                        Act(() => _api.SimulatePaymentAsync(_payment.Id, false));
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.BeginHorizontal();
                for (var i = 0; i < OperatorLabels.Length; i++)
                {
                    if (GUILayout.Button(OperatorLabels[i], i == _operator ? _tabOn : _tab, GUILayout.Height(38f)))
                    {
                        _operator = i;
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(12f);
                GUILayout.Label("Numéro " + OperatorLabels[_operator] + " (ex. 081 234 5678)", _small);
                _phone = GUILayout.TextField(_phone, 17, _field, GUILayout.Height(42f));
                GUILayout.Space(12f);
                GUI.enabled = !_busy;
                if (GUILayout.Button(_busy ? "Envoi…" : "Payer " + FomaPayFormat.Amount(_request.Amount, _request.Currency), _button, GUILayout.Height(46f)))
                {
                    var phone = FomaPayFormat.NormalizePhone(_phone);
                    if (phone == null)
                    {
                        _message = "Entrez un numéro à 9 chiffres, ex. 81 234 5678";
                    }
                    else
                    {
                        Act(() => _api.PayWithMobileMoneyAsync(_payment.Id, OperatorIds[_operator], phone));
                    }
                }
                GUI.enabled = true;
            }

            if (!string.IsNullOrEmpty(_message))
            {
                GUILayout.Space(8f);
                var color = _small.normal.textColor;
                _small.normal.textColor = Danger;
                GUILayout.Label(_message, _small);
                _small.normal.textColor = color;
            }
        }

        private void Outcome(string heading, string message, Color color)
        {
            GUILayout.Space(30f);
            var previous = _title.normal.textColor;
            _title.normal.textColor = color;
            GUILayout.Label(heading, _title);
            _title.normal.textColor = previous;
            GUILayout.Space(6f);
            GUILayout.Label(message, _text);
            GUILayout.Space(22f);
        }

        private void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _white);
            GUI.color = previous;
        }

        private Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void EnsureStyles()
        {
            if (_white != null)
            {
                return;
            }
            _white = Solid(Color.white);

            _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = true };
            _title.normal.textColor = Ink;
            _text = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            _text.normal.textColor = Ink;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _small.normal.textColor = Muted;
            _amount = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            _amount.normal.textColor = Ink;

            _button = Flat(Navy, Color.white, 15, FontStyle.Bold);
            _ghost = Flat(new Color32(0xEE, 0xF2, 0xF6, 0xFF), Ink, 13, FontStyle.Normal);
            _tab = Flat(new Color32(0xF5, 0xF7, 0xFA, 0xFF), Muted, 13, FontStyle.Bold);
            _tabOn = Flat(new Color32(0xD9, 0xF6, 0xF3, 0xFF), Navy, 13, FontStyle.Bold);

            _field = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 8, 8) };
            _field.normal.background = _field.focused.background = _field.hover.background = Solid(new Color32(0xF5, 0xF7, 0xFA, 0xFF));
            _field.normal.textColor = _field.focused.textColor = _field.hover.textColor = Ink;
        }

        private GUIStyle Flat(Color background, Color text, int size, FontStyle weight)
        {
            var style = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = weight, alignment = TextAnchor.MiddleCenter };
            var texture = Solid(background);
            style.normal.background = style.hover.background = style.active.background = style.focused.background = texture;
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = text;
            style.margin = new RectOffset(3, 3, 3, 3);
            return style;
        }
    }
}
