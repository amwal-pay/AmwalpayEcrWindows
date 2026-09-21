using System;

namespace AmwalPay.Ecr
{
    /// <summary>
    /// Deployment environment for Web Service ECR.
    /// <para>
    /// The SDK resolves Hub URLs internally; integrators pass only this enum in
    /// <see cref="EcrConfig.Environment"/>.
    /// </para>
    /// </summary>
    public enum EcrEnvironment
    {
        SIT,
        UAT,
        PROD,
    }

    public static class EcrEnvironments
    {
        public const EcrEnvironment Default = EcrEnvironment.SIT;

        /// <summary>Reads an environment by name, ignoring case. Unknown names read as <see cref="Default"/>.</summary>
        public static EcrEnvironment FromName(string? name)
        {
            var trimmed = name?.Trim();
            foreach (EcrEnvironment value in Enum.GetValues(typeof(EcrEnvironment)))
            {
                if (string.Equals(value.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
            return Default;
        }
    }
}
