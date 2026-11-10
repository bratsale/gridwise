using Confluent.Kafka;

namespace GridWise.Catalog.Services;

public static class KafkaCredentials
{
    /// <summary>
    /// Logs the client in to Kafka (SASL) when Kafka:Username is set: compose gives every service its own user. Without it (the tests
    /// with Testcontainers, a plain local Kafka) the client connects without logging in. Kafka:SaslMechanism is SCRAM-SHA-512 by
    /// default. SASL_PLAINTEXT: production would use SASL_SSL, so that nothing travels in clear text.
    /// </summary>
    public static T WithKafkaCredentials<T>(this T client, IConfiguration config) where T : ClientConfig
    {
        var username = config["Kafka:Username"];
        if (string.IsNullOrEmpty(username))
            return client;

        client.SecurityProtocol = SecurityProtocol.SaslPlaintext;
        client.SaslMechanism = Enum.Parse<SaslMechanism>((config["Kafka:SaslMechanism"] ?? "SCRAM-SHA-512").Replace("-", ""), ignoreCase: true);
        client.SaslUsername = username;
        client.SaslPassword = config["Kafka:Password"];
        return client;
    }
}
