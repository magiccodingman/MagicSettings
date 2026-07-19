namespace MagicSettings;

public static class MagicHttpClientBuilderExtensions
{
    public static IHttpClientBuilder AddMagicNodeAuthentication(this IHttpClientBuilder builder, string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        return builder.AddHttpMessageHandler(serviceProvider =>
            new MagicNodeAuthenticationHandler(serviceProvider.GetRequiredService<IMagicNodeAuthenticator>(), audience));
    }
}
