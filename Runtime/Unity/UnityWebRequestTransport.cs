using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace FomaPay.Unity
{
    /// <summary>Transport basé sur UnityWebRequest : fonctionne sur toutes les plateformes, WebGL compris.</summary>
    public sealed class UnityWebRequestTransport : IFomaPayTransport
    {
        public Task<FomaPayHttpResponse> SendAsync(FomaPayHttpRequest request)
        {
            var completion = new TaskCompletionSource<FomaPayHttpResponse>();
            var web = new UnityWebRequest(request.Url, request.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = request.TimeoutSeconds,
            };
            if (request.Body != null)
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body));
            }
            foreach (var header in request.Headers)
            {
                web.SetRequestHeader(header.Key, header.Value);
            }

            web.SendWebRequest().completed += _ =>
            {
                try
                {
                    // ProtocolError = le serveur a répondu (4xx / 5xx) : c'est une réponse, pas une panne réseau.
                    if (web.result == UnityWebRequest.Result.ConnectionError || web.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        var timedOut = web.error != null && web.error.ToLowerInvariant().Contains("timeout");
                        completion.SetException(timedOut
                            ? new FomaPayException(FomaPayErrorKind.Network, "ERROR_TIMEOUT", "FomaPay n'a pas répondu dans le délai imparti.")
                            : new FomaPayException(
                                FomaPayErrorKind.Network, "ERROR_NETWORK", "Impossible de joindre FomaPay. Vérifiez votre connexion Internet."));
                        return;
                    }

                    int.TryParse(web.GetResponseHeader("Retry-After"), out var retryAfter);
                    completion.SetResult(new FomaPayHttpResponse
                    {
                        Status = (int)web.responseCode,
                        Body = web.downloadHandler.text ?? "",
                        RetryAfterSeconds = retryAfter,
                    });
                }
                finally
                {
                    web.Dispose();
                }
            };
            return completion.Task;
        }
    }
}
