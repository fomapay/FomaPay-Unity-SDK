using System.Collections.Generic;
using FomaPay;
using FomaPay.Unity;
using UnityEngine;

/// <summary>
/// Exemple : un bouton « Acheter 500 gemmes ». Placez ce script et un FomaPayManager
/// (clé pk_test_… dans l'inspecteur) sur un GameObject de la scène, puis lancez le jeu.
/// </summary>
public class ShopExample : MonoBehaviour
{
    [SerializeField] private string playerId = "joueur42";

    private int _gems;
    private string _status = "";

    private void Buy()
    {
        var request = new PaymentRequest
        {
            Amount = 1000,
            Currency = Currencies.CDF,
            Description = "500 gemmes",
            OrderId = "gems_500:" + playerId,
            Metadata = new Dictionary<string, string> { ["player_id"] = playerId, ["product"] = "gems_500" },
        };

        FomaPayManager.Instance.Checkout(
            request,
            payment =>
            {
                // Pour un achat de valeur, créditez plutôt le joueur depuis votre serveur,
                // à la réception du webhook payment.succeeded.
                _gems += 500;
                _status = "Merci ! Paiement " + payment.Reference;
            },
            error => _status = error.Kind == FomaPayErrorKind.Cancelled ? "Achat annulé" : error.Message);
    }

    private void OnGUI()
    {
        if (FomaPayManager.Instance.IsCheckoutOpen)
        {
            return;
        }
        GUILayout.BeginArea(new Rect(20, 20, 320, 200));
        GUILayout.Label("Gemmes : " + _gems);
        if (GUILayout.Button("Acheter 500 gemmes — 1 000 CDF", GUILayout.Height(48)))
        {
            Buy();
        }
        GUILayout.Label(_status);
        GUILayout.EndArea();
    }
}
