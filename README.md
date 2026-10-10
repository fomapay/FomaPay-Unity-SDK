# FomaPay Unity SDK

SDK Unity officiel de **FomaPay** : vendez des achats en jeu payés par **portefeuille FomaPay** (dès le Niveau 0) ou par **Mobile Money** (M-Pesa, Orange Money, Airtel Money, dès le Niveau 1) en RDC.

- Unity 2021.3 LTS et plus récent, toutes plateformes (Android, iOS, WebGL, desktop)
- **Aucune dépendance** : ni paquet tiers, ni plugin natif
- Fenêtre de paiement incluse, sans prefab ni réglage de scène
- N'embarque que la clé **publique** `pk_…` : aucun secret dans le jeu
- Nouvelles tentatives automatiques, délai d'attente, reprise après une coupure de réseau

## Intégration en 3 étapes

### 1. Installer

Dans Unity : **Window › Package Manager › + › Add package from git URL…**

```
https://github.com/fomapay/FomaPay-Unity-SDK.git
```

### 2. Ajouter le gestionnaire à la scène

Créez un GameObject vide, ajoutez-lui le composant **FomaPay › FomaPay Manager**, puis collez votre clé publique `pk_test_…` dans l'inspecteur (FomaPay Business › Développeurs).

### 3. Vendre

```csharp
using FomaPay;
using FomaPay.Unity;

FomaPayManager.Instance.Checkout(
    new PaymentRequest
    {
        Amount = 1000,
        Currency = Currencies.CDF,
        Description = "500 gemmes",
        OrderId = "gems_500:" + playerId,
        Metadata = new Dictionary<string, string> { ["player_id"] = playerId },
    },
    payment => Inventory.AddGems(500),
    error =>
    {
        if (error.Kind != FomaPayErrorKind.Cancelled) Toast.Show(error.Message);
    });
```

La fenêtre propose les modes que le niveau KYC de votre boutique autorise :

- **FomaPay** : le bouton « Ouvrir FomaPay » bascule le joueur vers son portefeuille, sur le même téléphone ; le jeu détecte le paiement à son retour ;
- **Mobile Money** : le joueur choisit son opérateur, saisit son numéro et valide la demande reçue sur son téléphone.

Avec `async` / `await` :

```csharp
try
{
    Payment payment = await FomaPayManager.Instance.CheckoutAsync(request);
}
catch (FomaPayException e) when (e.Kind == FomaPayErrorKind.Cancelled)
{
    // le joueur a fermé la fenêtre
}
```

> **L'écran du joueur n'est pas une preuve de paiement.** Un jeu peut être modifié. Pour tout achat de valeur, créditez le joueur depuis votre serveur de jeu, à la réception du webhook `payment.succeeded` signé (voir les SDKs serveur : `fomapay-go`, `fomapay-python`, `fomapay-php`, `fomapay-java`, `FomaPay.NET`, `@fomapay/sdk-js/server`). Le champ `Metadata` vous y renvoie l'identifiant du joueur et du produit.

## Interface sur mesure

La fenêtre fournie est volontairement sobre (IMGUI), pour fonctionner partout sans réglage. Pour un écran aux couleurs de votre jeu, pilotez directement le client :

```csharp
FomaPayApi api = FomaPayManager.Instance.Api;

StoreInfo store = await api.GetStoreAsync();               // modes autorisés
Payment payment = await api.CreatePaymentAsync(request);

Application.OpenURL(payment.QrCode);                       // FomaPay Direct
// ou
await api.PayWithMobileMoneyAsync(payment.Id, Operators.AirtelMoney, "+243970000000");

payment = await api.WaitForPaymentAsync(payment.Id);       // attend l'issue
if (payment.IsSuccessful) { /* … */ }
```

`FomaPayFormat.Amount(1000, "CDF")` et `FomaPayFormat.NormalizePhone("081 234 5678")` vous aident à afficher les montants et à valider les numéros.

## Erreurs

Toutes les erreurs sont des `FomaPayException` :

| `Kind` | Signification | Exemples de `Code` |
| --- | --- | --- |
| `Api` | L'API a refusé la requête | `ERROR_LEVEL_0_LIMIT_REACHED`, `ERROR_VALIDATION` |
| `Network` | Pas de réponse | `ERROR_NETWORK`, `ERROR_TIMEOUT`, `ERROR_PAYMENT_TIMEOUT` |
| `Cancelled` | Le joueur a fermé la fenêtre | `ERROR_CHECKOUT_CLOSED` |
| `Configuration` | Mauvaise utilisation du SDK | `ERROR_INVALID_API_KEY`, `ERROR_MANAGER_MISSING` |

`Message` est rédigé pour être montré au joueur.

## Fiabilité

```csharp
FomaPayManager.Instance.Api.TimeoutSeconds = 15; // 30 par défaut
FomaPayManager.Instance.Api.MaxRetries = 3;      // 2 par défaut
```

- **Nouvelles tentatives** avec attente exponentielle (0,5 s, 1 s… plafonnée à 8 s). Ne sont rejouées que les requêtes sans risque : les lectures, et les créations de paiement, qui portent toujours un en-tête `Idempotency-Key`. Un `429` est toujours rejoué en respectant `Retry-After`.
- **Réseau mobile instable** : pendant l'attente du paiement, une coupure passagère n'interrompt rien ; le suivi reprend à l'interrogation suivante.
- **Pause du jeu** : les attentes utilisent le temps réel, la fenêtre fonctionne même avec `Time.timeScale = 0`.
- **Idempotence** : renseignez `PaymentRequest.IdempotencyKey` pour qu'un double appui ne crée qu'un paiement.

## Sandbox

Avec une clé `pk_test_…`, aucun argent ne circule ; la fenêtre affiche « [TEST] » et propose « Simuler un succès / un échec ». Numéros Mobile Money de test : terminé par `0001` → refusé, par `0002` → jamais validé, sinon confirmé après ~3 s.

Pour viser un backend local, renseignez « Api Url » dans l'inspecteur (`http://localhost:8080`).

## Structure du paquet

| Dossier | Contenu |
| --- | --- |
| `Runtime/Core` | Client, modèles, erreurs : C# pur, sans référence à Unity |
| `Runtime/Unity` | `FomaPayManager`, fenêtre de paiement, transport `UnityWebRequest` |
| `Samples~/Shop` | Exemple de boutique (importable depuis le Package Manager) |
| `Tests~` | Tests du cœur, exécutables sans Unity |

## Tests

Le cœur du SDK se teste sans Unity :

```bash
dotnet test Tests~/FomaPay.Unity.Core.Tests
```

Tests d'intégration contre le sandbox (non exécutés si la variable est absente) :

```bash
FOMAPAY_API_URL=http://localhost:8080 FOMAPAY_TEST_PUBLIC_KEY=pk_test_… dotnet test Tests~/FomaPay.Unity.Core.Tests
```

`dotnet build Tests~/UnityStubs` vérifie en plus que la couche Unity et l'exemple compilent, contre des bouchons de l'API Unity.

Licence MIT.
