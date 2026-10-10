using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace FomaPay.Unity
{
    /// <summary>
    /// Point d'entrée du SDK dans une scène. Ajoutez ce composant à un GameObject (menu
    /// « FomaPay › FomaPay Manager »), renseignez la clé publique dans l'inspecteur, puis :
    /// <code>
    /// FomaPayManager.Instance.Checkout(
    ///     new PaymentRequest { Amount = 1000, Currency = Currencies.CDF, Description = "500 gemmes" },
    ///     payment => Inventory.AddGems(500),
    ///     error => Debug.Log(error.Message));
    /// </code>
    /// </summary>
    [AddComponentMenu("FomaPay/FomaPay Manager")]
    [DisallowMultipleComponent]
    public sealed class FomaPayManager : MonoBehaviour
    {
        [Tooltip("Clé publique de la boutique (pk_test_… ou pk_live_…). Jamais la clé secrète.")]
        [SerializeField] private string publicKey = "";

        [Tooltip("Laisser vide en production. En développement : http://localhost:8080")]
        [SerializeField] private string apiUrl = "";

        [Tooltip("Conserver le gestionnaire d'une scène à l'autre.")]
        [SerializeField] private bool persistAcrossScenes = true;

        private FomaPayApi _api;
        private FomaPayCheckoutWindow _window;

        /// <summary>Le gestionnaire de la scène ; lève une erreur claire s'il n'y en a pas.</summary>
        public static FomaPayManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new FomaPayException(
                        FomaPayErrorKind.Configuration,
                        "ERROR_MANAGER_MISSING",
                        "Aucun FomaPayManager dans la scène : ajoutez le composant « FomaPay › FomaPay Manager » à un GameObject.");
                }
                return _instance;
            }
        }

        private static FomaPayManager _instance;

        /// <summary>Client bas niveau, pour construire sa propre interface.</summary>
        public FomaPayApi Api => _api ??= new FomaPayApi(publicKey, new UnityWebRequestTransport(), apiUrl) { Delay = WaitAsync };

        /// <summary>
        /// Attente pilotée par une coroutine plutôt que par Task.Delay : les minuteries .NET
        /// ne fonctionnent pas sur WebGL, et celle-ci continue quand le jeu est en pause (timeScale = 0).
        /// </summary>
        private Task WaitAsync(int milliseconds)
        {
            var completion = new TaskCompletionSource<bool>();
            StartCoroutine(Wait(milliseconds / 1000f, completion));
            return completion.Task;
        }

        private static IEnumerator Wait(float seconds, TaskCompletionSource<bool> completion)
        {
            yield return new WaitForSecondsRealtime(seconds);
            completion.TrySetResult(true);
        }

        /// <summary>Vrai tant que la fenêtre de paiement est ouverte.</summary>
        public bool IsCheckoutOpen => _window != null;

        /// <summary>Configure le gestionnaire par le code (au lieu de l'inspecteur).</summary>
        public void Configure(string newPublicKey, string newApiUrl = null)
        {
            publicKey = newPublicKey;
            apiUrl = newApiUrl ?? "";
            _api = null;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Ouvre la fenêtre de paiement. <paramref name="onSuccess"/> reçoit le paiement confirmé ;
        /// <paramref name="onFailure"/> reçoit l'erreur (Kind = Cancelled si le joueur a fermé la fenêtre).
        /// </summary>
        /// <remarks>
        /// L'écran du joueur n'est jamais une preuve de paiement : pour un achat de valeur, créditez-le
        /// depuis votre serveur de jeu à la réception du webhook payment.succeeded.
        /// </remarks>
        public async void Checkout(PaymentRequest request, Action<Payment> onSuccess, Action<FomaPayException> onFailure = null)
        {
            try
            {
                var payment = await CheckoutAsync(request);
                onSuccess?.Invoke(payment);
            }
            catch (FomaPayException e)
            {
                onFailure?.Invoke(e);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                onFailure?.Invoke(new FomaPayException(FomaPayErrorKind.Configuration, "ERROR_UNKNOWN", e.Message, 0, e));
            }
        }

        /// <summary>Variante asynchrone de <see cref="Checkout"/> : renvoie le paiement confirmé ou lève une <see cref="FomaPayException"/>.</summary>
        public Task<Payment> CheckoutAsync(PaymentRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (_window != null)
            {
                throw new FomaPayException(FomaPayErrorKind.Configuration, "ERROR_CHECKOUT_ALREADY_OPEN", "Une fenêtre de paiement est déjà ouverte.");
            }

            var api = Api; // valide la clé avant d'ouvrir la fenêtre
            var completion = new TaskCompletionSource<Payment>();
            _window = gameObject.AddComponent<FomaPayCheckoutWindow>();
            _window.Open(api, request, (payment, error) =>
            {
                Destroy(_window);
                _window = null;
                if (payment != null)
                {
                    completion.TrySetResult(payment);
                }
                else
                {
                    completion.TrySetException(error ?? new FomaPayException(
                        FomaPayErrorKind.Cancelled, "ERROR_CHECKOUT_CLOSED", "Le joueur a fermé la fenêtre de paiement."));
                }
            });
            return completion.Task;
        }
    }
}
