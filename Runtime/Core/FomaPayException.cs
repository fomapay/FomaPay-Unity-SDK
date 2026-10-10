using System;

namespace FomaPay
{
    /// <summary>D'où vient l'erreur.</summary>
    public enum FomaPayErrorKind
    {
        /// <summary>L'API a répondu avec une erreur.</summary>
        Api,

        /// <summary>Aucune réponse : connexion impossible (ERROR_NETWORK) ou délai dépassé (ERROR_TIMEOUT).</summary>
        Network,

        /// <summary>Mauvaise utilisation du SDK (clé mal formée…).</summary>
        Configuration,

        /// <summary>Le joueur a fermé la fenêtre de paiement.</summary>
        Cancelled,
    }

    /// <summary>
    /// Erreur FomaPay. <see cref="Code"/> est stable et exploitable par le programme
    /// (ex. ERROR_LEVEL_0_LIMIT_REACHED) ; le message est lisible par le joueur.
    /// </summary>
    public sealed class FomaPayException : Exception
    {
        public FomaPayException(FomaPayErrorKind kind, string code, string message, int status = 0, Exception inner = null)
            : base(message, inner)
        {
            Kind = kind;
            Code = code;
            Status = status;
        }

        public FomaPayErrorKind Kind { get; }

        public string Code { get; }

        /// <summary>Statut HTTP (0 quand il n'y a pas eu de réponse).</summary>
        public int Status { get; }

        public override string ToString() => Code + ": " + Message;
    }
}
